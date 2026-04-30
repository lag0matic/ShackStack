using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

namespace ShackStack.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const string SstvIdleSessionNotes = "ShackStack SSTV native sidecar  |  Signal  ---%  |  Mode Auto Detect  |  FSKID none";

    private static readonly TimeSpan RadioConnectUiTimeout = TimeSpan.FromSeconds(12);
    private static readonly byte[] WsjtxDirectedAlertWave = CreateWsjtxDirectedAlertWave();
    private readonly DispatcherTimer _smeterUiTimer;
    private readonly DispatcherTimer _longwaveRefreshTimer;
    private readonly DispatcherTimer _wefaxScheduleTimer;
    private double _displayedSmeterLevel;
    private double _targetSmeterLevel;
    private bool _disposed;
    private bool _monitorAutostartAttempted;
    private string _activeBandLabel = "20m";
    private int _activeFilterSlot = 2;
    private AppSettings _settings;
    private CancellationTokenSource? _cwSendCts;
    private CancellationTokenSource? _runtimeUiStateSaveCts;
    private Task? _cwSendTask;
    private readonly string _settingsPath;
    private readonly IRadioService? _radioService;
    private readonly IAppSettingsStore? _settingsStore;
    private readonly IAudioService? _audioService;
    private readonly IWaterfallService? _waterfallService;
    private readonly IWaterfallRenderSource? _waterfallRenderSource;
    private readonly IBandConditionsService? _bandConditionsService;
    private readonly IInteropService? _interopService;
    private readonly ICwDecoderHost? _cwDecoderHost;
    private readonly IRttyDecoderHost? _rttyDecoderHost;
    private readonly IKeyboardModeDecoderHost? _keyboardModeDecoderHost;
    private readonly IFreedvDigitalVoiceHost? _freedvDigitalVoiceHost;
    private readonly IFreedvReporterService? _freedvReporterService;
    private readonly ISstvDecoderHost? _sstvDecoderHost;
    private readonly ISstvTransmitService? _sstvTransmitService;
    private readonly IWefaxDecoderHost? _wefaxDecoderHost;
    private readonly IWsjtxModeHost? _wsjtxModeHost;
    private readonly ILongwaveService? _longwaveService;
    private readonly IDisposable? _radioSubscription;
    private readonly IDisposable? _audioLevelSubscription;
    private readonly IDisposable? _spectrumSubscription;
    private readonly IDisposable? _bandConditionsSubscription;
    private readonly IDisposable? _interopSubscription;
    private readonly IDisposable? _cwTelemetrySubscription;
    private readonly IDisposable? _cwDecodeSubscription;
    private readonly IDisposable? _rttyTelemetrySubscription;
    private readonly IDisposable? _rttyDecodeSubscription;
    private readonly IDisposable? _keyboardModeTelemetrySubscription;
    private readonly IDisposable? _keyboardModeDecodeSubscription;
    private readonly IDisposable? _freedvTelemetrySubscription;
    private readonly IDisposable? _freedvSpeechSubscription;
    private readonly IDisposable? _freedvReporterSubscription;
    private readonly IDisposable? _sstvTelemetrySubscription;
    private readonly IDisposable? _sstvImageSubscription;
    private readonly IDisposable? _wefaxTelemetrySubscription;
    private readonly IDisposable? _wefaxImageSubscription;
    private readonly IDisposable? _wsjtxTelemetrySubscription;
    private readonly IDisposable? _wsjtxDecodeSubscription;
    private Pcm16AudioClip? _wsjtxPreparedTransmitClip;
    private Pcm16AudioClip? _keyboardPreparedTransmitClip;
    private string? _keyboardPreparedTransmitFingerprint;
    private Pcm16AudioClip? _sstvPreparedTransmitClip;
    private string? _sstvPreparedTransmitFingerprint;
    private string? _sstvPreparedTransmitMode;
    private string? _sstvPreparedTransmitCwIdSummary;
    private string? _sstvPreparedTransmitImageFile;
    private string? _sstvPreparedTransmitWaveFile;
    private double _sstvPreparedTransmitDurationSeconds;
    private CancellationTokenSource? _sstvTxCts;
    private bool _wsjtxSlotSendInFlight;
    private bool _keyboardTxSendInFlight;
    private bool _sstvTxSendInFlight;
    private WsjtxModeTelemetry? _lastWsjtxTelemetry;
    private DateTime _lastWsjtxDirectedAlertUtc = DateTime.MinValue;
    private readonly string _sstvReceivedDirectory;
    private readonly string _sstvReplyDirectory;
    private readonly string _sstvTemplateDirectory;
    private readonly string _sstvTxDirectory;
    private readonly string _keyboardTxDirectory;
    private readonly string _freedvDebugDirectory;
    private readonly string _wefaxReceivedDirectory;
    private bool _isUpdatingCwRigSettingsFromRadio;
    private bool _cwRigSettingsDirty;
    private bool _isUpdatingVoiceRigSettingsFromRadio;
    private bool _voiceRigSettingsDirty;
    private bool _hasReceivedVoiceRigStateFromRadio;
    private bool _suppressRuntimeUiStatePersistence;
    private bool _isUpdatingNoiseReductionLevelFromRadio;
    private DateTimeOffset _noiseReductionInteractionUntilUtc;
    private readonly HashSet<string> _longwaveLoggedContactKeys = new(StringComparer.OrdinalIgnoreCase);

    public MainWindowViewModel(
        AppSettings settings,
        string settingsPath,
        IRadioService? radioService = null,
        IAppSettingsStore? settingsStore = null,
        IAudioService? audioService = null,
        IWaterfallService? waterfallService = null,
        IWaterfallRenderSource? waterfallRenderSource = null,
        IBandConditionsService? bandConditionsService = null,
        ILongwaveService? longwaveService = null,
        IInteropService? interopService = null,
        ICwDecoderHost? cwDecoderHost = null,
        IRttyDecoderHost? rttyDecoderHost = null,
        IKeyboardModeDecoderHost? keyboardModeDecoderHost = null,
        IFreedvDigitalVoiceHost? freedvDigitalVoiceHost = null,
        IFreedvReporterService? freedvReporterService = null,
        ISstvDecoderHost? sstvDecoderHost = null,
        ISstvTransmitService? sstvTransmitService = null,
        IWefaxDecoderHost? wefaxDecoderHost = null,
        IWsjtxModeHost? wsjtxModeHost = null)
    {
        _settings = settings;
        _settingsPath = settingsPath;
        _radioService = radioService;
        _settingsStore = settingsStore;
        _audioService = audioService;
        _waterfallService = waterfallService;
        _waterfallRenderSource = waterfallRenderSource;
        _bandConditionsService = bandConditionsService;
        _longwaveService = longwaveService;
        _interopService = interopService;
        _cwDecoderHost = cwDecoderHost;
        _rttyDecoderHost = rttyDecoderHost;
        _keyboardModeDecoderHost = keyboardModeDecoderHost;
        _freedvDigitalVoiceHost = freedvDigitalVoiceHost;
        _freedvReporterService = freedvReporterService;
        _sstvDecoderHost = sstvDecoderHost;
        _sstvTransmitService = sstvTransmitService;
        _wefaxDecoderHost = wefaxDecoderHost;
        _wsjtxModeHost = wsjtxModeHost;
        _sstvReceivedDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "sstv");
        _sstvReplyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "sstv-reply");
        _sstvTemplateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "sstv-templates");
        _sstvTxDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "sstv-tx");
        _keyboardTxDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "keyboard-tx");
        _freedvDebugDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "freedv-debug");
        _wefaxReceivedDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "wefax");
        Directory.CreateDirectory(_sstvReceivedDirectory);
        Directory.CreateDirectory(_sstvReplyDirectory);
        Directory.CreateDirectory(_sstvTemplateDirectory);
        Directory.CreateDirectory(_sstvTxDirectory);
        Directory.CreateDirectory(_keyboardTxDirectory);
        Directory.CreateDirectory(_freedvDebugDirectory);
        Directory.CreateDirectory(_wefaxReceivedDirectory);
        AttachSstvReplyLayoutChangeTracking(SstvReplyOverlayItems);
        AttachSstvReplyLayoutChangeTracking(SstvReplyImageOverlayItems);
        Theme = settings.Ui.Theme;
        WindowWidth = settings.Ui.WindowWidth;
        WindowHeight = settings.Ui.WindowHeight;
        RadioBackend = settings.Radio.ControlBackend;
        RadioPort = settings.Radio.CivPort;
        FlrigEndpoint = $"{settings.Interop.FlrigHost}:{settings.Interop.FlrigPort}";
        DiagnosticsRadioPort = settings.Radio.CivPort;
        DiagnosticsRadioBaud = settings.Radio.CivBaud.ToString();
        DiagnosticsRadioAddress = $"0x{settings.Radio.CivAddress:X2} ({settings.Radio.CivAddress})";
        DiagnosticsInterOpState = settings.Interop.FlrigEnabled ? "Starting..." : "Disabled";
        DiagnosticsInterOpEndpoint = $"{settings.Interop.FlrigHost}:{settings.Interop.FlrigPort}";
        DiagnosticsBandConditionsState = settings.Ui.BandConditionsEnabled ? "Enabled" : "Disabled";
        SettingsCallsign = FormatCallsign(settings.Station.Callsign);
        SettingsGridSquare = settings.Station.GridSquare;
        WsjtxOperatorCallsign = FormatCallsign(settings.Station.Callsign);
        WsjtxOperatorGridSquare = settings.Station.GridSquare.Trim().ToUpperInvariant();
        SettingsRadioBackend = settings.Radio.ControlBackend;
        SettingsCivPort = settings.Radio.CivPort;
        SettingsCivBaud = settings.Radio.CivBaud.ToString();
        SettingsCivAddress = FormatCivAddress(settings.Radio.CivAddress);
        SettingsTheme = settings.Ui.Theme;
        SettingsBandConditionsEnabled = settings.Ui.BandConditionsEnabled;
        SettingsShowExperimentalCw = settings.Ui.ShowExperimentalCw;
        SettingsLongwaveEnabled = settings.Longwave.Enabled;
        SettingsLongwaveBaseUrl = settings.Longwave.BaseUrl;
        SettingsLongwaveClientApiToken = settings.Longwave.ClientApiToken;
        SettingsLongwaveDefaultLogbookName = settings.Longwave.DefaultLogbookName;
        SettingsLongwaveDefaultLogbookNotes = settings.Longwave.DefaultLogbookNotes;
        IsBandConditionsEnabled = settings.Ui.BandConditionsEnabled;
        ShowCwPanel = settings.Ui.ShowExperimentalCw;
        SettingsFlrigEnabled = settings.Interop.FlrigEnabled;
        SettingsFlrigHost = settings.Interop.FlrigHost;
        SettingsFlrigPort = settings.Interop.FlrigPort.ToString();
        MonitorVolumePercent = Math.Clamp(settings.Audio.MonitorVolumePercent, 0, 100);
        FreedvSpeechVolumePercent = Math.Clamp(settings.Audio.FreedvMonitorVolumePercent, 0, 100);
        WaterfallFloorPercent = Math.Clamp(settings.Ui.WaterfallFloorPercent, 0, 95);
        WaterfallCeilingPercent = Math.Clamp(settings.Ui.WaterfallCeilingPercent, WaterfallFloorPercent + 1, 100);
        VoiceMicGainPercent = 50;
        LongwaveLogOperatorCallsign = FormatCallsign(settings.Station.Callsign);
        ApplyLongwaveSettingsState(settings);
        VoiceCompressionPercent = 0;
        VoiceRfPowerPercent = 100;
        CwPitchHz = 700;
        CwWpm = 20;
        CwDecoderProfile = "Adaptive Python";
        _voiceRigSettingsDirty = false;
        _cwRigSettingsDirty = false;
        SettingsStatusMessage = "Settings loaded";
        if (!string.IsNullOrWhiteSpace(SettingsCallsign))
        {
            KeyboardTransmitText = $"CQ CQ DE {SettingsCallsign} {SettingsCallsign} K";
        }

        RebuildWsjtxSuggestedMessages();
        LoadSstvArchiveImages();
        LoadWefaxArchiveImages();
        AvailableModes = BuildModePresets(RadioMode.Usb);
        AvailableBands = BuildBandPresets(_activeBandLabel);
        AvailableFilterWidths = BuildFilterPresets(_activeFilterSlot);
        SpectrumDbLabels = ["0 dB", "-20", "-40", "-60", "-80"];
        FrequencyMarkers = ["---", "---", "---", "---", "---"];
        SmeterSegmentBrushes = BuildSmeterBrushes(0);
        PreampOffBrush = BuildToggleBrush(false);
        Preamp1Brush = BuildToggleBrush(false);
        Preamp2Brush = BuildToggleBrush(false);
        SplitButtonBrush = BuildToggleBrush(false);
        AttenuatorButtonBrush = BuildToggleBrush(false);
        TunerButtonBrush = BuildToggleBrush(false);
        NoiseBlankerButtonBrush = BuildToggleBrush(false);
        NoiseReductionButtonBrush = BuildToggleBrush(false);
        AutoNotchButtonBrush = BuildToggleBrush(false);
        ManualNotchButtonBrush = BuildToggleBrush(false);
        IpPlusButtonBrush = BuildToggleBrush(false);
        FilterSharpBrush = BuildToggleBrush(true);
        FilterSoftBrush = BuildToggleBrush(false);
        Zoom1Brush = BuildToggleBrush(true);
        Zoom2Brush = BuildToggleBrush(false);
        Zoom5Brush = BuildToggleBrush(false);
        Zoom10Brush = BuildToggleBrush(false);
        BandConditionsUpdated = "Band conds: loading...";
        SolarFluxDisplay = "SFI —";
        SunspotsDisplay = "SN —";
        AIndexDisplay = "A —";
        KIndexDisplay = "K —";
        XRayDisplay = "X —";
        BandConditionsItems =
        [
            new BandConditionCellViewModel("80m-40m", "—", BuildConditionBrush("—"), "—", BuildConditionBrush("—")),
            new BandConditionCellViewModel("30m-20m", "—", BuildConditionBrush("—"), "—", BuildConditionBrush("—")),
            new BandConditionCellViewModel("17m-15m", "—", BuildConditionBrush("—"), "—", BuildConditionBrush("—")),
            new BandConditionCellViewModel("12m-10m", "—", BuildConditionBrush("—"), "—", BuildConditionBrush("—")),
        ];
        CurrentFrequencyHz = 14_175_000;
        FrequencyDisplay = "---";
        ModeDisplay = "---";
        PttState = "RX";
        ConnectionState = "Disconnected";
        UpdateHeaderCallsign();
        RadioStatusSummary = "Radio idle";
        IsOperatingWorkspace = true;
        CanConnect = radioService is not null
            && string.Equals(settings.Radio.ControlBackend, "direct", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.Radio.CivPort, "auto", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.Radio.CivPort);
        CanDisconnect = false;
        UpdateBandConditionsVisibility();
        UpdateModePanelsVisibility();
        _smeterUiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _smeterUiTimer.Tick += (_, _) => AnimateSmeter();
        _smeterUiTimer.Start();
        _longwaveRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _longwaveRefreshTimer.Tick += async (_, _) => await OnLongwaveRefreshTimerTickAsync();
        _longwaveRefreshTimer.Start();
        RebuildWefaxSchedule();
        _wefaxScheduleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _wefaxScheduleTimer.Tick += (_, _) => RebuildWefaxSchedule();
        _wefaxScheduleTimer.Start();

        if (radioService is not null)
        {
            _radioSubscription = radioService.StateStream.Subscribe(new Observer<RadioState>(state =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ApplyRadioState(state);
                });
            }));

            ApplyRadioState(radioService.CurrentState);
        }

        if (audioService is not null)
        {
            _audioLevelSubscription = audioService.LevelStream.Subscribe(new Observer<AudioLevels>(levels =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SetAudioLevelPercentages(levels);
                });
            }));
        }

        if (waterfallService is not null)
        {
            _spectrumSubscription = waterfallService.SpectrumStream.Subscribe(new Observer<SpectrumFrame>(frame =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SpectrumPathData = BuildSpectrumPath(frame.Bins, 600, 120);
                    FrequencyMarkers = BuildFrequencyMarkers(frame.CenterFrequencyHz, frame.SpanHz);
                    UpdateWaterfallBitmap();
                });
            }));
        }

        if (bandConditionsService is not null)
        {
            _bandConditionsSubscription = bandConditionsService.SnapshotStream.Subscribe(new Observer<BandConditionsSnapshot>(snapshot =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    BandConditionsUpdated = $"Band conds: {snapshot.Updated}";
                    SolarFluxDisplay = $"SFI {snapshot.SolarFlux}";
                    SunspotsDisplay = $"SN {snapshot.Sunspots}";
                    AIndexDisplay = $"A {snapshot.AIndex}";
                    KIndexDisplay = $"K {snapshot.KIndex}";
                    XRayDisplay = $"X {snapshot.XRay}";
                    BandConditionsItems = snapshot.Bands
                        .Select(static band => new BandConditionCellViewModel(
                            band.BandLabel,
                            band.DayCondition,
                            BuildConditionBrush(band.DayCondition),
                            band.NightCondition,
                            BuildConditionBrush(band.NightCondition)))
                        .ToArray();
                    DiagnosticsBandConditionsState = $"Updated {snapshot.Updated}";
                });
            }));
        }

        if (interopService is not null)
        {
            _interopSubscription = interopService.Events.Subscribe(new Observer<InteropEvent>(evt =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    DiagnosticsInterOpLastEvent = evt.Message;
                    DiagnosticsInterOpState = evt.Message.StartsWith("error ", StringComparison.OrdinalIgnoreCase)
                        ? $"Error: {evt.Message[6..]}"
                        : evt.Message;
                });
            }));
        }

        if (cwDecoderHost is not null)
        {
            _cwTelemetrySubscription = cwDecoderHost.TelemetryStream.Subscribe(new Observer<CwDecoderTelemetry>(telemetry =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    CwDecoderStatus = telemetry.Status;
                    CwDecoderWorker = telemetry.ActiveWorker;
                    CwDecoderConfidenceDisplay = $"{telemetry.Confidence:P0}";
                    CwEstimatedPitchDisplay = telemetry.EstimatedPitchHz > 0 ? $"{telemetry.EstimatedPitchHz} Hz" : "---";
                    CwEstimatedWpmDisplay = telemetry.EstimatedWpm > 0 ? $"{telemetry.EstimatedWpm}" : "---";
                    IsCwDecoderRunning = telemetry.IsRunning;
                });
            }));

            _cwDecodeSubscription = cwDecoderHost.DecodeStream.Subscribe(new Observer<CwDecodeChunk>(chunk =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (string.IsNullOrWhiteSpace(chunk.Text))
                    {
                        return;
                    }

                    CwDecodedText = string.IsNullOrEmpty(CwDecodedText)
                        ? chunk.Text
                        : $"{CwDecodedText}{chunk.Text}";
                    CwDecoderConfidenceDisplay = $"{chunk.Confidence:P0}";
                });
            }));
        }

        if (rttyDecoderHost is not null)
        {
            _rttyTelemetrySubscription = rttyDecoderHost.TelemetryStream.Subscribe(new Observer<RttyDecoderTelemetry>(telemetry =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    RttyRxStatus = telemetry.Status;
                    RttySessionNotes = $"{telemetry.ActiveWorker}  |  Signal {FormatTelemetryPercent(telemetry.SignalLevelPercent)}  |  Shift {telemetry.EstimatedShiftHz} Hz  |  Baud {telemetry.EstimatedBaud:0.##}";
                    if (telemetry.SuggestedAudioCenterHz > 0)
                    {
                        RttySuggestedAudioCenterHz = telemetry.SuggestedAudioCenterHz;
                        var confidenceLabel = telemetry.TuneConfidence >= 3.0
                            ? "good"
                            : telemetry.TuneConfidence >= 1.5
                                ? "weak"
                                : "listening";
                        var lockLabel = telemetry.IsCarrierLocked ? "locked" : "suggested";
                        RttyTuneHelperSuggestion =
                            $"Tune helper: {lockLabel} Audio Hz {telemetry.SuggestedAudioCenterHz:0} ({confidenceLabel}, score {telemetry.TuneConfidence:0.0}). " +
                            $"Mark/space approx {telemetry.SuggestedAudioCenterHz + telemetry.EstimatedShiftHz / 2.0:0}/{telemetry.SuggestedAudioCenterHz - telemetry.EstimatedShiftHz / 2.0:0} Hz.";
                    }
                });
            }));

            _rttyDecodeSubscription = rttyDecoderHost.DecodeStream.Subscribe(new Observer<RttyDecodeChunk>(chunk =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (string.IsNullOrWhiteSpace(chunk.Text))
                    {
                        return;
                    }

                    RttyDecodedText = $"{RttyDecodedText}{chunk.Text}";
                });
            }));
        }

        if (keyboardModeDecoderHost is not null)
        {
            _keyboardModeTelemetrySubscription = keyboardModeDecoderHost.TelemetryStream.Subscribe(new Observer<KeyboardModeDecoderTelemetry>(telemetry =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    KeyboardRxStatus = telemetry.Status;
                    KeyboardSuggestedAudioCenterHz = telemetry.SuggestedAudioCenterHz > 0
                        ? telemetry.SuggestedAudioCenterHz
                        : telemetry.TrackedAudioCenterHz;
                    KeyboardTuneHelperSuggestion =
                        $"Tune helper: peak {telemetry.SuggestedAudioCenterHz:0} Hz ({telemetry.SuggestedAudioScoreDb:+0.0;-0.0;0.0} dB) | " +
                        $"track {telemetry.TrackedAudioCenterHz:0.0} Hz | AFC {telemetry.FrequencyErrorHz:+0.00;-0.00;0.00} Hz | DCD {(telemetry.IsDcdOpen ? "open" : "closed")}.";
                    KeyboardSessionNotes =
                        $"{telemetry.ActiveWorker}  |  Signal {FormatTelemetryPercent(telemetry.SignalLevelPercent)}  |  Mode {telemetry.ModeLabel}  |  DCD {(telemetry.IsDcdOpen ? "open" : "closed")}";
                });
            }));

            _keyboardModeDecodeSubscription = keyboardModeDecoderHost.DecodeStream.Subscribe(new Observer<KeyboardModeDecodeChunk>(chunk =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!string.IsNullOrEmpty(chunk.Text))
                    {
                        KeyboardDecodedText = $"{KeyboardDecodedText}{chunk.Text}";
                    }
                });
            }));
        }

        if (freedvDigitalVoiceHost is not null)
        {
            _freedvTelemetrySubscription = freedvDigitalVoiceHost.TelemetryStream.Subscribe(new Observer<FreedvDigitalVoiceTelemetry>(telemetry =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    FreedvRxStatus = telemetry.Status;
                    var normalizedRadeCallsign = NormalizeFreedvRadeCallsign(telemetry.RadeCallsign);
                    var invalidRadeCallsign = !string.IsNullOrWhiteSpace(telemetry.RadeCallsign) && normalizedRadeCallsign is null;
                    var radeCallsign = normalizedRadeCallsign is not null
                        ? $"  |  RADE call {normalizedRadeCallsign}"
                        : invalidRadeCallsign
                            ? "  |  RADE call invalid"
                            : string.Empty;
                    FreedvSignalSummary =
                        $"Signal {FormatTelemetryPercent(telemetry.SignalLevelPercent)}  |  " +
                        $"Sync {FormatTelemetryPercent(telemetry.SyncPercent)}  |  " +
                        $"SNR {telemetry.SnrDb:0.0} dB";
                    if (normalizedRadeCallsign is not null)
                    {
                        FreedvLastRadeCallsign = normalizedRadeCallsign;
                        _ = ReportFreedvDecodedCallsignAsync(normalizedRadeCallsign, telemetry.ModeLabel, telemetry.SnrDb);
                    }
                    else if (invalidRadeCallsign && string.Equals(FreedvLastRadeCallsign, "None decoded", StringComparison.OrdinalIgnoreCase))
                    {
                        FreedvLastRadeCallsign = "Invalid EOO";
                    }

                    FreedvSessionNotes =
                        $"{telemetry.ActiveWorker}  |  Signal {FormatTelemetryPercent(telemetry.SignalLevelPercent)}  |  Sync {FormatTelemetryPercent(telemetry.SyncPercent)}  |  SNR {telemetry.SnrDb:0.0} dB{radeCallsign}";
                    FreedvRuntimeStatus = telemetry.IsCodec2RuntimeLoaded
                        ? $"FreeDV runtime loaded  |  Speech {telemetry.SpeechSampleRate} Hz  |  Modem {telemetry.ModemSampleRate} Hz"
                        : "FreeDV runtime missing. Bundle codec2.dll/libcodec2.dll or librade.dll, or set the runtime path override.";
                });
            }));

            _freedvSpeechSubscription = freedvDigitalVoiceHost.SpeechStream.Subscribe(new Observer<Pcm16AudioClip>(clip =>
            {
                _ = PlayFreedvSpeechFrameAsync(clip);
            }));
        }

        if (freedvReporterService is not null)
        {
            _freedvReporterSubscription = freedvReporterService.SnapshotStream.Subscribe(new Observer<FreedvReporterSnapshot>(snapshot =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    FreedvReporterStatus = snapshot.Status;
                    FreedvReporterStationCount = snapshot.Stations.Count;
                    RebuildFreedvReporterStations(snapshot.Stations);
                });
            }));
        }

        if (sstvDecoderHost is not null)
        {
            _sstvTelemetrySubscription = sstvDecoderHost.TelemetryStream.Subscribe(new Observer<SstvDecoderTelemetry>(telemetry =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SstvRxStatus = telemetry.Status;
                    if (!string.IsNullOrWhiteSpace(telemetry.FskIdCallsign))
                    {
                        SstvDecodedFskIdCallsign = telemetry.FskIdCallsign.Trim().ToUpperInvariant();
                    }

                    var fskId = string.IsNullOrWhiteSpace(SstvDecodedFskIdCallsign)
                        ? "FSKID none"
                        : $"FSKID {SstvDecodedFskIdCallsign}";
                    SstvSessionNotes = $"{telemetry.ActiveWorker}  |  Signal {FormatTelemetryPercent(telemetry.SignalLevelPercent)}  |  Mode {telemetry.DetectedMode}  |  {fskId}";
                });
            }));

            _sstvImageSubscription = sstvDecoderHost.ImageStream.Subscribe(new Observer<SstvImageFrame>(frame =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SstvImageStatus = frame.Status;
                    UpdateSstvPreview(frame.ImagePath);
                });
            }));
        }

        if (wefaxDecoderHost is not null)
        {
            _wefaxTelemetrySubscription = wefaxDecoderHost.TelemetryStream.Subscribe(new Observer<WefaxDecoderTelemetry>(telemetry =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    WefaxRxStatus = telemetry.Status;
                    WefaxSessionNotes = $"{telemetry.ActiveWorker}  |  Lines {telemetry.LinesReceived}  |  {WefaxSelectedFilter} {WefaxCenterHz}/{WefaxShiftHz} Hz  |  Auto {telemetry.AlignedOffset}  |  Slant {WefaxManualSlant}  |  Offset {WefaxManualOffset}  |  Start {telemetry.StartConfidence:P0}";
                });
            }));

            _wefaxImageSubscription = wefaxDecoderHost.ImageStream.Subscribe(new Observer<WefaxImageFrame>(frame =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    WefaxImageStatus = frame.Status;
                    UpdateWefaxPreview(frame.ImagePath);
                });
            }));
        }

        if (wsjtxModeHost is not null)
        {
            _wsjtxTelemetrySubscription = wsjtxModeHost.TelemetryStream.Subscribe(new Observer<WsjtxModeTelemetry>(telemetry =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _lastWsjtxTelemetry = telemetry;
                    WsjtxRxStatus = telemetry.Status;
                    WsjtxClockStatus = telemetry.ClockDisciplineStatus;
                    WsjtxCycleLengthSeconds = telemetry.CycleLengthSeconds;
                    WsjtxSecondsToNextCycle = telemetry.SecondsToNextCycle;
                    WsjtxCycleDisplay = $"{telemetry.ModeLabel}  |  {telemetry.CycleLengthSeconds:0.#}s cycle  |  Next {telemetry.SecondsToNextCycle:0.0}s";
                    UpdateWsjtxSessionNotes(telemetry);
                    OnPropertyChanged(nameof(WsjtxTransmitArmSummary));
                });
            }));

            _wsjtxDecodeSubscription = wsjtxModeHost.DecodeStream.Subscribe(new Observer<WsjtxDecodeMessage>(message =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    UpsertWsjtxMessage(message);
                    HandleWsjtxAutoSequence(message);
                    OnPropertyChanged(nameof(WsjtxHasMessages));
                });
            }));
        }

        ApplyWaterfallDisplaySettings();
        AvailableModes = BuildModePresets(SelectedMode);
        AvailableBands = BuildBandPresets(_activeBandLabel);
        _ = InitializeAudioAsync();
    }

    [ObservableProperty]
    private string title = "ShackStack";

    [ObservableProperty]
    private string headerCallsign = string.Empty;

    [ObservableProperty]
    private WorkspaceKind currentWorkspace = WorkspaceKind.Operating;

    [ObservableProperty]
    private bool isOperatingWorkspace;

    [ObservableProperty]
    private bool isSettingsWorkspace;

    [ObservableProperty]
    private bool isDiagnosticsWorkspace;

    [ObservableProperty]
    private string theme = "dark";

    [ObservableProperty]
    private int windowWidth = 1280;

    [ObservableProperty]
    private int windowHeight = 780;

    [ObservableProperty]
    private string radioBackend = "direct";

    [ObservableProperty]
    private string radioPort = "auto";

    [ObservableProperty]
    private string flrigEndpoint = "127.0.0.1:12345";

    [ObservableProperty]
    private string connectionState = "Disconnected";

    [ObservableProperty]
    private string frequencyDisplay = "---";

    [ObservableProperty]
    private long currentFrequencyHz;

    [ObservableProperty]
    private long vfoAFrequencyHz = 14_175_000;

    [ObservableProperty]
    private long vfoBFrequencyHz = 14_180_000;

    [ObservableProperty]
    private bool isSplitEnabled;

    [ObservableProperty]
    private bool isVfoBActive;

    public bool IsVfoAActive => !IsVfoBActive;

    [ObservableProperty]
    private IBrush splitButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private string modeDisplay = "---";

    [ObservableProperty]
    private int selectedModePanelTabIndex;

    [ObservableProperty]
    private string pttState = "RX";

    [ObservableProperty]
    private string filterWidthDisplay = "---";

    [ObservableProperty]
    private string radioStatusSummary = "Radio idle";

    [ObservableProperty]
    private bool canConnect;

    [ObservableProperty]
    private bool canDisconnect;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private IReadOnlyList<ModePreset> availableModes = Array.Empty<ModePreset>();

    [ObservableProperty]
    private IReadOnlyList<BandPreset> availableBands = Array.Empty<BandPreset>();

    [ObservableProperty]
    private IReadOnlyList<FilterPreset> availableFilterWidths = Array.Empty<FilterPreset>();

    [ObservableProperty]
    private IReadOnlyList<string> spectrumDbLabels = Array.Empty<string>();

    [ObservableProperty]
    private IReadOnlyList<string> frequencyMarkers = Array.Empty<string>();

    [ObservableProperty]
    private IReadOnlyList<IBrush> smeterSegmentBrushes = Array.Empty<IBrush>();

    [ObservableProperty]
    private string smeterDisplay = "S0";

    [ObservableProperty]
    private IBrush zoom1Brush = BuildToggleBrush(true);

    [ObservableProperty]
    private IBrush zoom2Brush = BuildToggleBrush(false);

    [ObservableProperty]
    private IBrush zoom5Brush = BuildToggleBrush(false);

    [ObservableProperty]
    private IBrush zoom10Brush = BuildToggleBrush(false);

    [ObservableProperty]
    private string bandConditionsUpdated = "Band conds: loading...";

    [ObservableProperty]
    private string solarFluxDisplay = "SFI —";

    [ObservableProperty]
    private string sunspotsDisplay = "SN —";

    [ObservableProperty]
    private string aIndexDisplay = "A —";

    [ObservableProperty]
    private string kIndexDisplay = "K —";

    [ObservableProperty]
    private string xRayDisplay = "X —";

    [ObservableProperty]
    private IReadOnlyList<BandConditionCellViewModel> bandConditionsItems = Array.Empty<BandConditionCellViewModel>();

    [ObservableProperty]
    private bool isBandConditionsEnabled = true;

    [ObservableProperty]
    private bool showBandConditionsPanel = true;

    [ObservableProperty]
    private bool showCwPanel;

    [ObservableProperty]
    private string diagnosticsRadioPort = "auto";

    [ObservableProperty]
    private string diagnosticsRadioBaud = "---";

    [ObservableProperty]
    private string diagnosticsRadioAddress = "---";

    [ObservableProperty]
    private string diagnosticsAudioRadioRx = "Not configured";

    [ObservableProperty]
    private string diagnosticsAudioRadioTx = "Not configured";

    [ObservableProperty]
    private string diagnosticsAudioPcTx = "Not configured";

    [ObservableProperty]
    private string diagnosticsAudioPcRx = "Not configured";

    [ObservableProperty]
    private string diagnosticsAudioMonitorState = "Stopped";

    [ObservableProperty]
    private string diagnosticsVoiceTxState = "TX audio idle";

    [ObservableProperty]
    private string diagnosticsInterOpState = "Unknown";

    [ObservableProperty]
    private string diagnosticsInterOpEndpoint = "---";

    [ObservableProperty]
    private string diagnosticsInterOpLastEvent = "No activity yet";

    [ObservableProperty]
    private string diagnosticsBandConditionsState = "Unknown";

    private static Pcm16AudioClip WithLeadingSilence(Pcm16AudioClip clip, int milliseconds)
    {
        if (milliseconds <= 0 || clip.SampleRate <= 0 || clip.Channels <= 0)
        {
            return clip;
        }

        var bytesPerSampleFrame = clip.Channels * 2;
        var silenceFrames = (int)Math.Ceiling(clip.SampleRate * (milliseconds / 1000.0));
        var silenceBytes = Math.Max(0, silenceFrames * bytesPerSampleFrame);
        if (silenceBytes == 0)
        {
            return clip;
        }

        var padded = new byte[silenceBytes + clip.PcmBytes.Length];
        Buffer.BlockCopy(clip.PcmBytes, 0, padded, silenceBytes, clip.PcmBytes.Length);
        return new Pcm16AudioClip(padded, clip.SampleRate, clip.Channels);
    }


    private async Task ApplyPureDigitalReceivePresetAsync()
    {
        if (_radioService is null)
        {
            return;
        }

        // Digital image/weak-signal modes want the cleanest practical receive
        // path, so disable DSP helpers that can distort the tones we need to decode.
        await _radioService.SetFilterSlotAsync(1, CancellationToken.None);
        await _radioService.SetNoiseBlankerAsync(false, CancellationToken.None);
        await _radioService.SetAutoNotchAsync(false, CancellationToken.None);
        await _radioService.SetManualNotchAsync(false, 1, 128, CancellationToken.None);
        await _radioService.SetNoiseReductionAsync(false, 0, CancellationToken.None);
    }


    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (_radioService is null)
        {
            return;
        }

        IsBusy = true;
        RadioStatusSummary = "Connecting to radio...";
        CanConnect = false;

        try
        {
            using var connectCts = new CancellationTokenSource(RadioConnectUiTimeout);
            await _radioService.ConnectAsync(
                new RadioConnectionOptions(
                    _settings.Radio.CivPort,
                    _settings.Radio.CivBaud,
                    _settings.Radio.CivAddress),
                connectCts.Token);
        }
        catch (OperationCanceledException)
        {
            ConnectionState = "Error";
            RadioStatusSummary = "Connect failed: radio did not answer before timeout.";
            IsBusy = false;
            CanConnect = true;
        }
        catch (Exception ex)
        {
            ConnectionState = "Error";
            RadioStatusSummary = $"Connect failed: {ex.Message}";
            IsBusy = false;
            CanConnect = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        if (_radioService is null)
        {
            return;
        }

        IsBusy = true;
        RadioStatusSummary = "Disconnecting...";
        CanDisconnect = false;

        try
        {
            await _radioService.DisconnectAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ConnectionState = "Error";
            RadioStatusSummary = $"Disconnect failed: {ex.Message}";
            IsBusy = false;
            CanDisconnect = true;
        }
    }

    partial void OnCanConnectChanged(bool value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanDisconnectChanged(bool value) => DisconnectCommand.NotifyCanExecuteChanged();

    private static string FormatTelemetryPercent(int percent)
        => $"{Math.Clamp(percent, 0, 100),3}%";

    private static string FormatCallsign(string callsign) => callsign.Trim().ToUpperInvariant();


    private void UpdateHeaderCallsign()
    {
        if (!string.Equals(ConnectionState, "Connected", StringComparison.OrdinalIgnoreCase))
        {
            HeaderCallsign = "CONNECTING...";
            return;
        }

        HeaderCallsign = string.IsNullOrWhiteSpace(SettingsCallsign)
            ? "PLEASE CONFIGURE CALLSIGN"
            : SettingsCallsign;
    }

    private static bool TryParseUiFrequencyHz(string frequencyLabel, out long hz)
    {
        hz = 0;
        var text = frequencyLabel.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"(\d+(?:\.\d+)?)\s*(MHz|kHz|Hz)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var match = matches
            .Cast<System.Text.RegularExpressions.Match>()
            .FirstOrDefault(candidate => candidate.Groups[2].Success)
            ?? matches.Cast<System.Text.RegularExpressions.Match>().FirstOrDefault();
        if (match is null || !match.Success)
        {
            return false;
        }

        if (!double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var unit = match.Groups[2].Success ? match.Groups[2].Value : string.Empty;
        var multiplier = unit.Equals("MHz", StringComparison.OrdinalIgnoreCase)
            ? 1_000_000.0
            : unit.Equals("kHz", StringComparison.OrdinalIgnoreCase)
                ? 1_000.0
                : unit.Equals("Hz", StringComparison.OrdinalIgnoreCase)
                    ? 1.0
                    : value >= 1_000_000.0
                        ? 1.0
                        : value >= 1_000.0
                            ? 1_000.0
                            : 1_000_000.0;

        hz = (long)Math.Round(value * multiplier);
        return hz > 0;
    }


    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _smeterUiTimer.Stop();
        _longwaveRefreshTimer.Stop();
        _wefaxScheduleTimer.Stop();
        _cwSendCts?.Cancel();
        _runtimeUiStateSaveCts?.Cancel();
        _sstvTxCts?.Cancel();
        WsjtxTransmitArmedLocal = false;

        using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        StopRuntimeServices(shutdownCts.Token);

        _radioSubscription?.Dispose();
        _audioLevelSubscription?.Dispose();
        _spectrumSubscription?.Dispose();
        _bandConditionsSubscription?.Dispose();
        _interopSubscription?.Dispose();
        _cwTelemetrySubscription?.Dispose();
        _cwDecodeSubscription?.Dispose();
        _rttyTelemetrySubscription?.Dispose();
        _rttyDecodeSubscription?.Dispose();
        _keyboardModeTelemetrySubscription?.Dispose();
        _keyboardModeDecodeSubscription?.Dispose();
        _freedvTelemetrySubscription?.Dispose();
        _freedvSpeechSubscription?.Dispose();
        _freedvReporterSubscription?.Dispose();
        _wsjtxTelemetrySubscription?.Dispose();
        _wsjtxDecodeSubscription?.Dispose();
        _sstvTelemetrySubscription?.Dispose();
        _sstvImageSubscription?.Dispose();
        _wefaxTelemetrySubscription?.Dispose();
        _wefaxImageSubscription?.Dispose();
        _cwSendCts?.Dispose();
        _runtimeUiStateSaveCts?.Dispose();
        _sstvTxCts?.Dispose();

    }

    private void StopRuntimeServices(CancellationToken ct)
    {
        RunShutdownStep(token => _radioService?.SetPttAsync(false, token) ?? Task.CompletedTask, ct);
        RunShutdownStep(token => _radioService?.SetCwKeyAsync(false, token) ?? Task.CompletedTask, ct);
        RunShutdownStep(token => _radioService?.StopCwSendAsync(token) ?? Task.CompletedTask, ct);
        RunShutdownStep(token => _audioService?.StopTransmitAsync(token) ?? Task.CompletedTask, ct);

        RunShutdownStep(token => _cwDecoderHost?.StopAsync(token) ?? Task.CompletedTask, ct);
        RunShutdownStep(token => _rttyDecoderHost?.StopAsync(token) ?? Task.CompletedTask, ct);
        RunShutdownStep(token => _keyboardModeDecoderHost?.StopAsync(token) ?? Task.CompletedTask, ct);
        RunShutdownStep(token => _freedvDigitalVoiceHost?.StopTransmitAsync(token) ?? Task.CompletedTask, ct);
        RunShutdownStep(token => _freedvDigitalVoiceHost?.StopAsync(token) ?? Task.CompletedTask, ct);
        RunShutdownStep(token => _freedvReporterService?.DisconnectAsync(token) ?? Task.CompletedTask, ct);
        RunShutdownStep(token => _sstvDecoderHost?.StopAsync(token) ?? Task.CompletedTask, ct);
        RunShutdownStep(token => _wefaxDecoderHost?.StopAsync(token) ?? Task.CompletedTask, ct);
        RunShutdownStep(token => _wsjtxModeHost?.StopAsync(token) ?? Task.CompletedTask, ct);

        RunShutdownStep(token => _audioService?.StopReceiveAsync(token) ?? Task.CompletedTask, ct);
        RunShutdownStep(token => _interopService?.StopAsync(token) ?? Task.CompletedTask, ct);
        RunShutdownStep(token => _radioService?.DisconnectAsync(token) ?? Task.CompletedTask, ct);
    }

    private static void RunShutdownStep(Func<CancellationToken, Task> step, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var task = step(ct);
            task.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort cleanup: shutdown should not hang on an already-stopped device or worker.
        }
    }

}
