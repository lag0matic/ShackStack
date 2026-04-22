namespace ShackStack.DecoderHost.Sstv.Core;

internal sealed class NativeSstvReceiver
{
    private const int WorkingSampleRate = SstvWorkingConfig.WorkingSampleRate;
    private const double MinAutoSyncProminence = 1.34;
    private const double MinConfiguredSyncProminence = 1.18;
    private const double MinAutoSyncLeadRatio = 1.06;
    private const double MinMartin1LeadRatio = 1.03;
    private const int SessionIdleSignalThreshold = 6;
    private const double MinChunkActivityForPendingVis = 0.010;
    private const double MinChunkActivityForSession = 0.006;
    private readonly MmsstvDemodState _demodState = new(WorkingSampleRate);
    private readonly MmsstvLevelTracker _levelTracker = new(WorkingSampleRate) { FastAgc = true };
    private readonly MmsstvSyncFilterBank _syncFilters = new(WorkingSampleRate);
    private readonly MmsstvDemodulatorBank _controlDemodulators = new(WorkingSampleRate, narrow: false);
    private readonly MmsstvIirFilter _controlLowPass = new();
    private readonly List<float> _samples = [];
    private readonly List<float> _controlSamples = [];
    private double _controlDcEstimate;
    private int _signalLevelPercent;
    private int _sessionIdleSamples;
    private int? _pendingVisDetectedSample;
    private int _visSearchFrameIndex;
    private int _lastForceProbeSample;
    private int? _lastAvtStartSample;
    private int? _pendingVisSearchOriginSample;
    private bool _configuredSessionStarted;
    private double _lastChunkActivity;
    private double _lastRawChunkActivity;
    private string _detectedMode = "Auto Detect";
    private string? _latestImagePath;
    private SstvModeProfile? _pendingVisProfile;
    private NativeImageSession? _session;
    private string? _pendingSessionStatus;
    private string _syncStatus = "Listening for VIS / sync tones";
    private string _sessionOrigin = "idle";
    private double _lastSyncProminence;

    public string ConfiguredMode { get; private set; } = "Auto Detect";
    public string FrequencyLabel { get; private set; } = "14.230 MHz USB";
    public int ManualSlant { get; private set; }
    public int ManualOffset { get; private set; }
    public bool IsRunning { get; private set; }
    public int SignalLevelPercent => _signalLevelPercent;
    public string DetectedMode => _detectedMode;
    public string? LatestImagePath => _latestImagePath;
    public string SyncStatus => _syncStatus;
    public string SessionOrigin => _sessionOrigin;
    public double LastSyncProminence => _lastSyncProminence;

    public NativeSstvReceiver()
    {
        _controlLowPass.MakeIir(2600.0, WorkingSampleRate, 2, 0, 0.0);
        _controlLowPass.Clear();
    }

    public void Configure(string? mode, string? frequencyLabel, int? manualSlant, int? manualOffset)
    {
        ConfiguredMode = MmsstvModeResolver.NormalizeName(string.IsNullOrWhiteSpace(mode) ? "Auto Detect" : mode.Trim());
        FrequencyLabel = string.IsNullOrWhiteSpace(frequencyLabel) ? FrequencyLabel : frequencyLabel.Trim();
        ManualSlant = manualSlant ?? 0;
        ManualOffset = manualOffset ?? 0;
        _detectedMode = ConfiguredMode;
        _syncStatus = ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase)
            ? "Listening for VIS / sync tones"
            : $"Listening for {ConfiguredMode} sync";
        _session?.SetManualAlignment(ManualSlant, ManualOffset);
    }

    public void SetManualAlignment(int manualSlant, int manualOffset)
    {
        ManualSlant = manualSlant;
        ManualOffset = manualOffset;
        _session?.SetManualAlignment(ManualSlant, ManualOffset);
    }

    public void Start() => IsRunning = true;

    public void Stop()
    {
        IsRunning = false;
        _demodState.ResetForStop();
    }

    public void Reset()
    {
        IsRunning = false;
        _demodState.Reset();
        _signalLevelPercent = 0;
        _sessionIdleSamples = 0;
        _pendingVisDetectedSample = null;
        _lastChunkActivity = 0.0;
        _lastRawChunkActivity = 0.0;
        _samples.Clear();
        _controlSamples.Clear();
        _levelTracker.Init();
        _syncFilters.Clear();
        _controlLowPass.Clear();
        _controlDcEstimate = 0.0;
        _visSearchFrameIndex = 0;
        _lastForceProbeSample = 0;
        _lastAvtStartSample = null;
        _pendingVisSearchOriginSample = null;
        _configuredSessionStarted = false;
        _session = null;
        _latestImagePath = null;
        _pendingVisProfile = null;
        _detectedMode = ConfiguredMode;
        _pendingSessionStatus = null;
        _syncStatus = ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase)
            ? "Listening for VIS / sync tones"
            : $"Listening for {ConfiguredMode} sync";
        _sessionOrigin = "idle";
        _lastSyncProminence = 0.0;
    }

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
            return $"Need more buffered audio before force-starting {profile.Name}";
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
        _signalLevelPercent = SstvAudioMath.EstimateSignalLevelPercent(controlSamples);
        _lastChunkActivity = AverageAbsolute(controlSamples);
        _lastRawChunkActivity = AverageAbsolute(monoWorkingSamples);
        if (monoWorkingSamples.Length > 0)
        {
            _samples.AddRange(monoWorkingSamples);
            _controlSamples.AddRange(controlSamples);
            TrimWorkingSamples();
        }

        if (_session is null)
        {
            ProcessEarlyDemodSyncModes(controlSamples);
        }

        if (_session is null
            && ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase)
            && !ShouldHoldCurrentVisFollowUp())
        {
            DetectVisIfPossible(out imageUpdated);
            MaybeStartFromPendingVis();
        }

        MaybeForceStartAuto();
        MaybeForceStartFromConfig();
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
                rawPll,
                WorkingSampleRate,
                1);

            if (result.Event != MmsstvDemodState.EarlySyncEvent.None)
            {
                lastMeaningful = result;

                if (result.Event is MmsstvDemodState.EarlySyncEvent.AvtEnterForcedStart
                    or MmsstvDemodState.EarlySyncEvent.ForcedStartReady)
                {
                    _lastAvtStartSample = Math.Max(0, chunkBaseSample + i + 1);
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
                TryStartCatalogMode(result.ModeId, "interval-sync", "Interval sync start");
                break;

            case MmsstvDemodState.EarlySyncEvent.LeadInTriggered:
                _syncStatus = "1200 Hz lead-in detected";
                break;

            case MmsstvDemodState.EarlySyncEvent.LeadInEnteredVis:
                _syncStatus = "1200 Hz lead-in confirmed; decoding VIS";
                break;

            case MmsstvDemodState.EarlySyncEvent.LeadInLost:
                _syncStatus = "1200 Hz lead-in lost";
                break;

            case MmsstvDemodState.EarlySyncEvent.StopWaitComplete:
                _syncStatus = "Stop wait complete";
                break;

            case MmsstvDemodState.EarlySyncEvent.DecodeVisPending:
                _syncStatus = $"Decoding VIS bits for {(_pendingVisProfile?.Name ?? _detectedMode)}";
                break;

            case MmsstvDemodState.EarlySyncEvent.VisLostToneSeparation:
                _syncStatus = "VIS decode lost tone separation";
                break;

            case MmsstvDemodState.EarlySyncEvent.VisEnterExtended:
                _demodState.BeginExtendedVisDecode(WorkingSampleRate);
                _syncStatus = "VIS extension detected";
                break;

            case MmsstvDemodState.EarlySyncEvent.VisResolvedMode:
                if (MmsstvModeCatalog.Profiles.FirstOrDefault(p => p.Id == result.ModeId) is { } profile)
                {
                    _pendingVisProfile = profile;
                    _detectedMode = profile.Name;
                    _demodState.BeginApplyNextMode(WorkingSampleRate);
                    _syncStatus = $"VIS decoded {profile.Name}; confirming 1200 Hz sync";
                }
                break;

            case MmsstvDemodState.EarlySyncEvent.VisResetToAutoStart:
                _syncStatus = "VIS decode reset";
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

    private void TryStartCatalogMode(SstvModeId modeId, string origin, string status)
    {
        var profile = MmsstvModeCatalog.Profiles.FirstOrDefault(p => p.Id == modeId && p.DecodePlanned);
        if (profile is null)
        {
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
        var allowLegacyVisPattern = !ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase);
        if (!VisDetector.TryDetect(
                _controlSamples,
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
        if (result is not null && result.Value.Prominence >= MinConfiguredSyncProminence)
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
                _demodState.SyncMode = MmsstvDemodSyncMode.WaitingForSyncTrigger;
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
        if (_session is not null || ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase))
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
            var profileName = session.Profile.Name;
            EndActiveSession($"Listening for the next {ListeningTargetLabel()}", preserveDetectedMode: true);
            imageUpdated = true;
            return $"Transmission ended for {profileName}; standing by for the next image";
        }

        var (updated, status) = _session.DecodeAvailableLines([.. _samples]);
        if (!string.IsNullOrWhiteSpace(status))
        {
            _latestImagePath = session.ImagePath;
            _syncStatus = $"Receiving {session.Profile.Name}";
            _sessionIdleSamples = 0;
            imageUpdated = true;
            if (session.Completed)
            {
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
            var profileName = session.Profile.Name;
            EndActiveSession($"Listening for the next {ListeningTargetLabel()}", preserveDetectedMode: true);
            imageUpdated = true;
            return $"Transmission ended for {profileName}; standing by for the next image";
        }

        return null;
    }

    private void TryConsumeForcedStart()
    {
        if (_session is not null || _demodState.SyncMode != MmsstvDemodSyncMode.ForcedStart)
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

        if (profile.Id == SstvModeId.Avt90)
        {
            var avtStartSample = _lastAvtStartSample
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
        var lineSamples = MmsstvTimingEngine.CalculateLineSamples(profile, WorkingSampleRate);
        var syncSamples = Math.Max(8, (int)Math.Round(profile.SyncMs * WorkingSampleRate / 1000.0));
        var syncAnchorOffset = 0;
        if (profile.Family == "scottie")
        {
            var gapSamples = Math.Max(1, (int)Math.Round(profile.GapMs * WorkingSampleRate / 1000.0));
            var scanSamples = (int)Math.Round(profile.ScanMs * WorkingSampleRate / 1000.0);
            syncAnchorOffset = (scanSamples * 2) + (gapSamples * 2);
        }

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
            ? Math.Min(_controlSamples.Count - syncSamples - 1, anchorSample.Value + anchorLookahead)
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
        _session = new NativeImageSession(profile, startSample, _demodState);
        _session.SetManualAlignment(ManualSlant, ManualOffset);
        _demodState.EnterStartedMode();
        _configuredSessionStarted = true;
        _pendingVisProfile = null;
        _pendingVisSearchOriginSample = null;
        _latestImagePath = _session.ImagePath;
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
        _lastForceProbeSample = 0;
        _sessionIdleSamples = 0;
        _sessionOrigin = "idle";
        _lastSyncProminence = 0.0;
        _demodState.Reset();
        _levelTracker.Init();
        _syncFilters.Clear();
        _controlLowPass.Clear();
        _controlDcEstimate = 0.0;
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
        var dcBlocked = sample - _controlDcEstimate;
        var bandLimited = _controlLowPass.Process(dcBlocked * 16384.0) / 16384.0;
        return (float)Math.Clamp(bandLimited, -1.0, 1.0);
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
