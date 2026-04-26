namespace ShackStack.DecoderHost.Sstv.Core;

internal sealed class NativeSstvReceiver
{
    private const int WorkingSampleRate = SstvWorkingConfig.WorkingSampleRate;
    private const double MinAutoSyncProminence = 1.34;
    private const double MinConfiguredSyncProminence = 1.18;
    private const double MinAutoSyncLeadRatio = 1.06;
    private const double MinMartin1LeadRatio = 1.03;
    private const int SessionIdleSignalThreshold = 6;
    private const int SessionMissingSyncLineThreshold = 8;
    private const double MinChunkActivityForPendingVis = 0.010;
    private const double MinChunkActivityForSession = 0.006;
    private const double MinSessionSyncProminence = 1.20;
    private const bool MmsstvAutoStopDefault = false;
    private static readonly bool UseMmsstvFullRxForSessionStart =
        !string.Equals(Environment.GetEnvironmentVariable("SHACKSTACK_SSTV_USE_MMSSTV_FULL_RX"), "0", StringComparison.Ordinal);
    private readonly MmsstvDemodState _demodState = new(WorkingSampleRate);
    private readonly MmsstvLevelTracker _levelTracker = new(WorkingSampleRate) { FastAgc = true };
    private readonly MmsstvSyncFilterBank _syncFilters = new(WorkingSampleRate);
    private readonly MmsstvDemodulatorBank _controlDemodulators = new(WorkingSampleRate, narrow: false);
    private readonly MmsstvFskIdDecoder _fskIdDecoder = new(WorkingSampleRate);
    private readonly MmsstvFirFilter _controlBandPass = new();
    private readonly List<float> _samples = [];
    private readonly List<float> _controlSamples = [];
    private double _controlDcEstimate;
    private double _controlPreviousInput;
    private bool? _controlBandPassActive;
    private bool? _controlBandPassSyncRestart;
    private int _signalLevelPercent;
    private int _sessionIdleSamples;
    private int _sessionMissingSyncSamples;
    private int? _pendingVisDetectedSample;
    private int _visSearchFrameIndex;
    private int _lastForceProbeSample;
    private int? _lastAvtStartSample;
    private int? _lastForcedStartSample;
    private int? _lastCatalogStartSample;
    private int? _pendingVisSearchOriginSample;
    private bool _configuredSessionStarted;
    private bool _manualForceStartPending;
    private bool _mmsstvRemoteSyncStart = true;
    private bool _mmsstvSyncRestart = true;
    private bool _mmsstvAutoSync = true;
    private double _lastChunkActivity;
    private double _lastRawChunkActivity;
    private string _detectedMode = "Auto Detect";
    private string? _latestImagePath;
    private string? _lastMmsstvSlantDebug;
    private string? _lastMmsstvSyncAdjustDebug;
    private string? _lastFskIdCallsign;
    private SstvModeProfile? _pendingVisProfile;
    private NativeImageSession? _session;
    private NativeImageSession? _lastCompletedSession;
    private string? _pendingSessionStatus;
    private string _syncStatus = "Listening for VIS / sync tones";
    private string _sessionOrigin = "idle";
    private double _lastSyncProminence;

    public string ConfiguredMode { get; private set; } = "Auto Detect";
    public string FrequencyLabel { get; private set; } = "14.230 MHz USB";
    public bool IsRunning { get; private set; }
    public int SignalLevelPercent => _signalLevelPercent;
    public string DetectedMode => _detectedMode;
    public string? LatestImagePath => _latestImagePath;
    public string? LastMmsstvSlantDebug => _lastMmsstvSlantDebug;
    public string? LastMmsstvSyncAdjustDebug => _lastMmsstvSyncAdjustDebug;
    public string? LastFskIdCallsign => _lastFskIdCallsign;
    public string SyncStatus => _syncStatus;
    public string SessionOrigin => _sessionOrigin;
    public double LastSyncProminence => _lastSyncProminence;
    public bool MmsstvRemoteSyncStart => _mmsstvRemoteSyncStart;
    public bool MmsstvSyncRestart => _mmsstvSyncRestart;
    public bool MmsstvAutoSync => _mmsstvAutoSync;
    public bool MmsstvAutoStop { get; private set; } = MmsstvAutoStopDefault;

    public NativeSstvReceiver()
    {
        ConfigureControlBandPass(activeReceive: false);
    }

    public void Configure(string? mode, string? frequencyLabel)
    {
        ConfiguredMode = MmsstvModeResolver.NormalizeName(string.IsNullOrWhiteSpace(mode) ? "Auto Detect" : mode.Trim());
        FrequencyLabel = string.IsNullOrWhiteSpace(frequencyLabel) ? FrequencyLabel : frequencyLabel.Trim();
        _detectedMode = ConfiguredMode;
        _syncStatus = ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase)
            ? "Listening for VIS / sync tones"
            : $"Listening for {ConfiguredMode} sync";
    }

    public void Configure(string? mode, string? frequencyLabel, int manualSlant, int manualOffset)
        => Configure(mode, frequencyLabel);

    public void SetMmsstvRxControls(
        bool remoteSyncStart,
        bool syncRestart,
        bool autoSync,
        bool autoStop)
    {
        _mmsstvRemoteSyncStart = remoteSyncStart;
        _mmsstvSyncRestart = syncRestart;
        _mmsstvAutoSync = autoSync;
        MmsstvAutoStop = autoStop;
        ApplyMmsstvRxControlFlags();
        ConfigureControlBandPass(_session is not null || IsMmsstvAcquisitionActive());
    }

    public bool ApplyMmsstvPostReceiveSlantCorrection()
    {
        var session = _session ?? _lastCompletedSession;
        if (session is null)
        {
            return false;
        }

        if (!session.ApplyMmsstvPostReceiveSlantCorrection(force: true))
        {
            _lastMmsstvSlantDebug = session.MmsstvSlantDebug;
            return false;
        }

        _latestImagePath = session.ImagePath;
        _lastMmsstvSlantDebug = session.MmsstvSlantDebug;
        _detectedMode = session.Profile.Name;
        return true;
    }

    public bool ApplyMmsstvPostReceiveSyncAdjustment()
    {
        var session = _session ?? _lastCompletedSession;
        if (session is null)
        {
            return false;
        }

        var applied = session.ApplyMmsstvPostReceiveSyncAdjustment();
        _latestImagePath = session.ImagePath;
        _lastMmsstvSyncAdjustDebug = session.MmsstvSyncAdjustDebug;
        _detectedMode = session.Profile.Name;
        return applied;
    }

    public bool ApplyMmsstvLiveSyncSkip()
    {
        var session = _session;
        if (session is null)
        {
            _lastMmsstvSyncAdjustDebug = "live-sync: no active SSTV receive session";
            return false;
        }

        var applied = session.ApplyMmsstvLiveSyncSkip(out var debug);
        _lastMmsstvSyncAdjustDebug = debug;
        _detectedMode = session.Profile.Name;
        _syncStatus = applied
            ? $"Applied MMSSTV live sync skip for {session.Profile.Name}"
            : $"MMSSTV live sync skip not applied for {session.Profile.Name}";
        return applied;
    }

    public void Start() => IsRunning = true;

    public void Stop()
    {
        IsRunning = false;
        _manualForceStartPending = false;
        _demodState.ResetForStop();
    }

    public void Reset()
    {
        IsRunning = false;
        _demodState.Reset();
        _fskIdDecoder.Reset();
        _signalLevelPercent = 0;
        _sessionIdleSamples = 0;
        _sessionMissingSyncSamples = 0;
        _pendingVisDetectedSample = null;
        _lastChunkActivity = 0.0;
        _lastRawChunkActivity = 0.0;
        _samples.Clear();
        _controlSamples.Clear();
        _levelTracker.Init();
        _syncFilters.Clear();
        _controlBandPass.Clear();
        _controlDcEstimate = 0.0;
        _controlPreviousInput = 0.0;
        _controlBandPassActive = null;
        _controlBandPassSyncRestart = null;
        ConfigureControlBandPass(activeReceive: false);
        _visSearchFrameIndex = 0;
        _lastForceProbeSample = 0;
        _lastAvtStartSample = null;
        _lastForcedStartSample = null;
        _lastCatalogStartSample = null;
        _pendingVisSearchOriginSample = null;
        _configuredSessionStarted = false;
        _session = null;
        _lastCompletedSession = null;
        _latestImagePath = null;
        _lastMmsstvSlantDebug = null;
        _lastMmsstvSyncAdjustDebug = null;
        _lastFskIdCallsign = null;
        _pendingVisProfile = null;
        _manualForceStartPending = false;
        _detectedMode = ConfiguredMode;
        _pendingSessionStatus = null;
        _syncStatus = ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase)
            ? "Listening for VIS / sync tones"
            : $"Listening for {ConfiguredMode} sync";
        _sessionOrigin = "idle";
        _lastSyncProminence = 0.0;
    }

    private void ApplyMmsstvRxControlFlags()
    {
        _demodState.SyncRestart = _mmsstvSyncRestart ? 1 : 0;
    }

    private bool IsMmsstvRxUnlocked()
        => MmsstvAutoStop || _mmsstvSyncRestart || _mmsstvAutoSync;

    public string ForceStartConfiguredMode()
    {
        if (!IsRunning)
        {
            return "Receiver idle; start RX before forcing SSTV decode";
        }

        if (_session is not null)
        {
            return $"Already receiving {_session.Profile.Name}";
        }

        if (ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase))
        {
            return "Choose a specific SSTV mode before using Force Start";
        }

        if (!MmsstvModeCatalog.TryResolve(ConfiguredMode, out var profile) || !profile.DecodePlanned)
        {
            return $"Configured SSTV mode {ConfiguredMode} is not available for force-start";
        }

        if (_samples.Count < Math.Max(WorkingSampleRate / 2, MmsstvTimingEngine.CalculateLineSamples(profile, WorkingSampleRate)))
        {
            _manualForceStartPending = true;
            _syncStatus = $"Force start queued for {profile.Name}; buffering audio";
            return $"Force start queued for {profile.Name}; buffering audio";
        }

        _manualForceStartPending = false;
        if (UseMmsstvFullRxForSessionStart)
        {
            StartSession(
                _samples.Count,
                profile,
                $"Manual force start {profile.Name}",
                "manual-force-source",
                0.0);
            return $"Manual force start armed for {profile.Name} at the current sample";
        }

        var result = FindBestSyncStart(profile);
        if (result is not null)
        {
            StartSession(
                result.Value.StartSample,
                profile,
                $"Manual force start {profile.Name}",
                "manual-force-sync",
                result.Value.Prominence);
            return $"Manual force start armed for {profile.Name} (sync {result.Value.Prominence:0.00}x)";
        }

        var fallbackStart = EstimateFallbackStart(profile);
        StartSession(
            fallbackStart,
            profile,
            $"Manual force start {profile.Name} (no sync)",
            "manual-force-raw",
            0.0);
        return $"Manual force start armed for {profile.Name} without sync lock";
    }

    public string HandleAudio(float[] monoWorkingSamples, out bool imageUpdated)
    {
        imageUpdated = false;
        if (!IsRunning)
        {
            return "Receiver idle";
        }

        var controlSamples = BuildControlSamples(monoWorkingSamples);
        var fskIdCallsign = _fskIdDecoder.Process(monoWorkingSamples);
        if (!string.IsNullOrWhiteSpace(fskIdCallsign))
        {
            _lastFskIdCallsign = fskIdCallsign;
        }

        _signalLevelPercent = SstvAudioMath.EstimateSignalLevelPercent(controlSamples);
        _lastChunkActivity = AverageAbsolute(controlSamples);
        _lastRawChunkActivity = AverageAbsolute(monoWorkingSamples);
        var chunkBaseSample = _samples.Count;
        if (monoWorkingSamples.Length > 0)
        {
            _samples.AddRange(monoWorkingSamples);
            _controlSamples.AddRange(controlSamples);
            TrimWorkingSamples();
        }

        if (_session is null
            && ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase))
        {
            if (_mmsstvRemoteSyncStart)
            {
                DetectVisIfPossible(out imageUpdated);
                MaybeStartFromPendingVis();
            }
        }

        var shouldRunMmsstvSyncScanner =
            (_mmsstvRemoteSyncStart && _session is null && _pendingVisProfile is null)
            || (_mmsstvRemoteSyncStart && _session is not null && IsMmsstvRxUnlocked() && _mmsstvSyncRestart);
        if (shouldRunMmsstvSyncScanner)
        {
            ProcessEarlyDemodSyncModes(controlSamples);
        }

        if (_session is null
            && ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase)
            && !IsMmsstvAcquisitionActive()
            && !ShouldHoldCurrentVisFollowUp())
        {
            if (_mmsstvRemoteSyncStart)
            {
                DetectVisIfPossible(out imageUpdated);
                MaybeStartFromPendingVis();
            }
        }

        MaybeForceStartAuto();
        MaybeForceStartFromConfig();
        MaybeApplyPendingManualForceStart();
        _session?.AppendLiveMmsstvSamples(monoWorkingSamples, chunkBaseSample);
        var decodeStatus = DecodeSessionLines(controlSamples, ref imageUpdated);
        if (!string.IsNullOrWhiteSpace(decodeStatus))
        {
            return decodeStatus;
        }

        if (!string.IsNullOrWhiteSpace(_pendingSessionStatus))
        {
            var pending = _pendingSessionStatus;
            _pendingSessionStatus = null;
            return pending!;
        }

        if (!string.IsNullOrWhiteSpace(_syncStatus))
        {
            return _syncStatus;
        }

        var dominant = DominantTone(controlSamples);
        return dominant > 0
            ? $"Monitoring SSTV audio ({dominant:0} Hz)"
            : "Monitoring SSTV audio";
    }

    private void ProcessEarlyDemodSyncModes(ReadOnlySpan<float> controlSamples)
    {
        if (controlSamples.Length == 0)
        {
            return;
        }

        ApplyMmsstvRxControlFlags();
        var toneBank = _demodState.SyncToneBank;
        _syncFilters.Retune(toneBank);
        ProcessEarlySyncModesSampleAccurate(controlSamples);
    }

    private void ProcessEarlySyncModesSampleAccurate(ReadOnlySpan<float> controlSamples)
    {
        var lastMeaningful = new MmsstvDemodState.EarlySyncResult(MmsstvDemodState.EarlySyncEvent.None);
        var chunkBaseSample = _samples.Count - controlSamples.Length;
        for (var i = 0; i < controlSamples.Length; i++)
        {
            var tones = _syncFilters.ProcessSample(controlSamples[i]);
            var rawPll = _controlDemodulators.ProcessRaw(controlSamples[i], MmsstvDemodulatorType.Pll);
            var result = _demodState.AdvanceEarlySyncState(
                tones.Tone1080,
                tones.Tone1200,
                tones.Tone1320,
                tones.Tone1900,
                tones.ToneFsk,
                rawPll,
                WorkingSampleRate,
                1);

            if (result.Event != MmsstvDemodState.EarlySyncEvent.None)
            {
                lastMeaningful = result;

                var eventSample = Math.Max(0, chunkBaseSample + i + 1);
                if (result.Event == MmsstvDemodState.EarlySyncEvent.StartCatalogMode)
                {
                    _lastCatalogStartSample = eventSample;
                }

                if (result.Event is MmsstvDemodState.EarlySyncEvent.ApplyEnterForcedStart
                    or MmsstvDemodState.EarlySyncEvent.AvtEnterForcedStart
                    or MmsstvDemodState.EarlySyncEvent.ForcedStartReady)
                {
                    _lastForcedStartSample = eventSample;
                    if (result.Event == MmsstvDemodState.EarlySyncEvent.AvtEnterForcedStart)
                    {
                        _lastAvtStartSample = eventSample;
                    }
                }

                if (ShouldStopEarlySyncScan(result.Event))
                {
                    break;
                }
            }
        }

        if (lastMeaningful.Event != MmsstvDemodState.EarlySyncEvent.None)
        {
            ApplyEarlySyncResult(lastMeaningful);
        }
    }

    private static bool ShouldStopEarlySyncScan(MmsstvDemodState.EarlySyncEvent syncEvent)
        => syncEvent is MmsstvDemodState.EarlySyncEvent.StartCatalogMode
            or MmsstvDemodState.EarlySyncEvent.AvtEnterForcedStart
            or MmsstvDemodState.EarlySyncEvent.ForcedStartReady
            or MmsstvDemodState.EarlySyncEvent.AvtEnterPeriodWait;

    private static bool IsSampleAccurateAvtMode(MmsstvDemodSyncMode syncMode)
        => syncMode is MmsstvDemodSyncMode.AvtWaitFor1900
            or MmsstvDemodSyncMode.AvtAttackConfirm
            or MmsstvDemodSyncMode.AvtExtendedVis
            or MmsstvDemodSyncMode.AvtPeriodWait
            or MmsstvDemodSyncMode.AvtRestart;

    private void ApplyEarlySyncResult(MmsstvDemodState.EarlySyncResult result)
    {
        switch (result.Event)
        {
            case MmsstvDemodState.EarlySyncEvent.StartCatalogMode:
                if (_session is null)
                {
                    TryStartCatalogMode(result.ModeId, "interval-sync", "Interval sync start");
                }
                break;

            case MmsstvDemodState.EarlySyncEvent.LeadInTriggered:
                _syncStatus = "1200 Hz lead-in detected";
                break;

            case MmsstvDemodState.EarlySyncEvent.LeadInEnteredVis:
                _syncStatus = "1200 Hz lead-in confirmed; decoding VIS";
                break;

            case MmsstvDemodState.EarlySyncEvent.LeadInLost:
                _syncStatus = ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase)
                    ? "Listening for VIS / sync tones"
                    : $"Listening for {ConfiguredMode} sync";
                break;

            case MmsstvDemodState.EarlySyncEvent.StopWaitComplete:
                _syncStatus = "Stop wait complete";
                break;

            case MmsstvDemodState.EarlySyncEvent.DecodeVisPending:
                _syncStatus = $"Decoding VIS bits for {(_pendingVisProfile?.Name ?? _detectedMode)}";
                break;

            case MmsstvDemodState.EarlySyncEvent.VisLostToneSeparation:
                _syncStatus = GetListeningStatus();
                break;

            case MmsstvDemodState.EarlySyncEvent.VisEnterExtended:
                _demodState.BeginExtendedVisDecode(WorkingSampleRate);
                _syncStatus = "VIS extension detected";
                break;

            case MmsstvDemodState.EarlySyncEvent.VisResolvedMode:
                if (MmsstvModeCatalog.Profiles.FirstOrDefault(p => p.Id == result.ModeId) is { } profile)
                {
                    if (ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase)
                        && TryResolveBufferedDirectVis(out var directProfile)
                        && directProfile.Id != profile.Id)
                    {
                        profile = directProfile;
                        _demodState.VisData = profile.VisCode;
                        _demodState.NextMode = (int)profile.Id;
                    }

                    _pendingVisProfile = profile;
                    _detectedMode = profile.Name;
                    _demodState.BeginApplyNextMode(WorkingSampleRate);
                    _syncStatus = $"VIS decoded {profile.Name}; confirming 1200 Hz sync";
                }
                break;

            case MmsstvDemodState.EarlySyncEvent.VisResetToAutoStart:
                _syncStatus = GetListeningStatus();
                break;

            case MmsstvDemodState.EarlySyncEvent.ApplyEnterForcedStart:
                _syncStatus = $"1200 Hz confirm complete for {(_pendingVisProfile?.Name ?? _detectedMode)}";
                break;

            case MmsstvDemodState.EarlySyncEvent.ApplyEnterAvtWait:
                _syncStatus = $"AVT follow-up armed for {(_pendingVisProfile?.Name ?? _detectedMode)}";
                break;

            case MmsstvDemodState.EarlySyncEvent.ApplyLostFollowup:
                _syncStatus = "VIS follow-up sync confirm failed";
                break;

            case MmsstvDemodState.EarlySyncEvent.ForcedStartReady:
            case MmsstvDemodState.EarlySyncEvent.AvtEnterForcedStart:
                if (result.Event == MmsstvDemodState.EarlySyncEvent.AvtEnterForcedStart)
                {
                    _syncStatus = $"AVT sync complete for {(_pendingVisProfile?.Name ?? _detectedMode)}";
                }
                TryConsumeForcedStart();
                break;

            case MmsstvDemodState.EarlySyncEvent.AvtEnterAttackConfirm:
                _syncStatus = "AVT attack confirmed";
                break;

            case MmsstvDemodState.EarlySyncEvent.AvtEnterExtendedVis:
                _syncStatus = "AVT extended VIS decode";
                break;

            case MmsstvDemodState.EarlySyncEvent.AvtEnterPeriodWait:
                _syncStatus = "AVT period wait";
                break;

            case MmsstvDemodState.EarlySyncEvent.AvtRevertToWait:
                _syncStatus = "AVT attack lost; waiting for 1900 Hz";
                break;
        }
    }

    private bool TryResolveBufferedDirectVis(out SstvModeProfile profile)
    {
        profile = default!;
        if (!VisDetector.TryDetect(
                _samples,
                Math.Max(0, _visSearchFrameIndex),
                allowLegacyPattern: false,
                out var nextFrameIndex,
                out var directProfile)
            || directProfile is null)
        {
            return false;
        }

        _visSearchFrameIndex = Math.Max(_visSearchFrameIndex, nextFrameIndex);
        _pendingVisSearchOriginSample = nextFrameIndex * WorkingSampleRate * 10 / 1000;
        _pendingVisDetectedSample = _samples.Count;
        profile = directProfile;
        return true;
    }

    private void TryStartCatalogMode(SstvModeId modeId, string origin, string status)
    {
        var profile = MmsstvModeCatalog.Profiles.FirstOrDefault(p => p.Id == modeId && p.DecodePlanned);
        if (profile is null)
        {
            return;
        }

        if (UseMmsstvFullRxForSessionStart)
        {
            if (_lastCatalogStartSample is int sourceStart)
            {
                StartSession(sourceStart, profile, $"{status} {profile.Name}", origin, 0.0);
            }

            return;
        }

        var result = FindBestSyncStart(profile);
        if (result is null)
        {
            return;
        }

        StartSession(result.Value.StartSample, profile, $"{status} {profile.Name}", origin, result.Value.Prominence);
    }

    private void DetectVisIfPossible(out bool imageUpdated)
    {
        imageUpdated = false;
        if (_pendingVisProfile is not null
            && !ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var allowLegacyVisPattern = !ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase);
        if (!VisDetector.TryDetect(
                _samples,
                _visSearchFrameIndex,
                allowLegacyVisPattern,
                out var nextFrameIndex,
                out var profile))
        {
            _visSearchFrameIndex = nextFrameIndex;
            return;
        }

        _visSearchFrameIndex = nextFrameIndex;
        if (profile is null)
        {
            return;
        }

        var visSearchOrigin = nextFrameIndex * WorkingSampleRate * 10 / 1000;
        _pendingVisSearchOriginSample = visSearchOrigin;
        _pendingVisDetectedSample = _samples.Count;

        _demodState.SyncMode = MmsstvDemodSyncMode.DecodeVis;
        _demodState.VisData = profile.VisCode;
        _demodState.NextMode = (int)profile.Id;

        var result = FindBestSyncStart(profile, visSearchOrigin);
        if (result is not null
            && result.Value.Prominence >= MinConfiguredSyncProminence)
        {
            StartSession(result.Value.StartSample, profile, $"VIS + sync lock {profile.Name}", "vis+sync", result.Value.Prominence);
            imageUpdated = false;
        }
        else
        {
            _pendingVisProfile = profile;
            _detectedMode = profile.Name;
            _sessionOrigin = "vis";
            _lastSyncProminence = result?.Prominence ?? 0.0;
            if (profile.Id == SstvModeId.Avt90)
            {
                _demodState.ResolveApplyNextMode(has1200Sync: true, shouldRequestSave: false, WorkingSampleRate);
                _syncStatus = $"AVT follow-up armed for {profile.Name}";
                ReplayBufferedAvtFollowUp(visSearchOrigin);
            }
            else
            {
                _demodState.BeginApplyNextMode(WorkingSampleRate);
                _syncStatus = $"VIS detected for {profile.Name}; waiting for line sync";
            }
        }
    }

    private bool ShouldHoldCurrentVisFollowUp()
        => _pendingVisProfile is not null
            && _demodState.SyncMode is not MmsstvDemodSyncMode.WaitingForSyncTrigger
            and not MmsstvDemodSyncMode.DecodeVis;

    private bool IsMmsstvAcquisitionActive()
        => _demodState.SyncMode is
            MmsstvDemodSyncMode.Confirm1200Continuation or
            MmsstvDemodSyncMode.DecodeVis or
            MmsstvDemodSyncMode.DecodeExtendedVis or
            MmsstvDemodSyncMode.ApplyNextMode or
            MmsstvDemodSyncMode.ForcedStart or
            MmsstvDemodSyncMode.AvtWaitFor1900 or
            MmsstvDemodSyncMode.AvtAttackConfirm or
            MmsstvDemodSyncMode.AvtExtendedVis or
            MmsstvDemodSyncMode.AvtPeriodWait or
            MmsstvDemodSyncMode.AvtRestart;

    private void ReplayBufferedAvtFollowUp(int startSample)
    {
        if (!IsSampleAccurateAvtMode(_demodState.SyncMode) || _samples.Count <= startSample)
        {
            return;
        }

        var replayCount = _samples.Count - Math.Max(0, startSample);
        if (replayCount <= 0)
        {
            return;
        }

        var replay = _controlSamples.GetRange(Math.Max(0, startSample), replayCount).ToArray();
        var toneBank = _demodState.SyncToneBank;
        _syncFilters.Retune(toneBank);
        ProcessEarlySyncModesSampleAccurate(replay);
    }

    private void MaybeStartFromPendingVis()
    {
        if (_session is not null || _pendingVisProfile is null)
        {
            return;
        }

        if (ShouldAbandonPendingVis())
        {
            var pendingName = _pendingVisProfile.Name;
            ClearPendingVis();
            _syncStatus = $"VIS follow-up expired for {pendingName}; listening for the next {ListeningTargetLabel()}";
            return;
        }

        var result = FindBestSyncStart(_pendingVisProfile, _pendingVisSearchOriginSample);
        if (result is not null
            && result.Value.Prominence >= MinConfiguredSyncProminence
            && HasStrongActivityAround(result.Value.StartSample, _pendingVisProfile))
        {
            StartSession(
                result.Value.StartSample,
                _pendingVisProfile,
                $"VIS + sync lock {_pendingVisProfile.Name}",
                "vis+sync",
                result.Value.Prominence);
            return;
        }

        _lastSyncProminence = result?.Prominence ?? 0.0;
        if (_pendingVisProfile.Id == SstvModeId.Avt90)
        {
            if (_demodState.SyncMode is MmsstvDemodSyncMode.WaitingForSyncTrigger
                or MmsstvDemodSyncMode.DecodeVis
                or MmsstvDemodSyncMode.ApplyNextMode)
            {
                _demodState.ResolveApplyNextMode(has1200Sync: true, shouldRequestSave: false, WorkingSampleRate);
            }

            _syncStatus = $"AVT follow-up armed for {_pendingVisProfile.Name}";
            return;
        }

        if (_demodState.SyncMode is MmsstvDemodSyncMode.WaitingForSyncTrigger or MmsstvDemodSyncMode.DecodeVis)
        {
            _demodState.BeginApplyNextMode(WorkingSampleRate);
        }
        _syncStatus = $"VIS detected for {_pendingVisProfile.Name}; waiting for line sync";
    }

    private void MaybeForceStartAuto()
    {
        if (_session is not null
            || _pendingVisProfile is not null
            || IsMmsstvAcquisitionActive()
            || UseMmsstvFullRxForSessionStart
            || !_mmsstvRemoteSyncStart
            || !_mmsstvAutoSync
            || !ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_controlSamples.Count < WorkingSampleRate * 4 || (_controlSamples.Count - _lastForceProbeSample) < WorkingSampleRate * 2)
        {
            return;
        }

        if (_lastRawChunkActivity < MinChunkActivityForPendingVis)
        {
            return;
        }

        SstvModeProfile? bestProfile = null;
        SyncCandidate? best = null;
        SstvModeProfile? runnerUpProfile = null;
        SyncCandidate? runnerUp = null;

        foreach (var profile in MmsstvModeCatalog.AutoSyncProfiles)
        {
            if (profile.Id == SstvModeId.Martin2)
            {
                continue;
            }

            var result = FindBestSyncStart(profile);
            if (result is null)
            {
                continue;
            }

            if (best is null || result.Value.Prominence > best.Value.Prominence)
            {
                runnerUp = best;
                runnerUpProfile = bestProfile;
                best = result.Value;
                bestProfile = profile;
            }
            else if (runnerUp is null || result.Value.Prominence > runnerUp.Value.Prominence)
            {
                runnerUp = result.Value;
                runnerUpProfile = profile;
            }
        }

        _lastForceProbeSample = _controlSamples.Count;
        if (bestProfile is not null && best is not null)
        {
            var hasActivity = HasStrongActivityAround(best.Value.StartSample, bestProfile);
            var requiredLeadRatio = bestProfile.Id == SstvModeId.Martin1
                ? MinMartin1LeadRatio
                : MinAutoSyncLeadRatio;
            var hasSeparation = runnerUp is null || best.Value.Prominence >= (runnerUp.Value.Prominence * requiredLeadRatio);
            if (best.Value.Prominence >= MinAutoSyncProminence && hasActivity && hasSeparation)
            {
                StartSession(best.Value.StartSample, bestProfile, $"Auto sync lock {bestProfile.Name}", "auto-sync", best.Value.Prominence);
            }
            else
            {
                _sessionOrigin = "auto-probe";
                _lastSyncProminence = best.Value.Prominence;
                _syncStatus = !hasActivity
                    ? $"Possible {bestProfile.Name} sync seen without enough image activity; waiting for VIS or a stronger lock"
                    : !hasSeparation && runnerUpProfile is not null
                        ? $"Ambiguous sync candidate {bestProfile.Name} vs {runnerUpProfile.Name}; waiting for VIS or a clearer lock"
                        : $"Weak sync candidate {bestProfile.Name} ({best.Value.Prominence:0.00}x); waiting for stronger lock";
            }
        }
    }

    private void MaybeForceStartFromConfig()
    {
        if (_session is not null
            || IsMmsstvAcquisitionActive()
            || !_mmsstvRemoteSyncStart
            || ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_controlSamples.Count < WorkingSampleRate * 3 || (_controlSamples.Count - _lastForceProbeSample) < WorkingSampleRate * 2)
        {
            return;
        }

        if (!MmsstvModeCatalog.TryResolve(ConfiguredMode, out var profile) || !profile.DecodePlanned)
        {
            return;
        }

        var result = FindBestSyncStart(profile);
        _lastForceProbeSample = _controlSamples.Count;
        if (result is null)
        {
            return;
        }

        if (result.Value.Prominence >= MinConfiguredSyncProminence)
        {
            StartSession(result.Value.StartSample, profile, $"{profile.Name} sync lock", "configured-sync", result.Value.Prominence);
        }
        else
        {
            _demodState.SyncMode = MmsstvDemodSyncMode.WaitingForSyncTrigger;
            _sessionOrigin = "configured-probe";
            _lastSyncProminence = result.Value.Prominence;
            _syncStatus = $"Weak {profile.Name} sync candidate ({result.Value.Prominence:0.00}x); waiting for stronger lock";
        }
    }

    private void MaybeApplyPendingManualForceStart()
    {
        if (!_manualForceStartPending
            || _session is not null
            || ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase)
            || !MmsstvModeCatalog.TryResolve(ConfiguredMode, out var profile)
            || !profile.DecodePlanned)
        {
            return;
        }

        if (_samples.Count < Math.Max(WorkingSampleRate / 2, MmsstvTimingEngine.CalculateLineSamples(profile, WorkingSampleRate)))
        {
            _syncStatus = $"Force start queued for {profile.Name}; buffering audio";
            return;
        }

        _manualForceStartPending = false;
        StartSession(
            _samples.Count,
            profile,
            $"Manual force start {profile.Name}",
            "manual-force-source",
            0.0);
    }

    private string? DecodeSessionLines(float[] controlSamples, ref bool imageUpdated)
    {
        if (_session is null)
        {
            return null;
        }

        var session = _session;
        if (ShouldAutoStopSession(controlSamples, session))
        {
            session.PersistSnapshot();
            _lastCompletedSession = session;
            var profileName = session.Profile.Name;
            EndActiveSession($"Listening for the next {ListeningTargetLabel()}", preserveDetectedMode: true);
            imageUpdated = true;
            return $"Transmission ended for {profileName}; standing by for the next image";
        }

        var (updated, status) = _session.DecodeAvailableLines([.. _samples]);
        if (!string.IsNullOrWhiteSpace(status))
        {
            _latestImagePath = session.ImagePath;
            _lastMmsstvSlantDebug = session.MmsstvSlantDebug;
            _syncStatus = $"Receiving {session.Profile.Name}";
            _sessionIdleSamples = 0;
            imageUpdated = true;
            if (session.Completed)
            {
                _lastCompletedSession = session;
                EndActiveSession($"Listening for the next {ListeningTargetLabel()}", preserveDetectedMode: true);
            }
            return status;
        }

        if (updated > 0)
        {
            _syncStatus = $"Receiving {session.Profile.Name}";
            _sessionIdleSamples = 0;
            return $"Decoded {session.LineIndex}/{session.Profile.Height} lines";
        }

        if (ShouldAutoStopSession(controlSamples, session))
        {
            session.PersistSnapshot();
            _lastCompletedSession = session;
            var profileName = session.Profile.Name;
            EndActiveSession($"Listening for the next {ListeningTargetLabel()}", preserveDetectedMode: true);
            imageUpdated = true;
            return $"Transmission ended for {profileName}; standing by for the next image";
        }

        return null;
    }

    private void TryConsumeForcedStart()
    {
        if (_demodState.SyncMode != MmsstvDemodSyncMode.ForcedStart)
        {
            return;
        }

        if (_session is not null && !_mmsstvSyncRestart)
        {
            return;
        }

        SstvModeProfile? profile = null;
        if (_pendingVisProfile is not null && (int)_pendingVisProfile.Id == _demodState.NextMode)
        {
            profile = _pendingVisProfile;
        }
        else
        {
            profile = MmsstvModeCatalog.Profiles.FirstOrDefault(p => (int)p.Id == _demodState.NextMode);
        }

        if (profile is null)
        {
            _demodState.ResetToAutoStart();
            _syncStatus = "Forced start could not resolve a mode";
            return;
        }

        if (_session is not null)
        {
            _session.PersistSnapshot();
            _lastCompletedSession = _session;
            _session = null;
            _configuredSessionStarted = false;
        }

        if (profile.Id == SstvModeId.Avt90)
        {
            var avtStartSample = _lastAvtStartSample
                ?? _lastForcedStartSample
                ?? _pendingVisSearchOriginSample
                ?? Math.Max(0, _samples.Count - MmsstvTimingEngine.CalculateLineSamples(profile, WorkingSampleRate));
            StartSession(
                avtStartSample,
                profile,
                $"Forced start {profile.Name}",
                _pendingVisProfile is not null ? "vis+forced-start" : "forced-start",
                _lastSyncProminence);
            return;
        }

        if (UseMmsstvFullRxForSessionStart)
        {
            if (_lastForcedStartSample is int sourceStart)
            {
                StartSession(
                    sourceStart,
                    profile,
                    $"Forced start {profile.Name}",
                    _pendingVisProfile is not null ? "vis+forced-start" : "forced-start",
                    _lastSyncProminence);
            }
            else
            {
                _demodState.ResetToAutoStart();
                _syncStatus = $"Forced start did not have a source start sample for {profile.Name}";
            }

            return;
        }

        var result = FindBestSyncStart(profile, _pendingVisSearchOriginSample);
        if (result is null)
        {
            _demodState.ResetToAutoStart();
            _syncStatus = $"Forced start lost sync for {profile.Name}";
            return;
        }

        StartSession(
            result.Value.StartSample,
            profile,
            $"Forced start {profile.Name}",
            _pendingVisProfile is not null ? "vis+forced-start" : "forced-start",
            result.Value.Prominence);
    }

    private SyncCandidate? FindBestSyncStart(SstvModeProfile profile, int? anchorSample = null)
    {
        var geometry = MmsstvPictureGeometry.Create(profile, WorkingSampleRate);
        var lineSamples = geometry.LineSamples;
        var syncSamples = Math.Max(8, geometry.SyncSamples);
        var syncAnchorOffset = geometry.SyncStartSamples;

        if (_controlSamples.Count < lineSamples * 12)
        {
            return null;
        }

        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_controlSamples);
        var candidateStep = Math.Max(4, WorkingSampleRate / 1500);
        var activeIndex = -1;
        for (var i = 0; i < span.Length; i++)
        {
            if (Math.Abs(span[i]) > 0.02f)
            {
                activeIndex = i;
                break;
            }
        }

        if (activeIndex < 0)
        {
            return null;
        }

        var searchOrigin = anchorSample.HasValue
            ? Math.Max(activeIndex, Math.Max(0, anchorSample.Value))
            : activeIndex;
        var anchorLookahead = Math.Max(
            syncSamples * 8,
            (lineSamples / 2) + syncAnchorOffset + (syncSamples * 2));
        var searchLimit = anchorSample.HasValue
            ? _controlSamples.Count - syncSamples - 1
            : Math.Min(_controlSamples.Count - syncSamples - 1, searchOrigin + (lineSamples * 2));
        if (searchLimit <= searchOrigin)
        {
            return null;
        }

        int? bestStart = null;
        var bestScore = -1.0;
        var bestProminence = 0.0;
        for (var candidate = searchOrigin; candidate < searchLimit; candidate += candidateStep)
        {
            var score = 0.0;
            var compare = 0.0;
            var used = 0;
            for (var lineIndex = 0; lineIndex < 10; lineIndex++)
            {
                var pos = candidate + (lineIndex * lineSamples);
                var syncPos = pos + syncAnchorOffset;
                if (syncPos + syncSamples > _samples.Count)
                {
                    break;
                }

                var block = span.Slice(syncPos, syncSamples);
                var toneBank = _demodState.SyncToneBank;
                score += SstvAudioMath.TonePower(block, WorkingSampleRate, toneBank.Tone1200Hz);
                compare += SstvAudioMath.TonePower(block, WorkingSampleRate, toneBank.Tone1080Hz);
                compare += SstvAudioMath.TonePower(block, WorkingSampleRate, toneBank.Tone1320Hz);
                compare += SstvAudioMath.TonePower(block, WorkingSampleRate, toneBank.Tone1900Hz);
                used++;
            }

            if (used < 5)
            {
                continue;
            }

            var normalized = score / used;
            var prominence = normalized / Math.Max(1e-9, compare / (used * 3.0));
            if (prominence > bestProminence || (Math.Abs(prominence - bestProminence) < 0.01 && normalized > bestScore))
            {
                bestScore = normalized;
                bestProminence = prominence;
                bestStart = candidate;
            }
        }

        if (bestStart is null || bestScore <= 0.0)
        {
            return null;
        }

        return new SyncCandidate(bestStart.Value, bestScore, bestProminence);
    }

    private void StartSession(int startSample, SstvModeProfile profile, string status, string origin, double prominence)
    {
        if (_configuredSessionStarted)
        {
            return;
        }

        _demodState.PrepareForModeStart(true);
        _demodState.NextMode = (int)profile.Id;
        _demodState.VisData = profile.VisCode;
        _session = new NativeImageSession(
            profile,
            startSample,
            _demodState,
            _mmsstvSyncRestart,
            _mmsstvAutoSync,
            MmsstvAutoStop);
        _lastCompletedSession = null;
        _session.SeedMmsstvFullRx([.. _samples]);
        _demodState.EnterStartedMode();
        _configuredSessionStarted = true;
        _manualForceStartPending = false;
        _pendingVisProfile = null;
        _pendingVisSearchOriginSample = null;
        _lastForcedStartSample = null;
        _lastCatalogStartSample = null;
        _latestImagePath = _session.ImagePath;
        _lastMmsstvSlantDebug = _session.MmsstvSlantDebug;
        _detectedMode = profile.Name;
        _pendingSessionStatus = status;
        _syncStatus = $"Receiving {profile.Name}";
        _sessionOrigin = origin;
        _lastSyncProminence = prominence;
        _sessionIdleSamples = 0;
    }

    private int EstimateFallbackStart(SstvModeProfile profile)
    {
        if (_samples.Count <= 0)
        {
            return 0;
        }

        var lineSamples = MmsstvTimingEngine.CalculateLineSamples(profile, WorkingSampleRate);
        var recentWindow = Math.Max(lineSamples * 2, WorkingSampleRate / 2);
        var windowStart = Math.Max(0, _samples.Count - recentWindow);
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_controlSamples);
        for (var i = windowStart; i < _controlSamples.Count; i++)
        {
            if (Math.Abs(span[i]) >= 0.012f)
            {
                return i;
            }
        }

        return Math.Max(0, _samples.Count - recentWindow);
    }

    private bool ShouldAutoStopSession(ReadOnlySpan<float> controlSamples, NativeImageSession session)
    {
        if (controlSamples.Length == 0)
        {
            return false;
        }

        if (session.LineIndex > 0 && HasSessionSync(controlSamples))
        {
            _sessionMissingSyncSamples = 0;
        }
        else if (session.LineIndex > 0)
        {
            _sessionMissingSyncSamples += controlSamples.Length;
            if (_sessionMissingSyncSamples >= SessionMissingSyncThresholdSamples(session.Profile))
            {
                return true;
            }
        }

        var dominantTone = DominantTone(controlSamples);
        var plausibleTone =
            dominantTone >= 1000.0
            && dominantTone <= 2400.0
            && (_lastRawChunkActivity >= (MinChunkActivityForSession * 0.5));
        if (plausibleTone
            || _lastChunkActivity >= MinChunkActivityForSession
            || _lastRawChunkActivity >= MinChunkActivityForSession)
        {
            _sessionIdleSamples = 0;
            return false;
        }

        _sessionIdleSamples += controlSamples.Length;
        return _sessionIdleSamples >= SessionIdleThresholdSamples(session.Profile);
    }

    private bool HasSessionSync(ReadOnlySpan<float> controlSamples)
    {
        var windowSamples = WorkingSampleRate / 50;
        if (controlSamples.Length < windowSamples)
        {
            return false;
        }

        var toneBank = _demodState.SyncToneBank;
        var stepSamples = Math.Max(1, windowSamples / 2);
        for (var offset = 0; offset + windowSamples <= controlSamples.Length; offset += stepSamples)
        {
            var window = controlSamples.Slice(offset, windowSamples);
            var syncPower = SstvAudioMath.TonePower(window, WorkingSampleRate, toneBank.Tone1200Hz);
            var comparePower =
                SstvAudioMath.TonePower(window, WorkingSampleRate, toneBank.Tone1080Hz) +
                SstvAudioMath.TonePower(window, WorkingSampleRate, toneBank.Tone1320Hz) +
                SstvAudioMath.TonePower(window, WorkingSampleRate, toneBank.Tone1900Hz);
            var prominence = syncPower / Math.Max(1e-9, comparePower / 3.0);
            if (prominence >= MinSessionSyncProminence)
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldAbandonPendingVis()
    {
        if (_pendingVisProfile is null || _pendingVisDetectedSample is null)
        {
            return false;
        }

        if (_lastChunkActivity >= MinChunkActivityForPendingVis
            || _lastRawChunkActivity >= MinChunkActivityForPendingVis)
        {
            return false;
        }

        var elapsedSamples = Math.Max(0, _samples.Count - _pendingVisDetectedSample.Value);
        return elapsedSamples >= Math.Max(WorkingSampleRate * 2, SessionIdleThresholdSamples(_pendingVisProfile));
    }

    private bool HasStrongActivityAround(int startSample, SstvModeProfile profile)
    {
        if (_controlSamples.Count == 0)
        {
            return false;
        }

        var lineSamples = MmsstvTimingEngine.CalculateLineSamples(profile, WorkingSampleRate);
        var window = Math.Max(lineSamples * 2, WorkingSampleRate / 2);
        var start = Math.Clamp(startSample, 0, Math.Max(0, _controlSamples.Count - 1));
        var endExclusive = Math.Clamp(start + window, 0, _controlSamples.Count);
        if (endExclusive <= start)
        {
            return false;
        }

        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_controlSamples).Slice(start, endExclusive - start);
        return AverageAbsolute(span) >= MinChunkActivityForPendingVis;
    }

    private static int SessionIdleThresholdSamples(SstvModeProfile profile)
    {
        var lineSamples = MmsstvTimingEngine.CalculateLineSamples(profile, WorkingSampleRate);
        return Math.Max(WorkingSampleRate * 3, lineSamples * 6);
    }

    private static int SessionMissingSyncThresholdSamples(SstvModeProfile profile)
    {
        var lineSamples = MmsstvTimingEngine.CalculateLineSamples(profile, WorkingSampleRate);
        return Math.Max(WorkingSampleRate * 2, lineSamples * SessionMissingSyncLineThreshold);
    }

    private void ClearPendingVis()
    {
        _pendingVisProfile = null;
        _pendingVisSearchOriginSample = null;
        _pendingVisDetectedSample = null;
        _demodState.ResetToAutoStart();
    }

    private void EndActiveSession(string listeningStatus, bool preserveDetectedMode)
    {
        _session = null;
        _configuredSessionStarted = false;
        ClearPendingVis();
        _lastAvtStartSample = null;
        _lastForcedStartSample = null;
        _lastCatalogStartSample = null;
        _lastForceProbeSample = 0;
        _sessionIdleSamples = 0;
        _sessionMissingSyncSamples = 0;
        _sessionOrigin = "idle";
        _lastSyncProminence = 0.0;
        _demodState.Reset();
        _levelTracker.Init();
        _syncFilters.Clear();
        _controlBandPass.Clear();
        _controlDcEstimate = 0.0;
        _controlPreviousInput = 0.0;
        _controlBandPassActive = null;
        _controlBandPassSyncRestart = null;
        ConfigureControlBandPass(activeReceive: false);
        _samples.Clear();
        _controlSamples.Clear();
        _visSearchFrameIndex = 0;
        if (!preserveDetectedMode)
        {
            _detectedMode = ConfiguredMode;
        }

        _syncStatus = listeningStatus;
    }

    private string ListeningTargetLabel()
        => ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase)
            ? "VIS / sync tones"
            : $"{ConfiguredMode} sync";

    private string GetListeningStatus()
        => $"Listening for {ListeningTargetLabel()}";

    private void TrimWorkingSamples()
    {
        if (_session is not null)
        {
            return;
        }

        const int maxSamples = WorkingSampleRate * 40;
        if (_samples.Count > maxSamples)
        {
            var drop = _samples.Count - maxSamples;
            _samples.RemoveRange(0, drop);
            _visSearchFrameIndex = Math.Max(0, _visSearchFrameIndex - (drop / 120));
        }

        if (_controlSamples.Count <= maxSamples)
        {
            return;
        }

        var controlDrop = _controlSamples.Count - maxSamples;
        _controlSamples.RemoveRange(0, controlDrop);
        _visSearchFrameIndex = Math.Max(0, _visSearchFrameIndex - (controlDrop / 120));
    }

    private float[] BuildControlSamples(ReadOnlySpan<float> monoWorkingSamples)
    {
        if (monoWorkingSamples.Length == 0)
        {
            return [];
        }

        ConfigureControlBandPass(_session is not null || IsMmsstvAcquisitionActive());
        var control = new float[monoWorkingSamples.Length];
        for (var i = 0; i < monoWorkingSamples.Length; i++)
        {
            var filtered = ApplyControlFrontEnd(monoWorkingSamples[i]);
            var scaled = filtered * 16384.0;
            _levelTracker.Process(scaled);
            _levelTracker.Fix();
            var agc = _levelTracker.ApplyAgc(scaled) / 16384.0;
            control[i] = (float)Math.Clamp(agc, -1.0, 1.0);
        }

        return control;
    }

    private float ApplyControlFrontEnd(float sample)
    {
        _controlDcEstimate = (_controlDcEstimate * 0.995) + (sample * 0.005);
        var scaled = SstvAudioMath.ToMmsstvPcmScale((float)(sample - _controlDcEstimate));
        var lowPassed = (scaled + _controlPreviousInput) * 0.5;
        _controlPreviousInput = scaled;
        var bandLimited = _controlBandPass.Process(lowPassed) / 16384.0;
        return (float)Math.Clamp(bandLimited, -1.0, 1.0);
    }

    private void ConfigureControlBandPass(bool activeReceive)
    {
        if (_controlBandPassActive == activeReceive && _controlBandPassSyncRestart == _mmsstvSyncRestart)
        {
            return;
        }

        var tapCount = Math.Max(1, (int)(24.0 * WorkingSampleRate / 11025.0));
        var lowCutHz = activeReceive
            ? (_mmsstvSyncRestart ? 1100.0 : 1200.0)
            : 400.0;
        var highCutHz = activeReceive ? 2600.0 : 2500.0;
        _controlBandPass.Create(
            tapCount,
            MmsstvFirFilter.FilterType.BandPass,
            WorkingSampleRate,
            lowCutHz,
            highCutHz,
            20.0,
            1.0);
        _controlBandPassActive = activeReceive;
        _controlBandPassSyncRestart = _mmsstvSyncRestart;
    }

    private static double DominantTone(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return 0.0;
        }

        var bestPower = 0.0;
        var bestFreq = 0.0;
        for (var freq = 1000.0; freq <= 2400.0; freq += 100.0)
        {
            var power = SstvAudioMath.TonePower(samples, WorkingSampleRate, freq);
            if (power > bestPower)
            {
                bestPower = power;
                bestFreq = freq;
            }
        }

        return bestFreq;
    }

    private static double AverageAbsolute(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return 0.0;
        }

        double sum = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            sum += Math.Abs(samples[i]);
        }

        return sum / samples.Length;
    }

    private readonly record struct SyncCandidate(int StartSample, double Score, double Prominence);
}
