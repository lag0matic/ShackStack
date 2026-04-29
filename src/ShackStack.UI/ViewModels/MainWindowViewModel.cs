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
                    RxLevelPercent = Math.Clamp((int)Math.Round(levels.RxLevel * 100f), 0, 100);
                    TxLevelPercent = Math.Clamp((int)Math.Round(levels.TxLevel * 100f), 0, 100);
                    MicLevelPercent = Math.Clamp((int)Math.Round(levels.MicLevel * 100f), 0, 100);
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
                    var radeCallsign = string.IsNullOrWhiteSpace(telemetry.RadeCallsign)
                        ? string.Empty
                        : $"  |  RADE call {telemetry.RadeCallsign}";
                    FreedvSignalSummary =
                        $"Signal {FormatTelemetryPercent(telemetry.SignalLevelPercent)}  |  " +
                        $"Sync {FormatTelemetryPercent(telemetry.SyncPercent)}  |  " +
                        $"SNR {telemetry.SnrDb:0.0} dB";
                    if (!string.IsNullOrWhiteSpace(telemetry.RadeCallsign))
                    {
                        FreedvLastRadeCallsign = telemetry.RadeCallsign.Trim().ToUpperInvariant();
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
    private RadioMode selectedMode = RadioMode.Usb;

    [ObservableProperty]
    private int selectedFilterSlot = 2;

    [ObservableProperty]
    private int preampLevel;

    [ObservableProperty]
    private IBrush preampOffBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private IBrush preamp1Brush = BuildToggleBrush(false);

    [ObservableProperty]
    private IBrush preamp2Brush = BuildToggleBrush(false);

    [ObservableProperty]
    private bool isAttenuatorEnabled;

    [ObservableProperty]
    private IBrush attenuatorButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private bool isTunerEnabled;

    [ObservableProperty]
    private IBrush tunerButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private bool isNoiseBlankerEnabled;

    [ObservableProperty]
    private IBrush noiseBlankerButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private bool isNoiseReductionEnabled;

    [ObservableProperty]
    private IBrush noiseReductionButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private int noiseReductionLevel;

    public IReadOnlyList<int> NoiseReductionLevelOptions { get; } = Enumerable.Range(0, 16).ToArray();

    [ObservableProperty]
    private bool isAutoNotchEnabled;

    [ObservableProperty]
    private IBrush autoNotchButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private bool isManualNotchEnabled;

    [ObservableProperty]
    private IBrush manualNotchButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private int manualNotchWidth = 1;

    public IReadOnlyList<int> ManualNotchWidthOptions { get; } = [0, 1, 2];

    public IReadOnlyList<string> ManualNotchWidthLabels { get; } = ["Wide", "Mid", "Nar"];

    [ObservableProperty]
    private string selectedManualNotchWidth = "Mid";

    [ObservableProperty]
    private int manualNotchPosition = 128;

    [ObservableProperty]
    private bool isIpPlusEnabled;

    [ObservableProperty]
    private IBrush ipPlusButtonBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private bool isFilterShapeSoft;

    [ObservableProperty]
    private IBrush filterSharpBrush = BuildToggleBrush(true);

    [ObservableProperty]
    private IBrush filterSoftBrush = BuildToggleBrush(false);

    [ObservableProperty]
    private string audioMonitorState = "Stopped";

    [ObservableProperty]
    private int rxLevelPercent;

    [ObservableProperty]
    private int monitorVolumePercent = 75;

    [ObservableProperty]
    private int freedvSpeechVolumePercent = 75;

    [ObservableProperty]
    private string spectrumPathData = string.Empty;

    [ObservableProperty]
    private Bitmap? waterfallBitmap;

    [ObservableProperty]
    private bool canStopAudio;

    [ObservableProperty]
    private int txLevelPercent;

    [ObservableProperty]
    private int micLevelPercent;

    [ObservableProperty]
    private int waterfallFloorPercent = 8;

    [ObservableProperty]
    private int waterfallCeilingPercent = 92;

    [ObservableProperty]
    private int waterfallZoom = 1;

    [ObservableProperty]
    private string settingsCallsign = string.Empty;

    [ObservableProperty]
    private string settingsGridSquare = string.Empty;

    [ObservableProperty]
    private string settingsRadioBackend = "direct";

    [ObservableProperty]
    private string settingsCivPort = "auto";

    [ObservableProperty]
    private string settingsCivBaud = "115200";

    [ObservableProperty]
    private string settingsCivAddress = "94h";

    [ObservableProperty]
    private string settingsTheme = "dark";

    [ObservableProperty]
    private bool settingsBandConditionsEnabled = true;

    [ObservableProperty]
    private bool settingsShowExperimentalCw;

    [ObservableProperty]
    private bool settingsFlrigEnabled;

    [ObservableProperty]
    private string settingsFlrigHost = "127.0.0.1";

    [ObservableProperty]
    private string settingsFlrigPort = "12345";

    [ObservableProperty]
    private string settingsStatusMessage = string.Empty;

    [ObservableProperty]
    private bool settingsLongwaveEnabled;

    [ObservableProperty]
    private string settingsLongwaveBaseUrl = string.Empty;

    [ObservableProperty]
    private string settingsLongwaveClientApiToken = string.Empty;

    [ObservableProperty]
    private string settingsLongwaveDefaultLogbookName = "ShackStack Home";

    [ObservableProperty]
    private string settingsLongwaveDefaultLogbookNotes = "LONGWAVE_KIND=standard;POTA_MODE=hunting";

    [ObservableProperty]
    private IReadOnlyList<AudioDeviceInfo> rxDeviceOptions = Array.Empty<AudioDeviceInfo>();

    [ObservableProperty]
    private IReadOnlyList<AudioDeviceInfo> outputDeviceOptions = Array.Empty<AudioDeviceInfo>();

    [ObservableProperty]
    private AudioDeviceInfo? selectedRxDevice;

    [ObservableProperty]
    private AudioDeviceInfo? selectedTxDevice;

    [ObservableProperty]
    private AudioDeviceInfo? selectedMicDevice;

    [ObservableProperty]
    private AudioDeviceInfo? selectedMonitorDevice;

    [ObservableProperty]
    private AudioDeviceInfo? selectedFreedvMonitorDevice;

    [ObservableProperty]
    private string voiceRxDeviceDisplay = "Not configured";

    [ObservableProperty]
    private string voiceTxDeviceDisplay = "Not configured";

    [ObservableProperty]
    private string voiceMicDeviceDisplay = "Not configured";

    [ObservableProperty]
    private string voiceMonitorDeviceDisplay = "Not configured";

    [ObservableProperty]
    private string freedvMonitorDeviceDisplay = "Not configured";

    [ObservableProperty]
    private string voiceTxStatus = "TX audio idle";

    [ObservableProperty]
    private int voiceMicGainPercent = 100;

    [ObservableProperty]
    private int voiceCompressionPercent;

    [ObservableProperty]
    private int voiceRfPowerPercent = 100;

    [ObservableProperty]
    private int cwPitchHz = 700;

    [ObservableProperty]
    private int cwWpm = 20;

    [ObservableProperty]
    private int cwBandwidthHz = 220;

    [ObservableProperty]
    private bool cwMatchedFilterEnabled = true;

    [ObservableProperty]
    private bool cwTrackingEnabled = true;

    [ObservableProperty]
    private int cwTrackingRangeWpm = 8;

    [ObservableProperty]
    private int cwLowerWpmLimit = 5;

    [ObservableProperty]
    private int cwUpperWpmLimit = 60;

    [ObservableProperty]
    private IReadOnlyList<string> cwAttackOptions = ["Fast", "Normal", "Slow"];

    [ObservableProperty]
    private string cwSelectedAttack = "Normal";

    [ObservableProperty]
    private IReadOnlyList<string> cwDecayOptions = ["Fast", "Normal", "Slow"];

    [ObservableProperty]
    private string cwSelectedDecay = "Slow";

    [ObservableProperty]
    private IReadOnlyList<string> cwNoiseCharacterOptions = ["Suppress", "Asterisk", "Underscore", "Space"];

    [ObservableProperty]
    private string cwSelectedNoiseCharacter = "Suppress";

    [ObservableProperty]
    private bool cwAutoToneSearchEnabled = true;

    [ObservableProperty]
    private bool cwAfcEnabled = true;

    [ObservableProperty]
    private int cwToneSearchSpanHz = 250;

    [ObservableProperty]
    private IReadOnlyList<string> cwSquelchOptions = ["Off", "Low", "Medium", "High"];

    [ObservableProperty]
    private string cwSelectedSquelch = "Off";

    [ObservableProperty]
    private IReadOnlyList<string> cwSpacingOptions = ["Tight", "Normal", "Loose"];

    [ObservableProperty]
    private string cwSelectedSpacing = "Normal";

    [ObservableProperty]
    private string cwDecoderProfile = "Adaptive Python";

    [ObservableProperty]
    private string cwDecoderStatus = "Ready";

    [ObservableProperty]
    private string cwDecoderWorker = "None";

    [ObservableProperty]
    private string cwDecoderConfidenceDisplay = "0%";

    [ObservableProperty]
    private string cwEstimatedPitchDisplay = "---";

    [ObservableProperty]
    private string cwEstimatedWpmDisplay = "---";

    [ObservableProperty]
    private string cwDecodedText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> rttyProfileOptions =
    [
        "170 Hz / 45.45 baud",
        "170 Hz / 75 baud",
        "425 Hz / 45.45 baud"
    ];

    [ObservableProperty]
    private string rttySelectedProfile = "170 Hz / 45.45 baud";

    [ObservableProperty]
    private IReadOnlyList<string> rttyFrequencyOptions =
    [
        "14.080 MHz USB",
        "14.083 MHz USB",
        "7.080 MHz LSB",
        "3.580 MHz LSB"
    ];

    [ObservableProperty]
    private string rttySelectedFrequency = "14.080 MHz USB";

    [ObservableProperty]
    private bool rttyDecodeCurrentRadioFrequency = true;

    [ObservableProperty]
    private string rttyAudioCenterHz = "1700";

    [ObservableProperty]
    private string rttyTuneHelperSuggestion = "Start RX, then place an RTTY signal in the passband; helper will suggest Audio Hz.";

    [ObservableProperty]
    private double rttySuggestedAudioCenterHz;

    [ObservableProperty]
    private bool rttyReversePolarity;

    [ObservableProperty]
    private string rttyRxStatus = "RTTY receiver ready";

    [ObservableProperty]
    private string rttySessionNotes = "For IC-7300 audio RTTY, use USB-D/LSB-D rather than native RTTY mode. Tune the signal, select shift/baud, then start receive.";

    [ObservableProperty]
    private string rttyDecodedText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> keyboardModeOptions = ["BPSK31", "BPSK63"];

    [ObservableProperty]
    private string keyboardSelectedMode = "BPSK31";

    [ObservableProperty]
    private IReadOnlyList<string> keyboardFrequencyOptions =
    [
        "14.070 MHz USB-D",
        "7.070 MHz LSB-D",
        "21.070 MHz USB-D",
        "28.120 MHz USB-D"
    ];

    [ObservableProperty]
    private string keyboardSelectedFrequency = "14.070 MHz USB-D";

    [ObservableProperty]
    private bool keyboardUseCurrentRadioFrequency = true;

    [ObservableProperty]
    private string keyboardAudioCenterHz = "1000";

    [ObservableProperty]
    private bool keyboardReversePolarity;

    [ObservableProperty]
    private string keyboardTuneHelperSuggestion = "Start RX, tune the PSK trace into the passband, then apply the helper if AFC settles off-center.";

    [ObservableProperty]
    private double keyboardSuggestedAudioCenterHz;

    [ObservableProperty]
    private string keyboardRxStatus = "Keyboard modes ready";

    [ObservableProperty]
    private string keyboardSessionNotes = "PSK31/63 receive uses the fldigi-derived GPL sidecar. Use USB-D/LSB-D and place the trace near the selected audio center.";

    [ObservableProperty]
    private string keyboardDecodedText = string.Empty;

    [ObservableProperty]
    private string keyboardTransmitText = "CQ CQ DE W8STR W8STR K";

    [ObservableProperty]
    private string keyboardPreparedTransmitStatus = "No PSK TX audio prepared.";

    [ObservableProperty]
    private string keyboardPreparedTransmitPath = "No prepared PSK WAV.";

    public bool KeyboardHasPreparedTransmitClip => _keyboardPreparedTransmitClip is not null;

    [ObservableProperty]
    private IReadOnlyList<string> freedvModeOptions = ["RADEV1", "700D", "700E", "700C", "1600"];

    [ObservableProperty]
    private string freedvSelectedMode = "RADEV1";

    [ObservableProperty]
    private IReadOnlyList<string> freedvFrequencyOptions =
    [
        "14.236 MHz USB-D",
        "7.177 MHz LSB-D",
        "21.313 MHz USB-D",
        "28.330 MHz USB-D"
    ];

    [ObservableProperty]
    private string freedvSelectedFrequency = "14.236 MHz USB-D";

    [ObservableProperty]
    private bool freedvUseCurrentRadioFrequency = true;

    [ObservableProperty]
    private int freedvRxFrequencyOffsetHz;

    [ObservableProperty]
    private string freedvRxStatus = "FreeDV digital voice ready";

    [ObservableProperty]
    private string freedvSessionNotes = "Use USB-D/LSB-D with the official Codec2 FreeDV runtime. 700D is the practical first HF mode.";

    [ObservableProperty]
    private string freedvRuntimeStatus = "Codec2 runtime not loaded yet.";

    [ObservableProperty]
    private string freedvDecodedAudioStatus = "No decoded FreeDV speech yet.";

    [ObservableProperty]
    private string freedvSignalSummary = "Signal ---%  |  Sync ---%  |  SNR --.- dB";

    [ObservableProperty]
    private string freedvLastRadeCallsign = "None decoded";

    [ObservableProperty]
    private string freedvLastDecodedSpeechPath = "No decoded speech capture yet.";

    [ObservableProperty]
    private string freedvFieldNotes = "For live RADEV1: tune USB-D near the signal, Start RX, then adjust RX offset only if level is present but sync will not lock.";

    [ObservableProperty]
    private bool isFreedvTransmitting;

    [ObservableProperty]
    private IReadOnlyList<string> wsjtxModeOptions = WsjtxModeCatalog.GetOperatorModeLabels();

    [ObservableProperty]
    private string wsjtxSelectedMode = "FT8";

    [ObservableProperty]
    private IReadOnlyList<string> wsjtxFrequencyOptions = WsjtxModeCatalog.GetFrequencyLabels("FT8");

    [ObservableProperty]
    private string wsjtxSelectedFrequency = WsjtxModeCatalog.GetDefaultFrequencyLabel("FT8");

    private int wsjtxDirectedAlertTestSequence;

    [ObservableProperty]
    private IReadOnlyList<string> js8ModeOptions = WsjtxModeCatalog.GetJs8ModeLabels();

    [ObservableProperty]
    private string js8SelectedMode = "JS8 Normal";

    [ObservableProperty]
    private IReadOnlyList<string> js8FrequencyOptions = WsjtxModeCatalog.GetFrequencyLabels("JS8 Normal");

    [ObservableProperty]
    private string js8SelectedFrequency = WsjtxModeCatalog.GetDefaultFrequencyLabel("JS8 Normal");

    [ObservableProperty]
    private ObservableCollection<WsjtxSuggestedMessageItem> js8SuggestedMessages = [];

    [ObservableProperty]
    private WsjtxSuggestedMessageItem? selectedJs8SuggestedMessage;

    [ObservableProperty]
    private string js8TargetCallsign = string.Empty;

    [ObservableProperty]
    private string js8ComposeText = string.Empty;

    [ObservableProperty]
    private string js8ComposeStatus = "Receive and compose staging are ready.";

    [ObservableProperty]
    private bool wsjtxAutoSequenceEnabled = true;

    [ObservableProperty]
    private IReadOnlyList<WsjtxReplyAutomationModeItem> wsjtxReplyAutomationModeOptions =
    [
        new("manual", "Manual", "Suggest the next reply, but do not stage or ready it automatically."),
        new("stage", "Auto Stage Only", "Auto-select and stage the next reply for the active weak-signal conversation lane."),
        new("ready", "Auto Ready Next", "Auto-stage, prepare, and arm the next reply for the locked weak-signal conversation lane."),
    ];

    [ObservableProperty]
    private WsjtxReplyAutomationModeItem selectedWsjtxReplyAutomationMode = new("stage", "Auto Stage Only", "Auto-select and stage the next reply for the active weak-signal conversation lane.");

    [ObservableProperty]
    private string wsjtxRxStatus = "WSJT-style digital receiver ready";

    [ObservableProperty]
    private string wsjtxClockStatus = "Checking system clock discipline...";

    [ObservableProperty]
    private string wsjtxCycleDisplay = "FT8  |  15.0s cycle  |  Next --.-s";

    [ObservableProperty]
    private string wsjtxSessionNotes = "WSJT-X weak-signal operating desk ready.";

    [ObservableProperty]
    private ObservableCollection<WsjtxMessageItem> wsjtxMessages = [];

    [ObservableProperty]
    private ObservableCollection<WsjtxMessageItem> wsjtxRxFrequencyMessages = [];

    [ObservableProperty]
    private ObservableCollection<WsjtxMessageItem> wsjtxConversationMessages = [];

    [ObservableProperty]
    private WsjtxMessageItem? selectedWsjtxMessage;

    [ObservableProperty]
    private int wsjtxRxAudioFrequencyHz = 1500;

    [ObservableProperty]
    private int wsjtxTxAudioFrequencyHz = 1500;

    [ObservableProperty]
    private bool wsjtxHoldTxFrequency;

    [ObservableProperty]
    private ObservableCollection<WsjtxSuggestedMessageItem> wsjtxSuggestedMessages = [];

    [ObservableProperty]
    private ObservableCollection<LongwaveSpotSummaryItem> longwavePotaSpots = [];

    [ObservableProperty]
    private LongwaveSpotSummaryItem? selectedLongwavePotaSpot;

    [ObservableProperty]
    private ObservableCollection<LongwaveSpotSummaryItem> voiceLongwavePotaSpots = [];

    [ObservableProperty]
    private LongwaveSpotSummaryItem? selectedVoiceLongwavePotaSpot;

    [ObservableProperty]
    private IReadOnlyList<string> voiceLongwaveBandFilterOptions =
    [
        "All bands",
        "160m",
        "80m",
        "40m",
        "30m",
        "20m",
        "17m",
        "15m",
        "12m",
        "10m",
        "6m",
    ];

    [ObservableProperty]
    private string selectedVoiceLongwaveBandFilter = "All bands";

    [ObservableProperty]
    private string voicePotaSpotComment = string.Empty;

    [ObservableProperty]
    private string voicePotaSpotCallsign = string.Empty;

    [ObservableProperty]
    private string voicePotaSpotParkReference = string.Empty;

    [ObservableProperty]
    private string voicePotaSpotFrequencyKhz = "14286.0";

    [ObservableProperty]
    private string voicePotaSpotMode = "SSB";

    [ObservableProperty]
    private ObservableCollection<LongwaveLogbookItem> longwaveLogbooks = [];

    [ObservableProperty]
    private LongwaveLogbookItem? selectedLongwaveLogbook;

    [ObservableProperty]
    private ObservableCollection<LongwaveRecentContactItem> longwaveRecentContacts = [];

    [ObservableProperty]
    private LongwaveRecentContactItem? selectedLongwaveRecentContact;

    [ObservableProperty]
    private string longwaveNewLogbookName = string.Empty;

    [ObservableProperty]
    private string longwaveSelectedLogbookName = string.Empty;

    [ObservableProperty]
    private string longwaveSelectedLogbookOperatorCallsign = string.Empty;

    [ObservableProperty]
    private string longwaveSelectedLogbookParkReference = string.Empty;

    [ObservableProperty]
    private string longwaveSelectedLogbookActivationDate = string.Empty;

    [ObservableProperty]
    private string longwaveSelectedLogbookNotes = string.Empty;

    [ObservableProperty]
    private bool isLongwaveBusy;

    private bool _longwaveEnsureLoadInFlight;

    [ObservableProperty]
    private string longwaveStatus = "Longwave integration disabled.";

    [ObservableProperty]
    private string longwaveOperatorSummary = "Longwave integration disabled.";

    [ObservableProperty]
    private string longwaveLogStatus = "Ready to log from rig or selected spot.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingLongwaveContact))]
    [NotifyPropertyChangedFor(nameof(IsCreatingLongwaveContact))]
    [NotifyPropertyChangedFor(nameof(LongwaveContactEditorSummary))]
    private string longwaveEditingContactId = string.Empty;

    public bool IsEditingLongwaveContact => !string.IsNullOrWhiteSpace(LongwaveEditingContactId);
    public bool IsCreatingLongwaveContact => !IsEditingLongwaveContact;

    public string LongwaveContactEditorSummary => IsEditingLongwaveContact
        ? $"Editing {LongwaveLogCallsign} from {LongwaveLogQsoDate} {LongwaveLogTimeOn} UTC."
        : "New contact. Use the rig, a spot, or the fields below.";

    public string LongwaveQuickLogSummary
    {
        get
        {
            var station = string.IsNullOrWhiteSpace(LongwaveLogCallsign)
                ? "no station selected"
                : FormatCallsign(LongwaveLogCallsign);
            var logbook = SelectedLongwaveLogbook?.Name ?? "default logbook";
            var mode = string.IsNullOrWhiteSpace(LongwaveLogMode) ? "mode ?" : LongwaveLogMode.Trim().ToUpperInvariant();
            var band = string.IsNullOrWhiteSpace(LongwaveLogBand) ? "band ?" : LongwaveLogBand.Trim();
            var frequency = double.TryParse(LongwaveLogFrequencyKhz, out var khz) && khz > 0
                ? $"{khz / 1000d:0.000000} MHz"
                : "freq ?";
            var report = string.IsNullOrWhiteSpace(LongwaveLogRstSent) && string.IsNullOrWhiteSpace(LongwaveLogRstReceived)
                ? "report --/--"
                : $"report {LongwaveLogRstSent}/{LongwaveLogRstReceived}";

            return $"Will log {station} | {band} {mode} | {frequency} | {report} | {logbook}";
        }
    }

    public string LongwaveSelectedLogbookQrzUploadSummary
    {
        get
        {
            if (SelectedLongwaveLogbook is null)
            {
                return "Select a logbook to see QRZ upload state.";
            }

            var uploaded = LongwaveRecentContacts.Count(static contact =>
                string.Equals(contact.QrzUploadStatus, "Y", StringComparison.OrdinalIgnoreCase));
            var pending = Math.Max(0, LongwaveRecentContacts.Count - uploaded);
            return $"{SelectedLongwaveLogbook.Name}: {pending} QRZ pending, {uploaded} uploaded.";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogOperatorCallsign = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveContactEditorSummary))]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogCallsign = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveContactEditorSummary))]
    private string longwaveLogQsoDate = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveContactEditorSummary))]
    private string longwaveLogTimeOn = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogMode = "SSB";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogBand = "20m";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogFrequencyKhz = "14074.0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogRstSent = "59";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongwaveQuickLogSummary))]
    private string longwaveLogRstReceived = "59";

    [ObservableProperty]
    private string longwaveLogParkReference = string.Empty;

    [ObservableProperty]
    private string longwaveLogGridSquare = string.Empty;

    [ObservableProperty]
    private string longwaveLogName = string.Empty;

    [ObservableProperty]
    private string longwaveLogQth = string.Empty;

    [ObservableProperty]
    private string longwaveLogCounty = string.Empty;

    [ObservableProperty]
    private string longwaveLogState = string.Empty;

    [ObservableProperty]
    private string longwaveLogCountry = string.Empty;

    [ObservableProperty]
    private string longwaveLogDxcc = string.Empty;

    private double? _longwaveLogLatitude;
    private double? _longwaveLogLongitude;

    [ObservableProperty]
    private WsjtxSuggestedMessageItem? selectedWsjtxSuggestedMessage;

    [ObservableProperty]
    private WsjtxSuggestedMessageItem? wsjtxQueuedTransmitMessage;

    [ObservableProperty]
    private string wsjtxManualTransmitText = string.Empty;

    [ObservableProperty]
    private WsjtxPreparedTransmit? wsjtxPreparedTransmit;

    [ObservableProperty]
    private string wsjtxPreparedTransmitStatus = "No TX signal prepared.";

    [ObservableProperty]
    private string wsjtxPreparedTransmitPath = "No prepared TX artifact.";

    [ObservableProperty]
    private bool wsjtxTransmitArmedLocal;

    [ObservableProperty]
    private string wsjtxTransmitArmStatus = "Nothing armed.";

    [ObservableProperty]
    private double wsjtxSecondsToNextCycle = double.NaN;

    [ObservableProperty]
    private double wsjtxCycleLengthSeconds = 15.0;

    [ObservableProperty]
    private bool wsjtxAwaitingReply;


    private double _lastObservedWsjtxSecondsToNextCycle = double.NaN;
    private bool _isSettingWsjtxManualTransmitText;
    private bool _wsjtxManualTransmitOverride;

    [ObservableProperty]
    private WsjtxActiveSession? wsjtxActiveSession;

    [ObservableProperty]
    private bool wsjtxCallingCq;

    [ObservableProperty]
    private string wsjtxOperatorCallsign = string.Empty;

    [ObservableProperty]
    private string wsjtxOperatorGridSquare = string.Empty;

    [ObservableProperty]
    private string wsjtxCurrentQsoCallsign = "No active QSO";

    [ObservableProperty]
    private string wsjtxCurrentQsoLookupSummary = "Start or track a contact to see a quick QRZ summary here.";

    [ObservableProperty]
    private string wsjtxCurrentQsoLookupDetails = string.Empty;

    [ObservableProperty]
    private string wsjtxCurrentQsoLookupStatus = "Idle";

    private int _wsjtxLookupGeneration;

    public bool WsjtxHasMessages => WsjtxMessages.Count > 0;

    public bool WsjtxHasRxFrequencyMessages => WsjtxRxFrequencyMessages.Count > 0;

    public bool WsjtxHasConversationMessages => WsjtxConversationMessages.Count > 0;

    public string WsjtxSelectedMessageText => SelectedWsjtxMessage?.MessageText ?? "No decodes yet.";

    public string Js8SelectedDecodeSummary => SelectedWsjtxMessage is null || !SelectedWsjtxMessage.ModeText.StartsWith("JS8", StringComparison.OrdinalIgnoreCase)
        ? "Select a JS8 decode to target a reply."
        : $"{SelectedWsjtxMessage.MessageText}  |  {SelectedWsjtxMessage.SnrText}  |  {SelectedWsjtxMessage.HzText}";

    public string Js8TargetSummary => string.IsNullOrWhiteSpace(Js8TargetCallsign)
        ? "No JS8 target selected."
        : $"Target: {Js8TargetCallsign}";

    public string Js8TransmitReadiness => WsjtxPreparedTransmit is not null && WsjtxPreparedTransmit.ModeLabel.StartsWith("JS8", StringComparison.OrdinalIgnoreCase)
        ? WsjtxTransmitArmSummary
        : "JS8 TX generation is wired for short Varicode/Huffman text frames; prepare before arming live PTT.";

    public string WsjtxRxFrequencyTitle => IsWsprMode(WsjtxSelectedMode)
        ? "WSPR Spots"
        : $"Rx Frequency ({WsjtxRxAudioFrequencyHz:+0;-0;0} Hz)";

    public string WsjtxTxFrequencyTitle => $"Tx Offset ({WsjtxTxAudioFrequencyHz:+0;-0;0} Hz)";

    public string WsjtxRxTrackStatus => IsWsprMode(WsjtxSelectedMode)
        ? "WSPR reports RF spot frequency; audio-offset tracking is not used."
        : SelectedWsjtxMessage is null
        ? "Select a decode to track its audio offset."
        : $"Selected offset {SelectedWsjtxMessage.FrequencyOffsetHz:+0;-0;0} Hz";

    public string WsjtxTxTrackStatus => WsjtxHoldTxFrequency
        ? $"TX held at {WsjtxTxAudioFrequencyHz:+0;-0;0} Hz"
        : $"TX follows RX track ({WsjtxTxAudioFrequencyHz:+0;-0;0} Hz)";

    public string WsjtxSuggestedMessagePreview => SelectedWsjtxSuggestedMessage?.MessageText ?? "No reply or CQ selected.";

    public string WsjtxEffectiveTransmitText => NormalizeWsjtxTransmitText(WsjtxManualTransmitText)
        ?? WsjtxQueuedTransmitMessage?.MessageText
        ?? string.Empty;

    public string WsjtxQueuedTransmitPreview => string.IsNullOrWhiteSpace(WsjtxEffectiveTransmitText)
        ? "No TX message staged."
        : WsjtxEffectiveTransmitText;

    public string WsjtxPreparedTransmitSummary => WsjtxPreparedTransmitStatus;

    public string WsjtxReplyAutomationSummary => SelectedWsjtxReplyAutomationMode.Summary;

    public bool WsjtxSelectedModeSupportsQso => IsWsjtxQsoMode(WsjtxSelectedMode);

    public string WsjtxLongwaveLogPreview
    {
        get
        {
            if (!TryBuildWsjtxLongwaveLogContext(out var context))
            {
                return "Log target: no active/tracked QSO yet.";
            }

            var logbook = SelectedLongwaveLogbook?.Name ?? "default Longwave logbook";
            var band = DeriveBandFromFrequencyKhz(CurrentFrequencyHz / 1000d);
            var mode = WsjtxSelectedMode.Trim().ToUpperInvariant();
            return $"Will log: {context.OperatorCallsign} worked {context.StationCallsign}  |  {band} {mode}  |  {CurrentFrequencyHz / 1_000_000d:0.000000} MHz  |  {logbook}";
        }
    }

    public string WsjtxLongwaveLogDetail
    {
        get
        {
            if (!TryBuildWsjtxLongwaveLogContext(out var context))
            {
                return "Start or track a QSO first. If no QSO is active, select a decode row with a callsign.";
            }

            var report = context.SignalReportDb.ToString("+#;-#;0");
            return $"UTC date/time is taken at click time. Report fields will be {report}/{report}.";
        }
    }

    public string WsjtxTransmitArmSummary
    {
        get
        {
            if (WsjtxTransmitArmedLocal)
            {
                var countdown = double.IsFinite(WsjtxSecondsToNextCycle)
                    ? $"next slot in {WsjtxSecondsToNextCycle:0.0}s"
                    : "next slot timing unavailable";
                return $"ARMED  |  {countdown}  |  {WsjtxTransmitArmStatus}";
            }

            if (WsjtxAwaitingReply)
            {
                var countdown = double.IsFinite(WsjtxSecondsToNextCycle)
                    ? $"Next slot {WsjtxSecondsToNextCycle:0.0}s away"
                    : "Waiting for cycle timing";
                return $"WAITING  |  {countdown}  |  {WsjtxTransmitArmStatus}";
            }

            if (WsjtxPreparedTransmit is not null)
            {
                var countdown = double.IsFinite(WsjtxSecondsToNextCycle)
                    ? $"Next slot {WsjtxSecondsToNextCycle:0.0}s away"
                    : "Waiting for cycle timing";
                return $"READY  |  {countdown}  |  {WsjtxTransmitArmStatus}";
            }

            return WsjtxTransmitArmStatus;
        }
    }

    public string WsjtxTransmitPlanSummary =>
        $"TX {WsjtxTxAudioFrequencyHz:+0;-0;0} Hz  |  RX {WsjtxRxAudioFrequencyHz:+0;-0;0} Hz  |  {(WsjtxHoldTxFrequency ? "Hold TX" : "TX follows RX")}";

    public string WsjtxQsoRailSummary
    {
        get
        {
            var parts = new List<string>();

            if (WsjtxCallingCq && WsjtxActiveSession is null)
            {
                parts.Add("Calling CQ");
            }
            else if (WsjtxActiveSession is not null)
            {
                parts.Add(WsjtxAwaitingReply
                    ? $"Waiting for {WsjtxActiveSession.OtherCall}"
                    : $"Reply from {WsjtxActiveSession.OtherCall}");
            }
            else
            {
                parts.Add("No active QSO");
            }

            if (!WsjtxAwaitingReply && WsjtxQueuedTransmitMessage is not null)
            {
                parts.Add($"Next: {WsjtxQueuedTransmitMessage.MessageText}");
            }

            if (WsjtxTransmitArmedLocal)
            {
                parts.Add("Ready for next slot");
            }
            else if (WsjtxPreparedTransmit is not null)
            {
                parts.Add("Prepared");
            }

            return string.Join("  |  ", parts);
        }
    }

    public string WsjtxConversationStatus => WsjtxHasConversationMessages
        ? "Current exchange"
        : "No active conversation yet.";

    public string WsjtxActiveSessionSummary => WsjtxActiveSession is null
        ? (WsjtxCallingCq ? "Calling CQ and waiting for a reply." : "No active weak-signal contact.")
        : (WsjtxAwaitingReply
            ? $"Waiting for {WsjtxActiveSession.OtherCall} on {WsjtxActiveSession.FrequencyOffsetHz:+0;-0;0} Hz"
            : $"Working {WsjtxActiveSession.OtherCall} on {WsjtxActiveSession.FrequencyOffsetHz:+0;-0;0} Hz");

    [ObservableProperty]
    private IReadOnlyList<string> sstvModeOptions =
    [
        "Auto Detect",
        "Lock Martin M1",
        "Lock Martin M2",
        "Lock Scottie 1",
        "Lock Scottie 2",
        "Lock Robot 36",
        "Lock PD 120"
    ];

    [ObservableProperty]
    private string sstvSelectedMode = "Auto Detect";

    [ObservableProperty]
    private IReadOnlyList<string> sstvFrequencyOptions =
    [
        "14.230 MHz USB-D",
        "14.233 MHz USB-D",
        "7.171 MHz LSB-D",
        "3.845 MHz LSB-D"
    ];

    [ObservableProperty]
    private string sstvSelectedFrequency = "14.230 MHz USB-D";

    [ObservableProperty]
    private string sstvRxStatus = "SSTV receiver ready";

    [ObservableProperty]
    private string sstvImageStatus = "No image captured yet";

    [ObservableProperty]
    private string sstvSessionNotes = SstvIdleSessionNotes;

    [ObservableProperty]
    private string sstvDecodedFskIdCallsign = string.Empty;

    [ObservableProperty]
    private Bitmap? sstvPreviewBitmap;

    [ObservableProperty]
    private bool sstvHasPreview;

    public bool SstvShowPlaceholder => !SstvHasPreview;

    [ObservableProperty]
    private ObservableCollection<SstvImageItem> sstvReceivedImages = [];

    [ObservableProperty]
    private SstvImageItem? selectedSstvReceivedImage;

    [ObservableProperty]
    private ObservableCollection<SstvImageItem> sstvReplyImages = [];

    [ObservableProperty]
    private SstvImageItem? selectedSstvReplyBaseImage;

    [ObservableProperty]
    private ObservableCollection<SstvOverlayItemViewModel> sstvReplyOverlayItems = [];

    [ObservableProperty]
    private SstvOverlayItemViewModel? selectedSstvReplyOverlayItem;

    [ObservableProperty]
    private ObservableCollection<SstvImageOverlayItemViewModel> sstvReplyImageOverlayItems = [];

    [ObservableProperty]
    private SstvImageOverlayItemViewModel? selectedSstvReplyImageOverlayItem;

    [ObservableProperty]
    private ObservableCollection<SstvTemplateItem> sstvReplyLayoutTemplates = [];

    [ObservableProperty]
    private SstvTemplateItem? selectedSstvReplyLayoutTemplate;

    [ObservableProperty]
    private string sstvReplyTemplateName = "Default Reply";

    [ObservableProperty]
    private string sstvReplyTemplateStatus = "Save a layout template to reuse overlay positioning later.";

    [ObservableProperty]
    private string? sstvReplyPresetKind;

    [ObservableProperty]
    private IReadOnlyList<SstvReplyLayoutPreset> sstvReplyLayoutPresets =
    [
        new("CQ Card", "cq"),
        new("QSL + RX", "qsl-rx"),
        new("Signal Report", "report"),
        new("TNX / 73", "73"),
        new("Station ID", "id"),
    ];

    [ObservableProperty]
    private IReadOnlyList<string> sstvReplyTemplates =
    [
        "CQ SSTV DE %m",
        "%m 599 %tocall",
        "QSL SSTV - TNX DE %m",
        "SSTV REPORT: RSV 595",
        "TNX QSO - 73 DE %m",
        "%m %g",
    ];

    [ObservableProperty]
    private IReadOnlyList<string> sstvTxModeHints =
    [
        "Martin/Scottie: common live QSOs and repeaters.",
        "Robot: short, robust exchanges when time matters.",
        "PD: higher-detail picture modes; slower but clean.",
    ];

    [ObservableProperty]
    private IReadOnlyList<string> sstvTxModeOptions =
    [
        "Martin 1",
        "Martin 2",
        "Scottie 1",
        "Scottie 2",
        "Scottie DX",
        "Robot 24",
        "Robot 36",
        "Robot 72",
        "PD 50",
        "PD 90",
        "PD 120",
        "PD 160",
        "PD 180",
        "PD 240",
        "PD 290",
    ];

    [ObservableProperty]
    private string sstvSelectedTxMode = "Martin 1";

    [ObservableProperty]
    private bool sstvTxCwIdEnabled;

    [ObservableProperty]
    private bool sstvTxFskIdEnabled = true;

    [ObservableProperty]
    private string sstvTxCwIdText = "DE %m";

    [ObservableProperty]
    private int sstvTxCwIdFrequencyHz = 1000;

    [ObservableProperty]
    private int sstvTxCwIdWpm = 28;

    [ObservableProperty]
    private string sstvTransmitStatus = "Prepare a reply image to stage SSTV TX.";

    [ObservableProperty]
    private string sstvPreparedTransmitPath = "No prepared SSTV TX artifact.";

    [ObservableProperty]
    private Bitmap? sstvPreparedTransmitBitmap;

    [ObservableProperty]
    private string sstvPreparedTransmitImagePath = "No prepared TX image.";

    [ObservableProperty]
    private string sstvPreparedTransmitSummary = "No prepared SSTV TX image/audio.";

    [ObservableProperty]
    private bool sstvTxIsSending;

    public bool SstvHasPreparedTransmitPreview => SstvPreparedTransmitBitmap is not null;

    public bool SstvHasPreparedTransmitClip => _sstvPreparedTransmitClip is not null;

    public Bitmap? SstvSelectedReceivedBitmap => SelectedSstvReceivedImage?.Bitmap;

    public string SstvSelectedReceivedPath => SelectedSstvReceivedImage?.Path ?? "No saved image selected";

    public Bitmap? SstvReplyPreviewBitmap => SelectedSstvReplyBaseImage?.Bitmap;

    public double SstvReplyCanvasWidth => SelectedSstvReplyBaseImage?.Bitmap.PixelSize.Width ?? 320;

    public double SstvReplyCanvasHeight => SelectedSstvReplyBaseImage?.Bitmap.PixelSize.Height ?? 256;

    public bool SstvReplyHasBaseImage => SelectedSstvReplyBaseImage is not null;

    public bool SstvReplyShowPlaceholder => !SstvReplyHasBaseImage;

    public string SstvReceivedFolderPath => _sstvReceivedDirectory;

    public string SstvReplyFolderPath => _sstvReplyDirectory;

    public string SstvTemplateFolderPath => _sstvTemplateDirectory;

    public IReadOnlyList<string> SstvReplyFontOptions => ["Segoe UI", "Arial", "Consolas", "Georgia", "Tahoma", "Verdana"];

    [ObservableProperty]
    private IReadOnlyList<string> wefaxModeOptions =
    [
        "IOC 576 / 120 LPM",
        "IOC 576 / 90 LPM",
        "IOC 576 / 60 LPM",
        "IOC 288 / 120 LPM",
    ];

    [ObservableProperty]
    private string wefaxSelectedMode = "IOC 576 / 120 LPM";

    [ObservableProperty]
    private IReadOnlyList<string> wefaxFrequencyOptions =
    [
        "NOAA Atlantic 4235.0 kHz USB-D",
        "NOAA Atlantic 6340.5 kHz USB-D",
        "NOAA Atlantic 9110.0 kHz USB-D",
        "NOAA Atlantic 12750.0 kHz USB-D",
        "NOAA Pacific 4346.0 kHz USB-D",
        "NOAA Pacific 8682.0 kHz USB-D",
        "NOAA Pacific 12786.0 kHz USB-D",
        "NOAA Pacific 17151.2 kHz USB-D",
        "NOAA Gulf 4317.9 kHz USB-D",
        "NOAA Gulf 8503.9 kHz USB-D",
        "NOAA Gulf 12789.9 kHz USB-D",
        "NOAA Hawaii 9982.5 kHz USB-D",
        "NOAA Hawaii 11090.0 kHz USB-D",
        "NOAA Kodiak 8459.0 kHz USB-D",
        "NOAA Kodiak 12412.5 kHz USB-D",
    ];

    [ObservableProperty]
    private string wefaxSelectedFrequency = "NOAA Atlantic 12750.0 kHz USB-D";

    [ObservableProperty]
    private ObservableCollection<WefaxScheduleItem> wefaxScheduleItems = [];

    [ObservableProperty]
    private WefaxScheduleItem? selectedWefaxScheduleItem;

    [ObservableProperty]
    private string wefaxScheduleStatus = "NOAA radiofax schedule loaded from built-in UTC table.";

    [ObservableProperty]
    private int wefaxManualSlant;

    [ObservableProperty]
    private int wefaxManualOffset;

    [ObservableProperty]
    private int wefaxCenterHz = 1900;

    [ObservableProperty]
    private int wefaxShiftHz = 800;

    [ObservableProperty]
    private int wefaxMaxRows = 1500;

    [ObservableProperty]
    private IReadOnlyList<string> wefaxFilterOptions = ["Narrow", "Medium", "Wide"];

    [ObservableProperty]
    private string wefaxSelectedFilter = "Medium";

    [ObservableProperty]
    private bool wefaxAutoAlign = false;

    [ObservableProperty]
    private int wefaxAutoAlignAfterRows = 30;

    [ObservableProperty]
    private int wefaxAutoAlignEveryRows = 10;

    [ObservableProperty]
    private int wefaxAutoAlignStopRows = 500;

    [ObservableProperty]
    private double wefaxCorrelationThreshold = 0.05;

    [ObservableProperty]
    private int wefaxCorrelationRows = 15;

    [ObservableProperty]
    private bool wefaxInvertImage;

    [ObservableProperty]
    private bool wefaxBinaryImage;

    [ObservableProperty]
    private int wefaxBinaryThreshold = 128;

    [ObservableProperty]
    private bool wefaxNoiseRemoval;

    [ObservableProperty]
    private int wefaxNoiseThreshold = 24;

    [ObservableProperty]
    private int wefaxNoiseMargin = 1;

    [ObservableProperty]
    private string wefaxRxStatus = "WeFAX receiver ready";

    [ObservableProperty]
    private string wefaxImageStatus = "No WeFAX image captured yet";

    [ObservableProperty]
    private string wefaxSessionNotes = "Live auto-align is off by default because real maps can fool seam tracking. Use Start RX for scheduled captures, or Start Now if you joined late.";

    [ObservableProperty]
    private Bitmap? wefaxPreviewBitmap;

    [ObservableProperty]
    private bool wefaxHasPreview;

    public bool WefaxShowPlaceholder => !WefaxHasPreview;

    [ObservableProperty]
    private ObservableCollection<WefaxImageItem> wefaxReceivedImages = [];

    [ObservableProperty]
    private WefaxImageItem? selectedWefaxReceivedImage;

    public Bitmap? WefaxSelectedReceivedBitmap => SelectedWefaxReceivedImage?.Bitmap;

    public string WefaxSelectedReceivedPath => SelectedWefaxReceivedImage?.Path ?? "No saved WeFAX image selected";

    public string WefaxReceivedFolderPath => _wefaxReceivedDirectory;

    [ObservableProperty]
    private string cwSendText = string.Empty;

    [ObservableProperty]
    private string cwTxStatus = "CW TX idle";

    [ObservableProperty]
    private bool isCwDecoderRunning;

    [ObservableProperty]
    private bool isCwSending;

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

    public string SettingsPath => _settingsPath;

    [RelayCommand]
    private void ShowOperating() => CurrentWorkspace = WorkspaceKind.Operating;

    [RelayCommand]
    private void ShowSettings() => CurrentWorkspace = WorkspaceKind.Settings;

    [RelayCommand]
    private void ShowDiagnostics() => CurrentWorkspace = WorkspaceKind.Diagnostics;

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (_settingsStore is null)
        {
            SettingsStatusMessage = "Settings store unavailable";
            return;
        }

        if (!int.TryParse(SettingsCivBaud, out var civBaud)
            || !TryParseCivAddress(SettingsCivAddress, out var civAddress)
            || !int.TryParse(SettingsFlrigPort, out var flrigPort))
        {
            SettingsStatusMessage = "Enter valid values for baud, address (94h / 0x94 / 148), and FLRig port";
            return;
        }

        var updated = _settings with
        {
            Station = _settings.Station with
            {
                Callsign = SettingsCallsign.Trim().ToUpperInvariant(),
                GridSquare = SettingsGridSquare.Trim().ToUpperInvariant(),
            },
            Radio = _settings.Radio with
            {
                ControlBackend = SettingsRadioBackend.Trim(),
                CivPort = SettingsCivPort.Trim(),
                CivBaud = civBaud,
                CivAddress = civAddress,
            },
            Audio = _settings.Audio with
            {
                RxDeviceId = SelectedRxDevice?.DeviceId ?? string.Empty,
                TxDeviceId = SelectedTxDevice?.DeviceId ?? string.Empty,
                MicDeviceId = SelectedMicDevice?.DeviceId ?? string.Empty,
                MonitorDeviceId = SelectedMonitorDevice?.DeviceId ?? string.Empty,
                FreedvMonitorDeviceId = SelectedFreedvMonitorDevice?.DeviceId ?? string.Empty,
                MonitorVolumePercent = MonitorVolumePercent,
                FreedvMonitorVolumePercent = FreedvSpeechVolumePercent,
                MicGainPercent = VoiceMicGainPercent,
                VoiceCompressionPercent = VoiceCompressionPercent,
            },
            Interop = _settings.Interop with
            {
                FlrigEnabled = SettingsFlrigEnabled,
                FlrigHost = SettingsFlrigHost.Trim(),
                FlrigPort = flrigPort,
            },
            Longwave = _settings.Longwave with
            {
                Enabled = SettingsLongwaveEnabled,
                BaseUrl = SettingsLongwaveBaseUrl.Trim(),
                ClientApiToken = SettingsLongwaveClientApiToken.Trim(),
                DefaultLogbookName = SettingsLongwaveDefaultLogbookName.Trim(),
                DefaultLogbookNotes = SettingsLongwaveDefaultLogbookNotes.Trim(),
            },
            Ui = _settings.Ui with
            {
                Theme = SettingsTheme.Trim(),
                BandConditionsEnabled = SettingsBandConditionsEnabled,
                ShowExperimentalCw = SettingsShowExperimentalCw,
                WaterfallFloorPercent = WaterfallFloorPercent,
                WaterfallCeilingPercent = WaterfallCeilingPercent,
            }
        };

        await _settingsStore.SaveAsync(updated, CancellationToken.None);
        ApplySettings(updated);
        SettingsStatusMessage = "Saved. Radio changes apply on next connect.";
    }

    [RelayCommand]
    private async Task ReloadSettingsAsync()
    {
        if (_settingsStore is null)
        {
            SettingsStatusMessage = "Settings store unavailable";
            return;
        }

        var reloaded = await _settingsStore.LoadAsync(CancellationToken.None);
        ApplySettings(reloaded);
        SettingsStatusMessage = "Reloaded from disk";
    }

    [RelayCommand]
    private async Task RefreshLongwaveSpotsAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            LongwaveStatus = "Refreshing POTA spots...";
            var settings = BuildCurrentLongwaveSettings();
            var context = await _longwaveService.GetOperatorContextAsync(settings, CancellationToken.None);
            var logbooks = await _longwaveService.GetLogbooksAsync(settings, CancellationToken.None);
            var spots = await _longwaveService.GetPotaSpotsAsync(settings, CancellationToken.None);
            LongwaveOperatorSummary = $"Longwave operator {context.Callsign}  |  {spots.Count} POTA spots loaded";
            LongwaveLogbooks = new ObservableCollection<LongwaveLogbookItem>(
                logbooks.Select(ToLongwaveLogbookItem));
            SelectedLongwaveLogbook = SelectPreferredLongwaveLogbook(LongwaveLogbooks, SelectedLongwaveLogbook, settings.DefaultLogbookName);
            var contacts = await _longwaveService.GetContactsAsync(settings, SelectedLongwaveLogbook?.Id, CancellationToken.None);
            LongwavePotaSpots = new ObservableCollection<LongwaveSpotSummaryItem>(
                spots.Select(spot => ToLongwaveSpotSummaryItem(spot, _longwaveLoggedContactKeys.Contains(spot.Id))));
            RebuildVoiceLongwavePotaSpots();
            LongwaveRecentContacts = new ObservableCollection<LongwaveRecentContactItem>(
                contacts.Take(50).Select(ToLongwaveRecentContactItem));
            LongwaveStatus = spots.Count == 0
                ? "No POTA spots returned from Longwave."
                : $"Loaded {spots.Count} POTA spots from Longwave.";
            if (SelectedLongwaveLogbook is not null)
            {
                LongwaveLogStatus = $"Using Longwave logbook {SelectedLongwaveLogbook.Name}.";
            }
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    public async Task EnsureLongwaveDataLoadedAsync()
    {
        if (_longwaveService is null
            || _longwaveEnsureLoadInFlight
            || IsLongwaveBusy
            || LongwaveLogbooks.Count > 0)
        {
            return;
        }

        var settings = BuildCurrentLongwaveSettings();
        if (!settings.Enabled
            || string.IsNullOrWhiteSpace(settings.BaseUrl)
            || string.IsNullOrWhiteSpace(settings.ClientApiToken))
        {
            return;
        }

        try
        {
            _longwaveEnsureLoadInFlight = true;
            await RefreshLongwaveSpotsAsync();
        }
        finally
        {
            _longwaveEnsureLoadInFlight = false;
        }
    }

    [RelayCommand]
    private async Task TuneSelectedLongwaveSpotAsync()
    {
        if (SelectedLongwavePotaSpot is null)
        {
            LongwaveStatus = "Select a POTA spot first.";
            return;
        }

        await TuneRadioForLongwaveSpotAsync(SelectedLongwavePotaSpot);
    }

    [RelayCommand]
    private async Task TuneSelectedVoiceLongwaveSpotAsync()
    {
        if (SelectedVoiceLongwavePotaSpot is null)
        {
            LongwaveStatus = "Select a voice POTA spot first.";
            return;
        }

        SelectedLongwavePotaSpot = SelectedVoiceLongwavePotaSpot;
        await TuneRadioForLongwaveSpotAsync(SelectedVoiceLongwavePotaSpot);
    }

    [RelayCommand]
    private async Task WorkSelectedVoiceLongwaveSpotAsync()
    {
        if (SelectedVoiceLongwavePotaSpot is null)
        {
            LongwaveStatus = "Select a voice POTA spot first.";
            return;
        }

        SelectedLongwavePotaSpot = SelectedVoiceLongwavePotaSpot;
        ApplySpotToLongwaveLog(SelectedVoiceLongwavePotaSpot);
        await TuneRadioForLongwaveSpotAsync(SelectedVoiceLongwavePotaSpot);
        LongwaveLogStatus = $"Ready to work {SelectedVoiceLongwavePotaSpot.ActivatorCallsign} at {SelectedVoiceLongwavePotaSpot.ParkReference}.";
    }

    [RelayCommand]
    private void UseRigForVoicePotaSpot()
    {
        VoicePotaSpotFrequencyKhz = $"{CurrentFrequencyHz / 1000d:0.0}";
        VoicePotaSpotMode = MapRadioModeToPotaMode(SelectedMode);
        if (string.IsNullOrWhiteSpace(VoicePotaSpotCallsign))
        {
            VoicePotaSpotCallsign = FormatCallsign(LongwaveLogCallsign);
        }

        if (string.IsNullOrWhiteSpace(VoicePotaSpotParkReference))
        {
            VoicePotaSpotParkReference = LongwaveLogParkReference.Trim().ToUpperInvariant();
        }

        LongwaveStatus = $"Spot form filled from rig: {VoicePotaSpotFrequencyKhz} kHz {VoicePotaSpotMode}.";
    }

    [RelayCommand]
    private void UseSelectedSpotForLongwaveLog()
    {
        if (SelectedLongwavePotaSpot is null)
        {
            LongwaveLogStatus = "Select a POTA spot first.";
            return;
        }

        ApplySpotToLongwaveLog(SelectedLongwavePotaSpot);
        LongwaveLogStatus = $"Prefilled log from {SelectedLongwavePotaSpot.ActivatorCallsign} at {SelectedLongwavePotaSpot.ParkReference}.";
    }

    [RelayCommand]
    private void UseRigForLongwaveLog()
    {
        LongwaveLogFrequencyKhz = $"{CurrentFrequencyHz / 1000d:0.0}";
        LongwaveLogBand = DeriveBandFromFrequencyKhz(CurrentFrequencyHz / 1000d);
        LongwaveLogMode = MapRadioModeToLogMode(SelectedMode);
        LongwaveLogOperatorCallsign = FormatCallsign(SettingsCallsign);
        LongwaveLogGridSquare = SettingsGridSquare.Trim().ToUpperInvariant();
        LongwaveLogStatus = $"Prefilled log from rig: {LongwaveLogBand} {LongwaveLogMode} at {LongwaveLogFrequencyKhz} kHz.";
    }

    [RelayCommand]
    private async Task PostVoicePotaSpotAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        var activatorCall = FormatCallsign(VoicePotaSpotCallsign);
        var park = VoicePotaSpotParkReference.Trim().ToUpperInvariant();
        var mode = string.IsNullOrWhiteSpace(VoicePotaSpotMode)
            ? MapRadioModeToPotaMode(SelectedMode)
            : VoicePotaSpotMode.Trim().ToUpperInvariant();
        var spotterCall = FormatCallsign(
            string.IsNullOrWhiteSpace(LongwaveLogOperatorCallsign)
                ? SettingsCallsign
                : LongwaveLogOperatorCallsign);

        if (string.IsNullOrWhiteSpace(activatorCall) || string.IsNullOrWhiteSpace(park))
        {
            LongwaveStatus = "POTA spot needs an activator callsign and park reference.";
            return;
        }

        if (!double.TryParse(VoicePotaSpotFrequencyKhz, out var frequencyKhz) || frequencyKhz <= 0)
        {
            LongwaveStatus = "POTA spot needs a valid frequency in kHz.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            LongwaveStatus = $"Posting POTA spot for {activatorCall}...";
            var band = DeriveBandFromFrequencyKhz(frequencyKhz);
            var created = await _longwaveService.CreatePotaSpotAsync(
                BuildCurrentLongwaveSettings(),
                new LongwavePotaSpotDraft(
                    activatorCall,
                    park,
                    frequencyKhz,
                    mode,
                    band,
                    VoicePotaSpotComment,
                    spotterCall),
                CancellationToken.None);

            var item = ToLongwaveSpotSummaryItem(created, isLogged: false);
            LongwavePotaSpots.Insert(0, item);
            SelectedLongwavePotaSpot = item;
            RebuildVoiceLongwavePotaSpots();
            SelectedVoiceLongwavePotaSpot = VoiceLongwavePotaSpots.FirstOrDefault(spot => spot.Id == item.Id) ?? SelectedVoiceLongwavePotaSpot;
            VoicePotaSpotComment = string.Empty;
            VoicePotaSpotCallsign = string.Empty;
            VoicePotaSpotParkReference = string.Empty;
            LongwaveStatus = $"Posted POTA spot for {created.ActivatorCallsign} at {created.ParkReference}.";
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private void UseRigForCwLongwaveLog()
    {
        UseRigForModeLongwaveLog("CW", "599", "599");
        LongwaveLogStatus = $"Prefilled CW log from rig: {LongwaveLogBand} CW at {LongwaveLogFrequencyKhz} kHz.";
    }

    [RelayCommand]
    private void UseRigForRttyLongwaveLog()
    {
        UseRigForModeLongwaveLog("RTTY", "599", "599");
        LongwaveLogStatus = $"Prefilled RTTY log from rig: {LongwaveLogBand} RTTY at {LongwaveLogFrequencyKhz} kHz.";
    }

    [RelayCommand]
    private void UseRigForKeyboardLongwaveLog()
    {
        var mode = string.IsNullOrWhiteSpace(KeyboardSelectedMode)
            ? "PSK"
            : KeyboardSelectedMode.Trim().ToUpperInvariant();
        UseRigForModeLongwaveLog(mode, "599", "599");
        LongwaveLogStatus = $"Prefilled {LongwaveLogMode} log from rig: {LongwaveLogBand} at {LongwaveLogFrequencyKhz} kHz.";
    }

    [RelayCommand]
    private void UseRigForSstvLongwaveLog()
    {
        UseRigForModeLongwaveLog("SSTV", "595", "595");
        if (!string.IsNullOrWhiteSpace(SstvDecodedFskIdCallsign))
        {
            LongwaveLogCallsign = FormatCallsign(SstvDecodedFskIdCallsign);
        }

        LongwaveLogStatus = $"Prefilled SSTV log from rig: {LongwaveLogBand} SSTV at {LongwaveLogFrequencyKhz} kHz.";
    }

    [RelayCommand]
    private void UseRigForFreedvLongwaveLog()
    {
        UseRigForModeLongwaveLog("FREEDV", "59", "59");
        if (!string.IsNullOrWhiteSpace(FreedvLastRadeCallsign) && !FreedvLastRadeCallsign.StartsWith("None", StringComparison.OrdinalIgnoreCase))
        {
            LongwaveLogCallsign = FormatCallsign(FreedvLastRadeCallsign);
        }

        LongwaveLogStatus = $"Prefilled FreeDV log from rig: {LongwaveLogBand} FreeDV at {LongwaveLogFrequencyKhz} kHz.";
    }

    [RelayCommand]
    private void UseDecodedSstvFskIdForLog()
    {
        var callsign = FormatCallsign(SstvDecodedFskIdCallsign);
        if (string.IsNullOrWhiteSpace(callsign))
        {
            LongwaveLogStatus = "No SSTV FSKID callsign has been decoded yet.";
            return;
        }

        LongwaveLogCallsign = callsign;
        if (string.IsNullOrWhiteSpace(LongwaveLogMode) || string.Equals(LongwaveLogMode, "SSB", StringComparison.OrdinalIgnoreCase))
        {
            UseRigForModeLongwaveLog("SSTV", "595", "595");
            LongwaveLogCallsign = callsign;
        }

        LongwaveLogStatus = $"Using SSTV FSKID {callsign} for the log.";
    }

    [RelayCommand]
    private void UseFreedvRadeCallForLog()
    {
        var callsign = FormatCallsign(FreedvLastRadeCallsign);
        if (string.IsNullOrWhiteSpace(callsign) || callsign.StartsWith("NONE", StringComparison.OrdinalIgnoreCase))
        {
            LongwaveLogStatus = "No FreeDV RADE callsign has been decoded yet.";
            return;
        }

        LongwaveLogCallsign = callsign;
        if (string.IsNullOrWhiteSpace(LongwaveLogMode) || string.Equals(LongwaveLogMode, "SSB", StringComparison.OrdinalIgnoreCase))
        {
            UseRigForModeLongwaveLog("FREEDV", "59", "59");
            LongwaveLogCallsign = callsign;
        }

        LongwaveLogStatus = $"Using FreeDV RADE callsign {callsign} for the log.";
    }

    private void UseRigForModeLongwaveLog(string mode, string sentReport, string receivedReport)
    {
        UseRigForLongwaveLog();
        LongwaveLogMode = mode.Trim().ToUpperInvariant();
        LongwaveLogRstSent = sentReport;
        LongwaveLogRstReceived = receivedReport;
        OnPropertyChanged(nameof(LongwaveQuickLogSummary));
    }

    [RelayCommand]
    private void UseSelectedJs8ForLongwaveLog()
    {
        var selected = SelectedWsjtxMessage;
        if (selected is null || !selected.ModeText.StartsWith("JS8", StringComparison.OrdinalIgnoreCase))
        {
            LongwaveLogStatus = "Select a JS8 decode before preparing a log.";
            return;
        }

        var callsign = FormatCallsign(Js8TargetCallsign);
        if (string.IsNullOrWhiteSpace(callsign))
        {
            callsign = FormatCallsign(TryExtractCallsign(selected.MessageText) ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(callsign))
        {
            LongwaveLogStatus = "Selected JS8 decode does not contain an obvious callsign.";
            return;
        }

        LongwaveLogOperatorCallsign = FormatCallsign(SettingsCallsign);
        LongwaveLogCallsign = callsign;
        LongwaveLogMode = "JS8";
        LongwaveLogBand = DeriveBandFromFrequencyKhz(CurrentFrequencyHz / 1000d);
        LongwaveLogFrequencyKhz = $"{CurrentFrequencyHz / 1000d:0.0}";
        LongwaveLogRstSent = selected.SnrDb.ToString("+#;-#;0");
        LongwaveLogRstReceived = selected.SnrDb.ToString("+#;-#;0");
        LongwaveLogGridSquare = SettingsGridSquare.Trim().ToUpperInvariant();
        LongwaveLogStatus = $"Prepared JS8 log for {callsign} from selected decode at {selected.FrequencyOffsetHz:+0;-0;0} Hz.";
    }

    [RelayCommand]
    private async Task AutofillLongwaveLookupAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveLogStatus = "Longwave service unavailable.";
            return;
        }

        var callsign = FormatCallsign(LongwaveLogCallsign);
        if (string.IsNullOrWhiteSpace(callsign))
        {
            LongwaveLogStatus = "Enter or select a callsign first.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            LongwaveLogStatus = $"Looking up {callsign} via Longwave/QRZ...";
            var lookup = await _longwaveService.LookupCallsignAsync(BuildCurrentLongwaveSettings(), callsign, CancellationToken.None);
            ApplyLongwaveLookup(lookup);
            LongwaveLogStatus = $"Autofilled location for {lookup.Callsign}.";
        }
        catch (Exception ex)
        {
            LongwaveLogStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task LogCurrentQsoAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveLogStatus = "Longwave service unavailable.";
            return;
        }

        if (!double.TryParse(LongwaveLogFrequencyKhz, out var frequencyKhz) || frequencyKhz <= 0)
        {
            LongwaveLogStatus = "Enter a valid frequency in kHz.";
            return;
        }

        var operatorCall = FormatCallsign(LongwaveLogOperatorCallsign);
        var stationCall = FormatCallsign(LongwaveLogCallsign);
        if (string.IsNullOrWhiteSpace(operatorCall) || string.IsNullOrWhiteSpace(stationCall))
        {
            LongwaveLogStatus = "Operator and station callsigns are required.";
            return;
        }

        var qsoTime = GetLongwaveEditorQsoTimeUtc();
        var logKey = BuildLongwaveLogDedupeKey(stationCall, LongwaveLogMode, frequencyKhz, qsoTime);
        if (!_longwaveLoggedContactKeys.Add(logKey))
        {
            LongwaveLogStatus = "This QSO was already logged from this session.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            LongwaveLogStatus = "Posting contact to Longwave...";
            var settings = BuildCurrentLongwaveSettings();
            var logbook = SelectedLongwaveLogbook is not null
                ? new LongwaveLogbook(
                    SelectedLongwaveLogbook.Id,
                    SelectedLongwaveLogbook.Name,
                    SelectedLongwaveLogbook.OperatorCallsign,
                    SelectedLongwaveLogbook.ParkReference,
                    SelectedLongwaveLogbook.ActivationDate,
                    SelectedLongwaveLogbook.Notes,
                    SelectedLongwaveLogbook.ContactCount)
                : await _longwaveService.GetOrCreateLogbookAsync(settings, operatorCall, CancellationToken.None);
            EnsureLongwaveLogbookSelected(logbook);
            var draft = BuildLongwaveContactDraft(logbook.Id, stationCall, operatorCall, frequencyKhz, qsoTime);
            var created = await _longwaveService.CreateContactAsync(
                settings,
                draft,
                CancellationToken.None);

            UpsertLongwaveRecentContact(created);
            LongwaveLogStatus = BuildLongwaveLoggedContactSummary(created, logbook.Name);
            LongwaveStatus = $"Longwave logged {created.StationCallsign} on {created.Band} {created.Mode}.";
            MarkLongwaveSpotLogged(created.SourceSpotId);
            await RefreshLongwaveContactsAsync();
        }
        catch (Exception ex)
        {
            _longwaveLoggedContactKeys.Remove(logKey);
            LongwaveLogStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpdateSelectedLongwaveContactAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveLogStatus = "Longwave service unavailable.";
            return;
        }

        if (SelectedLongwaveRecentContact is null || string.IsNullOrWhiteSpace(LongwaveEditingContactId))
        {
            LongwaveLogStatus = "Select a logged contact before saving changes.";
            return;
        }

        if (!double.TryParse(LongwaveLogFrequencyKhz, out var frequencyKhz) || frequencyKhz <= 0)
        {
            LongwaveLogStatus = "Enter a valid frequency in kHz.";
            return;
        }

        var operatorCall = FormatCallsign(LongwaveLogOperatorCallsign);
        var stationCall = FormatCallsign(LongwaveLogCallsign);
        if (string.IsNullOrWhiteSpace(operatorCall) || string.IsNullOrWhiteSpace(stationCall))
        {
            LongwaveLogStatus = "Operator and station callsigns are required.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            LongwaveLogStatus = $"Saving changes for {stationCall}...";
            var logbookId = SelectedLongwaveLogbook?.Id ?? SelectedLongwaveRecentContact.LogbookId;
            var draft = BuildLongwaveContactDraft(logbookId, stationCall, operatorCall, frequencyKhz, GetLongwaveEditorQsoTimeUtc());
            var updated = await _longwaveService.UpdateContactAsync(
                BuildCurrentLongwaveSettings(),
                LongwaveEditingContactId,
                draft,
                CancellationToken.None);
            UpsertLongwaveRecentContact(updated);
            LongwaveEditingContactId = updated.Id;
            LongwaveLogStatus = BuildLongwaveLoggedContactSummary(updated, SelectedLongwaveLogbook?.Name ?? "Longwave");
            LongwaveStatus = $"Longwave updated {updated.StationCallsign} on {updated.Band} {updated.Mode}.";
            await RefreshLongwaveContactsAsync();
        }
        catch (Exception ex)
        {
            LongwaveLogStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private void NewLongwaveContact()
    {
        SelectedLongwaveRecentContact = null;
        LongwaveEditingContactId = string.Empty;
        LongwaveLogCallsign = string.Empty;
        LongwaveLogQsoDate = string.Empty;
        LongwaveLogTimeOn = string.Empty;
        LongwaveLogParkReference = string.Empty;
        LongwaveLogName = string.Empty;
        LongwaveLogQth = string.Empty;
        LongwaveLogCounty = string.Empty;
        LongwaveLogState = string.Empty;
        LongwaveLogCountry = string.Empty;
        LongwaveLogDxcc = string.Empty;
        _longwaveLogLatitude = null;
        _longwaveLogLongitude = null;
        LongwaveLogStatus = "Ready for a new Longwave contact.";
    }

    [RelayCommand]
    private async Task CreateLongwaveLogbookAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        var name = LongwaveNewLogbookName.Trim();
        var operatorCall = FormatCallsign(LongwaveLogOperatorCallsign);
        if (string.IsNullOrWhiteSpace(name))
        {
            LongwaveStatus = "Enter a logbook name first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(operatorCall))
        {
            LongwaveStatus = "Operator callsign is required to create a logbook.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            var settings = BuildCurrentLongwaveSettings();
            var created = await _longwaveService.CreateLogbookAsync(settings, name, operatorCall, null, CancellationToken.None);
            LongwaveLogbooks.Insert(0, ToLongwaveLogbookItem(created));
            SelectedLongwaveLogbook = LongwaveLogbooks.FirstOrDefault(item => item.Id == created.Id);
            LongwaveNewLogbookName = string.Empty;
            await RefreshLongwaveContactsAsync();
            LongwaveStatus = $"Created logbook {created.Name}.";
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpdateSelectedLongwaveLogbookAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        if (SelectedLongwaveLogbook is null)
        {
            LongwaveStatus = "Select a logbook first.";
            return;
        }

        var name = LongwaveSelectedLogbookName.Trim();
        var operatorCall = FormatCallsign(LongwaveSelectedLogbookOperatorCallsign);
        if (string.IsNullOrWhiteSpace(name))
        {
            LongwaveStatus = "Logbook name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(operatorCall))
        {
            LongwaveStatus = "Logbook operator callsign is required.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            var target = SelectedLongwaveLogbook;
            var updated = await _longwaveService.UpdateLogbookAsync(
                BuildCurrentLongwaveSettings(),
                target.Id,
                name,
                operatorCall,
                LongwaveSelectedLogbookParkReference,
                LongwaveSelectedLogbookActivationDate,
                LongwaveSelectedLogbookNotes,
                CancellationToken.None);
            var updatedItem = ToLongwaveLogbookItem(updated);
            var index = LongwaveLogbooks.IndexOf(target);
            if (index >= 0)
            {
                LongwaveLogbooks[index] = updatedItem;
            }
            else
            {
                LongwaveLogbooks.Insert(0, updatedItem);
            }

            SelectedLongwaveLogbook = updatedItem;
            LongwaveStatus = $"Updated logbook {updated.Name}.";
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedLongwaveLogbookAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        if (SelectedLongwaveLogbook is null)
        {
            LongwaveStatus = "Select a logbook first.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            var target = SelectedLongwaveLogbook;
            await _longwaveService.DeleteLogbookAsync(BuildCurrentLongwaveSettings(), target.Id, CancellationToken.None);
            LongwaveLogbooks.Remove(target);
            SelectedLongwaveLogbook = LongwaveLogbooks.FirstOrDefault();
            await RefreshLongwaveContactsAsync();
            LongwaveStatus = $"Deleted logbook {target.Name}.";
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportSelectedLongwaveLogbookAdifAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        if (SelectedLongwaveLogbook is null)
        {
            LongwaveStatus = "Select a logbook before exporting ADIF.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            var target = SelectedLongwaveLogbook;
            LongwaveStatus = $"Exporting {target.Name} ADIF...";
            var adif = await _longwaveService.ExportLogbookAdifAsync(
                BuildCurrentLongwaveSettings(),
                target.Id,
                CancellationToken.None);

            var exportRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "longwave-adif");
            Directory.CreateDirectory(exportRoot);
            var safeName = MakeSafeFileName(target.Name);
            var path = Path.Combine(exportRoot, $"{safeName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.adi");
            await File.WriteAllTextAsync(path, adif, CancellationToken.None);
            LongwaveStatus = $"Exported ADIF to {path}.";
            LongwaveLogStatus = LongwaveStatus;
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
            LongwaveLogStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedLongwaveContactAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        if (SelectedLongwaveRecentContact is null)
        {
            LongwaveStatus = "Select a contact first.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            var target = SelectedLongwaveRecentContact;
            await _longwaveService.DeleteContactAsync(BuildCurrentLongwaveSettings(), target.Id, CancellationToken.None);
            LongwaveRecentContacts.Remove(target);
            SelectedLongwaveRecentContact = LongwaveRecentContacts.FirstOrDefault();
            LongwaveStatus = $"Deleted contact {target.StationCallsign}.";
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshLongwaveContactsNowAsync()
    {
        try
        {
            IsLongwaveBusy = true;
            await RefreshLongwaveContactsAsync();
            var uploaded = LongwaveRecentContacts.Count(static contact =>
                string.Equals(contact.QrzUploadStatus, "Y", StringComparison.OrdinalIgnoreCase));
            var pending = Math.Max(0, LongwaveRecentContacts.Count - uploaded);
            LongwaveStatus = $"Loaded {LongwaveRecentContacts.Count} contacts. QRZ uploaded {uploaded}, pending {pending}.";
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private async Task UploadSelectedLongwaveLogbookToQrzAsync()
    {
        if (_longwaveService is null)
        {
            LongwaveStatus = "Longwave service unavailable.";
            return;
        }

        if (SelectedLongwaveLogbook is null)
        {
            LongwaveStatus = "Select a logbook before uploading to QRZ.";
            return;
        }

        try
        {
            IsLongwaveBusy = true;
            var target = SelectedLongwaveLogbook;
            var pending = LongwaveRecentContacts.Count(static contact =>
                !string.Equals(contact.QrzUploadStatus, "Y", StringComparison.OrdinalIgnoreCase));
            if (pending <= 0)
            {
                LongwaveStatus = $"{target.Name} has no QRZ-pending contacts.";
                LongwaveLogStatus = LongwaveStatus;
                return;
            }

            LongwaveStatus = $"Uploading {pending} pending contact(s) from {target.Name} to QRZ via Longwave...";
            var result = await _longwaveService.UploadLogbookToQrzAsync(
                BuildCurrentLongwaveSettings(),
                target.Id,
                CancellationToken.None);
            await RefreshLongwaveContactsAsync();
            var uploaded = LongwaveRecentContacts.Count(static contact =>
                string.Equals(contact.QrzUploadStatus, "Y", StringComparison.OrdinalIgnoreCase));
            LongwaveStatus = result.Uploaded
                ? $"{result.Message} Refreshed {uploaded} uploaded contact badge(s)."
                : result.Message;
            LongwaveLogStatus = LongwaveStatus;
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
            LongwaveLogStatus = ex.Message;
        }
        finally
        {
            IsLongwaveBusy = false;
        }
    }

    [RelayCommand]
    private void UseSelectedWsjtxForLongwaveLog()
    {
        if (!TryBuildWsjtxLongwaveLogContext(out var context))
        {
            WsjtxRxStatus = "Track a QSO or select a decode with a callsign first.";
            return;
        }

        LongwaveLogOperatorCallsign = context.OperatorCallsign;
        LongwaveLogCallsign = context.StationCallsign;
        LongwaveLogMode = WsjtxSelectedMode.Trim().ToUpperInvariant();
        LongwaveLogBand = DeriveBandFromFrequencyKhz(CurrentFrequencyHz / 1000d);
        LongwaveLogFrequencyKhz = $"{CurrentFrequencyHz / 1000d:0.0}";
        var snrText = context.SignalReportDb.ToString("+#;-#;0");
        LongwaveLogRstSent = snrText;
        LongwaveLogRstReceived = snrText;
        LongwaveLogStatus = $"Prefilled Longwave log from {context.StationCallsign} on {LongwaveLogBand} {LongwaveLogMode}.";
    }

    private bool TryBuildWsjtxLongwaveLogContext(out WsjtxLongwaveLogContext context)
    {
        var myCall = FormatCallsign(WsjtxOperatorCallsign);
        var activeCall = FormatCallsign(WsjtxActiveSession?.OtherCall ?? string.Empty);
        var selected = SelectedWsjtxMessage;

        if (string.IsNullOrWhiteSpace(activeCall))
        {
            var state = ClassifyWsjtxQsoState(selected?.MessageText, myCall);
            activeCall = FormatCallsign(state.OtherCall ?? TryExtractCallsign(selected?.MessageText) ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(myCall) || string.IsNullOrWhiteSpace(activeCall))
        {
            context = default;
            return false;
        }

        var anchor = FindLatestWsjtxMessageForCall(activeCall) ?? selected;
        var report = anchor?.SnrDb ?? 0;
        context = new WsjtxLongwaveLogContext(myCall, activeCall, report);
        return true;
    }

    private WsjtxMessageItem? FindLatestWsjtxMessageForCall(string callsign)
    {
        var normalizedCall = FormatCallsign(callsign);
        if (string.IsNullOrWhiteSpace(normalizedCall))
        {
            return null;
        }

        return WsjtxConversationMessages
            .Concat(WsjtxMessages)
            .Where(message => WsjtxMessageMentionsCall(message.MessageText, normalizedCall))
            .OrderByDescending(message => message.TimestampUtc)
            .FirstOrDefault();
    }

    private static bool WsjtxMessageMentionsCall(string messageText, string callsign)
    {
        var tokens = messageText
            .ToUpperInvariant()
            .Split([' ', '\t', '\r', '\n', ',', ';', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Contains(callsign, StringComparer.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task LogSelectedWsjtxQsoAsync()
    {
        if (!TryBuildWsjtxLongwaveLogContext(out _))
        {
            WsjtxRxStatus = "Track a QSO or select a decode with a callsign first.";
            return;
        }

        UseSelectedWsjtxForLongwaveLog();
        await LogCurrentQsoAsync();
        WsjtxRxStatus = $"Longwave: {LongwaveLogStatus}";
    }

    [RelayCommand]
    private async Task StartCwDecoderAsync()
    {
        if (_cwDecoderHost is null)
        {
            CwDecoderStatus = "CW decoder host unavailable";
            return;
        }

        var lowerLimit = Math.Clamp(CwLowerWpmLimit, 5, 60);
        var upperLimit = Math.Clamp(CwUpperWpmLimit, lowerLimit + 1, 80);
        var config = new CwDecoderConfiguration(
            Math.Clamp(CwPitchHz, 300, 1200),
            Math.Clamp(CwWpm, 5, 60),
            CwDecoderProfile,
            Math.Clamp(CwBandwidthHz, 40, 600),
            CwMatchedFilterEnabled,
            CwTrackingEnabled,
            Math.Clamp(CwTrackingRangeWpm, 1, 30),
            lowerLimit,
            upperLimit,
            CwSelectedAttack,
            CwSelectedDecay,
            CwSelectedNoiseCharacter,
            CwAutoToneSearchEnabled,
            CwAfcEnabled,
            Math.Clamp(CwToneSearchSpanHz, 50, 800),
            CwSelectedSquelch,
            CwSelectedSpacing);
        await _cwDecoderHost.ConfigureAsync(config, CancellationToken.None);
        await _cwDecoderHost.StartAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task StopCwDecoderAsync()
    {
        if (_cwDecoderHost is null)
        {
            return;
        }

        await _cwDecoderHost.StopAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task ResetCwDecoderAsync()
    {
        if (_cwDecoderHost is null)
        {
            return;
        }

        CwDecodedText = string.Empty;
        await _cwDecoderHost.ResetAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void ClearCwDecodedText()
    {
        CwDecodedText = string.Empty;
    }

    [RelayCommand]
    private void LoadCwMacro(string? label)
    {
        var text = ExpandCwMacro(label);
        if (string.IsNullOrWhiteSpace(text))
        {
            CwTxStatus = "No CW macro selected.";
            return;
        }

        CwSendText = text;
        CwTxStatus = $"Loaded CW macro: {text}";
    }

    [RelayCommand]
    private async Task SendCwMacroAsync(string? label)
    {
        LoadCwMacro(label);
        await QueueCwSendTextAsync();
    }

    [RelayCommand]
    private void StartRttyReceive()
    {
        if (_rttyDecoderHost is null)
        {
            RttyRxStatus = "RTTY decoder host unavailable";
            return;
        }

        _ = StartRttyReceiveCoreAsync();
    }

    [RelayCommand]
    private void StopRttyReceive()
    {
        if (_rttyDecoderHost is null)
        {
            return;
        }

        _ = _rttyDecoderHost.StopAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void ResetRttySession()
    {
        if (_rttyDecoderHost is not null)
        {
            _ = _rttyDecoderHost.ResetAsync(CancellationToken.None);
        }

        RttyRxStatus = "RTTY receiver ready";
        RttySessionNotes = "For IC-7300 audio RTTY, use USB-D/LSB-D rather than native RTTY mode. Tune the signal, select shift/baud, then start receive.";
        RttyTuneHelperSuggestion = "Start RX, then place an RTTY signal in the passband; helper will suggest Audio Hz.";
        RttySuggestedAudioCenterHz = 0;
        RttyDecodedText = string.Empty;
    }

    [RelayCommand]
    private void ClearRttyDecodedText() => RttyDecodedText = string.Empty;

    [RelayCommand]
    private void StartKeyboardReceive()
    {
        if (_keyboardModeDecoderHost is null)
        {
            KeyboardRxStatus = "Keyboard decoder host unavailable";
            return;
        }

        _ = StartKeyboardReceiveCoreAsync();
    }

    [RelayCommand]
    private void StopKeyboardReceive()
    {
        if (_keyboardModeDecoderHost is not null)
        {
            _ = _keyboardModeDecoderHost.StopAsync(CancellationToken.None);
        }
    }

    [RelayCommand]
    private void ResetKeyboardSession()
    {
        if (_keyboardModeDecoderHost is not null)
        {
            _ = _keyboardModeDecoderHost.ResetAsync(CancellationToken.None);
        }

        KeyboardRxStatus = "Keyboard modes ready";
        KeyboardSessionNotes = "PSK31/63 receive uses the fldigi-derived GPL sidecar. Use USB-D/LSB-D and place the trace near the selected audio center.";
        KeyboardTuneHelperSuggestion = "Start RX, tune the PSK trace into the passband, then apply the helper if AFC settles off-center.";
        KeyboardSuggestedAudioCenterHz = 0;
        KeyboardDecodedText = string.Empty;
    }

    [RelayCommand]
    private void ClearKeyboardDecodedText() => KeyboardDecodedText = string.Empty;

    [RelayCommand]
    private Task PrepareKeyboardTransmitAsync()
    {
        var text = KeyboardTransmitText.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _keyboardPreparedTransmitClip = null;
            KeyboardPreparedTransmitStatus = "Type PSK text before preparing TX audio.";
            KeyboardPreparedTransmitPath = "No prepared PSK WAV.";
            OnPropertyChanged(nameof(KeyboardHasPreparedTransmitClip));
            return Task.CompletedTask;
        }

        try
        {
            Directory.CreateDirectory(_keyboardTxDirectory);
            var mode = string.Equals(KeyboardSelectedMode, "BPSK63", StringComparison.OrdinalIgnoreCase)
                ? "BPSK63"
                : "BPSK31";
            var audioCenterHz = ParseKeyboardAudioCenterHz(KeyboardAudioCenterHz);
            var clip = PskBpskWaveformGenerator.Generate(mode, text, audioCenterHz);
            var timestamp = DateTime.Now;
            var safeMode = mode.ToLowerInvariant();
            var wavPath = Path.Combine(_keyboardTxDirectory, $"{timestamp:yyyyMMdd_HHmmss}_{safeMode}.wav");

            SstvReplyRenderer.WriteWaveFile(wavPath, clip);
            _keyboardPreparedTransmitClip = clip;
            _keyboardPreparedTransmitFingerprint = BuildKeyboardTransmitFingerprint();
            var durationSeconds = clip.PcmBytes.Length / (double)(clip.SampleRate * clip.Channels * 2);
            KeyboardPreparedTransmitStatus = $"Prepared {mode} TX WAV at {audioCenterHz:0} Hz audio center ({durationSeconds:0.0}s).";
            KeyboardPreparedTransmitPath = wavPath;
            OnPropertyChanged(nameof(KeyboardHasPreparedTransmitClip));
        }
        catch (Exception ex)
        {
            _keyboardPreparedTransmitClip = null;
            _keyboardPreparedTransmitFingerprint = null;
            KeyboardPreparedTransmitStatus = $"PSK TX prepare failed: {ex.Message}";
            KeyboardPreparedTransmitPath = "No prepared PSK WAV.";
            OnPropertyChanged(nameof(KeyboardHasPreparedTransmitClip));
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SendKeyboardTransmitAsync()
    {
        if (_keyboardTxSendInFlight)
        {
            return;
        }

        _keyboardTxSendInFlight = true;
        var pttRaised = false;
        var txAudioStarted = false;

        try
        {
            var interlockError = ValidateKeyboardLiveTransmitInterlock();
            if (interlockError is not null)
            {
                KeyboardPreparedTransmitStatus = interlockError;
                VoiceTxStatus = "TX audio idle";
                RadioStatusSummary = interlockError;
                return;
            }

            if (KeyboardUseCurrentRadioFrequency)
            {
                await SetRadioForKeyboardAudioDataModeAsync().ConfigureAwait(false);
            }
            else
            {
                await TuneRadioForKeyboardAsync(KeyboardSelectedFrequency).ConfigureAwait(false);
            }

            var route = BuildCurrentAudioRoute();
            var liveClip = WithLeadingSilence(_keyboardPreparedTransmitClip!, 250);
            var clipDurationMs = Math.Max(250, (int)Math.Ceiling(
                liveClip.PcmBytes.Length / (double)(liveClip.SampleRate * liveClip.Channels * 2) * 1000.0));
            var mode = string.Equals(KeyboardSelectedMode, "BPSK63", StringComparison.OrdinalIgnoreCase) ? "BPSK63" : "BPSK31";
            var text = KeyboardTransmitText.Trim();

            await _audioService!.StartTransmitPcmAsync(route, liveClip, CancellationToken.None).ConfigureAwait(false);
            txAudioStarted = true;
            await _radioService!.SetPttAsync(true, CancellationToken.None).ConfigureAwait(false);
            pttRaised = true;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                KeyboardPreparedTransmitStatus = $"Sending {mode} on-air: {text}";
                VoiceTxStatus = "PSK TX audio live";
                RadioStatusSummary = $"Keyboard TX live  |  {mode}  |  {KeyboardAudioCenterHz} Hz audio";
            });

            await Task.Delay(clipDurationMs + 150, CancellationToken.None).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                KeyboardPreparedTransmitStatus = $"PSK TX sent: {mode}. Prepared WAV retained.";
                VoiceTxStatus = "TX audio idle";
                RadioStatusSummary = "Keyboard TX complete";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                KeyboardPreparedTransmitStatus = $"PSK TX failed: {ex.Message}";
                VoiceTxStatus = "TX audio idle";
                RadioStatusSummary = $"Keyboard TX failed: {ex.Message}";
            });
        }
        finally
        {
            try
            {
                if (pttRaised && _radioService is not null)
                {
                    await _radioService.SetPttAsync(false, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
                // Leave shutdown paths to force PTT low if CI-V readback is in a bad mood.
            }

            try
            {
                if (txAudioStarted && _audioService is not null)
                {
                    await _audioService.StopTransmitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            _keyboardTxSendInFlight = false;
        }
    }

    [RelayCommand]
    private async Task ApplyKeyboardTuneHelperAsync()
    {
        if (KeyboardSuggestedAudioCenterHz <= 0)
        {
            KeyboardTuneHelperSuggestion = "No PSK tuning estimate is available yet. Start RX and wait for DCD/AFC to settle.";
            return;
        }

        KeyboardAudioCenterHz = $"{KeyboardSuggestedAudioCenterHz:0.0}";
        KeyboardTuneHelperSuggestion = $"Applied Audio Hz {KeyboardAudioCenterHz}.";
        if (_keyboardModeDecoderHost is not null)
        {
            await ConfigureKeyboardDecoderAsync();
            KeyboardTuneHelperSuggestion = $"Applied Audio Hz {KeyboardAudioCenterHz}; decoder reconfigured live.";
        }
    }

    [RelayCommand]
    private void StartFreedvReceive()
    {
        if (_freedvDigitalVoiceHost is null)
        {
            FreedvRxStatus = "FreeDV host unavailable";
            return;
        }

        _ = StartFreedvReceiveCoreAsync();
    }

    [RelayCommand]
    private void StopFreedvReceive()
    {
        _ = StopFreedvReceiveCoreAsync();
    }

    [RelayCommand]
    private void ResetFreedvSession()
    {
        if (_freedvDigitalVoiceHost is not null)
        {
            _ = _freedvDigitalVoiceHost.ResetAsync(CancellationToken.None);
        }

        FreedvRxStatus = "FreeDV digital voice ready";
        FreedvSessionNotes = "Use USB-D/LSB-D with the official Codec2 FreeDV runtime. 700D is the practical first HF mode.";
        FreedvRuntimeStatus = "Codec2 runtime not loaded yet.";
        FreedvDecodedAudioStatus = "No decoded FreeDV speech yet.";
        FreedvSignalSummary = "Signal ---%  |  Sync ---%  |  SNR --.- dB";
        FreedvLastRadeCallsign = "None decoded";
    }

    [RelayCommand]
    private async Task StartFreedvTransmitAsync()
    {
        if (_freedvDigitalVoiceHost is null)
        {
            FreedvRxStatus = "FreeDV host unavailable";
            return;
        }

        if (_radioService is null || CanConnect)
        {
            FreedvRxStatus = "FreeDV TX blocked: radio is not connected.";
            return;
        }

        if (_audioService is null || SelectedMicDevice is null || SelectedTxDevice is null)
        {
            FreedvRxStatus = "FreeDV TX blocked: mic/TX audio device is not configured.";
            return;
        }

        if (IsFreedvTransmitting)
        {
            return;
        }

        try
        {
            IsFreedvTransmitting = true;
            FreedvRxStatus = "Starting FreeDV TX...";
            if (FreedvUseCurrentRadioFrequency)
            {
                await SetRadioForFreedvAudioDataModeAsync().ConfigureAwait(false);
            }
            else
            {
                await TuneRadioForFreedvAsync(FreedvSelectedFrequency).ConfigureAwait(false);
            }

            var frequencyLabel = FreedvUseCurrentRadioFrequency
                ? "Current radio frequency"
                : FreedvSelectedFrequency;
            await _freedvDigitalVoiceHost.ConfigureAsync(
                new FreedvDigitalVoiceConfiguration(
                    FreedvSelectedMode,
                    frequencyLabel,
                    FreedvUseCurrentRadioFrequency,
                    FreedvRxFrequencyOffsetHz,
                    FormatCallsign(SettingsCallsign)),
                CancellationToken.None).ConfigureAwait(false);
            await _freedvDigitalVoiceHost.StartTransmitAsync(BuildCurrentAudioRoute(), CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(150).ConfigureAwait(false);
            await _radioService.SetPttAsync(true, CancellationToken.None).ConfigureAwait(false);
            FreedvRxStatus = $"FreeDV TX live ({FreedvSelectedMode})";
            FreedvSignalSummary = $"TX active  |  Mode {FreedvSelectedMode}  |  EOO call {FormatCallsign(SettingsCallsign)}";
            FreedvSessionNotes = string.Equals(FreedvSelectedMode, "RADEV1", StringComparison.OrdinalIgnoreCase)
                ? "Speak normally; RADEV1 TX will append your callsign in the end-of-over frame when you stop transmit."
                : "Speak normally; Codec2 FreeDV is encoding mic audio to the radio TX device.";
        }
        catch (Exception ex)
        {
            FreedvRxStatus = $"FreeDV TX failed: {ex.Message}";
            await StopFreedvTransmitSafelyAsync().ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task StopFreedvTransmitAsync()
    {
        await StopFreedvTransmitSafelyAsync().ConfigureAwait(false);
        FreedvRxStatus = "FreeDV TX stopped";
    }

    [RelayCommand]
    private void OpenFreedvDebugFolder()
    {
        try
        {
            Directory.CreateDirectory(_freedvDebugDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _freedvDebugDirectory,
                UseShellExecute = true,
            });
            FreedvDecodedAudioStatus = $"Opened FreeDV captures: {_freedvDebugDirectory}";
        }
        catch (Exception ex)
        {
            FreedvDecodedAudioStatus = $"Could not open FreeDV captures: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyRttyTuneHelperAsync()
    {
        if (RttySuggestedAudioCenterHz <= 0)
        {
            RttyTuneHelperSuggestion = "No RTTY tone pair has been detected yet. Start RX and tune until the two tones are visible/audible.";
            return;
        }

        RttyAudioCenterHz = $"{RttySuggestedAudioCenterHz:0}";
        RttyTuneHelperSuggestion = $"Applied Audio Hz {RttyAudioCenterHz}.";

        if (_rttyDecoderHost is null)
        {
            return;
        }

        var (shiftHz, baudRate) = ParseRttyProfile(RttySelectedProfile);
        var frequencyLabel = RttyDecodeCurrentRadioFrequency
            ? "Current radio frequency"
            : RttySelectedFrequency;
        var config = new RttyDecoderConfiguration(
            RttySelectedProfile,
            shiftHz,
            baudRate,
            frequencyLabel,
            RttySuggestedAudioCenterHz,
            RttyReversePolarity);
        await _rttyDecoderHost.ConfigureAsync(config, CancellationToken.None);
        RttyTuneHelperSuggestion = $"Applied Audio Hz {RttyAudioCenterHz}; decoder reconfigured live.";
    }

    [RelayCommand]
    private void StartWsjtxReceive()
    {
        if (_wsjtxModeHost is null)
        {
            WsjtxRxStatus = "WSJT-style host unavailable";
            return;
        }

        _ = StartWsjtxReceiveCoreAsync();
    }

    [RelayCommand]
    private async Task StartJs8ReceiveAsync()
    {
        if (_wsjtxModeHost is null)
        {
            WsjtxRxStatus = "JS8 host unavailable";
            return;
        }

        ActivateJs8Desk();
        await StartWsjtxReceiveCoreAsync();
    }

    [RelayCommand]
    private void StopWsjtxReceive()
    {
        if (_wsjtxModeHost is null)
        {
            return;
        }

        _ = _wsjtxModeHost.StopAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void ResetWsjtxSession()
    {
        if (_wsjtxModeHost is not null)
        {
            _ = _wsjtxModeHost.ResetAsync(CancellationToken.None);
        }

        WsjtxMessages.Clear();
        WsjtxRxFrequencyMessages.Clear();
        OnPropertyChanged(nameof(WsjtxHasMessages));
        OnPropertyChanged(nameof(WsjtxHasRxFrequencyMessages));
        SelectedWsjtxMessage = null;
        WsjtxRxAudioFrequencyHz = 1500;
        WsjtxTxAudioFrequencyHz = 1500;
        WsjtxHoldTxFrequency = false;
        WsjtxActiveSession = null;
        WsjtxCallingCq = false;
        WsjtxSuggestedMessages.Clear();
        SelectedWsjtxSuggestedMessage = null;
        WsjtxQueuedTransmitMessage = null;
        SetWsjtxManualTransmitText(string.Empty, isManualOverride: false);
        WsjtxPreparedTransmit = null;
        _wsjtxPreparedTransmitClip = null;
        WsjtxPreparedTransmitStatus = "No TX signal prepared.";
        WsjtxPreparedTransmitPath = "No prepared TX artifact.";
        WsjtxTransmitArmedLocal = false;
        WsjtxAwaitingReply = false;
        WsjtxTransmitArmStatus = "Nothing armed.";
        WsjtxRxStatus = "WSJT-style digital receiver ready";
        WsjtxClockStatus = "Checking system clock discipline...";
        WsjtxCycleDisplay = $"{WsjtxSelectedMode}  |  {GetWsjtxCycleLengthSeconds(WsjtxSelectedMode):0.#}s cycle  |  Next --.-s";
        WsjtxSessionNotes = DescribeWsjtxMode(WsjtxSelectedMode);
    }

    private void ResetWsjtxSessionView(string modeLabel, string status)
    {
        WsjtxMessages.Clear();
        WsjtxRxFrequencyMessages.Clear();
        OnPropertyChanged(nameof(WsjtxHasMessages));
        OnPropertyChanged(nameof(WsjtxHasRxFrequencyMessages));
        SelectedWsjtxMessage = null;
        WsjtxRxAudioFrequencyHz = 1500;
        WsjtxTxAudioFrequencyHz = 1500;
        WsjtxHoldTxFrequency = false;
        WsjtxActiveSession = null;
        WsjtxCallingCq = false;
        WsjtxSuggestedMessages.Clear();
        SelectedWsjtxSuggestedMessage = null;
        WsjtxQueuedTransmitMessage = null;
        SetWsjtxManualTransmitText(string.Empty, isManualOverride: false);
        WsjtxPreparedTransmit = null;
        _wsjtxPreparedTransmitClip = null;
        WsjtxPreparedTransmitStatus = "No TX signal prepared.";
        WsjtxPreparedTransmitPath = "No prepared TX artifact.";
        WsjtxTransmitArmedLocal = false;
        WsjtxAwaitingReply = false;
        WsjtxTransmitArmStatus = "Nothing armed.";
        WsjtxRxStatus = status;
        WsjtxClockStatus = "Checking system clock discipline...";
        WsjtxCycleDisplay = $"{modeLabel}  |  {GetWsjtxCycleLengthSeconds(modeLabel):0.#}s cycle  |  Next --.-s";
        WsjtxSessionNotes = DescribeWsjtxMode(modeLabel);
    }

    [RelayCommand]
    private void ClearWsjtxMessages()
    {
        WsjtxMessages.Clear();
        WsjtxRxFrequencyMessages.Clear();
        SelectedWsjtxMessage = null;
        WsjtxRxAudioFrequencyHz = 1500;
        WsjtxTxAudioFrequencyHz = 1500;
        WsjtxHoldTxFrequency = false;
        WsjtxActiveSession = null;
        WsjtxCallingCq = false;
        WsjtxQueuedTransmitMessage = null;
        SetWsjtxManualTransmitText(string.Empty, isManualOverride: false);
        WsjtxPreparedTransmit = null;
        _wsjtxPreparedTransmitClip = null;
        WsjtxPreparedTransmitStatus = "No TX signal prepared.";
        WsjtxPreparedTransmitPath = "No prepared TX artifact.";
        WsjtxTransmitArmedLocal = false;
        WsjtxAwaitingReply = false;
        WsjtxTransmitArmStatus = "Nothing armed.";
        OnPropertyChanged(nameof(WsjtxHasMessages));
        OnPropertyChanged(nameof(WsjtxHasRxFrequencyMessages));
    }

    [RelayCommand]
    private void TrackSelectedWsjtxOffset()
    {
        if (IsWsprMode(WsjtxSelectedMode))
        {
            WsjtxRxStatus = "WSPR monitor does not use manual audio-offset tracking.";
            return;
        }

        if (SelectedWsjtxMessage is null)
        {
            return;
        }

        WsjtxRxAudioFrequencyHz = SelectedWsjtxMessage.FrequencyOffsetHz;
        if (!WsjtxHoldTxFrequency)
        {
            WsjtxTxAudioFrequencyHz = WsjtxRxAudioFrequencyHz;
        }
        WsjtxRxStatus = $"Tracking {WsjtxRxAudioFrequencyHz:+0;-0;0} Hz in Rx Frequency";
    }

    [RelayCommand]
    private void ResetWsjtxRxFrequencyTrack()
    {
        WsjtxRxAudioFrequencyHz = 1500;
        if (!WsjtxHoldTxFrequency)
        {
            WsjtxTxAudioFrequencyHz = 1500;
        }
        WsjtxRxStatus = "Rx Frequency reset to +1500 Hz";
    }

    [RelayCommand]
    private void InjectWsjtxDirectedTestDecode()
    {
        var myCall = FormatCallsign(WsjtxOperatorCallsign);
        if (string.IsNullOrWhiteSpace(myCall))
        {
            myCall = FormatCallsign(SettingsCallsign);
        }

        if (string.IsNullOrWhiteSpace(myCall))
        {
            WsjtxRxStatus = "Set your callsign before running the directed-message alert test.";
            return;
        }

        var sequence = Interlocked.Increment(ref wsjtxDirectedAlertTestSequence);
        var mode = IsWsjtxQsoMode(WsjtxSelectedMode) ? WsjtxSelectedMode : "FT8";
        var offsetHz = Math.Clamp(WsjtxRxAudioFrequencyHz + sequence % 7, 200, 3000);
        var report = -7 - sequence % 3;
        var message = new WsjtxDecodeMessage(
            TimestampUtc: DateTime.UtcNow,
            ModeLabel: mode,
            FrequencyOffsetHz: offsetHz,
            SnrDb: report,
            DeltaTimeSeconds: 0.2,
            MessageText: $"K1TST {myCall} {report:+00;-00;00}",
            Confidence: 1.0,
            IsDirectedToMe: true,
            IsCq: false);

        UpsertWsjtxMessage(message);
        SelectedWsjtxMessage = WsjtxMessages.FirstOrDefault();
        WsjtxRxStatus = $"Injected directed test decode to {myCall}; alert tone should play.";
        OnPropertyChanged(nameof(WsjtxHasMessages));
    }

    [RelayCommand]
    private void SetSelectedWsjtxTxOffset()
    {
        if (IsWsprMode(WsjtxSelectedMode))
        {
            WsjtxRxStatus = "WSPR TX/QSO rail is disabled for now; RF spots are receive-only.";
            return;
        }

        if (SelectedWsjtxMessage is null)
        {
            return;
        }

        WsjtxTxAudioFrequencyHz = SelectedWsjtxMessage.FrequencyOffsetHz;
        WsjtxRxStatus = $"TX offset set to {WsjtxTxAudioFrequencyHz:+0;-0;0} Hz";
    }

    [RelayCommand]
    private void SyncWsjtxTxToRx()
    {
        WsjtxTxAudioFrequencyHz = WsjtxRxAudioFrequencyHz;
        WsjtxRxStatus = $"TX offset synced to {WsjtxTxAudioFrequencyHz:+0;-0;0} Hz";
    }

    [RelayCommand]
    private void NudgeWsjtxTxOffsetDown50() => AdjustWsjtxTxOffset(-50);

    [RelayCommand]
    private void NudgeWsjtxTxOffsetUp50() => AdjustWsjtxTxOffset(50);

    [RelayCommand]
    private void NudgeWsjtxTxOffsetDown100() => AdjustWsjtxTxOffset(-100);

    [RelayCommand]
    private void NudgeWsjtxTxOffsetUp100() => AdjustWsjtxTxOffset(100);

    private void AdjustWsjtxTxOffset(int deltaHz)
    {
        WsjtxHoldTxFrequency = true;
        WsjtxTxAudioFrequencyHz = Math.Clamp(WsjtxTxAudioFrequencyHz + deltaHz, 200, 3900);
        WsjtxRxStatus = $"TX offset set to {WsjtxTxAudioFrequencyHz:+0;-0;0} Hz";
    }

    [RelayCommand]
    private void StageSelectedWsjtxSuggestedMessage()
        => StageWsjtxSuggestedMessage(SelectedWsjtxSuggestedMessage);

    [RelayCommand]
    private void StageWsjtxSuggestedMessage(WsjtxSuggestedMessageItem? message)
    {
        if (!IsWsjtxQsoMode(WsjtxSelectedMode))
        {
            WsjtxRxStatus = $"{WsjtxSelectedMode} is receive/monitor only in ShackStack for now.";
            return;
        }

        if (message is null)
        {
            return;
        }

        SelectedWsjtxSuggestedMessage = message;
        WsjtxQueuedTransmitMessage = message;
        SetWsjtxManualTransmitText(message.MessageText, isManualOverride: false);
        var myCall = FormatCallsign(WsjtxOperatorCallsign);
        if (string.Equals(message.Label, "CQ", StringComparison.OrdinalIgnoreCase))
        {
            WsjtxCallingCq = true;
            WsjtxActiveSession = null;
        }
        else
        {
            var state = ClassifyWsjtxQsoState(message.MessageText, myCall);
            if (!string.IsNullOrWhiteSpace(state.OtherCall))
            {
                var sessionOffset = SelectedWsjtxMessage?.FrequencyOffsetHz
                    ?? WsjtxActiveSession?.FrequencyOffsetHz
                    ?? WsjtxRxAudioFrequencyHz;
                WsjtxActiveSession = new WsjtxActiveSession(state.OtherCall, sessionOffset, WsjtxSelectedMode);
                WsjtxRxAudioFrequencyHz = sessionOffset;
                if (!WsjtxHoldTxFrequency)
                {
                    WsjtxTxAudioFrequencyHz = sessionOffset;
                }
                WsjtxCallingCq = false;
            }
        }
        WsjtxPreparedTransmitStatus = "Staged message ready.";
        WsjtxPreparedTransmitPath = "No prepared TX artifact.";
        WsjtxPreparedTransmit = null;
        _wsjtxPreparedTransmitClip = null;
        WsjtxTransmitArmedLocal = false;
        WsjtxAwaitingReply = false;
        WsjtxTransmitArmStatus = "Prepared message not generated yet.";
        WsjtxRxStatus = $"Staged TX: {message.Label} | {message.MessageText}";
    }

    [RelayCommand]
    private void StageWsjtxCq()
    {
        if (!IsWsjtxQsoMode(WsjtxSelectedMode))
        {
            WsjtxRxStatus = $"{WsjtxSelectedMode} is receive/monitor only in ShackStack for now.";
            return;
        }

        var myCall = FormatCallsign(WsjtxOperatorCallsign);
        if (string.IsNullOrWhiteSpace(myCall))
        {
            WsjtxRxStatus = "No CQ message available. Set your callsign first.";
            return;
        }

        var myGrid = FormatWeakSignalGrid(WsjtxOperatorGridSquare);
        var cqText = string.IsNullOrWhiteSpace(myGrid)
            ? $"CQ {myCall}"
            : $"CQ {myCall} {myGrid}";

        var cqMessage = new WsjtxSuggestedMessageItem("CQ", cqText, "Call CQ on current TX offset");
        SelectedWsjtxSuggestedMessage = cqMessage;
        WsjtxQueuedTransmitMessage = cqMessage;
        SetWsjtxManualTransmitText(cqMessage.MessageText, isManualOverride: false);
        WsjtxCallingCq = true;
        WsjtxActiveSession = null;
        WsjtxPreparedTransmitStatus = "Staged CQ ready.";
        WsjtxPreparedTransmitPath = "No prepared TX artifact.";
        WsjtxPreparedTransmit = null;
        _wsjtxPreparedTransmitClip = null;
        WsjtxTransmitArmedLocal = false;
        WsjtxAwaitingReply = false;
        WsjtxTransmitArmStatus = "Prepared CQ not generated yet.";
        WsjtxRxStatus = "Staged TX: CQ";
    }

    [RelayCommand]
    private void UseSelectedJs8Decode()
    {
        if (SelectedWsjtxMessage is null || !SelectedWsjtxMessage.ModeText.StartsWith("JS8", StringComparison.OrdinalIgnoreCase))
        {
            Js8ComposeStatus = "Select a JS8 decode first.";
            return;
        }

        var target = TryExtractCallsign(SelectedWsjtxMessage.MessageText);
        if (string.IsNullOrWhiteSpace(target))
        {
            Js8ComposeStatus = "Selected JS8 decode does not contain an obvious callsign.";
            return;
        }

        Js8TargetCallsign = target;
        WsjtxRxAudioFrequencyHz = SelectedWsjtxMessage.FrequencyOffsetHz;
        if (!WsjtxHoldTxFrequency)
        {
            WsjtxTxAudioFrequencyHz = SelectedWsjtxMessage.FrequencyOffsetHz;
        }

        RebuildJs8SuggestedMessages();
        Js8ComposeStatus = $"JS8 target set to {target} at {SelectedWsjtxMessage.FrequencyOffsetHz:+0;-0;0} Hz.";
    }

    [RelayCommand]
    private void StageSelectedJs8SuggestedMessage()
    {
        if (SelectedJs8SuggestedMessage is null)
        {
            Js8ComposeStatus = "Choose a JS8 quick message first.";
            return;
        }

        Js8ComposeText = SelectedJs8SuggestedMessage.MessageText;
        Js8ComposeStatus = $"Staged JS8 text: {SelectedJs8SuggestedMessage.Label}. Prepare TX audio before arming.";
    }

    [RelayCommand]
    private void StageJs8Heartbeat()
    {
        var myCall = FormatCallsign(WsjtxOperatorCallsign);
        if (string.IsNullOrWhiteSpace(myCall))
        {
            Js8ComposeStatus = "Set your callsign before staging a heartbeat.";
            return;
        }

        var grid = FormatWeakSignalGrid(WsjtxOperatorGridSquare);
        Js8ComposeText = string.IsNullOrWhiteSpace(grid)
            ? $"{myCall}: @HB HEARTBEAT"
            : $"{myCall}: @HB HEARTBEAT {grid}";
        Js8ComposeStatus = "Staged JS8 heartbeat text. Prepare TX audio before arming.";
    }

    [RelayCommand]
    private void StageJs8Cq()
    {
        var myCall = FormatCallsign(WsjtxOperatorCallsign);
        if (string.IsNullOrWhiteSpace(myCall))
        {
            Js8ComposeStatus = "Set your callsign before staging CQ.";
            return;
        }

        var grid = FormatWeakSignalGrid(WsjtxOperatorGridSquare);
        Js8ComposeText = string.IsNullOrWhiteSpace(grid)
            ? $"CQ CQ CQ DE {myCall}"
            : $"CQ CQ CQ DE {myCall} {grid}";
        Js8ComposeStatus = "Staged JS8 CQ text. Prepare TX audio before arming.";
    }

    [RelayCommand]
    private void ClearJs8Compose()
    {
        Js8TargetCallsign = string.Empty;
        Js8ComposeText = string.Empty;
        SelectedJs8SuggestedMessage = Js8SuggestedMessages.FirstOrDefault();
        Js8ComposeStatus = "JS8 compose cleared.";
        OnPropertyChanged(nameof(Js8TargetSummary));
    }

    [RelayCommand]
    private async Task PrepareJs8TransmitAsync()
    {
        var text = NormalizeWsjtxTransmitText(Js8ComposeText);
        if (string.IsNullOrWhiteSpace(text))
        {
            Js8ComposeStatus = "Type or stage JS8 text before preparing TX.";
            return;
        }

        WsjtxSelectedMode = Js8SelectedMode;
        WsjtxSelectedFrequency = Js8SelectedFrequency;
        SetWsjtxManualTransmitText(text, isManualOverride: true);
        WsjtxQueuedTransmitMessage = new WsjtxSuggestedMessageItem("JS8", text, "JS8 compose message");
        Js8ComposeStatus = $"Preparing {Js8SelectedMode} TX audio...";

        await PrepareWsjtxQueuedTransmitAsync().ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Js8ComposeStatus = WsjtxPreparedTransmit is not null && WsjtxPreparedTransmit.ModeLabel.StartsWith("JS8", StringComparison.OrdinalIgnoreCase)
                ? $"Prepared JS8 TX: {WsjtxPreparedTransmit.MessageText}"
                : WsjtxPreparedTransmitStatus;
            OnPropertyChanged(nameof(Js8TransmitReadiness));
        });
    }

    [RelayCommand]
    private async Task PrepareAndArmJs8TransmitAsync()
    {
        await PrepareJs8TransmitAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (WsjtxPreparedTransmit is null || !WsjtxPreparedTransmit.ModeLabel.StartsWith("JS8", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ArmPreparedWsjtxTransmit();
            Js8ComposeStatus = WsjtxTransmitArmedLocal
                ? $"Armed JS8 for next {Js8SelectedMode} slot."
                : WsjtxTransmitArmStatus;
            OnPropertyChanged(nameof(Js8TransmitReadiness));
        });
    }

    [RelayCommand]
    private async Task CallWsjtxCqNextSlotAsync()
    {
        StageWsjtxCq();
        await PrepareAndArmQueuedWsjtxTransmitAsync("CQ");
    }

    [RelayCommand]
    private async Task SendNextWsjtxMessageAsync()
    {
        var transmitText = NormalizeWsjtxTransmitText(WsjtxManualTransmitText)
            ?? WsjtxQueuedTransmitMessage?.MessageText;
        if (string.IsNullOrWhiteSpace(transmitText))
        {
            WsjtxRxStatus = "Select, stage, or type a weak-signal TX message first.";
            return;
        }

        if (WsjtxQueuedTransmitMessage is null || !string.Equals(WsjtxQueuedTransmitMessage.MessageText, transmitText, StringComparison.Ordinal))
        {
            WsjtxQueuedTransmitMessage = new WsjtxSuggestedMessageItem("Manual", transmitText, "Manual TX message");
        }

        await PrepareAndArmQueuedWsjtxTransmitAsync(WsjtxQueuedTransmitMessage.Label);
    }

    [RelayCommand]
    private async Task PrepareAndArmWsjtxSuggestedMessageAsync(WsjtxSuggestedMessageItem? message)
    {
        if (message is null)
        {
            return;
        }

        StageWsjtxSuggestedMessage(message);
        await PrepareAndArmQueuedWsjtxTransmitAsync(message.Label);
    }

    [RelayCommand]
    private void ClearWsjtxQueuedMessage()
    {
        WsjtxQueuedTransmitMessage = null;
        SetWsjtxManualTransmitText(string.Empty, isManualOverride: false);
        WsjtxPreparedTransmit = null;
        _wsjtxPreparedTransmitClip = null;
        WsjtxActiveSession = null;
        WsjtxCallingCq = false;
        WsjtxConversationMessages.Clear();
        WsjtxPreparedTransmitStatus = "No TX signal prepared.";
        WsjtxPreparedTransmitPath = "No prepared TX artifact.";
        WsjtxTransmitArmedLocal = false;
        WsjtxAwaitingReply = false;
        WsjtxTransmitArmStatus = "Nothing armed.";
        WsjtxRxStatus = "Cleared staged TX message";
        OnPropertyChanged(nameof(WsjtxHasConversationMessages));
        OnPropertyChanged(nameof(WsjtxConversationStatus));
    }

    [RelayCommand]
    private async Task PrepareWsjtxQueuedTransmitAsync()
    {
        if (_wsjtxModeHost is null)
        {
            WsjtxPreparedTransmitStatus = "WSJT host unavailable";
            WsjtxPreparedTransmitPath = "No prepared TX artifact.";
            return;
        }

        if (!IsWsjtxQsoMode(WsjtxSelectedMode))
        {
            WsjtxPreparedTransmitStatus = $"{WsjtxSelectedMode} transmit is not available from this QSO rail.";
            WsjtxPreparedTransmitPath = "No prepared TX artifact.";
            WsjtxTransmitArmStatus = $"{WsjtxSelectedMode} is receive/monitor only for now.";
            return;
        }

        var transmitText = NormalizeWsjtxTransmitText(WsjtxManualTransmitText)
            ?? WsjtxQueuedTransmitMessage?.MessageText;
        if (string.IsNullOrWhiteSpace(transmitText))
        {
            WsjtxPreparedTransmitStatus = "Stage or type a TX message first.";
            WsjtxPreparedTransmitPath = "No prepared TX artifact.";
            return;
        }

        if (WsjtxQueuedTransmitMessage is null || !string.Equals(WsjtxQueuedTransmitMessage.MessageText, transmitText, StringComparison.Ordinal))
        {
            WsjtxQueuedTransmitMessage = new WsjtxSuggestedMessageItem("Manual", transmitText, "Manual TX message");
        }

        WsjtxPreparedTransmitStatus = $"Preparing {WsjtxSelectedMode} TX signal...";
        WsjtxPreparedTransmitPath = "Working...";

        var result = await _wsjtxModeHost
            .PrepareTransmitAsync(
                WsjtxSelectedMode,
                transmitText,
                WsjtxTxAudioFrequencyHz,
                CancellationToken.None)
            .ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            WsjtxPreparedTransmit = result.PreparedTransmit;
            _wsjtxPreparedTransmitClip = result.PreparedClip;
            WsjtxPreparedTransmitStatus = result.Status;
            WsjtxPreparedTransmitPath = result.PreparedTransmit?.WaveFilePath ?? "No prepared TX artifact.";
            WsjtxTransmitArmedLocal = false;
            WsjtxAwaitingReply = false;
            WsjtxTransmitArmStatus = result.PreparedTransmit is null
                ? "Prepare TX signal to enable arming."
                : $"Prepared {result.PreparedTransmit.GeneratorName} output; ready to arm.";
            WsjtxRxStatus = result.Status;
        });
    }

    private async Task PrepareAndArmQueuedWsjtxTransmitAsync(string actionLabel)
    {
        await PrepareWsjtxQueuedTransmitAsync();

        if (WsjtxPreparedTransmit is null)
        {
            return;
        }

        ArmPreparedWsjtxTransmit();
        if (WsjtxTransmitArmedLocal)
        {
            WsjtxRxStatus = $"{actionLabel} armed for next slot";
        }
    }

    private async Task AutoReadyWsjtxQueuedTransmitAsync(string status, string actionLabel)
    {
        try
        {
            WsjtxPreparedTransmitStatus = "Auto-preparing next reply...";
            WsjtxTransmitArmStatus = "Auto-readying next reply for the next slot.";
            await PrepareAndArmQueuedWsjtxTransmitAsync(actionLabel).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (WsjtxTransmitArmedLocal)
                {
                    WsjtxRxStatus = $"{status} | Auto-readied next reply";
                    WsjtxTransmitArmStatus = "Next reply is ready for the next slot.";
                }
                else if (WsjtxPreparedTransmit is not null)
                {
                    WsjtxRxStatus = $"{status} | Auto-staged next reply, but arming needs attention";
                }
                else
                {
                    WsjtxRxStatus = $"{status} | Auto-ready failed";
                }
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                WsjtxRxStatus = $"{status} | Auto-ready failed: {ex.Message}";
                WsjtxTransmitArmStatus = "Auto-ready failed.";
            });
        }
    }

    [RelayCommand]
    private void ArmPreparedWsjtxTransmit()
    {
        if (WsjtxPreparedTransmit is null)
        {
            WsjtxTransmitArmedLocal = false;
            WsjtxTransmitArmStatus = "Prepare TX signal before arming.";
            WsjtxRxStatus = "Nothing prepared to arm";
            return;
        }

        var interlockError = ValidateWsjtxLiveTransmitInterlock();
        if (interlockError is not null)
        {
            WsjtxTransmitArmedLocal = false;
            WsjtxAwaitingReply = false;
            WsjtxTransmitArmStatus = interlockError;
            WsjtxRxStatus = "TX blocked by interlock";
            return;
        }

        WsjtxTransmitArmedLocal = true;
        WsjtxAwaitingReply = false;
        WsjtxTransmitArmStatus = $"Armed {WsjtxPreparedTransmit.ModeLabel} TX at {WsjtxPreparedTransmit.TxAudioFrequencyHz:+0;-0;0} Hz";
        WsjtxRxStatus = "Live TX armed for next slot";
    }

    [RelayCommand]
    private void DisarmWsjtxTransmit()
    {
        WsjtxTransmitArmedLocal = false;
        WsjtxAwaitingReply = false;
        WsjtxTransmitArmStatus = WsjtxPreparedTransmit is null
            ? "Nothing armed."
            : "Prepared TX retained; not armed.";
        WsjtxRxStatus = "TX disarmed";
    }

    private void UpsertWsjtxMessage(WsjtxDecodeMessage message)
    {
        var incoming = BuildWsjtxMessageItem(message);
        var existingIndex = FindExistingWsjtxMessageIndex(WsjtxMessages, message);
        var isNewMessage = existingIndex < 0;
        if (existingIndex >= 0)
        {
            var previouslySelected = SelectedWsjtxMessage is not null
                && ReferenceEquals(WsjtxMessages[existingIndex], SelectedWsjtxMessage);
            WsjtxMessages[existingIndex] = incoming;
            if (previouslySelected)
            {
                SelectedWsjtxMessage = incoming;
            }
        }
        else
        {
            WsjtxMessages.Insert(0, incoming);
        }

        for (var i = WsjtxMessages.Count - 1; i >= 0; i--)
        {
            if ((DateTime.UtcNow - WsjtxMessages[i].TimestampUtc).TotalMinutes > 30)
            {
                var wasSelected = ReferenceEquals(WsjtxMessages[i], SelectedWsjtxMessage);
                WsjtxMessages.RemoveAt(i);
                if (wasSelected)
                {
                    SelectedWsjtxMessage = null;
                }
            }
        }

        while (WsjtxMessages.Count > 200)
        {
            var removeIndex = WsjtxMessages.Count - 1;
            var wasSelected = ReferenceEquals(WsjtxMessages[removeIndex], SelectedWsjtxMessage);
            WsjtxMessages.RemoveAt(removeIndex);
            if (wasSelected)
            {
                SelectedWsjtxMessage = null;
            }
        }

        RebuildWsjtxRxFrequencyMessages();
        TrackWsjtxConversationMessage(incoming);

        if (isNewMessage && IsWsjtxDirectedToOperator(message) && !incoming.IsOwnTransmit)
        {
            PlayWsjtxDirectedAlert();
        }
    }

    private bool IsWsjtxDirectedToOperator(WsjtxDecodeMessage message)
    {
        if (message.IsDirectedToMe)
        {
            return true;
        }

        var myCall = FormatCallsign(WsjtxOperatorCallsign);
        return !string.IsNullOrWhiteSpace(myCall) && WsjtxMessageMentionsCall(message.MessageText, myCall);
    }

    private void PlayWsjtxDirectedAlert()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastWsjtxDirectedAlertUtc).TotalSeconds < 3)
        {
            return;
        }

        _lastWsjtxDirectedAlertUtc = now;

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _ = Task.Run(PlayWsjtxDirectedAlertCore);
        }
        catch
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private static void PlayWsjtxDirectedAlertCore()
    {
        try
        {
            if (PlaySound(WsjtxDirectedAlertWave, IntPtr.Zero, 0x0004 | 0x0002))
            {
                return;
            }

            _ = MessageBeep(0x00000040);
        }
        catch
        {
        }
    }

    private static byte[] CreateWsjtxDirectedAlertWave()
    {
        const int sampleRate = 44100;
        const short channels = 1;
        const short bitsPerSample = 16;
        const double durationSeconds = 0.28;
        var sampleCount = (int)(sampleRate * durationSeconds);
        var dataBytes = sampleCount * sizeof(short);
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataBytes);

        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (double)sampleRate;
            var frequency = t < durationSeconds * 0.48 ? 1046.5 : 1318.5;
            var attack = Math.Min(1.0, t / 0.015);
            var release = Math.Min(1.0, (durationSeconds - t) / 0.035);
            var envelope = Math.Min(attack, release);
            var sample = (short)(Math.Sin(2.0 * Math.PI * frequency * t) * 18000 * envelope);
            writer.Write(sample);
        }

        return stream.ToArray();
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool MessageBeep(uint uType);

    [DllImport("winmm.dll", SetLastError = false)]
    private static extern bool PlaySound(byte[] pszSound, IntPtr hmod, uint fdwSound);

    private void InsertOutgoingWsjtxMessage(WsjtxPreparedTransmit prepared)
    {
        var outgoing = BuildOutgoingWsjtxMessageItem(prepared);
        WsjtxMessages.Insert(0, outgoing);
        InsertWsjtxConversationMessage(outgoing);

        while (WsjtxMessages.Count > 200)
        {
            var removeIndex = WsjtxMessages.Count - 1;
            var wasSelected = ReferenceEquals(WsjtxMessages[removeIndex], SelectedWsjtxMessage);
            WsjtxMessages.RemoveAt(removeIndex);
            if (wasSelected)
            {
                SelectedWsjtxMessage = null;
            }
        }

        RebuildWsjtxRxFrequencyMessages();
    }

    private void TrackWsjtxConversationMessage(WsjtxMessageItem incoming)
    {
        var myCall = FormatCallsign(WsjtxOperatorCallsign);
        if (string.IsNullOrWhiteSpace(myCall))
        {
            return;
        }

        if (WsjtxCallingCq && incoming.IsDirectedToMe)
        {
            InsertWsjtxConversationMessage(incoming);
            return;
        }

        if (WsjtxActiveSession is null)
        {
            return;
        }

        var state = ClassifyWsjtxQsoState(incoming.MessageText, myCall);
        if (!string.Equals(state.OtherCall, WsjtxActiveSession.OtherCall, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Math.Abs(incoming.FrequencyOffsetHz - WsjtxActiveSession.FrequencyOffsetHz) > GetWsjtxRxFrequencyWindowHz(incoming.ModeText))
        {
            return;
        }

        InsertWsjtxConversationMessage(incoming);
    }

    private void InsertWsjtxConversationMessage(WsjtxMessageItem message)
    {
        var existingIndex = WsjtxConversationMessages
            .ToList()
            .FindIndex(item =>
                item.TimestampUtc == message.TimestampUtc
                && item.FrequencyOffsetHz == message.FrequencyOffsetHz
                && string.Equals(item.MessageText, message.MessageText, StringComparison.Ordinal)
                && string.Equals(item.ModeText, message.ModeText, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            WsjtxConversationMessages[existingIndex] = message;
        }
        else
        {
            WsjtxConversationMessages.Add(message);
        }

        while (WsjtxConversationMessages.Count > 24)
        {
            WsjtxConversationMessages.RemoveAt(0);
        }

        OnPropertyChanged(nameof(WsjtxHasConversationMessages));
        OnPropertyChanged(nameof(WsjtxConversationStatus));
    }

    private int FindExistingWsjtxMessageIndex(IReadOnlyList<WsjtxMessageItem> collection, WsjtxDecodeMessage message)
    {
        var normalizedText = NormalizeWsjtxMessageText(message.MessageText);
        var windowSeconds = GetWsjtxDuplicateWindowSeconds(message.ModeLabel);
        for (var i = 0; i < collection.Count; i++)
        {
            var existing = collection[i];
            if (!string.Equals(existing.ModeText, message.ModeLabel, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(NormalizeWsjtxMessageText(existing.MessageText), normalizedText, StringComparison.Ordinal))
            {
                continue;
            }

            if (Math.Abs(existing.FrequencyOffsetHz - message.FrequencyOffsetHz) > 25)
            {
                continue;
            }

            if (Math.Abs((existing.TimestampUtc - message.TimestampUtc).TotalSeconds) > windowSeconds)
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    private void RebuildWsjtxRxFrequencyMessages()
    {
        WsjtxRxFrequencyMessages.Clear();
        foreach (var item in WsjtxMessages)
        {
            if (IsWsprMode(item.ModeText)
                || Math.Abs(item.FrequencyOffsetHz - WsjtxRxAudioFrequencyHz) <= GetWsjtxRxFrequencyPaneWindowHz(item.ModeText))
            {
                WsjtxRxFrequencyMessages.Add(item);
            }
        }

        OnPropertyChanged(nameof(WsjtxHasRxFrequencyMessages));
    }

    private void HandleWsjtxAutoSequence(WsjtxDecodeMessage message)
    {
        if (!WsjtxAutoSequenceEnabled)
        {
            return;
        }

        var myCall = FormatCallsign(WsjtxOperatorCallsign);
        if (string.IsNullOrWhiteSpace(myCall))
        {
            return;
        }

        var state = ClassifyWsjtxQsoState(message.MessageText, myCall);
        if (WsjtxCallingCq && message.IsDirectedToMe && !string.IsNullOrWhiteSpace(state.OtherCall))
        {
            if (!IsWsjtxMessageOnExpectedReplyLane(message))
            {
                return;
            }

            WsjtxCallingCq = false;
            WsjtxActiveSession = new WsjtxActiveSession(state.OtherCall, message.FrequencyOffsetHz, message.ModeLabel);
            SelectAndTrackWsjtxMessage(message);
            PromoteTopWsjtxSuggestion($"AutoSeq: {state.OtherCall} replied to CQ");
            return;
        }

        if (WsjtxActiveSession is null || string.IsNullOrWhiteSpace(state.OtherCall))
        {
            return;
        }

        if (!string.Equals(WsjtxActiveSession.ModeLabel, message.ModeLabel, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(WsjtxActiveSession.OtherCall, state.OtherCall, StringComparison.OrdinalIgnoreCase))
        {
            if (message.IsDirectedToMe && IsWsjtxMessageOnExpectedReplyLane(message))
            {
                WsjtxRxStatus = $"Additional caller {state.OtherCall} heard; staying with {WsjtxActiveSession.OtherCall}";
            }
            return;
        }

        if (Math.Abs(WsjtxActiveSession.FrequencyOffsetHz - message.FrequencyOffsetHz) > GetWsjtxRxFrequencyWindowHz(message.ModeLabel))
        {
            return;
        }

        WsjtxActiveSession = WsjtxActiveSession with { FrequencyOffsetHz = message.FrequencyOffsetHz };
        SelectAndTrackWsjtxMessage(message);
        PromoteTopWsjtxSuggestion($"AutoSeq: next reply for {state.OtherCall}");
    }

    private bool IsWsjtxMessageOnExpectedReplyLane(WsjtxDecodeMessage message)
    {
        var referenceHz = WsjtxHoldTxFrequency ? WsjtxTxAudioFrequencyHz : WsjtxRxAudioFrequencyHz;
        if (WsjtxCallingCq)
        {
            referenceHz = WsjtxTxAudioFrequencyHz;
        }

        return Math.Abs(message.FrequencyOffsetHz - referenceHz) <= GetWsjtxRxFrequencyWindowHz(message.ModeLabel);
    }

    private void SelectAndTrackWsjtxMessage(WsjtxDecodeMessage message)
    {
        var match = WsjtxMessages.FirstOrDefault(item =>
            item.TimestampUtc == message.TimestampUtc
            && item.FrequencyOffsetHz == message.FrequencyOffsetHz
            && string.Equals(item.MessageText, message.MessageText, StringComparison.Ordinal)
            && string.Equals(item.ModeText, message.ModeLabel, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            SelectedWsjtxMessage = match;
        }

        WsjtxRxAudioFrequencyHz = message.FrequencyOffsetHz;
        if (!WsjtxHoldTxFrequency)
        {
            WsjtxTxAudioFrequencyHz = message.FrequencyOffsetHz;
        }
    }

    private WsjtxMessageItem? FindActiveWsjtxConversationAnchor(string myCall)
    {
        if (WsjtxActiveSession is null || string.IsNullOrWhiteSpace(myCall))
        {
            return null;
        }

        return WsjtxMessages.FirstOrDefault(item =>
        {
            if (!string.Equals(item.ModeText, WsjtxActiveSession.ModeLabel, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var state = ClassifyWsjtxQsoState(item.MessageText, myCall);
            if (!string.Equals(state.OtherCall, WsjtxActiveSession.OtherCall, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (item.IsOwnTransmit)
            {
                return false;
            }

            return true;
        });
    }

    private void PromoteTopWsjtxSuggestion(string status)
    {
        var top = WsjtxSuggestedMessages.FirstOrDefault();
        if (top is null)
        {
            return;
        }

        var hadPreparedAudio = WsjtxPreparedTransmit is not null || _wsjtxPreparedTransmitClip is not null;
        SelectedWsjtxSuggestedMessage = top;
        WsjtxQueuedTransmitMessage = ShouldAutoStageWsjtxReplies ? top : null;
        if (ShouldAutoStageWsjtxReplies && !_wsjtxManualTransmitOverride)
        {
            SetWsjtxManualTransmitText(top.MessageText, isManualOverride: false);
        }
        else if (!ShouldAutoStageWsjtxReplies && !_wsjtxManualTransmitOverride)
        {
            SetWsjtxManualTransmitText(string.Empty, isManualOverride: false);
        }
        WsjtxPreparedTransmit = null;
        _wsjtxPreparedTransmitClip = null;
        WsjtxPreparedTransmitStatus = ShouldAutoStageWsjtxReplies
            ? (hadPreparedAudio
                ? "Previous prepared audio is stale; next reply auto-staged."
                : "Next reply auto-staged.")
            : (hadPreparedAudio
                ? "Previous prepared audio is stale; choose Send Next when ready."
                : "Next reply suggested; choose Send Next when ready.");
        WsjtxPreparedTransmitPath = "No prepared TX artifact.";
        WsjtxTransmitArmedLocal = false;
        WsjtxAwaitingReply = false;
        WsjtxTransmitArmStatus = ShouldAutoStageWsjtxReplies
            ? "Next reply staged automatically."
            : "Next reply suggested.";
        WsjtxRxStatus = ShouldAutoStageWsjtxReplies
            ? $"{status} | Auto-staged next reply"
            : $"{status} | Next reply suggested";

        if (ShouldAutoReadyWsjtxReplies && WsjtxQueuedTransmitMessage is not null)
        {
            _ = AutoReadyWsjtxQueuedTransmitAsync(status, WsjtxQueuedTransmitMessage.Label);
        }
    }

    private static WsjtxMessageItem BuildWsjtxMessageItem(WsjtxDecodeMessage message)
    {
        var localTime = message.TimestampUtc.ToLocalTime();
        var isDirected = message.IsDirectedToMe;
        var isCq = message.IsCq;
        var normalized = NormalizeWsjtxMessageText(message.MessageText);
        var highlight = GetWsjtxHighlight(normalized, isDirected, isCq, false);
        var badgeText = IsWsprMode(message.ModeLabel)
            ? "WSPR"
            : highlight.BadgeText;
        return new WsjtxMessageItem(
            message.TimestampUtc,
            localTime.ToString("HH:mm:ss"),
            message.ModeLabel,
            $"{message.SnrDb} dB",
            $"{message.DeltaTimeSeconds:+0.0;-0.0;0.0}",
            FormatWsjtxFrequencyText(message),
            message.MessageText,
            message.FrequencyOffsetHz,
            message.SnrDb,
            message.DeltaTimeSeconds,
            isDirected,
            isCq,
            false,
            highlight.RowBackground,
            highlight.AccentBrush,
            highlight.MessageBrush,
            highlight.MetaBrush,
            badgeText,
            highlight.ShowBadge,
            highlight.BadgeBackground,
            highlight.BadgeForeground);
    }

    private static string FormatWsjtxFrequencyText(WsjtxDecodeMessage message)
    {
        if (IsWsprMode(message.ModeLabel) && message.FrequencyOffsetHz > 100_000)
        {
            return $"{message.FrequencyOffsetHz / 1_000_000d:0.000000} MHz";
        }

        return $"{message.FrequencyOffsetHz:+0;-0;0} Hz";
    }

    private static WsjtxMessageItem BuildOutgoingWsjtxMessageItem(WsjtxPreparedTransmit prepared)
    {
        var timestampUtc = DateTime.UtcNow;
        var localTime = timestampUtc.ToLocalTime();
        var normalized = NormalizeWsjtxMessageText(prepared.MessageText);
        var highlight = GetWsjtxHighlight(normalized, false, normalized.StartsWith("CQ ", StringComparison.OrdinalIgnoreCase), true);
        return new WsjtxMessageItem(
            timestampUtc,
            localTime.ToString("HH:mm:ss"),
            prepared.ModeLabel,
            "TX",
            "--",
            $"{prepared.TxAudioFrequencyHz:+0;-0;0} Hz",
            prepared.MessageText,
            prepared.TxAudioFrequencyHz,
            0,
            0,
            false,
            normalized.StartsWith("CQ ", StringComparison.OrdinalIgnoreCase),
            true,
            highlight.RowBackground,
            highlight.AccentBrush,
            highlight.MessageBrush,
            highlight.MetaBrush,
            highlight.BadgeText,
            highlight.ShowBadge,
            highlight.BadgeBackground,
            highlight.BadgeForeground);
    }

    private static string NormalizeWsjtxMessageText(string messageText) =>
        string.Join(' ', messageText
            .Trim()
            .ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static WsjtxHighlightStyle GetWsjtxHighlight(string normalizedText, bool isDirected, bool isCq, bool isOwnTransmit)
    {
        if (isOwnTransmit)
        {
            return new WsjtxHighlightStyle(
                "#231B12",
                "#FFC27A",
                "#FFF4E8",
                "#D4B694",
                "TX",
                true,
                "#7A4A1D",
                "#FFF7EF");
        }

        if (isDirected)
        {
            return new WsjtxHighlightStyle(
                "#20345B",
                "#7DC2FF",
                "#F7FBFF",
                "#D1E4FF",
                "TO ME",
                true,
                "#0D6EBA",
                "#FFFFFF");
        }

        if (TryGetSpecialCqLabel(normalizedText, out var cqLabel))
        {
            return new WsjtxHighlightStyle(
                cqLabel == "CQ DX" ? "#231722" : "#1A1F2C",
                cqLabel == "CQ DX" ? "#FFB5EA" : "#C7D4FF",
                cqLabel == "CQ DX" ? "#FFF0FB" : "#F2F5FF",
                cqLabel == "CQ DX" ? "#DDBAD4" : "#B8C5EA",
                cqLabel,
                true,
                cqLabel == "CQ DX" ? "#7A2E6A" : "#314D84",
                cqLabel == "CQ DX" ? "#FFF4FD" : "#F5F8FF");
        }

        if (normalizedText == "QRZ" || normalizedText.StartsWith("QRZ ", StringComparison.OrdinalIgnoreCase))
        {
            return new WsjtxHighlightStyle(
                "#222018",
                "#F0D58F",
                "#FFF9EA",
                "#D4C49B",
                "QRZ",
                true,
                "#6E5A21",
                "#FFF8E7");
        }

        if (isCq)
        {
            return new WsjtxHighlightStyle(
                "#17261D",
                "#9DDEAE",
                "#ECFFF1",
                "#B5D7BF",
                "CQ",
                true,
                "#255235",
                "#F5FFF7");
        }

        return new WsjtxHighlightStyle(
            "#0B1018",
            "#AFC4FF",
            "#E6EAF7",
            "#98A2BE",
            "FT",
            false,
            "Transparent",
            "#C4C8D8");
    }

    private static bool TryGetSpecialCqLabel(string normalizedText, out string label)
    {
        label = string.Empty;
        if (!normalizedText.StartsWith("CQ ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tokens = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            return false;
        }

        var second = tokens[1];
        if (string.Equals(second, "DX", StringComparison.OrdinalIgnoreCase))
        {
            label = "CQ DX";
            return true;
        }

        if (IsDirectionalCqToken(second))
        {
            label = $"CQ {second}";
            return true;
        }

        return false;
    }

    private static bool IsDirectionalCqToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return token.ToUpperInvariant() switch
        {
            "AF" or "AN" or "AS" or "EU" or "NA" or "OC" or "SA" or
            "JA" or "VK" or "DX" or "TEST" => true,
            _ => token.Length is >= 1 and <= 4
                && token.Any(char.IsLetter)
                && token.All(ch => char.IsLetterOrDigit(ch) || ch == '/')
                && !LooksLikeCallsign(token)
        };
    }

    private static bool LooksLikeCallsign(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var upper = token.ToUpperInvariant();
        return upper.Any(char.IsDigit) && upper.Any(char.IsLetter) && upper.Length >= 3;
    }

    private static double GetWsjtxDuplicateWindowSeconds(string modeLabel) => modeLabel.Trim().ToUpperInvariant() switch
    {
        "WSPR" or "FST4W" => 180,
        "JT65" or "JT9" or "JT4" => 120,
        _ => 90,
    };

    private static int GetWsjtxRxFrequencyWindowHz(string modeLabel) => modeLabel.Trim().ToUpperInvariant() switch
    {
        "WSPR" or "FST4W" => 20,
        "MSK144" => 200,
        _ => 100,
    };

    private static int GetWsjtxRxFrequencyPaneWindowHz(string modeLabel) => modeLabel.Trim().ToUpperInvariant() switch
    {
        "WSPR" or "FST4W" => 20,
        "MSK144" => 50,
        _ => 0,
    };

    private sealed record WsjtxHighlightStyle(
        string RowBackground,
        string AccentBrush,
        string MessageBrush,
        string MetaBrush,
        string BadgeText,
        bool ShowBadge,
        string BadgeBackground,
        string BadgeForeground);

    private enum WsjtxQsoStage
    {
        None,
        Cq,
        Qrz,
        ReportToMe,
        RogerToMe,
        Rr73ToMe,
        SignoffToMe,
    }

    private sealed record WsjtxQsoState(WsjtxQsoStage Stage, string? OtherCall);

    private readonly record struct WsjtxLongwaveLogContext(string OperatorCallsign, string StationCallsign, int SignalReportDb);

    [RelayCommand]
    private void StartSstvReceive()
    {
        if (_sstvDecoderHost is null)
        {
            SstvRxStatus = "SSTV decoder host unavailable";
            return;
        }

        _ = StartSstvReceiveCoreAsync();
    }

    [RelayCommand]
    private void StopSstvReceive()
    {
        if (_sstvDecoderHost is null)
        {
            return;
        }

        _ = _sstvDecoderHost.StopAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void ForceStartSstvReceive()
    {
        if (_sstvDecoderHost is null)
        {
            SstvRxStatus = "SSTV decoder host unavailable";
            return;
        }

        _ = ForceStartSstvReceiveCoreAsync();
    }

    [RelayCommand]
    private void ResetSstvSession()
    {
        if (_sstvDecoderHost is not null)
        {
            _ = _sstvDecoderHost.ResetAsync(CancellationToken.None);
        }

        SstvRxStatus = "SSTV receiver ready";
        SstvImageStatus = "No image captured yet";
        SstvSessionNotes = SstvIdleSessionNotes;
        SstvDecodedFskIdCallsign = string.Empty;
        UpdateSstvPreview(null);
    }

    [RelayCommand]
    private void RefreshSstvArchive()
    {
        LoadSstvArchiveImages();
    }

    [RelayCommand]
    private void StartWefaxReceive()
    {
        if (_wefaxDecoderHost is null)
        {
            WefaxRxStatus = "WeFAX decoder host unavailable";
            return;
        }

        _ = StartWefaxReceiveCoreAsync(forceNow: false);
    }

    [RelayCommand]
    private void StartWefaxNow()
    {
        if (_wefaxDecoderHost is null)
        {
            WefaxRxStatus = "WeFAX decoder host unavailable";
            return;
        }

        _ = StartWefaxReceiveCoreAsync(forceNow: true);
    }

    [RelayCommand]
    private void StopWefaxReceive()
    {
        if (_wefaxDecoderHost is null)
        {
            return;
        }

        _ = _wefaxDecoderHost.StopAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void ResetWefaxSession()
    {
        if (_wefaxDecoderHost is not null)
        {
            _ = _wefaxDecoderHost.ResetAsync(CancellationToken.None);
        }

        WefaxRxStatus = "WeFAX receiver ready";
        WefaxImageStatus = "No WeFAX image captured yet";
        WefaxSessionNotes = "Live auto-align is off by default because real maps can fool seam tracking. Use Start RX for scheduled captures, or Start Now if you joined late.";
        UpdateWefaxPreview(null);
    }

    [RelayCommand]
    private void RefreshWefaxArchive()
    {
        LoadWefaxArchiveImages();
    }

    [RelayCommand]
    private void RefreshWefaxSchedule()
    {
        RebuildWefaxSchedule();
    }

    [RelayCommand]
    private void ApplyWefaxScheduleItem(WefaxScheduleItem? item)
    {
        if (item is null)
        {
            return;
        }

        WefaxSelectedMode = item.ModeLabel;
        WefaxSelectedFrequency = item.FrequencyLabel;
        SelectedWefaxScheduleItem = item;
        WefaxScheduleStatus = $"Selected {item.Station}: {item.Product} at {item.TimeText} UTC.";
    }

    [RelayCommand]
    private void ApplySstvReplyTemplate(string template)
    {
        if (!string.IsNullOrWhiteSpace(template) && SelectedSstvReplyOverlayItem is not null)
        {
            SstvReplyPresetKind = null;
            SelectedSstvReplyOverlayItem.Text = ExpandSstvReplyMacro(template);
            SstvTransmitStatus = "Reply text changed; prepare TX when ready.";
        }
    }

    [RelayCommand]
    [SupportedOSPlatform("windows")]
    private async Task PrepareSstvReplyTransmitAsync()
    {
        if (_sstvTransmitService is null)
        {
            SstvTransmitStatus = "Native SSTV TX builder unavailable";
            return;
        }

        if (SelectedSstvReplyBaseImage is null)
        {
            SstvTransmitStatus = "Choose a reply base image first";
            return;
        }

        try
        {
            DeletePreparedSstvTransmitArtifacts();
            var timestamp = DateTime.Now;
            Directory.CreateDirectory(_sstvTxDirectory);
            var stem = $"{timestamp:yyyyMMdd_HHmmss}_{SstvSelectedTxMode.ToLowerInvariant().Replace(' ', '_')}";
            var pngPath = Path.Combine(_sstvTxDirectory, $"{stem}.png");
            var wavPath = Path.Combine(_sstvTxDirectory, $"{stem}.wav");
            var preparedFingerprint = BuildSstvTransmitFingerprint();
            var preparedMode = SstvSelectedTxMode;
            var transmitOptions = BuildSstvTransmitOptions();
            var preparedTextOverlays = SstvReplyOverlayItems
                .Select(item => new SstvOverlayItemViewModel
                {
                    Text = ExpandSstvReplyMacro(item.Text),
                    X = item.X,
                    Y = item.Y,
                    FontSize = item.FontSize,
                    FontFamilyName = item.FontFamilyName,
                    Red = item.Red,
                    Green = item.Green,
                    Blue = item.Blue
                })
                .ToArray();
            var rgb24 = SstvReplyRenderer.RenderRgb24(
                SelectedSstvReplyBaseImage.Path,
                preparedTextOverlays,
                SstvReplyImageOverlayItems,
                pngPath,
                out var width,
                out var height);

            _sstvPreparedTransmitClip = await _sstvTransmitService
                .BuildTransmitClipAsync(
                    preparedMode,
                    rgb24,
                    width,
                    height,
                    transmitOptions,
                    CancellationToken.None)
                .ConfigureAwait(false);

            SstvReplyRenderer.WriteWaveFile(wavPath, _sstvPreparedTransmitClip);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _sstvPreparedTransmitFingerprint = preparedFingerprint;
                _sstvPreparedTransmitMode = preparedMode;
                _sstvPreparedTransmitImageFile = pngPath;
                _sstvPreparedTransmitWaveFile = wavPath;
                var idParts = new List<string>();
                if (transmitOptions.FskIdEnabled)
                {
                    idParts.Add($"FSKID {transmitOptions.FskIdCallsign}");
                }

                if (transmitOptions.CwIdEnabled)
                {
                    idParts.Add($"CW ID {transmitOptions.CwIdText} @ {transmitOptions.CwIdFrequencyHz} Hz/{transmitOptions.CwIdWpm} WPM");
                }

                _sstvPreparedTransmitCwIdSummary = idParts.Count == 0 ? null : string.Join(" + ", idParts);
                SstvPreparedTransmitPath = $"{pngPath}  |  {wavPath}";
                SstvPreparedTransmitImagePath = pngPath;
                SstvPreparedTransmitBitmap = new Bitmap(pngPath);
                OnPropertyChanged(nameof(SstvHasPreparedTransmitPreview));
                OnPropertyChanged(nameof(SstvHasPreparedTransmitClip));
                _sstvPreparedTransmitDurationSeconds = _sstvPreparedTransmitClip.PcmBytes.Length / (double)(_sstvPreparedTransmitClip.SampleRate * _sstvPreparedTransmitClip.Channels * 2);
                var idSummary = string.Join(
                    string.Empty,
                    transmitOptions.FskIdEnabled ? " + FSKID" : string.Empty,
                    transmitOptions.CwIdEnabled ? " + CWID" : string.Empty);
                SstvTransmitStatus = $"Prepared {preparedMode}{idSummary} TX ({_sstvPreparedTransmitDurationSeconds:0.0}s)";
                RefreshPreparedSstvTransmitSummary();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ClearPreparedSstvTransmit($"SSTV TX prepare failed: {ex.Message}");
            });
        }
    }

    private SstvTransmitOptions BuildSstvTransmitOptions()
    {
        var stationCallsign = string.IsNullOrWhiteSpace(SettingsCallsign)
            ? "CALL"
            : SettingsCallsign.Trim().ToUpperInvariant();
        var cwidText = (SstvTxCwIdText ?? string.Empty)
            .Replace("%m", stationCallsign, StringComparison.OrdinalIgnoreCase)
            .Replace("%tocall", SstvDecodedFskIdCallsign, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return new SstvTransmitOptions(
            SstvTxCwIdEnabled,
            cwidText,
            SstvTxCwIdFrequencyHz,
            SstvTxCwIdWpm,
            SstvTxFskIdEnabled,
            stationCallsign);
    }

    private string BuildSstvTransmitFingerprint()
    {
        var overlays = string.Join(
            "|",
            SstvReplyOverlayItems.Select(static item =>
                $"{item.Text}\u001f{item.X:0.###}\u001f{item.Y:0.###}\u001f{item.FontSize:0.###}\u001f{item.FontFamilyName}\u001f{item.ColorHex}"));
        var imageOverlays = string.Join(
            "|",
            SstvReplyImageOverlayItems.Select(static item =>
                $"{item.Path}\u001f{item.X:0.###}\u001f{item.Y:0.###}\u001f{item.Width:0.###}\u001f{item.Height:0.###}"));

        return string.Join(
            "\u001e",
            SelectedSstvReplyBaseImage?.Path ?? string.Empty,
            SstvSelectedTxMode,
            SstvTxCwIdEnabled,
            SstvTxCwIdText ?? string.Empty,
            SstvTxCwIdFrequencyHz,
            SstvTxCwIdWpm,
            SstvTxFskIdEnabled,
            SstvDecodedFskIdCallsign,
            overlays,
            imageOverlays);
    }

    private void ClearPreparedSstvTransmit(string status)
    {
        DeletePreparedSstvTransmitArtifacts();
        _sstvPreparedTransmitClip = null;
        _sstvPreparedTransmitFingerprint = null;
        _sstvPreparedTransmitMode = null;
        _sstvPreparedTransmitCwIdSummary = null;
        _sstvPreparedTransmitDurationSeconds = 0;
        SstvPreparedTransmitPath = "No prepared SSTV TX artifact.";
        SstvPreparedTransmitImagePath = "No prepared TX image.";
        SstvPreparedTransmitSummary = "No prepared SSTV TX image/audio.";
        SstvPreparedTransmitBitmap = null;
        OnPropertyChanged(nameof(SstvHasPreparedTransmitPreview));
        OnPropertyChanged(nameof(SstvHasPreparedTransmitClip));
        SstvTransmitStatus = status;
    }

    private void DeletePreparedSstvTransmitArtifacts()
    {
        SstvPreparedTransmitBitmap = null;
        foreach (var path in new[] { _sstvPreparedTransmitImageFile, _sstvPreparedTransmitWaveFile })
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best effort only: a preview or audio device may still be releasing the file.
            }
        }

        _sstvPreparedTransmitImageFile = null;
        _sstvPreparedTransmitWaveFile = null;
    }

    private void RefreshPreparedSstvTransmitSummary()
    {
        if (_sstvPreparedTransmitClip is null)
        {
            SstvPreparedTransmitSummary = "No prepared SSTV TX image/audio.";
            return;
        }

        var stale = string.Equals(_sstvPreparedTransmitFingerprint, BuildSstvTransmitFingerprint(), StringComparison.Ordinal)
            ? "ready"
            : "stale - prepare again before sending";
        var cwid = string.IsNullOrWhiteSpace(_sstvPreparedTransmitCwIdSummary)
            ? "CW ID off"
            : _sstvPreparedTransmitCwIdSummary;
        var route = SelectedTxDevice is null ? "No TX audio device selected" : $"TX route: {SelectedTxDevice.FriendlyName}";
        SstvPreparedTransmitSummary = $"{stale}: {_sstvPreparedTransmitMode ?? SstvSelectedTxMode}  |  {_sstvPreparedTransmitDurationSeconds:0.0}s  |  {cwid}  |  {route}";
    }

    private void AttachSstvReplyLayoutChangeTracking(ObservableCollection<SstvOverlayItemViewModel> items)
    {
        items.CollectionChanged += OnSstvReplyLayoutCollectionChanged;
        foreach (var item in items)
        {
            item.PropertyChanged += OnSstvReplyLayoutItemChanged;
        }
    }

    private void AttachSstvReplyLayoutChangeTracking(ObservableCollection<SstvImageOverlayItemViewModel> items)
    {
        items.CollectionChanged += OnSstvReplyLayoutCollectionChanged;
        foreach (var item in items)
        {
            item.PropertyChanged += OnSstvReplyLayoutItemChanged;
        }
    }

    private void OnSstvReplyLayoutCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is SstvOverlayItemViewModel textItem)
                {
                    textItem.PropertyChanged += OnSstvReplyLayoutItemChanged;
                }
                else if (item is SstvImageOverlayItemViewModel imageItem)
                {
                    imageItem.PropertyChanged += OnSstvReplyLayoutItemChanged;
                }
            }
        }

        MarkSstvReplyLayoutDirty();
    }

    private void OnSstvReplyLayoutItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkSstvReplyLayoutDirty();
    }

    private void MarkSstvReplyLayoutDirty()
    {
        RefreshPreparedSstvTransmitSummary();
        if (!SstvTxIsSending)
        {
            SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
        }
    }

    [RelayCommand]
    [SupportedOSPlatform("windows")]
    private async Task SendSstvReplyTransmitAsync()
    {
        if (_sstvTxSendInFlight)
        {
            return;
        }

        _sstvTxSendInFlight = true;
        var txAudioStarted = false;
        var pttRaised = false;
        _sstvTxCts?.Cancel();
        _sstvTxCts?.Dispose();
        _sstvTxCts = new CancellationTokenSource();
        var token = _sstvTxCts.Token;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SstvTxIsSending = true;
                SstvTransmitStatus = $"Checking prepared {SstvSelectedTxMode} TX...";
            });

            if (_sstvPreparedTransmitClip is null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SstvTransmitStatus = "SSTV TX blocked: prepare TX first.";
                });
                return;
            }

            if (!string.Equals(_sstvPreparedTransmitFingerprint, BuildSstvTransmitFingerprint(), StringComparison.Ordinal))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RefreshPreparedSstvTransmitSummary();
                    SstvTransmitStatus = "SSTV TX blocked: prepared image/audio is stale. Press Prepare TX again.";
                });
                return;
            }

            var interlockError = ValidateSstvLiveTransmitInterlock();
            if (interlockError is not null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SstvTransmitStatus = interlockError;
                    VoiceTxStatus = "TX audio idle";
                    RadioStatusSummary = interlockError;
                });
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RefreshPreparedSstvTransmitSummary();
                SstvTransmitStatus = $"Keying radio for {_sstvPreparedTransmitMode ?? SstvSelectedTxMode}...";
            });

            await TuneRadioForSstvAsync(SstvSelectedFrequency, strict: true).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            var route = BuildCurrentAudioRoute();
            var clip = WithLeadingSilence(_sstvPreparedTransmitClip, 250);
            var clipDurationMs = Math.Max(500, (int)Math.Ceiling(
                clip.PcmBytes.Length / (double)(clip.SampleRate * clip.Channels * 2) * 1000.0));

            await _audioService!.StartTransmitPcmAsync(route, clip, token).ConfigureAwait(false);
            txAudioStarted = true;
            await _radioService!.SetPttAsync(true, token).ConfigureAwait(false);
            pttRaised = true;
            await VerifySstvPttRaisedAsync(token).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                VoiceTxStatus = "SSTV TX audio live";
                SstvTransmitStatus = $"Sending {_sstvPreparedTransmitMode ?? SstvSelectedTxMode} on-air";
                RadioStatusSummary = $"SSTV TX live  |  {_sstvPreparedTransmitMode ?? SstvSelectedTxMode}  |  {SstvSelectedFrequency}";
            });

            await Task.Delay(clipDurationMs + 150, token).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ClearPreparedSstvTransmit($"SSTV TX sent: {_sstvPreparedTransmitMode ?? SstvSelectedTxMode}. Prepared artifacts discarded.");
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ClearPreparedSstvTransmit("SSTV TX stopped. Prepared artifacts discarded.");
                VoiceTxStatus = "TX audio idle";
                RadioStatusSummary = "SSTV TX stopped.";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SstvTransmitStatus = $"SSTV TX failed: {ex.Message}";
                VoiceTxStatus = "TX audio idle";
                RadioStatusSummary = $"SSTV TX failed: {ex.Message}";
            });
        }
        finally
        {
            try
            {
                if (txAudioStarted && _audioService is not null)
                {
                    await _audioService.StopTransmitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            try
            {
                if (pttRaised && _radioService is not null)
                {
                    await _radioService.SetPttAsync(false, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SstvTxIsSending = false;
                VoiceTxStatus = "TX audio idle";
                RefreshPreparedSstvTransmitSummary();
            });

            _sstvTxSendInFlight = false;
        }
    }

    [RelayCommand]
    private async Task StopSstvReplyTransmitAsync()
    {
        _sstvTxCts?.Cancel();

        try
        {
            if (_audioService is not null)
            {
                await _audioService.StopTransmitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            if (_radioService is not null)
            {
                await _radioService.SetPttAsync(false, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SstvTxIsSending = false;
            VoiceTxStatus = "TX audio idle";
            ClearPreparedSstvTransmit("SSTV TX stop requested. Prepared artifacts discarded.");
            RadioStatusSummary = "SSTV TX stop requested.";
        });
    }

    [RelayCommand]
    private void SaveSstvReplyLayoutTemplate()
    {
        Directory.CreateDirectory(_sstvTemplateDirectory);

        var normalizedName = string.IsNullOrWhiteSpace(SstvReplyTemplateName)
            ? $"Reply Template {DateTime.Now:yyyyMMdd_HHmmss}"
            : SstvReplyTemplateName.Trim();
        var fileName = string.Concat(normalizedName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var fullPath = Path.Combine(_sstvTemplateDirectory, $"{fileName}.json");

        var payload = new SstvOverlayTemplateFile(
            normalizedName,
            SstvReplyOverlayItems.Select(static item => new SstvOverlayTemplateItemFile(
                item.Text,
                item.X,
                item.Y,
                item.FontSize,
                item.FontFamilyName,
                item.ColorHex)).ToArray(),
            SstvReplyImageOverlayItems.Select(static item => new SstvImageOverlayTemplateItemFile(
                item.Label,
                item.Path,
                item.X,
                item.Y,
                item.Width,
                item.Height)).ToArray(),
            SstvReplyPresetKind);

        try
        {
            File.WriteAllText(fullPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            var presetText = string.IsNullOrWhiteSpace(SstvReplyPresetKind) ? string.Empty : $" ({SstvReplyPresetKind})";
            SstvReplyTemplateStatus = $"Saved template '{normalizedName}'{presetText}";
            LoadSstvArchiveImages();
            SelectedSstvReplyLayoutTemplate = SstvReplyLayoutTemplates.FirstOrDefault(t =>
                string.Equals(t.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            SstvReplyTemplateStatus = $"Template save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void LoadSstvReplyLayoutTemplate()
    {
        if (SelectedSstvReplyLayoutTemplate is null)
        {
            SstvReplyTemplateStatus = "Choose a saved template first";
            return;
        }

        try
        {
            var json = File.ReadAllText(SelectedSstvReplyLayoutTemplate.Path);
            var payload = JsonSerializer.Deserialize<SstvOverlayTemplateFile>(json);
            if (payload is null || (payload.Items.Count == 0 && (payload.ImageItems?.Count ?? 0) == 0))
            {
                SstvReplyTemplateStatus = "Template was empty";
                return;
            }

            var items = payload.Items.Select(static item => new SstvOverlayItemViewModel
            {
                Text = item.Text,
                X = item.X,
                Y = item.Y,
                FontSize = item.FontSize,
                FontFamilyName = item.FontFamily,
            }).ToArray();

            foreach (var (overlay, saved) in items.Zip(payload.Items))
            {
                overlay.SetColorFromHex(saved.Color);
            }

            SstvReplyOverlayItems = new ObservableCollection<SstvOverlayItemViewModel>(items);
            SelectedSstvReplyOverlayItem = SstvReplyOverlayItems.FirstOrDefault();
            var missingImages = new List<string>();
            var imageItems = new List<SstvImageOverlayItemViewModel>();
            foreach (var item in payload.ImageItems ?? [])
            {
                if (!File.Exists(item.Path))
                {
                    missingImages.Add(item.Label);
                    continue;
                }

                try
                {
                    imageItems.Add(new SstvImageOverlayItemViewModel
                    {
                        Label = item.Label,
                        Path = item.Path,
                        Bitmap = new Bitmap(item.Path),
                        X = item.X,
                        Y = item.Y,
                        Width = item.Width,
                        Height = item.Height,
                    });
                }
                catch
                {
                    missingImages.Add(item.Label);
                }
            }

            SstvReplyImageOverlayItems = new ObservableCollection<SstvImageOverlayItemViewModel>(imageItems);
            SelectedSstvReplyImageOverlayItem = SstvReplyImageOverlayItems.FirstOrDefault();
            SstvReplyTemplateName = payload.Name;
            SstvReplyPresetKind = payload.PresetKind;
            var presetText = string.IsNullOrWhiteSpace(payload.PresetKind) ? string.Empty : $" ({payload.PresetKind})";
            var missingText = missingImages.Count == 0
                ? string.Empty
                : $" Missing image overlays: {string.Join(", ", missingImages)}.";
            SstvReplyTemplateStatus = $"Loaded template '{payload.Name}'{presetText}.{missingText}";
            SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
        }
        catch (Exception ex)
        {
            SstvReplyTemplateStatus = $"Template load failed: {ex.Message}";
        }
    }

    public void ImportSstvReplyBaseImage(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            SstvReplyTemplateStatus = "Choose an existing image to import";
            return;
        }

        var extension = Path.GetExtension(sourcePath);
        var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bmp",
            ".png",
            ".jpg",
            ".jpeg",
        };
        if (!supportedExtensions.Contains(extension))
        {
            SstvReplyTemplateStatus = "Import supports BMP, PNG, JPG, and JPEG images";
            return;
        }

        try
        {
            var destination = SstvReplyArchiveStore.ImportReplyBaseImage(sourcePath, _sstvReplyDirectory);
            LoadSstvArchiveImages();
            SelectedSstvReplyBaseImage = SstvReplyImages.FirstOrDefault(item =>
                string.Equals(item.Path, destination, StringComparison.OrdinalIgnoreCase));
            SstvReplyTemplateStatus = $"Imported reply base '{Path.GetFileName(destination)}'";
            SstvTransmitStatus = "Reply base imported; choose a quick layout or prepare TX.";
        }
        catch (Exception ex)
        {
            SstvReplyTemplateStatus = $"Import failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DuplicateSelectedSstvReplyBaseImage()
    {
        if (SelectedSstvReplyBaseImage is null || !File.Exists(SelectedSstvReplyBaseImage.Path))
        {
            SstvReplyTemplateStatus = "Choose a reply base image to duplicate";
            return;
        }

        try
        {
            var destination = SstvReplyArchiveStore.DuplicateReplyBaseImage(SelectedSstvReplyBaseImage.Path, _sstvReplyDirectory);
            LoadSstvArchiveImages();
            SelectedSstvReplyBaseImage = SstvReplyImages.FirstOrDefault(item =>
                string.Equals(item.Path, destination, StringComparison.OrdinalIgnoreCase));
            SstvReplyTemplateStatus = $"Duplicated reply base '{Path.GetFileName(destination)}'";
            SstvTransmitStatus = "Reply base duplicated; prepare TX when ready.";
        }
        catch (Exception ex)
        {
            SstvReplyTemplateStatus = $"Duplicate failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ArchiveSelectedSstvReplyBaseImage()
    {
        if (SelectedSstvReplyBaseImage is null || !File.Exists(SelectedSstvReplyBaseImage.Path))
        {
            SstvReplyTemplateStatus = "Choose a reply base image to archive";
            return;
        }

        try
        {
            var archived = SstvReplyArchiveStore.ArchiveFile(SelectedSstvReplyBaseImage.Path, Path.Combine(_sstvReplyDirectory, "archived"));
            LoadSstvArchiveImages();
            SstvReplyTemplateStatus = $"Archived reply base '{Path.GetFileName(archived)}'";
            ClearPreparedSstvTransmit("Reply base archived; prepare TX when ready.");
        }
        catch (Exception ex)
        {
            SstvReplyTemplateStatus = $"Archive failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ArchiveSelectedSstvReplyLayoutTemplate()
    {
        if (SelectedSstvReplyLayoutTemplate is null || !File.Exists(SelectedSstvReplyLayoutTemplate.Path))
        {
            SstvReplyTemplateStatus = "Choose a template to archive";
            return;
        }

        try
        {
            var archived = SstvReplyArchiveStore.ArchiveFile(SelectedSstvReplyLayoutTemplate.Path, Path.Combine(_sstvTemplateDirectory, "archived"));
            LoadSstvArchiveImages();
            SstvReplyTemplateStatus = $"Archived template '{Path.GetFileNameWithoutExtension(archived)}'";
        }
        catch (Exception ex)
        {
            SstvReplyTemplateStatus = $"Template archive failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddSstvReplyOverlay(string? template = null)
    {
        SstvReplyPresetKind = null;
        var text = string.IsNullOrWhiteSpace(template)
            ? "QSL SSTV - TNX DE %m"
            : template.Trim();
        var item = new SstvOverlayItemViewModel
        {
            Text = ExpandSstvReplyMacro(text),
            X = 24 + (SstvReplyOverlayItems.Count * 12),
            Y = 24 + (SstvReplyOverlayItems.Count * 28),
            FontSize = 18,
            FontFamilyName = "Segoe UI",
        };
        SstvReplyOverlayItems.Add(item);
        SelectedSstvReplyOverlayItem = item;
        SstvReplyTemplateStatus = $"Added text box '{item.Text}'";
        SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
    }

    [RelayCommand]
    private void ReplyToSelectedSstvReceivedImage()
    {
        if (SelectedSstvReceivedImage is null)
        {
            SstvReplyTemplateStatus = "Choose a received image first";
            return;
        }

        if (SelectedSstvReplyBaseImage is null)
        {
            SelectedSstvReplyBaseImage = SstvReplyImages.FirstOrDefault();
        }

        if (SelectedSstvReplyBaseImage is null)
        {
            SstvReplyTemplateStatus = "Choose or import a reply base image first";
            return;
        }

        ApplySstvReplyLayoutPreset(new SstvReplyLayoutPreset("QSL + RX", "qsl-rx"));
        SstvReplyTemplateStatus = $"Reply layout staged for '{SelectedSstvReceivedImage.Label}'";
    }

    [RelayCommand]
    private void ApplySstvReplyLayoutPreset(SstvReplyLayoutPreset preset)
    {
        SstvReplyPresetKind = preset.Kind;
        SstvReplyOverlayItems.Clear();
        SstvReplyImageOverlayItems.Clear();

        switch (preset.Kind)
        {
            case "cq":
                SstvReplyTemplateName = "CQ SSTV";
                AddSstvReplyTextOverlay("CQ SSTV", 86, 48, 34, "#FFFFD166");
                AddSstvReplyTextOverlay("DE %m", 98, 104, 30, "#FFFFFFFF");
                AddSstvReplyTextOverlay("%g  |  %f", 66, 164, 18, "#FF9BE7FF");
                AddSstvReplyTextOverlay("PSE K", 130, 210, 18, "#FFC4C8D8");
                break;
            case "qsl-rx":
                SstvReplyTemplateName = "QSL With RX Thumbnail";
                AddSelectedSstvReceivedImageOverlay(24, 24, 118, 92);
                AddSstvReplyTextOverlay("QSL - TNX SSTV", 154, 38, 24, "#FFFFD166");
                AddSstvReplyTextOverlay("DE %m", 166, 88, 24, "#FFFFFFFF");
                AddSstvReplyTextOverlay("%g", 206, 128, 18, "#FF9BE7FF");
                AddSstvReplyTextOverlay("73!", 218, 186, 28, "#FFFFFFFF");
                break;
            case "report":
                SstvReplyTemplateName = "SSTV Signal Report";
                AddSstvReplyTextOverlay("SSTV REPORT", 78, 40, 28, "#FFFFD166");
                AddSstvReplyTextOverlay("RSV 595", 104, 100, 34, "#FFFFFFFF");
                AddSstvReplyTextOverlay("Good copy - 73", 78, 166, 20, "#FF9BE7FF");
                AddSstvReplyTextOverlay("DE %m", 110, 210, 20, "#FFC4C8D8");
                break;
            case "73":
                SstvReplyTemplateName = "TNX QSO 73";
                AddSstvReplyTextOverlay("TNX QSO", 92, 58, 34, "#FFFFD166");
                AddSstvReplyTextOverlay("73 DE %m", 78, 124, 28, "#FFFFFFFF");
                AddSstvReplyTextOverlay("%g", 132, 184, 20, "#FF9BE7FF");
                break;
            case "id":
            default:
                SstvReplyTemplateName = "Station ID";
                AddSstvReplyTextOverlay("%m", 92, 74, 42, "#FFFFFFFF");
                AddSstvReplyTextOverlay("%g", 128, 140, 24, "#FFFFD166");
                AddSstvReplyTextOverlay("%d %t", 94, 192, 18, "#FFC4C8D8");
                break;
        }

        SelectedSstvReplyOverlayItem = SstvReplyOverlayItems.FirstOrDefault();
        SelectedSstvReplyImageOverlayItem = SstvReplyImageOverlayItems.FirstOrDefault();
        SstvReplyTemplateStatus = $"Applied preset '{preset.Label}'";
        SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
    }

    private void AddSstvReplyTextOverlay(string text, double x, double y, double fontSize, string colorHex)
    {
        var item = new SstvOverlayItemViewModel
        {
            Text = ExpandSstvReplyMacro(text),
            X = x,
            Y = y,
            FontSize = fontSize,
            FontFamilyName = "Segoe UI",
        };
        item.SetColorFromHex(colorHex);
        SstvReplyOverlayItems.Add(item);
    }

    private bool AddSelectedSstvReceivedImageOverlay(double x, double y, double width, double height)
    {
        var image = SelectedSstvReceivedImage ?? SstvReceivedImages.FirstOrDefault();
        if (image is null)
        {
            return false;
        }

        var item = SstvImageOverlayItemViewModel.FromImage(image, SstvReplyImageOverlayItems.Count);
        item.X = x;
        item.Y = y;
        item.Width = width;
        item.Height = height;
        SstvReplyImageOverlayItems.Add(item);
        return true;
    }

    private string ExpandSstvReplyMacro(string value)
    {
        var stationCallsign = string.IsNullOrWhiteSpace(SettingsCallsign)
            ? "CALL"
            : SettingsCallsign.Trim().ToUpperInvariant();
        var toCall = string.IsNullOrWhiteSpace(SstvDecodedFskIdCallsign)
            ? "TOCALL"
            : SstvDecodedFskIdCallsign.Trim().ToUpperInvariant();
        var grid = string.IsNullOrWhiteSpace(SettingsGridSquare)
            ? "GRID"
            : SettingsGridSquare.Trim().ToUpperInvariant();
        var now = DateTime.Now;
        return (value ?? string.Empty)
            .Replace("%tocall", toCall, StringComparison.OrdinalIgnoreCase)
            .Replace("%m", stationCallsign, StringComparison.OrdinalIgnoreCase)
            .Replace("%g", grid, StringComparison.OrdinalIgnoreCase)
            .Replace("%f", SstvSelectedFrequency, StringComparison.OrdinalIgnoreCase)
            .Replace("%r", SelectedSstvReceivedImage?.Label ?? "RX IMAGE", StringComparison.OrdinalIgnoreCase)
            .Replace("%d", now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("%t", now.ToString("HH:mm"), StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void AddSstvReceivedThumbnail()
    {
        SstvReplyPresetKind = null;
        if (SelectedSstvReceivedImage is null)
        {
            SstvReplyTemplateStatus = "Choose a received image first";
            return;
        }

        AddSelectedSstvReceivedImageOverlay(24 + (SstvReplyImageOverlayItems.Count * 14), 24 + (SstvReplyImageOverlayItems.Count * 14), 112, 86);
        var item = SstvReplyImageOverlayItems.Last();
        SelectedSstvReplyImageOverlayItem = item;
        SstvReplyTemplateStatus = $"Added received thumbnail '{item.Label}'";
        SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
    }

    [RelayCommand]
    private void RemoveSstvReceivedThumbnail()
    {
        SstvReplyPresetKind = null;
        if (SelectedSstvReplyImageOverlayItem is null)
        {
            return;
        }

        var item = SelectedSstvReplyImageOverlayItem;
        SstvReplyImageOverlayItems.Remove(item);
        SelectedSstvReplyImageOverlayItem = SstvReplyImageOverlayItems.FirstOrDefault();
        SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
    }

    [RelayCommand]
    private void RemoveSstvReplyOverlay()
    {
        SstvReplyPresetKind = null;
        if (SelectedSstvReplyOverlayItem is null)
        {
            return;
        }

        var item = SelectedSstvReplyOverlayItem;
        SstvReplyOverlayItems.Remove(item);
        SelectedSstvReplyOverlayItem = SstvReplyOverlayItems.FirstOrDefault();
        SstvTransmitStatus = "Reply layout changed; prepare TX when ready.";
    }

    private async Task StartSstvReceiveCoreAsync()
    {
        if (_sstvDecoderHost is null)
        {
            return;
        }

        await TuneRadioForSstvAsync(SstvSelectedFrequency);
        var config = new SstvDecoderConfiguration(NormalizeSstvModeSelection(SstvSelectedMode), SstvSelectedFrequency);
        await _sstvDecoderHost.ConfigureAsync(config, CancellationToken.None);
        await _sstvDecoderHost.StartAsync(CancellationToken.None);
    }

    private async Task ForceStartSstvReceiveCoreAsync()
    {
        if (_sstvDecoderHost is null)
        {
            return;
        }

        await TuneRadioForSstvAsync(SstvSelectedFrequency);
        var config = new SstvDecoderConfiguration(NormalizeSstvModeSelection(SstvSelectedMode), SstvSelectedFrequency);
        await _sstvDecoderHost.ConfigureAsync(config, CancellationToken.None);
        await _sstvDecoderHost.StartAsync(CancellationToken.None);
        await _sstvDecoderHost.ForceStartAsync(CancellationToken.None);
        SstvRxStatus = $"Force-start requested for {config.Mode}";
        SstvSessionNotes = $"ShackStack SSTV native sidecar  |  Signal  ---%  |  Mode {config.Mode}  |  FSKID none";
    }

    private async Task StartRttyReceiveCoreAsync()
    {
        if (_rttyDecoderHost is null)
        {
            return;
        }

        if (RttyDecodeCurrentRadioFrequency)
        {
            await SetRadioForRttyAudioDataModeAsync();
        }
        else
        {
            await TuneRadioForRttyAsync(RttySelectedFrequency);
        }

        var (shiftHz, baudRate) = ParseRttyProfile(RttySelectedProfile);
        var frequencyLabel = RttyDecodeCurrentRadioFrequency
            ? "Current radio frequency"
            : RttySelectedFrequency;
        var audioCenterHz = ParseRttyAudioCenterHz(RttyAudioCenterHz);
        var config = new RttyDecoderConfiguration(RttySelectedProfile, shiftHz, baudRate, frequencyLabel, audioCenterHz, RttyReversePolarity);
        await _rttyDecoderHost.ConfigureAsync(config, CancellationToken.None);
        await _rttyDecoderHost.StartAsync(CancellationToken.None);
        RttySessionNotes = "RTTY audio decoder running. IC-7300 should be in USB-D or LSB-D; native RTTY is for the rig's FSK/RTTY path.";
    }

    private async Task StartKeyboardReceiveCoreAsync()
    {
        if (_keyboardModeDecoderHost is null)
        {
            return;
        }

        if (KeyboardUseCurrentRadioFrequency)
        {
            await SetRadioForKeyboardAudioDataModeAsync();
        }
        else
        {
            await TuneRadioForKeyboardAsync(KeyboardSelectedFrequency);
        }

        await ConfigureKeyboardDecoderAsync();
        await _keyboardModeDecoderHost.StartAsync(CancellationToken.None);
        KeyboardSessionNotes = "PSK receiver running. Use USB-D/LSB-D and tune the waterfall trace near the selected audio center.";
    }

    private async Task ConfigureKeyboardDecoderAsync()
    {
        if (_keyboardModeDecoderHost is null)
        {
            return;
        }

        var frequencyLabel = KeyboardUseCurrentRadioFrequency
            ? "Current radio frequency"
            : KeyboardSelectedFrequency;
        var config = new KeyboardModeDecoderConfiguration(
            KeyboardSelectedMode,
            frequencyLabel,
            ParseKeyboardAudioCenterHz(KeyboardAudioCenterHz),
            KeyboardReversePolarity);
        await _keyboardModeDecoderHost.ConfigureAsync(config, CancellationToken.None);
    }

    private string? ValidateKeyboardLiveTransmitInterlock()
    {
        if (_radioService is null || CanConnect)
        {
            return "PSK TX blocked: radio is not connected.";
        }

        if (_audioService is null)
        {
            return "PSK TX blocked: audio service unavailable.";
        }

        if (SelectedTxDevice is null)
        {
            return "PSK TX blocked: no TX audio device configured.";
        }

        if (_keyboardPreparedTransmitClip is null)
        {
            return "PSK TX blocked: prepare TX WAV first.";
        }

        if (!string.Equals(_keyboardPreparedTransmitFingerprint, BuildKeyboardTransmitFingerprint(), StringComparison.Ordinal))
        {
            return "PSK TX blocked: prepared audio is stale. Press Prepare TX WAV again.";
        }

        return null;
    }

    private string BuildKeyboardTransmitFingerprint()
    {
        var mode = string.Equals(KeyboardSelectedMode, "BPSK63", StringComparison.OrdinalIgnoreCase)
            ? "BPSK63"
            : "BPSK31";
        return string.Join('|',
            mode,
            KeyboardTransmitText.Trim(),
            ParseKeyboardAudioCenterHz(KeyboardAudioCenterHz).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
    }

    partial void OnKeyboardTransmitTextChanged(string value) => MarkKeyboardPreparedTransmitStale();

    partial void OnKeyboardSelectedModeChanged(string value) => MarkKeyboardPreparedTransmitStale();

    partial void OnKeyboardAudioCenterHzChanged(string value) => MarkKeyboardPreparedTransmitStale();

    private void MarkKeyboardPreparedTransmitStale()
    {
        if (_keyboardPreparedTransmitClip is null)
        {
            return;
        }

        KeyboardPreparedTransmitStatus = "PSK TX text/mode/audio changed; prepare TX WAV again.";
        OnPropertyChanged(nameof(KeyboardHasPreparedTransmitClip));
    }

    private async Task StartFreedvReceiveCoreAsync()
    {
        if (_freedvDigitalVoiceHost is null)
        {
            return;
        }

        if (_audioService is null)
        {
            FreedvRxStatus = "FreeDV RX blocked: audio service unavailable.";
            return;
        }

        if (SelectedRxDevice is null)
        {
            FreedvRxStatus = "FreeDV RX blocked: no RX audio device configured.";
            return;
        }

        if (GetSelectedFreedvMonitorDevice() is null)
        {
            FreedvRxStatus = "FreeDV RX blocked: no FreeDV speech output configured.";
            FreedvDecodedAudioStatus = "Choose a FreeDV speech output, or configure PC RX Audio as the fallback, before starting FreeDV RX.";
            return;
        }

        if (FreedvUseCurrentRadioFrequency)
        {
            await SetRadioForFreedvAudioDataModeAsync();
        }
        else
        {
            await TuneRadioForFreedvAsync(FreedvSelectedFrequency);
        }

        var frequencyLabel = FreedvUseCurrentRadioFrequency
            ? "Current radio frequency"
            : FreedvSelectedFrequency;
        try
        {
            await _audioService.StartReceiveCaptureAsync(BuildCurrentAudioRoute(), CancellationToken.None);
            AudioMonitorState = CanStopAudio ? AudioMonitorState : "Decoder capture";
            CanStopAudio = true;
            await ApplyFreedvSpeechVolumeAsync(FreedvSpeechVolumePercent);
        }
        catch (Exception ex)
        {
            FreedvRxStatus = $"FreeDV RX blocked: decoder audio capture did not start ({ex.Message}).";
            AudioMonitorState = $"Error: {ex.Message}";
            CanStopAudio = false;
            return;
        }

            FreedvDecodedAudioStatus = $"FreeDV decoder capture is active; decoded speech will play on {GetSelectedFreedvMonitorDevice()?.FriendlyName} at {FreedvSpeechVolumePercent}%.";
        await _freedvDigitalVoiceHost.ConfigureAsync(
            new FreedvDigitalVoiceConfiguration(
                FreedvSelectedMode,
                frequencyLabel,
                FreedvUseCurrentRadioFrequency,
                FreedvRxFrequencyOffsetHz,
                FormatCallsign(SettingsCallsign)),
            CancellationToken.None);
        await _freedvDigitalVoiceHost.StartAsync(CancellationToken.None);
        FreedvSessionNotes = $"FreeDV receiver running on {frequencyLabel}. RX offset {FreedvRxFrequencyOffsetHz:+0;-0;0} Hz.";
    }

    private async Task StopFreedvReceiveCoreAsync()
    {
        try
        {
            if (_freedvDigitalVoiceHost is not null)
            {
                await _freedvDigitalVoiceHost.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FreedvRxStatus = "FreeDV receiver stopped";
                FreedvDecodedAudioStatus = "FreeDV decoder stopped; normal RX audio monitor is unchanged.";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FreedvRxStatus = $"FreeDV stop failed: {ex.Message}";
            });
        }
    }

    private async Task TuneRadioForFreedvAsync(string frequencyLabel)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        if (!TryParseUiFrequencyHz(frequencyLabel, out var hz))
        {
            return;
        }

        try
        {
            var mode = frequencyLabel.Contains("LSB", StringComparison.OrdinalIgnoreCase)
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            await _radioService.SetModeAsync(mode, CancellationToken.None);
            await _radioService.SetFrequencyAsync(hz, CancellationToken.None);
            RadioStatusSummary = $"FreeDV tuned: {hz:N0} Hz {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"FreeDV tune failed: {ex.Message}";
        }
    }

    private async Task SetRadioForFreedvAudioDataModeAsync()
    {
        if (_radioService is null || CanConnect)
        {
            FreedvSessionNotes = "FreeDV RX using current radio frequency. Set the IC-7300 to USB-D/LSB-D.";
            return;
        }

        try
        {
            var currentMode = _radioService.CurrentState.Mode;
            var mode = currentMode == RadioMode.Lsb || currentMode == RadioMode.LsbData
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            if (currentMode != mode)
            {
                await _radioService.SetModeAsync(mode, CancellationToken.None);
            }

            RadioStatusSummary = $"FreeDV RX using current radio frequency in {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"FreeDV mode set failed: {ex.Message}";
            FreedvSessionNotes = "FreeDV RX will still start, but manually set the IC-7300 to USB-D/LSB-D.";
        }
    }

    private async Task StopFreedvTransmitSafelyAsync()
    {
        try
        {
            if (_radioService is not null)
            {
                await _radioService.SetPttAsync(false, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        try
        {
            if (_freedvDigitalVoiceHost is not null)
            {
                await _freedvDigitalVoiceHost.StopTransmitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        IsFreedvTransmitting = false;
    }

    public void ActivateJs8Desk()
    {
        WsjtxSelectedMode = Js8SelectedMode;
        WsjtxSelectedFrequency = Js8SelectedFrequency;
        WsjtxSessionNotes = DescribeWsjtxMode(Js8SelectedMode);
        WsjtxRxStatus = $"JS8 desk selected: {Js8SelectedMode}";
        RebuildJs8SuggestedMessages();
    }

    private async Task StartWsjtxReceiveCoreAsync()
    {
        if (_wsjtxModeHost is null)
        {
            return;
        }

        var resolvedModeLabel = ResolveWsjtxModeSelection(WsjtxSelectedMode, WsjtxSelectedFrequency);
        if (!string.Equals(WsjtxSelectedMode, resolvedModeLabel, StringComparison.OrdinalIgnoreCase))
        {
            WsjtxSelectedMode = resolvedModeLabel;
        }

        ResetWsjtxSessionView(resolvedModeLabel, $"Starting {resolvedModeLabel} receive...");
        await TuneRadioForWsjtxAsync(WsjtxSelectedFrequency);
        var modeDefinition = WsjtxModeCatalog.GetMode(resolvedModeLabel);
        var config = new WsjtxModeConfiguration(
            resolvedModeLabel,
            WsjtxSelectedFrequency,
            WsjtxAutoSequenceEnabled,
            false,
            false,
            false,
            false,
            modeDefinition.CycleLengthSeconds,
            modeDefinition.RequiresAccurateClock,
            WsjtxOperatorCallsign,
            WsjtxOperatorGridSquare,
            false);
        await _wsjtxModeHost.ConfigureAsync(config, CancellationToken.None);
        await _wsjtxModeHost.StartAsync(CancellationToken.None);
    }

    private async Task StartWefaxReceiveCoreAsync(bool forceNow)
    {
        if (_wefaxDecoderHost is null)
        {
            return;
        }

        var (ioc, lpm) = ParseWefaxMode(WefaxSelectedMode);
        await TuneRadioForWefaxAsync(WefaxSelectedFrequency);
        var config = new WefaxDecoderConfiguration(
            WefaxSelectedMode,
            ioc,
            lpm,
            WefaxSelectedFrequency,
            WefaxManualSlant,
            WefaxManualOffset,
            Math.Clamp(WefaxCenterHz, 1000, 2400),
            Math.Clamp(WefaxShiftHz, 750, 900),
            Math.Clamp(WefaxMaxRows, 1000, 10000),
            WefaxSelectedFilter,
            WefaxAutoAlign,
            Math.Clamp(WefaxAutoAlignAfterRows, 1, 500),
            Math.Clamp(WefaxAutoAlignEveryRows, 1, 100),
            Math.Clamp(WefaxAutoAlignStopRows, 1, 5000),
            Math.Clamp(WefaxCorrelationThreshold, 0.01, 0.10),
            Math.Clamp(WefaxCorrelationRows, 2, 25),
            WefaxInvertImage,
            WefaxBinaryImage,
            Math.Clamp(WefaxBinaryThreshold, 0, 255),
            WefaxNoiseRemoval,
            Math.Clamp(WefaxNoiseThreshold, 1, 96),
            Math.Clamp(WefaxNoiseMargin, 1, 2));
        await _wefaxDecoderHost.ConfigureAsync(config, CancellationToken.None);
        if (forceNow)
        {
            await _wefaxDecoderHost.StartNowAsync(CancellationToken.None);
        }
        else
        {
            await _wefaxDecoderHost.StartAsync(CancellationToken.None);
        }
    }

    private void RebuildWefaxSchedule()
    {
        var now = DateTime.UtcNow;
        var items = BuildWefaxScheduleItems(now);
        WefaxScheduleItems = new ObservableCollection<WefaxScheduleItem>(items);
        WefaxScheduleStatus = $"Schedule shown in UTC. Updated {now:HH:mm}Z from built-in NOAA/NWS radiofax table.";
    }

    private async Task TuneRadioForWefaxAsync(string frequencyLabel)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        if (!TryParseWefaxPublishedFrequencyHz(frequencyLabel, out var publishedHz))
        {
            return;
        }

        var dialHz = publishedHz - 1_900L;
        if (dialHz <= 0)
        {
            return;
        }

        try
        {
            var mode = frequencyLabel.Contains("LSB", StringComparison.OrdinalIgnoreCase)
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            await _radioService.SetModeAsync(mode, CancellationToken.None);
            await ApplyPureDigitalReceivePresetAsync().ConfigureAwait(false);
            await _radioService.SetFrequencyAsync(dialHz, CancellationToken.None);
            RadioStatusSummary = $"WeFAX tuned: {dialHz:N0} Hz {FormatModeDisplay(mode)}  |  FIL1 NB/NR/AN off";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"WeFAX tune failed: {ex.Message}";
        }
    }

    private async Task TuneRadioForSstvAsync(string frequencyLabel, bool strict = false)
    {
        if (_radioService is null || CanConnect)
        {
            if (strict)
            {
                throw new InvalidOperationException("Radio is not connected.");
            }

            return;
        }

        if (!TryParseUiFrequencyHz(frequencyLabel, out var hz))
        {
            if (strict)
            {
                throw new InvalidOperationException($"Could not parse SSTV frequency '{frequencyLabel}'.");
            }

            return;
        }

        try
        {
            var mode = frequencyLabel.Contains("LSB", StringComparison.OrdinalIgnoreCase)
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            await _radioService.SetModeAsync(mode, CancellationToken.None);
            await _radioService.SetFrequencyAsync(hz, CancellationToken.None);
            RadioStatusSummary = $"SSTV tuned: {hz:N0} Hz {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"SSTV tune failed: {ex.Message}";
            if (strict)
            {
                throw;
            }
        }
    }

    private async Task VerifySstvPttRaisedAsync(CancellationToken ct)
    {
        if (_radioService is null)
        {
            throw new InvalidOperationException("Radio service unavailable.");
        }

        if (_radioService.CurrentState.IsPttActive)
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(100 + (attempt * 100), ct).ConfigureAwait(false);
            try
            {
                await _radioService.RefreshStateAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // If the rig cannot be polled while PTT is changing, keep the successful
                // SetPttAsync command as authoritative instead of dropping transmit.
                return;
            }

            if (_radioService.CurrentState.IsPttActive)
            {
                return;
            }
        }

        // Some Icom CI-V paths can lag or momentarily report RX after accepting the PTT command.
        // SetPttAsync throws on command failure, so do not abort SSTV audio on an ambiguous readback.
    }

    private string? ValidateSstvLiveTransmitInterlock()
    {
        if (_radioService is null || CanConnect)
        {
            return "SSTV TX blocked: radio not connected.";
        }

        if (!_radioService.CurrentState.IsConnected)
        {
            return "SSTV TX blocked: radio control is not connected.";
        }

        if (_audioService is null)
        {
            return "SSTV TX blocked: audio service unavailable.";
        }

        if (SelectedTxDevice is null)
        {
            return "SSTV TX blocked: TX audio device not configured.";
        }

        if (string.IsNullOrWhiteSpace(SelectedTxDevice.DeviceId))
        {
            return "SSTV TX blocked: selected TX audio device has no device id.";
        }

        if (SelectedSstvReplyBaseImage is null)
        {
            return "SSTV TX blocked: no reply base image selected.";
        }

        if (_sstvPreparedTransmitClip is null)
        {
            return "SSTV TX blocked: prepare TX audio first.";
        }

        if (_sstvPreparedTransmitClip.PcmBytes.Length == 0)
        {
            return "SSTV TX blocked: prepared TX audio is empty.";
        }

        if (!string.Equals(_sstvPreparedTransmitFingerprint, BuildSstvTransmitFingerprint(), StringComparison.Ordinal))
        {
            return "SSTV TX blocked: prepared image/audio is stale. Press Prepare TX again.";
        }

        return null;
    }

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

    [RelayCommand]
    private async Task ApplyWefaxManualSlantAsync()
    {
        if (_wefaxDecoderHost is null)
        {
            return;
        }

        try
        {
            await _wefaxDecoderHost.SetManualSlantAsync(WefaxManualSlant, CancellationToken.None);
            await _wefaxDecoderHost.SetManualOffsetAsync(WefaxManualOffset, CancellationToken.None);
            WefaxRxStatus = $"WeFAX alignment set: slant {WefaxManualSlant}, offset {WefaxManualOffset}";
        }
        catch (Exception ex)
        {
            WefaxRxStatus = $"WeFAX alignment apply failed: {ex.Message}";
        }
    }

    private async Task TuneRadioForRttyAsync(string frequencyLabel)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        if (!TryParseUiFrequencyHz(frequencyLabel, out var hz))
        {
            return;
        }

        try
        {
            var mode = frequencyLabel.Contains("LSB", StringComparison.OrdinalIgnoreCase)
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            await _radioService.SetModeAsync(mode, CancellationToken.None);
            await _radioService.SetFrequencyAsync(hz, CancellationToken.None);
            RadioStatusSummary = $"RTTY tuned: {hz:N0} Hz {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"RTTY tune failed: {ex.Message}";
        }
    }

    private async Task SetRadioForRttyAudioDataModeAsync()
    {
        if (_radioService is null || CanConnect)
        {
            RttySessionNotes = "RTTY RX using current radio frequency. Set the IC-7300 to USB-D/LSB-D for audio RTTY.";
            return;
        }

        try
        {
            var currentMode = _radioService.CurrentState.Mode;
            var mode = currentMode == RadioMode.Lsb || currentMode == RadioMode.LsbData
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            if (currentMode != mode)
            {
                await _radioService.SetModeAsync(mode, CancellationToken.None);
            }

            RadioStatusSummary = $"RTTY RX using current radio frequency in {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"RTTY mode set failed: {ex.Message}";
            RttySessionNotes = "RTTY RX will still start, but manually set the IC-7300 to USB-D/LSB-D for audio RTTY.";
        }
    }

    private async Task TuneRadioForKeyboardAsync(string frequencyLabel)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        if (!TryParseUiFrequencyHz(frequencyLabel, out var hz))
        {
            return;
        }

        try
        {
            var mode = frequencyLabel.Contains("LSB", StringComparison.OrdinalIgnoreCase)
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            await _radioService.SetModeAsync(mode, CancellationToken.None);
            await _radioService.SetFrequencyAsync(hz, CancellationToken.None);
            RadioStatusSummary = $"Keyboard mode tuned: {hz:N0} Hz {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Keyboard mode tune failed: {ex.Message}";
        }
    }

    private async Task SetRadioForKeyboardAudioDataModeAsync()
    {
        if (_radioService is null || CanConnect)
        {
            KeyboardSessionNotes = "Keyboard RX using current radio frequency. Set the IC-7300 to USB-D/LSB-D for PSK.";
            return;
        }

        try
        {
            var currentMode = _radioService.CurrentState.Mode;
            var mode = currentMode == RadioMode.Lsb || currentMode == RadioMode.LsbData
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            if (currentMode != mode)
            {
                await _radioService.SetModeAsync(mode, CancellationToken.None);
            }

            RadioStatusSummary = $"Keyboard RX using current radio frequency in {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Keyboard mode set failed: {ex.Message}";
            KeyboardSessionNotes = "Keyboard RX will still start, but manually set the IC-7300 to USB-D/LSB-D.";
        }
    }

    private async Task TuneRadioForWsjtxAsync(string frequencyLabel)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        if (!TryParseUiFrequencyHz(frequencyLabel, out var hz))
        {
            return;
        }

        try
        {
            var mode = frequencyLabel.Contains("LSB", StringComparison.OrdinalIgnoreCase)
                ? RadioMode.LsbData
                : RadioMode.UsbData;
            await _radioService.SetModeAsync(mode, CancellationToken.None);
            await ApplyPureDigitalReceivePresetAsync().ConfigureAwait(false);
            await _radioService.SetFrequencyAsync(hz, CancellationToken.None);
            RadioStatusSummary = $"Weak-signal digital tuned: {hz:N0} Hz {FormatModeDisplay(mode)}  |  FIL1 NB/NR/AN off";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Weak-signal digital tune failed: {ex.Message}";
        }
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

    private async Task TuneRadioForLongwaveSpotAsync(LongwaveSpotSummaryItem spot)
    {
        if (_radioService is null || CanConnect)
        {
            LongwaveStatus = "Connect the radio before tuning a spot.";
            return;
        }

        var hz = (long)Math.Round(spot.FrequencyKhz * 1000d);
        var radioMode = MapSpotModeToRadioMode(spot.Mode, hz);

        try
        {
            await _radioService.SetModeAsync(radioMode, CancellationToken.None);
            await _radioService.SetFrequencyAsync(hz, CancellationToken.None);
            LongwaveStatus = $"Tuned {spot.ActivatorCallsign} on {spot.FrequencyText} {spot.Mode}.";
            RadioStatusSummary = $"POTA tuned: {hz:N0} Hz {FormatModeDisplay(radioMode)}";

            if (IsWeakSignalSpotMode(spot.Mode))
            {
                SelectedModePanelTabIndex = 3;
                WsjtxSelectedMode = NormalizeWeakSignalMode(spot.Mode);
            }
        }
        catch (Exception ex)
        {
            LongwaveStatus = $"Spot tune failed: {ex.Message}";
        }
    }

    private void ApplySpotToLongwaveLog(LongwaveSpotSummaryItem spot)
    {
        LongwaveLogCallsign = spot.ActivatorCallsign;
        LongwaveLogMode = spot.Mode.ToUpperInvariant();
        LongwaveLogBand = spot.Band;
        LongwaveLogFrequencyKhz = $"{spot.FrequencyKhz:0.0}";
        LongwaveLogParkReference = spot.ParkReference;
        LongwaveLogGridSquare = string.Empty;
        if (IsWeakSignalSpotMode(spot.Mode))
        {
            LongwaveLogRstSent = "-10";
            LongwaveLogRstReceived = "-10";
        }
    }

    private void ApplyLongwaveLookup(LongwaveCallsignLookup lookup)
    {
        LongwaveLogCallsign = lookup.Callsign;
        LongwaveLogName = lookup.Name ?? string.Empty;
        LongwaveLogQth = lookup.Qth ?? string.Empty;
        LongwaveLogCounty = lookup.County ?? string.Empty;
        LongwaveLogGridSquare = lookup.GridSquare?.Trim().ToUpperInvariant() ?? LongwaveLogGridSquare;
        LongwaveLogCountry = lookup.Country ?? string.Empty;
        LongwaveLogState = lookup.State?.Trim().ToUpperInvariant() ?? string.Empty;
        LongwaveLogDxcc = lookup.Dxcc ?? string.Empty;
        _longwaveLogLatitude = lookup.Latitude;
        _longwaveLogLongitude = lookup.Longitude;
    }

    private async Task RefreshWsjtxCurrentQsoLookupAsync(WsjtxActiveSession? session)
    {
        var generation = Interlocked.Increment(ref _wsjtxLookupGeneration);
        if (session is null || string.IsNullOrWhiteSpace(session.OtherCall))
        {
            ClearWsjtxCurrentQsoLookup();
            return;
        }

        var callsign = FormatCallsign(session.OtherCall);
        WsjtxCurrentQsoCallsign = callsign;

        if (_longwaveService is null)
        {
            WsjtxCurrentQsoLookupSummary = "Longwave/QRZ lookup service unavailable.";
            WsjtxCurrentQsoLookupDetails = string.Empty;
            WsjtxCurrentQsoLookupStatus = "Unavailable";
            return;
        }

        try
        {
            WsjtxCurrentQsoLookupSummary = $"Looking up {callsign}...";
            WsjtxCurrentQsoLookupDetails = string.Empty;
            WsjtxCurrentQsoLookupStatus = "Looking up";
            var lookup = await _longwaveService.LookupCallsignAsync(BuildCurrentLongwaveSettings(), callsign, CancellationToken.None);
            if (generation != _wsjtxLookupGeneration)
            {
                return;
            }

            WsjtxCurrentQsoCallsign = lookup.Callsign;
            WsjtxCurrentQsoLookupSummary = BuildWsjtxLookupSummary(lookup);
            WsjtxCurrentQsoLookupDetails = BuildWsjtxLookupDetails(lookup);
            WsjtxCurrentQsoLookupStatus = string.IsNullOrWhiteSpace(lookup.QrzUrl) ? "Ready" : lookup.QrzUrl!;
        }
        catch (Exception ex)
        {
            if (generation != _wsjtxLookupGeneration)
            {
                return;
            }

            WsjtxCurrentQsoLookupSummary = $"Lookup failed for {callsign}.";
            WsjtxCurrentQsoLookupDetails = ex.Message;
            WsjtxCurrentQsoLookupStatus = "Lookup failed";
        }
    }

    private void ClearWsjtxCurrentQsoLookup()
    {
        WsjtxCurrentQsoCallsign = "No active QSO";
        WsjtxCurrentQsoLookupSummary = "Start or track a contact to see a quick QRZ summary here.";
        WsjtxCurrentQsoLookupDetails = string.Empty;
        WsjtxCurrentQsoLookupStatus = "Idle";
    }

    private static string BuildWsjtxLookupSummary(LongwaveCallsignLookup lookup)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(lookup.Name))
        {
            parts.Add(lookup.Name!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(lookup.Qth))
        {
            parts.Add(lookup.Qth!.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(lookup.State))
        {
            parts.Add(lookup.State!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(lookup.Country))
        {
            parts.Add(lookup.Country!.Trim());
        }

        return parts.Count > 0
            ? string.Join("  |  ", parts)
            : $"QRZ match found for {lookup.Callsign}.";
    }

    private static string BuildWsjtxLookupDetails(LongwaveCallsignLookup lookup)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(lookup.GridSquare))
        {
            parts.Add($"Grid {lookup.GridSquare.Trim().ToUpperInvariant()}");
        }

        if (!string.IsNullOrWhiteSpace(lookup.County))
        {
            parts.Add(lookup.County!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(lookup.Dxcc))
        {
            parts.Add(lookup.Dxcc!.Trim());
        }

        return string.Join("  |  ", parts);
    }

    private void ApplyLongwaveSettingsState(AppSettings settings)
    {
        var current = BuildCurrentLongwaveSettings(settings);
        if (current.Enabled)
        {
            if (string.IsNullOrWhiteSpace(LongwaveStatus)
                || string.Equals(LongwaveStatus, "Longwave integration disabled.", StringComparison.Ordinal)
                || string.Equals(LongwaveStatus, "Enable Longwave in Settings to use POTA spots and logging.", StringComparison.Ordinal))
            {
                LongwaveStatus = "Longwave ready. Refresh spots or log a contact.";
            }

            LongwaveOperatorSummary = "Longwave integration enabled.";

            if (string.IsNullOrWhiteSpace(LongwaveLogStatus)
                || string.Equals(LongwaveLogStatus, "Enable Longwave in Settings to log contacts here.", StringComparison.Ordinal))
            {
                LongwaveLogStatus = "Ready to log from rig or selected spot.";
            }
        }
        else
        {
            LongwaveStatus = "Enable Longwave in Settings to use POTA spots and logging.";
            LongwaveOperatorSummary = "Longwave integration disabled.";
            LongwaveLogStatus = "Enable Longwave in Settings to log contacts here.";
        }
    }

    private LongwaveSettings BuildCurrentLongwaveSettings() => BuildCurrentLongwaveSettings(_settings);

    private LongwaveSettings BuildCurrentLongwaveSettings(AppSettings fallback) =>
        new(
            SettingsLongwaveEnabled,
            string.IsNullOrWhiteSpace(SettingsLongwaveBaseUrl) ? fallback.Longwave.BaseUrl : SettingsLongwaveBaseUrl.Trim(),
            string.IsNullOrWhiteSpace(SettingsLongwaveClientApiToken) ? fallback.Longwave.ClientApiToken : SettingsLongwaveClientApiToken.Trim(),
            string.IsNullOrWhiteSpace(SettingsLongwaveDefaultLogbookName) ? fallback.Longwave.DefaultLogbookName : SettingsLongwaveDefaultLogbookName.Trim(),
            string.IsNullOrWhiteSpace(SettingsLongwaveDefaultLogbookNotes) ? fallback.Longwave.DefaultLogbookNotes : SettingsLongwaveDefaultLogbookNotes.Trim());

    private static LongwaveSpotSummaryItem ToLongwaveSpotSummaryItem(LongwaveSpot spot, bool isLogged) =>
        new(
            spot.Id,
            spot.ActivatorCallsign,
            spot.ParkReference,
            spot.FrequencyKhz,
            spot.Mode,
            spot.Band,
            spot.Comments,
            spot.SpotterCallsign,
            spot.SpottedAtUtc,
            isLogged);

    private static LongwaveRecentContactItem ToLongwaveRecentContactItem(LongwaveContact contact) =>
        new(
            contact.Id,
            contact.LogbookId,
            contact.StationCallsign,
            contact.OperatorCallsign,
            contact.QsoDate,
            contact.TimeOn,
            contact.Mode,
            contact.Band,
            $"{contact.QsoDate} {contact.TimeOn}",
            contact.ParkReference,
            contact.FrequencyKhz,
            contact.RstSent,
            contact.RstReceived,
            contact.Name,
            contact.Qth,
            contact.County,
            contact.GridSquare,
            contact.Country,
            contact.State,
            contact.Dxcc,
            contact.QrzUploadStatus,
            contact.QrzUploadDate,
            contact.Latitude,
            contact.Longitude,
            contact.SourceSpotId);

    private static LongwaveLogbookItem ToLongwaveLogbookItem(LongwaveLogbook logbook) =>
        new(
            logbook.Id,
            logbook.Name,
            logbook.OperatorCallsign,
            logbook.ParkReference,
            logbook.ActivationDate,
            logbook.Notes,
            logbook.ContactCount);

    private LongwaveContactDraft BuildLongwaveContactDraft(
        string logbookId,
        string stationCall,
        string operatorCall,
        double frequencyKhz,
        DateTime qsoTimeUtc) =>
        new(
            logbookId,
            stationCall,
            operatorCall,
            qsoTimeUtc.ToString("yyyyMMdd"),
            qsoTimeUtc.ToString("HHmmss"),
            string.IsNullOrWhiteSpace(LongwaveLogBand) ? DeriveBandFromFrequencyKhz(frequencyKhz) : LongwaveLogBand.Trim(),
            LongwaveLogMode.Trim().ToUpperInvariant(),
            frequencyKhz,
            LongwaveLogParkReference,
            LongwaveLogRstSent,
            LongwaveLogRstReceived,
            LongwaveLogName,
            LongwaveLogQth,
            LongwaveLogCounty,
            LongwaveLogGridSquare,
            LongwaveLogCountry,
            LongwaveLogState,
            LongwaveLogDxcc,
            _longwaveLogLatitude,
            _longwaveLogLongitude,
            SelectedLongwavePotaSpot is not null
                && string.Equals(SelectedLongwavePotaSpot.ActivatorCallsign, stationCall, StringComparison.OrdinalIgnoreCase)
                    ? SelectedLongwavePotaSpot.Id
                    : SelectedLongwaveRecentContact?.SourceSpotId);

    private static string BuildLongwaveLoggedContactSummary(LongwaveContact contact, string logbookName)
    {
        var report = string.IsNullOrWhiteSpace(contact.RstSent) && string.IsNullOrWhiteSpace(contact.RstReceived)
            ? "no report"
            : $"{contact.RstSent ?? "--"}/{contact.RstReceived ?? "--"}";
        var park = string.IsNullOrWhiteSpace(contact.ParkReference) ? string.Empty : $" | {contact.ParkReference}";
        return $"Logged {contact.StationCallsign} | {contact.QsoDate} {contact.TimeOn} UTC | {contact.Band} {contact.Mode} | {contact.FrequencyKhz / 1000d:0.000000} MHz | RST {report} | {logbookName}{park}.";
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "longwave-logbook" : safe;
    }

    private DateTime GetLongwaveEditorQsoTimeUtc()
    {
        var date = LongwaveLogQsoDate.Trim();
        var time = LongwaveLogTimeOn.Trim();
        if (date.Length == 8
            && DateTime.TryParseExact(date, "yyyyMMdd", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsedDate))
        {
            if (time.Length is > 0 and < 6)
            {
                time = time.PadRight(6, '0');
            }

            if (time.Length == 6
                && TimeSpan.TryParseExact(time, "hhmmss", null, out var parsedTime))
            {
                return DateTime.SpecifyKind(parsedDate.Date + parsedTime, DateTimeKind.Utc);
            }
        }

        var now = DateTime.UtcNow;
        LongwaveLogQsoDate = now.ToString("yyyyMMdd");
        LongwaveLogTimeOn = now.ToString("HHmmss");
        return now;
    }

    private void ApplyLongwaveContactToEditor(LongwaveRecentContactItem contact)
    {
        LongwaveEditingContactId = contact.Id;
        var matchingLogbook = LongwaveLogbooks.FirstOrDefault(logbook => string.Equals(logbook.Id, contact.LogbookId, StringComparison.Ordinal));
        if (matchingLogbook is not null
            && !string.Equals(SelectedLongwaveLogbook?.Id, matchingLogbook.Id, StringComparison.Ordinal))
        {
            SelectedLongwaveLogbook = matchingLogbook;
        }

        LongwaveLogOperatorCallsign = contact.OperatorCallsign;
        LongwaveLogCallsign = contact.StationCallsign;
        LongwaveLogQsoDate = contact.QsoDate;
        LongwaveLogTimeOn = contact.TimeOn;
        LongwaveLogMode = contact.Mode;
        LongwaveLogBand = contact.Band;
        LongwaveLogFrequencyKhz = $"{contact.FrequencyKhz:0.0}";
        LongwaveLogRstSent = contact.RstSent ?? string.Empty;
        LongwaveLogRstReceived = contact.RstReceived ?? string.Empty;
        LongwaveLogParkReference = contact.ParkReference ?? string.Empty;
        LongwaveLogGridSquare = contact.GridSquare ?? string.Empty;
        LongwaveLogName = contact.Name ?? string.Empty;
        LongwaveLogQth = contact.Qth ?? string.Empty;
        LongwaveLogCounty = contact.County ?? string.Empty;
        LongwaveLogState = contact.State ?? string.Empty;
        LongwaveLogCountry = contact.Country ?? string.Empty;
        LongwaveLogDxcc = contact.Dxcc ?? string.Empty;
        _longwaveLogLatitude = contact.Latitude;
        _longwaveLogLongitude = contact.Longitude;
        LongwaveLogStatus = $"Loaded {contact.StationCallsign} for editing. {contact.QrzUploadText}.";
    }

    private void EnsureLongwaveLogbookSelected(LongwaveLogbook logbook)
    {
        var existing = LongwaveLogbooks.FirstOrDefault(item => string.Equals(item.Id, logbook.Id, StringComparison.Ordinal));
        if (existing is null)
        {
            existing = ToLongwaveLogbookItem(logbook);
            LongwaveLogbooks.Insert(0, existing);
        }

        SelectedLongwaveLogbook = existing;
    }

    private void UpsertLongwaveRecentContact(LongwaveContact contact)
    {
        var item = ToLongwaveRecentContactItem(contact);
        var existing = LongwaveRecentContacts.FirstOrDefault(existing => string.Equals(existing.Id, item.Id, StringComparison.Ordinal));
        if (existing is not null)
        {
            LongwaveRecentContacts.Remove(existing);
        }

        LongwaveRecentContacts.Insert(0, item);
        SelectedLongwaveRecentContact = item;
    }

    private void MarkLongwaveSpotLogged(string? sourceSpotId)
    {
        if (string.IsNullOrWhiteSpace(sourceSpotId))
        {
            return;
        }

        for (var i = 0; i < LongwavePotaSpots.Count; i++)
        {
            var item = LongwavePotaSpots[i];
            if (!string.Equals(item.Id, sourceSpotId, StringComparison.Ordinal))
            {
                continue;
            }

            var updated = item with { IsLogged = true };
            LongwavePotaSpots[i] = updated;
            if (SelectedLongwavePotaSpot?.Id == updated.Id)
            {
                SelectedLongwavePotaSpot = updated;
            }
            if (SelectedVoiceLongwavePotaSpot?.Id == updated.Id)
            {
                SelectedVoiceLongwavePotaSpot = updated;
            }
            break;
        }

        RebuildVoiceLongwavePotaSpots();
    }

    private void RebuildVoiceLongwavePotaSpots()
    {
        var bandFilter = SelectedVoiceLongwaveBandFilter;
        VoiceLongwavePotaSpots = new ObservableCollection<LongwaveSpotSummaryItem>(
            LongwavePotaSpots
                .Where(static spot => IsVoiceSpotMode(spot.Mode))
                .Where(spot => string.Equals(bandFilter, "All bands", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(spot.Band, bandFilter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static spot => spot.IsLogged)
                .ThenByDescending(static spot => spot.SpottedAtUtc));

        if (SelectedVoiceLongwavePotaSpot is not null)
        {
            SelectedVoiceLongwavePotaSpot = VoiceLongwavePotaSpots.FirstOrDefault(item => item.Id == SelectedVoiceLongwavePotaSpot.Id);
        }
    }

    private async Task RefreshLongwaveContactsAsync()
    {
        if (_longwaveService is null)
        {
            return;
        }

        var contacts = await _longwaveService.GetContactsAsync(BuildCurrentLongwaveSettings(), SelectedLongwaveLogbook?.Id, CancellationToken.None);
        LongwaveRecentContacts = new ObservableCollection<LongwaveRecentContactItem>(
            contacts.Take(50).Select(ToLongwaveRecentContactItem));
        OnPropertyChanged(nameof(LongwaveSelectedLogbookQrzUploadSummary));
        if (SelectedLongwaveRecentContact is not null)
        {
            SelectedLongwaveRecentContact = LongwaveRecentContacts.FirstOrDefault(item => item.Id == SelectedLongwaveRecentContact.Id);
        }
    }

    private async Task RefreshLongwaveContactsForSelectionAsync()
    {
        try
        {
            await RefreshLongwaveContactsAsync();
        }
        catch (Exception ex)
        {
            LongwaveStatus = ex.Message;
        }
    }

    private async Task OnLongwaveRefreshTimerTickAsync()
    {
        if (_longwaveService is null || IsLongwaveBusy)
        {
            return;
        }

        var settings = BuildCurrentLongwaveSettings();
        if (!settings.Enabled
            || string.IsNullOrWhiteSpace(settings.BaseUrl)
            || string.IsNullOrWhiteSpace(settings.ClientApiToken))
        {
            return;
        }

        try
        {
            await RefreshLongwaveSpotsAsync();
        }
        catch
        {
        }
    }

    private static string BuildLongwaveLogDedupeKey(string stationCall, string mode, double frequencyKhz, DateTime timestampUtc) =>
        $"{stationCall}|{mode.Trim().ToUpperInvariant()}|{Math.Round(frequencyKhz, 1):0.0}|{timestampUtc:yyyyMMddHHmm}";

    private static LongwaveLogbookItem? SelectPreferredLongwaveLogbook(
        IEnumerable<LongwaveLogbookItem> available,
        LongwaveLogbookItem? currentSelection,
        string preferredName)
    {
        var items = available.ToArray();
        if (currentSelection is not null)
        {
            var currentMatch = items.FirstOrDefault(item => string.Equals(item.Id, currentSelection.Id, StringComparison.Ordinal));
            if (currentMatch is not null)
            {
                return currentMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var preferred = items.FirstOrDefault(item => string.Equals(item.Name, preferredName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return items.FirstOrDefault();
    }

    private static RadioMode MapSpotModeToRadioMode(string mode, long frequencyHz)
    {
        var upper = mode.Trim().ToUpperInvariant();
        return upper switch
        {
            "FT8" or "FT4" or "WSPR" => RadioMode.UsbData,
            "RTTY" => RadioMode.Rtty,
            "CW" => RadioMode.Cw,
            "AM" => RadioMode.Am,
            "FM" => RadioMode.Fm,
            "SSTV" => frequencyHz < 10_000_000 ? RadioMode.LsbData : RadioMode.UsbData,
            "SSB" => frequencyHz < 10_000_000 ? RadioMode.Lsb : RadioMode.Usb,
            "USB" => RadioMode.Usb,
            "LSB" => RadioMode.Lsb,
            _ => frequencyHz < 10_000_000 ? RadioMode.Lsb : RadioMode.Usb,
        };
    }

    private static bool IsWeakSignalSpotMode(string mode) =>
        string.Equals(mode, "FT8", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "FT4", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "WSPR", StringComparison.OrdinalIgnoreCase)
        || mode.StartsWith("JS8", StringComparison.OrdinalIgnoreCase);

    private static bool IsWsjtxQsoMode(string mode) =>
        string.Equals(mode, "FT8", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "FT4", StringComparison.OrdinalIgnoreCase)
        || mode.StartsWith("JS8", StringComparison.OrdinalIgnoreCase);

    private static bool IsWsprMode(string mode) =>
        string.Equals(mode, "WSPR", StringComparison.OrdinalIgnoreCase);

    private bool ShouldAutoStageWsjtxReplies =>
        string.Equals(SelectedWsjtxReplyAutomationMode.Key, "stage", StringComparison.OrdinalIgnoreCase)
        || string.Equals(SelectedWsjtxReplyAutomationMode.Key, "ready", StringComparison.OrdinalIgnoreCase);

    private bool ShouldAutoReadyWsjtxReplies =>
        string.Equals(SelectedWsjtxReplyAutomationMode.Key, "ready", StringComparison.OrdinalIgnoreCase);

    private static bool IsVoiceSpotMode(string mode) =>
        string.Equals(mode, "SSB", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "USB", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "LSB", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, "FM", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeWeakSignalMode(string mode)
    {
        if (string.Equals(mode, "FT4", StringComparison.OrdinalIgnoreCase))
        {
            return "FT4";
        }

        if (string.Equals(mode, "WSPR", StringComparison.OrdinalIgnoreCase))
        {
            return "WSPR";
        }

        if (mode.StartsWith("JS8", StringComparison.OrdinalIgnoreCase))
        {
            return "JS8 Normal";
        }

        return "FT8";
    }

    private static string MapRadioModeToLogMode(RadioMode mode) => mode switch
    {
        RadioMode.Lsb or RadioMode.Usb => "SSB",
        RadioMode.LsbData or RadioMode.UsbData => "DATA",
        RadioMode.Cw => "CW",
        RadioMode.Am => "AM",
        RadioMode.Fm => "FM",
        RadioMode.Rtty => "RTTY",
        _ => "SSB",
    };

    private static string MapRadioModeToPotaMode(RadioMode mode) => mode switch
    {
        RadioMode.Lsb or RadioMode.Usb => "SSB",
        RadioMode.LsbData or RadioMode.UsbData => "DATA",
        RadioMode.Cw => "CW",
        RadioMode.Am => "AM",
        RadioMode.Fm => "FM",
        RadioMode.Rtty => "RTTY",
        _ => "SSB",
    };

    private static string DeriveBandFromFrequencyKhz(double frequencyKhz)
    {
        if (frequencyKhz is >= 1800 and < 2000) return "160m";
        if (frequencyKhz is >= 3500 and < 4000) return "80m";
        if (frequencyKhz is >= 7000 and < 7300) return "40m";
        if (frequencyKhz is >= 10100 and < 10150) return "30m";
        if (frequencyKhz is >= 14000 and < 14350) return "20m";
        if (frequencyKhz is >= 18068 and < 18168) return "17m";
        if (frequencyKhz is >= 21000 and < 21450) return "15m";
        if (frequencyKhz is >= 24890 and < 24990) return "12m";
        if (frequencyKhz is >= 28000 and < 29700) return "10m";
        if (frequencyKhz is >= 50000 and < 54000) return "6m";
        return "HF";
    }

    private static string NormalizeSstvModeSelection(string selection) => selection switch
    {
        "Lock Martin M1" or "Martin M1" or "Martin 1" => "Martin 1",
        "Lock Martin M2" or "Martin M2" or "Martin 2" => "Martin 2",
        "Lock Scottie 1" or "Scottie 1" => "Scottie 1",
        "Lock Scottie 2" or "Scottie 2" => "Scottie 2",
        "Lock Robot 36" or "Robot 36" => "Robot 36",
        "Lock PD 120" or "PD 120" => "PD 120",
        "Auto Detect" => "Auto Detect",
        _ => "Auto Detect",
    };

    [RelayCommand]
    private async Task ApplySstvPostReceiveSlantAsync()
    {
        if (_sstvDecoderHost is null)
        {
            SstvRxStatus = "SSTV decoder host unavailable";
            return;
        }

        try
        {
            await _sstvDecoderHost.ApplyPostReceiveSlantCorrectionAsync(CancellationToken.None);
            SstvRxStatus = "MMSSTV post-receive slant correction requested";
        }
        catch (Exception ex)
        {
            SstvRxStatus = $"MMSSTV slant correction failed: {ex.Message}";
        }
    }

    private void UpdateSstvPreview(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            SstvPreviewBitmap = null;
            SstvHasPreview = false;
            OnPropertyChanged(nameof(SstvShowPlaceholder));
            return;
        }

        try
        {
            SstvPreviewBitmap = new Bitmap(imagePath);
            SstvHasPreview = true;
            OnPropertyChanged(nameof(SstvShowPlaceholder));
            AddOrSelectSstvArchiveImage(imagePath);
        }
        catch
        {
            SstvPreviewBitmap = null;
            SstvHasPreview = false;
            OnPropertyChanged(nameof(SstvShowPlaceholder));
        }
    }

    private void UpdateWefaxPreview(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            WefaxPreviewBitmap = null;
            WefaxHasPreview = false;
            OnPropertyChanged(nameof(WefaxShowPlaceholder));
            return;
        }

        try
        {
            WefaxPreviewBitmap = new Bitmap(imagePath);
            WefaxHasPreview = true;
            OnPropertyChanged(nameof(WefaxShowPlaceholder));
            AddOrSelectWefaxArchiveImage(imagePath);
        }
        catch
        {
            WefaxPreviewBitmap = null;
            WefaxHasPreview = false;
            OnPropertyChanged(nameof(WefaxShowPlaceholder));
        }
    }

    private void LoadSstvArchiveImages()
    {
        var selectedReceivedPath = SelectedSstvReceivedImage?.Path;
        var selectedReplyPath = SelectedSstvReplyBaseImage?.Path;
        var selectedTemplatePath = SelectedSstvReplyLayoutTemplate?.Path;
        var archive = SstvReplyArchiveStore.Load(_sstvReceivedDirectory, _sstvReplyDirectory, _sstvTemplateDirectory);

        SstvReceivedImages = new ObservableCollection<SstvImageItem>(archive.ReceivedImages);
        SstvReplyImages = new ObservableCollection<SstvImageItem>(archive.ReplyImages);
        SstvReplyLayoutTemplates = new ObservableCollection<SstvTemplateItem>(archive.LayoutTemplates);
        SelectedSstvReceivedImage = SstvReplyArchiveStore.SelectByPathOrFirst(SstvReceivedImages, selectedReceivedPath);
        SelectedSstvReplyBaseImage = SstvReplyArchiveStore.SelectByPathOrFirst(SstvReplyImages, selectedReplyPath);
        SelectedSstvReplyLayoutTemplate = SstvReplyArchiveStore.SelectByPathOrFirst(SstvReplyLayoutTemplates, selectedTemplatePath);
        if (SstvReplyOverlayItems.Count == 0)
        {
            var defaultOverlay = new SstvOverlayItemViewModel();
            SstvReplyOverlayItems.Add(defaultOverlay);
            SelectedSstvReplyOverlayItem = defaultOverlay;
        }
        OnPropertyChanged(nameof(SstvSelectedReceivedBitmap));
        OnPropertyChanged(nameof(SstvSelectedReceivedPath));
        OnPropertyChanged(nameof(SstvReplyPreviewBitmap));
        OnPropertyChanged(nameof(SstvReplyCanvasWidth));
        OnPropertyChanged(nameof(SstvReplyCanvasHeight));
        OnPropertyChanged(nameof(SstvReplyHasBaseImage));
        OnPropertyChanged(nameof(SstvReplyShowPlaceholder));
        OnPropertyChanged(nameof(SstvReceivedFolderPath));
        OnPropertyChanged(nameof(SstvReplyFolderPath));
        OnPropertyChanged(nameof(SstvTemplateFolderPath));
    }

    private void LoadWefaxArchiveImages()
    {
        Directory.CreateDirectory(_wefaxReceivedDirectory);

        var receivedItems = new List<WefaxImageItem>();
        foreach (var file in new DirectoryInfo(_wefaxReceivedDirectory)
                     .EnumerateFiles("*.png", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(f => f.LastWriteTimeUtc)
                     .Take(60))
        {
            try
            {
                receivedItems.Add(new WefaxImageItem(
                    Path.GetFileNameWithoutExtension(file.Name),
                    file.FullName,
                    file.LastWriteTime,
                    new Bitmap(file.FullName)));
            }
            catch
            {
            }
        }

        WefaxReceivedImages = new ObservableCollection<WefaxImageItem>(receivedItems);
        SelectedWefaxReceivedImage ??= WefaxReceivedImages.FirstOrDefault();
        OnPropertyChanged(nameof(WefaxSelectedReceivedBitmap));
        OnPropertyChanged(nameof(WefaxSelectedReceivedPath));
        OnPropertyChanged(nameof(WefaxReceivedFolderPath));
    }

    private void AddOrSelectSstvArchiveImage(string imagePath)
    {
        var existing = SstvReceivedImages.FirstOrDefault(item =>
            string.Equals(item.Path, imagePath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            try
            {
                existing = SstvReplyArchiveStore.TryCreateImageItem(imagePath);
                if (existing is null)
                {
                    return;
                }

                SstvReceivedImages.Insert(0, existing);
            }
            catch
            {
                return;
            }
        }

        SelectedSstvReceivedImage = existing;
        OnPropertyChanged(nameof(SstvSelectedReceivedBitmap));
        OnPropertyChanged(nameof(SstvSelectedReceivedPath));
    }

    private void AddOrSelectWefaxArchiveImage(string imagePath)
    {
        var existing = WefaxReceivedImages.FirstOrDefault(item =>
            string.Equals(item.Path, imagePath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            try
            {
                var file = new FileInfo(imagePath);
                existing = new WefaxImageItem(
                    Path.GetFileNameWithoutExtension(file.Name),
                    file.FullName,
                    file.LastWriteTime,
                    new Bitmap(file.FullName));
                WefaxReceivedImages.Insert(0, existing);
            }
            catch
            {
                return;
            }
        }

        SelectedWefaxReceivedImage = existing;
        OnPropertyChanged(nameof(WefaxSelectedReceivedBitmap));
        OnPropertyChanged(nameof(WefaxSelectedReceivedPath));
    }

    partial void OnSelectedSstvReceivedImageChanged(SstvImageItem? value)
    {
        OnPropertyChanged(nameof(SstvSelectedReceivedBitmap));
        OnPropertyChanged(nameof(SstvSelectedReceivedPath));
    }

    partial void OnSelectedWefaxReceivedImageChanged(WefaxImageItem? value)
    {
        OnPropertyChanged(nameof(WefaxSelectedReceivedBitmap));
        OnPropertyChanged(nameof(WefaxSelectedReceivedPath));
    }

    partial void OnSelectedWefaxScheduleItemChanged(WefaxScheduleItem? value)
    {
        if (value is null)
        {
            return;
        }

        WefaxSelectedMode = value.ModeLabel;
        WefaxSelectedFrequency = value.FrequencyLabel;
        WefaxScheduleStatus = $"Selected {value.Station}: {value.Product} at {value.TimeText} UTC.";
    }

    partial void OnSelectedWsjtxMessageChanged(WsjtxMessageItem? value)
    {
        OnPropertyChanged(nameof(WsjtxSelectedMessageText));
        OnPropertyChanged(nameof(WsjtxRxTrackStatus));
        OnPropertyChanged(nameof(WsjtxLongwaveLogPreview));
        OnPropertyChanged(nameof(WsjtxLongwaveLogDetail));
        OnPropertyChanged(nameof(Js8SelectedDecodeSummary));
        RebuildWsjtxSuggestedMessages();
        if (value is not null && value.ModeText.StartsWith("JS8", StringComparison.OrdinalIgnoreCase))
        {
            var callsign = TryExtractCallsign(value.MessageText);
            if (!string.IsNullOrWhiteSpace(callsign))
            {
                Js8TargetCallsign = callsign;
            }

            RebuildJs8SuggestedMessages();
        }
    }

    partial void OnWsjtxRxAudioFrequencyHzChanged(int value)
    {
        var clamped = Math.Clamp(value, 200, 3900);
        if (clamped != value)
        {
            WsjtxRxAudioFrequencyHz = clamped;
            return;
        }

        OnPropertyChanged(nameof(WsjtxRxFrequencyTitle));
        OnPropertyChanged(nameof(WsjtxRxTrackStatus));
        OnPropertyChanged(nameof(WsjtxTransmitPlanSummary));
        if (!WsjtxHoldTxFrequency)
        {
            WsjtxTxAudioFrequencyHz = value;
            OnPropertyChanged(nameof(WsjtxTxTrackStatus));
            OnPropertyChanged(nameof(WsjtxTxFrequencyTitle));
        }
        RebuildWsjtxRxFrequencyMessages();
    }

    partial void OnWsjtxTxAudioFrequencyHzChanged(int value)
    {
        var clamped = Math.Clamp(value, 200, 3900);
        if (clamped != value)
        {
            WsjtxTxAudioFrequencyHz = clamped;
            return;
        }

        OnPropertyChanged(nameof(WsjtxTxFrequencyTitle));
        OnPropertyChanged(nameof(WsjtxTxTrackStatus));
        OnPropertyChanged(nameof(WsjtxTransmitPlanSummary));
    }

    partial void OnWsjtxAutoSequenceEnabledChanged(bool value)
    {
        UpdateWsjtxSessionNotes();
    }

    partial void OnSelectedWsjtxReplyAutomationModeChanged(WsjtxReplyAutomationModeItem value)
    {
        OnPropertyChanged(nameof(WsjtxReplyAutomationSummary));
    }

    partial void OnWsjtxHoldTxFrequencyChanged(bool value)
    {
        OnPropertyChanged(nameof(WsjtxTxTrackStatus));
        OnPropertyChanged(nameof(WsjtxTransmitPlanSummary));
        if (!value)
        {
            WsjtxTxAudioFrequencyHz = WsjtxRxAudioFrequencyHz;
        }
    }

    partial void OnSelectedWsjtxSuggestedMessageChanged(WsjtxSuggestedMessageItem? value)
    {
        OnPropertyChanged(nameof(WsjtxSuggestedMessagePreview));
    }

    partial void OnWsjtxQueuedTransmitMessageChanged(WsjtxSuggestedMessageItem? value)
    {
        OnPropertyChanged(nameof(WsjtxQueuedTransmitPreview));
        OnPropertyChanged(nameof(WsjtxEffectiveTransmitText));
        OnPropertyChanged(nameof(WsjtxQsoRailSummary));
    }

    partial void OnWsjtxManualTransmitTextChanged(string value)
    {
        if (!_isSettingWsjtxManualTransmitText)
        {
            _wsjtxManualTransmitOverride = !string.IsNullOrWhiteSpace(value);
            WsjtxPreparedTransmit = null;
            _wsjtxPreparedTransmitClip = null;
            WsjtxPreparedTransmitStatus = string.IsNullOrWhiteSpace(value)
                ? "No TX signal prepared."
                : "Manual TX message changed; prepare TX before sending.";
            WsjtxPreparedTransmitPath = "No prepared TX artifact.";
            WsjtxTransmitArmedLocal = false;
            WsjtxAwaitingReply = false;
            WsjtxTransmitArmStatus = string.IsNullOrWhiteSpace(value)
                ? "Nothing armed."
                : "Manual message not prepared yet.";
            if (!string.IsNullOrWhiteSpace(value))
            {
                WsjtxQueuedTransmitMessage = new WsjtxSuggestedMessageItem("Manual", NormalizeWsjtxTransmitText(value)!, "Manual TX message");
            }
        }

        OnPropertyChanged(nameof(WsjtxQueuedTransmitPreview));
        OnPropertyChanged(nameof(WsjtxEffectiveTransmitText));
        OnPropertyChanged(nameof(WsjtxQsoRailSummary));
    }

    partial void OnJs8TargetCallsignChanged(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            Js8TargetCallsign = normalized;
            return;
        }

        RebuildJs8SuggestedMessages();
        OnPropertyChanged(nameof(Js8TargetSummary));
    }

    partial void OnJs8ComposeTextChanged(string value)
    {
        Js8ComposeStatus = string.IsNullOrWhiteSpace(value)
            ? "Receive is wired. Compose is ready."
            : "JS8 text staged locally. Prepare TX audio before arming.";
    }

    partial void OnWsjtxOperatorCallsignChanged(string value)
    {
    }

    partial void OnSelectedTxDeviceChanged(AudioDeviceInfo? value)
    {
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnWsjtxPreparedTransmitChanged(WsjtxPreparedTransmit? value)
    {
        OnPropertyChanged(nameof(WsjtxTransmitArmSummary));
        OnPropertyChanged(nameof(WsjtxQsoRailSummary));
    }

    partial void OnWsjtxAwaitingReplyChanged(bool value)
    {
        OnPropertyChanged(nameof(WsjtxTransmitArmSummary));
        OnPropertyChanged(nameof(WsjtxActiveSessionSummary));
        OnPropertyChanged(nameof(WsjtxQsoRailSummary));
    }

    partial void OnWsjtxActiveSessionChanged(WsjtxActiveSession? value)
    {
        OnPropertyChanged(nameof(WsjtxActiveSessionSummary));
        OnPropertyChanged(nameof(WsjtxQsoRailSummary));
        OnPropertyChanged(nameof(WsjtxLongwaveLogPreview));
        OnPropertyChanged(nameof(WsjtxLongwaveLogDetail));
        _ = RefreshWsjtxCurrentQsoLookupAsync(value);
    }

    partial void OnWsjtxCallingCqChanged(bool value)
    {
        OnPropertyChanged(nameof(WsjtxActiveSessionSummary));
        OnPropertyChanged(nameof(WsjtxQsoRailSummary));
    }

    partial void OnWsjtxPreparedTransmitStatusChanged(string value)
    {
        OnPropertyChanged(nameof(WsjtxPreparedTransmitSummary));
    }

    partial void OnWsjtxTransmitArmedLocalChanged(bool value)
    {
        OnPropertyChanged(nameof(WsjtxTransmitArmSummary));
        OnPropertyChanged(nameof(WsjtxQsoRailSummary));
    }

    partial void OnWsjtxTransmitArmStatusChanged(string value)
    {
        OnPropertyChanged(nameof(WsjtxTransmitArmSummary));
    }

    partial void OnWsjtxSecondsToNextCycleChanged(double value)
    {
        TryAutoCompleteArmedWsjtxTransmit(value);
        OnPropertyChanged(nameof(WsjtxTransmitArmSummary));
    }

    private void TryAutoCompleteArmedWsjtxTransmit(double currentSecondsToNextCycle)
    {
        var previous = _lastObservedWsjtxSecondsToNextCycle;
        _lastObservedWsjtxSecondsToNextCycle = currentSecondsToNextCycle;

        if (!WsjtxTransmitArmedLocal || WsjtxPreparedTransmit is null || _wsjtxSlotSendInFlight)
        {
            return;
        }

        if (!double.IsFinite(previous) || !double.IsFinite(currentSecondsToNextCycle))
        {
            return;
        }

        // When the cycle boundary passes, SecondsToNextCycle jumps back up near the mode's full cycle length.
        if (currentSecondsToNextCycle <= previous + 0.75)
        {
            return;
        }

        _ = ExecuteArmedWsjtxTransmitAsync();
    }

    private string? ValidateWsjtxLiveTransmitInterlock()
    {
        if (_radioService is null || CanConnect)
        {
            return "Live TX blocked: radio is not connected.";
        }

        if (_audioService is null)
        {
            return "Live TX blocked: audio service unavailable.";
        }

        if (SelectedTxDevice is null)
        {
            return "Live TX blocked: no TX audio device configured.";
        }

        if (string.IsNullOrWhiteSpace(FormatCallsign(WsjtxOperatorCallsign)))
        {
            return "Live TX blocked: operator callsign is not set.";
        }

        if (string.IsNullOrWhiteSpace(WsjtxEffectiveTransmitText))
        {
            return "Live TX blocked: no staged TX message.";
        }

        if (WsjtxPreparedTransmit is null || _wsjtxPreparedTransmitClip is null)
        {
            return "Live TX blocked: prepare TX audio first.";
        }

        return null;
    }

    private void SetWsjtxManualTransmitText(string text, bool isManualOverride)
    {
        _isSettingWsjtxManualTransmitText = true;
        try
        {
            WsjtxManualTransmitText = text;
        }
        finally
        {
            _isSettingWsjtxManualTransmitText = false;
        }

        _wsjtxManualTransmitOverride = isManualOverride && !string.IsNullOrWhiteSpace(text);
        OnPropertyChanged(nameof(WsjtxQueuedTransmitPreview));
        OnPropertyChanged(nameof(WsjtxEffectiveTransmitText));
        OnPropertyChanged(nameof(WsjtxQsoRailSummary));
    }

    private static string? NormalizeWsjtxTransmitText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToUpperInvariant();
        return normalized.Length == 0 ? null : normalized;
    }

    private async Task ExecuteArmedWsjtxTransmitAsync()
    {
        if (WsjtxPreparedTransmit is null)
        {
            return;
        }

        if (_wsjtxSlotSendInFlight)
        {
            return;
        }

        _wsjtxSlotSendInFlight = true;
        var prepared = WsjtxPreparedTransmit;
        var preparedClip = _wsjtxPreparedTransmitClip;
        var sentMessage = prepared.MessageText;
        var attemptedLiveTransmit = false;
        var pttRaised = false;
        var txAudioStarted = false;
        WsjtxTransmitArmedLocal = false;

        try
        {
            var interlockError = ValidateWsjtxLiveTransmitInterlock();
            if (interlockError is not null)
            {
                WsjtxAwaitingReply = false;
                WsjtxTransmitArmStatus = interlockError;
                WsjtxRxStatus = "Live TX blocked before send";
                return;
            }

            var route = BuildCurrentAudioRoute();
            var liveClip = WithLeadingSilence(preparedClip!, 250);
            var audioService = _audioService!;
            var radioService = _radioService!;
            var clipDurationMs = Math.Max(250, (int)Math.Ceiling(
                liveClip.PcmBytes.Length / (double)(liveClip.SampleRate * liveClip.Channels * 2) * 1000.0));

            attemptedLiveTransmit = true;
            await audioService.StartTransmitPcmAsync(route, liveClip, CancellationToken.None).ConfigureAwait(false);
            txAudioStarted = true;
            await radioService.SetPttAsync(true, CancellationToken.None).ConfigureAwait(false);
            pttRaised = true;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                VoiceTxStatus = "WSJT TX audio live";
                WsjtxRxStatus = $"Live TX on-air: {sentMessage}";
                RadioStatusSummary = $"Weak-signal TX live  |  {prepared.ModeLabel}  |  {prepared.TxAudioFrequencyHz:+0;-0;0} Hz";
            });

            await Task.Delay(clipDurationMs + 150, CancellationToken.None).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() => CompleteArmedWsjtxTransmit(prepared, sentMessage, true));
        }
        catch (Exception ex)
        {
            try
            {
                if (_radioService is not null)
                {
                    await _radioService.SetPttAsync(false, CancellationToken.None).ConfigureAwait(false);
                }

                if (_audioService is not null)
                {
                    await _audioService.StopTransmitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch
            {
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                VoiceTxStatus = "TX audio idle";
                WsjtxAwaitingReply = false;
                WsjtxTransmitArmStatus = $"TX failed: {ex.Message}";
                WsjtxRxStatus = "Live TX failed";
                RadioStatusSummary = $"Weak-signal TX failed: {ex.Message}";
            });
        }
        finally
        {
            if (attemptedLiveTransmit)
            {
                try
                {
                    if (txAudioStarted && _audioService is not null)
                    {
                        await _audioService.StopTransmitAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch
                {
                }

                try
                {
                    if (pttRaised && _radioService is not null)
                    {
                        await _radioService.SetPttAsync(false, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch
                {
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    VoiceTxStatus = "TX audio idle";
                });
            }

            _wsjtxSlotSendInFlight = false;
        }
    }

    private void CompleteArmedWsjtxTransmit(WsjtxPreparedTransmit prepared, string sentMessage, bool wasLiveTransmit)
    {
        InsertOutgoingWsjtxMessage(prepared);
        WsjtxPreparedTransmitStatus = wasLiveTransmit
            ? $"On-air TX sent ({prepared.GeneratorName})."
            : $"Prepared artifact retained after slot send ({prepared.GeneratorName}).";
        WsjtxRxStatus = wasLiveTransmit
            ? $"Live TX sent on slot: {sentMessage}"
            : $"Simulated TX sent on slot: {sentMessage}";

        if (WsjtxCallingCq)
        {
            WsjtxAwaitingReply = true;
            WsjtxTransmitArmStatus = "CQ sent; waiting for a reply on the tracked lane.";
        }
        else if (WsjtxActiveSession is not null)
        {
            WsjtxAwaitingReply = true;
            WsjtxTransmitArmStatus = $"Waiting for {WsjtxActiveSession.OtherCall} on {WsjtxActiveSession.FrequencyOffsetHz:+0;-0;0} Hz";
        }
        else
        {
            WsjtxAwaitingReply = false;
            WsjtxTransmitArmStatus = $"Slot send complete at {prepared.TxAudioFrequencyHz:+0;-0;0} Hz";
        }
    }

    partial void OnWsjtxSelectedModeChanged(string value)
    {
        WsjtxFrequencyOptions = WsjtxModeCatalog.GetFrequencyLabels(value);
        if (!WsjtxFrequencyOptions.Contains(WsjtxSelectedFrequency, StringComparer.OrdinalIgnoreCase))
        {
            WsjtxSelectedFrequency = WsjtxModeCatalog.GetDefaultFrequencyLabel(value);
        }

        var mode = WsjtxModeCatalog.GetMode(value);
        WsjtxAutoSequenceEnabled = mode.SupportsAutoSequence && WsjtxAutoSequenceEnabled;
        WsjtxCycleDisplay = $"{mode.Label}  |  {mode.CycleLengthSeconds:0.#}s cycle  |  Next --.-s";
        WsjtxSessionNotes = DescribeWsjtxMode(value);
        OnPropertyChanged(nameof(WsjtxSelectedModeSupportsQso));
        OnPropertyChanged(nameof(WsjtxRxFrequencyTitle));
        OnPropertyChanged(nameof(WsjtxRxTrackStatus));
        OnPropertyChanged(nameof(WsjtxLongwaveLogPreview));
        OnPropertyChanged(nameof(WsjtxLongwaveLogDetail));
        ClearWsjtxMessages();
        WsjtxRxStatus = $"Mode selected: {value}";
        RebuildWsjtxSuggestedMessages();
    }

    partial void OnJs8SelectedModeChanged(string value)
    {
        Js8FrequencyOptions = WsjtxModeCatalog.GetFrequencyLabels(value);
        if (!Js8FrequencyOptions.Contains(Js8SelectedFrequency, StringComparer.OrdinalIgnoreCase))
        {
            Js8SelectedFrequency = WsjtxModeCatalog.GetDefaultFrequencyLabel(value);
        }

        if (WsjtxSelectedMode.StartsWith("JS8", StringComparison.OrdinalIgnoreCase))
        {
            WsjtxSelectedMode = value;
            WsjtxSelectedFrequency = Js8SelectedFrequency;
        }
    }

    partial void OnJs8SelectedFrequencyChanged(string value)
    {
        if (WsjtxSelectedMode.StartsWith("JS8", StringComparison.OrdinalIgnoreCase))
        {
            WsjtxSelectedFrequency = value;
        }
    }

    partial void OnNoiseReductionLevelChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 15);
        if (clamped != value)
        {
            NoiseReductionLevel = clamped;
            return;
        }

        if (!_isUpdatingNoiseReductionLevelFromRadio)
        {
            _noiseReductionInteractionUntilUtc = DateTimeOffset.UtcNow.AddSeconds(4);
        }
    }

    partial void OnManualNotchPositionChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 255);
        if (clamped != value)
        {
            ManualNotchPosition = clamped;
        }
    }

    partial void OnSelectedSstvReplyBaseImageChanged(SstvImageItem? value)
    {
        OnPropertyChanged(nameof(SstvReplyPreviewBitmap));
        OnPropertyChanged(nameof(SstvReplyCanvasWidth));
        OnPropertyChanged(nameof(SstvReplyCanvasHeight));
        OnPropertyChanged(nameof(SstvReplyHasBaseImage));
        OnPropertyChanged(nameof(SstvReplyShowPlaceholder));
        SstvTransmitStatus = value is null
            ? "Choose a reply image to prepare TX."
            : "Reply image changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSstvSelectedTxModeChanged(string value)
    {
        SstvTransmitStatus = "TX mode changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSstvTxCwIdEnabledChanged(bool value)
    {
        SstvTransmitStatus = "CW ID setting changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSstvTxFskIdEnabledChanged(bool value)
    {
        SstvTransmitStatus = "FSKID setting changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSstvTxCwIdTextChanged(string value)
    {
        SstvTransmitStatus = "CW ID text changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSstvTxCwIdFrequencyHzChanged(int value)
    {
        SstvTransmitStatus = "CW ID frequency changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSstvTxCwIdWpmChanged(int value)
    {
        SstvTransmitStatus = "CW ID speed changed; prepare TX when ready.";
        RefreshPreparedSstvTransmitSummary();
    }

    partial void OnSelectedSstvReplyLayoutTemplateChanged(SstvTemplateItem? value)
    {
        if (value is not null)
        {
            SstvReplyTemplateName = value.Name;
        }
    }

    partial void OnSstvReplyOverlayItemsChanged(ObservableCollection<SstvOverlayItemViewModel> value)
    {
        AttachSstvReplyLayoutChangeTracking(value);
        MarkSstvReplyLayoutDirty();
    }

    partial void OnSstvReplyImageOverlayItemsChanged(ObservableCollection<SstvImageOverlayItemViewModel> value)
    {
        AttachSstvReplyLayoutChangeTracking(value);
        MarkSstvReplyLayoutDirty();
    }

    [RelayCommand]
    private async Task QueueCwSendTextAsync()
    {
        var text = CwSendText.Trim();
        if (_radioService is null || CanConnect)
        {
            CwTxStatus = "Radio not connected";
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            CwTxStatus = "CW TX idle";
            return;
        }

        if (IsCwSending)
        {
            CwTxStatus = "CW send already in progress";
            return;
        }

        var normalized = text.ToUpperInvariant();
        CwSendText = normalized;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            CwTxStatus = "Nothing sendable in CW text";
            return;
        }

        _cwSendCts?.Cancel();
        _cwSendCts?.Dispose();
        _cwSendCts = new CancellationTokenSource();
        var token = _cwSendCts.Token;

        IsCwSending = true;
        CwTxStatus = $"Queueing {normalized.Length} chars to rig keyer...";
        _cwSendTask = RunCwSendAsync(normalized, token);
        await _cwSendTask;
    }

    [RelayCommand]
    private async Task StopCwSendAsync()
    {
        _cwSendCts?.Cancel();
        if (_radioService is not null)
        {
            try
            {
                await _radioService.StopCwSendAsync(CancellationToken.None);
            }
            catch
            {
            }
        }

        if (_cwSendTask is not null)
        {
            try
            {
                await _cwSendTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [RelayCommand]
    private async Task ApplyCwRigSettingsAsync()
    {
        if (_radioService is null || CanConnect)
        {
            CwTxStatus = "Radio not connected";
            return;
        }

        try
        {
            await _radioService.SetCwPitchAsync(CwPitchHz, CancellationToken.None);
            await _radioService.SetCwKeyerSpeedAsync(CwWpm, CancellationToken.None);
            _cwRigSettingsDirty = false;
            CwTxStatus = $"CW rig settings applied: {CwWpm} WPM, {CwPitchHz} Hz";
        }
        catch (Exception ex)
        {
            CwTxStatus = $"CW settings error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyVoiceRigSettingsAsync()
    {
        if (_radioService is null || CanConnect)
        {
            VoiceTxStatus = "Radio not connected";
            return;
        }

        try
        {
            await ApplyVoiceRigSettingsCoreAsync();
            VoiceTxStatus = $"Rig voice settings applied: mic {VoiceMicGainPercent}% / comp {VoiceCompressionPercent} / rf {VoiceRfPowerPercent}%";
        }
        catch (Exception ex)
        {
            VoiceTxStatus = $"Voice settings error: {ex.Message}";
        }
    }

    partial void OnCwPitchHzChanged(int value)
    {
        if (!_isUpdatingCwRigSettingsFromRadio)
        {
            _cwRigSettingsDirty = true;
        }
    }

    partial void OnCwWpmChanged(int value)
    {
        if (!_isUpdatingCwRigSettingsFromRadio)
        {
            _cwRigSettingsDirty = true;
        }
    }

    [RelayCommand]
    private async Task RefreshAudioDevicesAsync()
    {
        await LoadAudioDevicesAsync();
        SettingsStatusMessage = "Audio devices refreshed";
    }

    [RelayCommand]
    private async Task SetSelectedModeAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _radioService.SetModeAsync(SelectedMode, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Mode change failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetModeDirectAsync(RadioMode mode)
    {
        SelectedMode = mode;
        AvailableModes = BuildModePresets(mode);
        await SetSelectedModeAsync();
    }

    [RelayCommand]
    private async Task AdjustVfoBByStepAsync(long deltaHz)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            var next = Math.Max(30_000, VfoBFrequencyHz + deltaHz);
            await _radioService.SetVfoBFrequencyAsync(next, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"VFO B tune failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExchangeVfosAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.ToggleActiveVfoAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"A/B failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task EqualizeVfosAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.EqualizeVfosAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"A=B failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleSplitAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetSplitAsync(!IsSplitEnabled, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Split failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetFilterWidthAsync(object? filterSlot)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        var slot = filterSlot switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => SelectedFilterSlot,
        };

        IsBusy = true;
        try
        {
            await _radioService.SetFilterSlotAsync(slot, CancellationToken.None);
            UpdateActiveFilterSlot(slot);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Filter change failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task JumpToBandAsync(long frequencyHz)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _radioService.SetFrequencyAsync(frequencyHz, CancellationToken.None);
            UpdateActiveBand(frequencyHz);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Band jump failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AdjustFrequencyByStepAsync(long deltaHz)
    {
        if (_radioService is null || CanConnect || deltaHz == 0)
        {
            return;
        }

        var next = Math.Clamp(CurrentFrequencyHz + deltaHz, 100_000L, 450_000_000L);
        if (next == CurrentFrequencyHz)
        {
            return;
        }

        CurrentFrequencyHz = next;
        FrequencyDisplay = $"{next:N0} Hz";
        UpdateActiveBand(next);
        IsBusy = true;

        try
        {
            await _radioService.SetFrequencyAsync(next, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Tune failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SetWaterfallZoom(object? zoom)
    {
        var value = zoom switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => WaterfallZoom,
        };

        WaterfallZoom = Math.Clamp(value, 1, 10);
        Zoom1Brush = BuildToggleBrush(WaterfallZoom == 1);
        Zoom2Brush = BuildToggleBrush(WaterfallZoom == 2);
        Zoom5Brush = BuildToggleBrush(WaterfallZoom == 5);
        Zoom10Brush = BuildToggleBrush(WaterfallZoom == 10);
        ApplyWaterfallDisplaySettings();
    }

    public async Task SetPttPressedAsync(bool isPressed)
    {
        if (_radioService is null || _audioService is null || CanConnect)
        {
            return;
        }

        var shouldEnableTx = isPressed;
        var isCurrentlyTx = string.Equals(PttState, "TX", StringComparison.OrdinalIgnoreCase);
        if (shouldEnableTx == isCurrentlyTx)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var route = BuildCurrentAudioRoute();
            if (shouldEnableTx)
            {
                await _audioService.StartTransmitAsync(route, CancellationToken.None);
                await _radioService.SetPttAsync(true, CancellationToken.None);
                VoiceTxStatus = "TX audio live";
            }
            else
            {
                await _radioService.SetPttAsync(false, CancellationToken.None);
                await _audioService.StopTransmitAsync(CancellationToken.None);
                VoiceTxStatus = "TX audio idle";
            }
        }
        catch (Exception ex)
        {
            if (shouldEnableTx)
            {
                try
                {
                    await _audioService.StopTransmitAsync(CancellationToken.None);
                }
                catch
                {
                }
            }
            RadioStatusSummary = $"PTT change failed: {ex.Message}";
            VoiceTxStatus = $"TX error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunCwSendAsync(string text, CancellationToken ct)
    {
        var radioService = _radioService;
        if (radioService is null)
        {
            IsCwSending = false;
            CwTxStatus = "Radio not connected";
            return;
        }

        try
        {
            if (_audioService is not null)
            {
                try
                {
                    await _audioService.StopTransmitAsync(ct);
                }
                catch
                {
                }
            }

            if (SelectedMode != RadioMode.Cw)
            {
                await radioService.SetModeAsync(RadioMode.Cw, ct);
            }

            CwTxStatus = $"Sending: {text}";
            await radioService.SendCwTextAsync(text, ct);
            CwTxStatus = "CW send complete";
        }
        catch (OperationCanceledException)
        {
            if (_radioService is not null)
            {
                try
                {
                    await _radioService.StopCwSendAsync(CancellationToken.None);
                }
                catch
                {
                }
            }

            CwTxStatus = "CW send stopped";
        }
        catch (Exception ex)
        {
            if (_radioService is not null)
            {
                try
                {
                    await _radioService.StopCwSendAsync(CancellationToken.None);
                }
                catch
                {
                }
            }

            CwTxStatus = $"CW send error: {ex.Message}";
        }
        finally
        {
            IsCwSending = false;
        }
    }

    [RelayCommand]
    private async Task SetPreampAsync(object? level)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        var parsedLevel = level switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => PreampLevel,
        };

        try
        {
            await _radioService.SetPreampAsync(parsedLevel, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Preamp change failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleAttenuatorAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetAttenuatorAsync(!IsAttenuatorEnabled, 20, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"ATT change failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleTunerAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetTunerEnabledAsync(!IsTunerEnabled, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Tuner change failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RetuneAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.RetuneAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Retune failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleNoiseBlankerAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetNoiseBlankerAsync(!IsNoiseBlankerEnabled, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"NB change failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleNoiseReductionAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            var enable = !IsNoiseReductionEnabled;
            var level = enable ? Math.Max(NoiseReductionLevel, 5) : 0;
            _noiseReductionInteractionUntilUtc = DateTimeOffset.UtcNow.AddSeconds(2);
            await _radioService.SetNoiseReductionAsync(enable, level, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"NR change failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetNoiseReductionLevelAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            var level = Math.Clamp(NoiseReductionLevel, 0, 15);
            _noiseReductionInteractionUntilUtc = DateTimeOffset.UtcNow.AddSeconds(2);
            await _radioService.SetNoiseReductionAsync(level > 0 || IsNoiseReductionEnabled, level, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"NR level failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleAutoNotchAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetAutoNotchAsync(!IsAutoNotchEnabled, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"AN failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleManualNotchAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            var width = ParseManualNotchWidth(SelectedManualNotchWidth);
            var position = ParseManualNotchPosition();
            await _radioService.SetManualNotchAsync(!IsManualNotchEnabled, width, position, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"MN failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetManualNotchAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            var width = ParseManualNotchWidth(SelectedManualNotchWidth);
            var position = ParseManualNotchPosition();
            await _radioService.SetManualNotchAsync(IsManualNotchEnabled, width, position, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"MN set failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleIpPlusAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetIpPlusAsync(!IsIpPlusEnabled, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"IP+ change failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetFilterShapeAsync(string shape)
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            var soft = string.Equals(shape, "soft", StringComparison.OrdinalIgnoreCase);
            await _radioService.SetFilterShapeSoftAsync(soft, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"Filter shape failed: {ex.Message}";
        }
    }

    private async Task StartAudioMonitorAsync()
    {
        if (_audioService is null)
        {
            return;
        }

        var route = new AudioRoute(
            SelectedRxDevice?.DeviceId ?? string.Empty,
            SelectedTxDevice?.DeviceId ?? string.Empty,
            SelectedMicDevice?.DeviceId ?? string.Empty,
            SelectedMonitorDevice?.DeviceId ?? string.Empty);

        IsBusy = true;
        AudioMonitorState = "Starting...";

        try
        {
            await _audioService.StartReceiveAsync(route, CancellationToken.None);
            await ApplyMonitorVolumeAsync(MonitorVolumePercent);
            AudioMonitorState = "Running";
            CanStopAudio = true;
            SettingsStatusMessage = "Audio monitor started";
        }
        catch (Exception ex)
        {
            AudioMonitorState = $"Error: {ex.Message}";
            CanStopAudio = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopAudio))]
    private async Task StopAudioMonitorAsync()
    {
        if (_audioService is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _audioService.StopReceiveAsync(CancellationToken.None);
            AudioMonitorState = "Stopped";
            RxLevelPercent = 0;
            CanStopAudio = false;
            SettingsStatusMessage = "Audio monitor stopped";
        }
        catch (Exception ex)
        {
            AudioMonitorState = $"Stop failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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

    partial void OnCanStopAudioChanged(bool value) => StopAudioMonitorCommand.NotifyCanExecuteChanged();

    partial void OnSettingsCallsignChanged(string value)
    {
        var normalized = FormatCallsign(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            SettingsCallsign = normalized;
            return;
        }

        WsjtxOperatorCallsign = normalized;
        LongwaveLogOperatorCallsign = normalized;
        RebuildWsjtxSuggestedMessages();
        RebuildJs8SuggestedMessages();
        UpdateHeaderCallsign();
    }

    partial void OnSettingsGridSquareChanged(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            SettingsGridSquare = normalized;
            return;
        }

        WsjtxOperatorGridSquare = normalized;
        RebuildWsjtxSuggestedMessages();
        RebuildJs8SuggestedMessages();
    }

    partial void OnSelectedLongwavePotaSpotChanged(LongwaveSpotSummaryItem? value)
    {
        if (value is null)
        {
            return;
        }

        LongwaveStatus = $"Selected {value.ActivatorCallsign} on {value.FrequencyText} {value.Mode}.";
    }

    partial void OnSelectedVoiceLongwavePotaSpotChanged(LongwaveSpotSummaryItem? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedLongwavePotaSpot = value;
        LongwaveStatus = $"Selected voice spot {value.ActivatorCallsign} on {value.FrequencyText} {value.Mode}.";
    }

    partial void OnSelectedVoiceLongwaveBandFilterChanged(string value) => RebuildVoiceLongwavePotaSpots();

    partial void OnSelectedLongwaveRecentContactChanged(LongwaveRecentContactItem? value)
    {
        if (value is null)
        {
            LongwaveEditingContactId = string.Empty;
            return;
        }

        ApplyLongwaveContactToEditor(value);
    }

    partial void OnSelectedLongwaveLogbookChanged(LongwaveLogbookItem? value)
    {
        if (value is null)
        {
            LongwaveRecentContacts = [];
            LongwaveSelectedLogbookName = string.Empty;
            LongwaveSelectedLogbookOperatorCallsign = string.Empty;
            LongwaveSelectedLogbookParkReference = string.Empty;
            LongwaveSelectedLogbookActivationDate = string.Empty;
            LongwaveSelectedLogbookNotes = string.Empty;
            OnPropertyChanged(nameof(WsjtxLongwaveLogPreview));
            OnPropertyChanged(nameof(WsjtxLongwaveLogDetail));
            OnPropertyChanged(nameof(LongwaveSelectedLogbookQrzUploadSummary));
            OnPropertyChanged(nameof(LongwaveQuickLogSummary));
            return;
        }

        LongwaveSelectedLogbookName = value.Name;
        LongwaveSelectedLogbookOperatorCallsign = value.OperatorCallsign;
        LongwaveSelectedLogbookParkReference = value.ParkReference ?? string.Empty;
        LongwaveSelectedLogbookActivationDate = value.ActivationDate ?? string.Empty;
        LongwaveSelectedLogbookNotes = value.Notes ?? string.Empty;
        LongwaveLogStatus = $"Using Longwave logbook {value.Name}.";
        OnPropertyChanged(nameof(WsjtxLongwaveLogPreview));
        OnPropertyChanged(nameof(WsjtxLongwaveLogDetail));
        OnPropertyChanged(nameof(LongwaveSelectedLogbookQrzUploadSummary));
        OnPropertyChanged(nameof(LongwaveQuickLogSummary));
        _ = RefreshLongwaveContactsForSelectionAsync();
    }

    partial void OnCurrentFrequencyHzChanged(long value)
    {
        OnPropertyChanged(nameof(WsjtxLongwaveLogPreview));
        OnPropertyChanged(nameof(WsjtxLongwaveLogDetail));
    }

    partial void OnSettingsLongwaveEnabledChanged(bool value) => ApplyLongwaveSettingsState(_settings);
    partial void OnSettingsLongwaveBaseUrlChanged(string value) => ApplyLongwaveSettingsState(_settings);
    partial void OnSettingsLongwaveClientApiTokenChanged(string value) => ApplyLongwaveSettingsState(_settings);
    partial void OnSettingsLongwaveDefaultLogbookNameChanged(string value) => ApplyLongwaveSettingsState(_settings);
    partial void OnSettingsLongwaveDefaultLogbookNotesChanged(string value) => ApplyLongwaveSettingsState(_settings);

    partial void OnConnectionStateChanged(string value) => UpdateHeaderCallsign();

    partial void OnCurrentWorkspaceChanged(WorkspaceKind value)
    {
        IsOperatingWorkspace = value == WorkspaceKind.Operating;
        IsSettingsWorkspace = value == WorkspaceKind.Settings;
        IsDiagnosticsWorkspace = value == WorkspaceKind.Diagnostics;
        UpdateBandConditionsVisibility();
    }

    partial void OnSettingsBandConditionsEnabledChanged(bool value)
    {
        IsBandConditionsEnabled = value;
        UpdateBandConditionsVisibility();
    }

    partial void OnSettingsShowExperimentalCwChanged(bool value)
    {
        ShowCwPanel = value;
        UpdateModePanelsVisibility();
    }

    partial void OnIsBandConditionsEnabledChanged(bool value) => UpdateBandConditionsVisibility();

    partial void OnMonitorVolumePercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (clamped != value)
        {
            MonitorVolumePercent = clamped;
            return;
        }

        _ = ApplyMonitorVolumeAsync(clamped);
        ScheduleRuntimeUiStateSave();
    }

    partial void OnFreedvSpeechVolumePercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (clamped != value)
        {
            FreedvSpeechVolumePercent = clamped;
            return;
        }

        _ = ApplyFreedvSpeechVolumeAsync(clamped);
        ScheduleRuntimeUiStateSave();
    }

    partial void OnSelectedMonitorDeviceChanged(AudioDeviceInfo? value)
    {
        VoiceMonitorDeviceDisplay = value?.FriendlyName ?? "Not configured";
        FreedvMonitorDeviceDisplay = DescribeFreedvMonitorDevice();
    }

    partial void OnSelectedFreedvMonitorDeviceChanged(AudioDeviceInfo? value)
    {
        FreedvMonitorDeviceDisplay = DescribeFreedvMonitorDevice();
    }

    partial void OnAudioMonitorStateChanged(string value) => DiagnosticsAudioMonitorState = value;

    partial void OnVoiceTxStatusChanged(string value) => DiagnosticsVoiceTxState = value;

    partial void OnVoiceMicGainPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (clamped != value)
        {
            VoiceMicGainPercent = clamped;
            return;
        }

        if (!_isUpdatingVoiceRigSettingsFromRadio)
        {
            _voiceRigSettingsDirty = true;
        }
    }

    partial void OnVoiceCompressionPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 10);
        if (clamped != value)
        {
            VoiceCompressionPercent = clamped;
            return;
        }

        if (!_isUpdatingVoiceRigSettingsFromRadio)
        {
            _voiceRigSettingsDirty = true;
        }
    }

    partial void OnVoiceRfPowerPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 100);
        if (clamped != value)
        {
            VoiceRfPowerPercent = clamped;
            return;
        }

        if (!_isUpdatingVoiceRigSettingsFromRadio)
        {
            _voiceRigSettingsDirty = true;
        }
    }

    partial void OnWaterfallFloorPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 95);
        if (clamped != value)
        {
            WaterfallFloorPercent = clamped;
            return;
        }

        if (WaterfallFloorPercent >= WaterfallCeilingPercent)
        {
            WaterfallCeilingPercent = Math.Min(100, WaterfallFloorPercent + 1);
        }

        ApplyWaterfallDisplaySettings();
        ScheduleRuntimeUiStateSave();
    }

    partial void OnWaterfallCeilingPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 1, 100);
        if (clamped != value)
        {
            WaterfallCeilingPercent = clamped;
            return;
        }

        if (WaterfallCeilingPercent <= WaterfallFloorPercent)
        {
            WaterfallFloorPercent = Math.Max(0, WaterfallCeilingPercent - 1);
        }

        ApplyWaterfallDisplaySettings();
        ScheduleRuntimeUiStateSave();
    }

    private void ApplySettings(AppSettings settings)
    {
        _suppressRuntimeUiStatePersistence = true;
        try
        {
        _settings = settings;
        Theme = settings.Ui.Theme;
        WindowWidth = settings.Ui.WindowWidth;
        WindowHeight = settings.Ui.WindowHeight;
        RadioBackend = settings.Radio.ControlBackend;
        RadioPort = settings.Radio.CivPort;
        FlrigEndpoint = $"{settings.Interop.FlrigHost}:{settings.Interop.FlrigPort}";
        DiagnosticsRadioPort = settings.Radio.CivPort;
        DiagnosticsRadioBaud = settings.Radio.CivBaud.ToString();
        DiagnosticsRadioAddress = $"0x{settings.Radio.CivAddress:X2} ({settings.Radio.CivAddress})";
        DiagnosticsInterOpEndpoint = $"{settings.Interop.FlrigHost}:{settings.Interop.FlrigPort}";
        DiagnosticsBandConditionsState = settings.Ui.BandConditionsEnabled ? DiagnosticsBandConditionsState : "Disabled";

        SettingsCallsign = FormatCallsign(settings.Station.Callsign);
        SettingsGridSquare = settings.Station.GridSquare;
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
        _voiceRigSettingsDirty = false;
        LongwaveLogOperatorCallsign = FormatCallsign(settings.Station.Callsign);
        ApplyLongwaveSettingsState(settings);
        CanConnect = _radioService is not null
            && !CanDisconnect
            && string.Equals(_settings.Radio.ControlBackend, "direct", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_settings.Radio.CivPort, "auto", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_settings.Radio.CivPort);

        UpdateAudioSelectionsFromSettings();
        UpdateModePanelsVisibility();
        }
        finally
        {
            _suppressRuntimeUiStatePersistence = false;
        }
    }

    public async Task PersistRuntimeUiStateAsync()
    {
        if (_settingsStore is null)
        {
            return;
        }

        var updated = _settings with
        {
            Audio = _settings.Audio with
            {
                MonitorVolumePercent = MonitorVolumePercent,
            },
            Ui = _settings.Ui with
            {
                WaterfallFloorPercent = WaterfallFloorPercent,
                WaterfallCeilingPercent = WaterfallCeilingPercent,
            }
        };

        await _settingsStore.SaveAsync(updated, CancellationToken.None);
        _settings = updated;
    }

    private void ScheduleRuntimeUiStateSave()
    {
        if (_suppressRuntimeUiStatePersistence || _settingsStore is null)
        {
            return;
        }

        _runtimeUiStateSaveCts?.Cancel();
        _runtimeUiStateSaveCts?.Dispose();
        _runtimeUiStateSaveCts = new CancellationTokenSource();
        var ct = _runtimeUiStateSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested)
                {
                    await PersistRuntimeUiStateAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, ct);
    }

    private async Task InitializeAudioAsync()
    {
        await LoadAudioDevicesAsync();
        await EnsureAudioMonitorStartedAsync();
    }

    private async Task LoadAudioDevicesAsync()
    {
        if (_audioService is null)
        {
            return;
        }

        try
        {
            var devices = await _audioService.GetDevicesAsync(CancellationToken.None);
            var inputs = devices.Where(d => d.IsInput).ToArray();
            var outputs = devices.Where(d => d.IsOutput).ToArray();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RxDeviceOptions = inputs;
                OutputDeviceOptions = outputs;
                UpdateAudioSelectionsFromSettings();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SettingsStatusMessage = $"Audio enumeration failed: {ex.Message}";
            });
        }
    }

    private async Task EnsureAudioMonitorStartedAsync()
    {
        if (_monitorAutostartAttempted || _audioService is null || CanStopAudio)
        {
            return;
        }

        _monitorAutostartAttempted = true;

        if (SelectedRxDevice is null || SelectedMonitorDevice is null)
        {
            AudioMonitorState = "Not configured";
            return;
        }

        await StartAudioMonitorAsync();
    }

    private async Task ApplyMonitorVolumeAsync(int volumePercent)
    {
        if (_audioService is null)
        {
            return;
        }

        try
        {
            await _audioService.SetMonitorVolumeAsync(volumePercent / 100f, CancellationToken.None);
        }
        catch
        {
        }
    }

    private async Task ApplyFreedvSpeechVolumeAsync(int volumePercent)
    {
        if (_audioService is null)
        {
            return;
        }

        try
        {
            await _audioService.SetDecodedMonitorVolumeAsync(volumePercent / 100f, CancellationToken.None);
        }
        catch
        {
        }
    }

    private async Task ApplyVoiceRigSettingsCoreAsync()
    {
        if (_radioService is null || CanConnect)
        {
            return;
        }

        try
        {
            await _radioService.SetVoiceMicGainAsync(VoiceMicGainPercent, CancellationToken.None);
            await _radioService.SetVoiceCompressionAsync(VoiceCompressionPercent, CancellationToken.None);
            await _radioService.SetRfPowerAsync(VoiceRfPowerPercent, CancellationToken.None);
            _voiceRigSettingsDirty = false;
        }
        catch
        {
        }
    }

    private AudioRoute BuildCurrentAudioRoute() => new(
        SelectedRxDevice?.DeviceId ?? string.Empty,
        SelectedTxDevice?.DeviceId ?? string.Empty,
        SelectedMicDevice?.DeviceId ?? string.Empty,
        SelectedMonitorDevice?.DeviceId ?? string.Empty);

    private AudioRoute BuildFreedvMonitorAudioRoute() => new(
        SelectedRxDevice?.DeviceId ?? string.Empty,
        SelectedTxDevice?.DeviceId ?? string.Empty,
        SelectedMicDevice?.DeviceId ?? string.Empty,
        GetSelectedFreedvMonitorDevice()?.DeviceId ?? string.Empty);

    private AudioDeviceInfo? GetSelectedFreedvMonitorDevice() =>
        SelectedFreedvMonitorDevice;

    private string DescribeFreedvMonitorDevice()
    {
        if (SelectedFreedvMonitorDevice is null)
        {
            return "Not configured";
        }

        if (SelectedMonitorDevice is not null
            && string.Equals(SelectedFreedvMonitorDevice.DeviceId, SelectedMonitorDevice.DeviceId, StringComparison.Ordinal))
        {
            return $"{SelectedFreedvMonitorDevice.FriendlyName} (same physical output as PC RX)";
        }

        return SelectedFreedvMonitorDevice.FriendlyName;
    }

    private async Task PlayFreedvSpeechFrameAsync(Pcm16AudioClip clip)
    {
        var duration = clip.PcmBytes.Length / (double)(clip.SampleRate * clip.Channels * 2);
            var debugPath = SaveFreedvDecodedSpeechFrame(clip);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FreedvLastDecodedSpeechPath = debugPath ?? "Decoded speech was not saved.";
                FreedvDecodedAudioStatus = string.IsNullOrWhiteSpace(debugPath)
                    ? $"Decoded speech frame: {duration:0.00}s @ {clip.SampleRate} Hz"
                    : $"Decoded speech frame: {duration:0.00}s @ {clip.SampleRate} Hz | saved {Path.GetFileName(debugPath)}";
        });

        if (_audioService is null)
        {
            return;
        }

        try
        {
            await _audioService.PlayDecodedMonitorPcmAsync(BuildFreedvMonitorAudioRoute(), clip, CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FreedvLastDecodedSpeechPath = debugPath ?? "Decoded speech was not saved.";
                FreedvDecodedAudioStatus = string.IsNullOrWhiteSpace(debugPath)
                    ? $"Decoded speech playing: {duration:0.00}s @ {clip.SampleRate} Hz"
                    : $"Decoded speech playing: {duration:0.00}s @ {clip.SampleRate} Hz | saved {debugPath}";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                FreedvDecodedAudioStatus = $"FreeDV speech monitor failed: {ex.Message}";
            });
        }
    }

    private string? SaveFreedvDecodedSpeechFrame(Pcm16AudioClip clip)
    {
        try
        {
            var mode = FreedvSelectedMode.ToLowerInvariant();
            var path = Path.Combine(_freedvDebugDirectory, $"last_{mode}_speech.wav");
            WritePcm16Wave(path, clip);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static void WritePcm16Wave(string path, Pcm16AudioClip clip)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataSize = clip.PcmBytes.Length;
        var blockAlign = (short)(clip.Channels * sizeof(short));
        var byteRate = clip.SampleRate * blockAlign;

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)clip.Channels);
        writer.Write(clip.SampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        writer.Write(clip.PcmBytes);
    }

    private void ApplyWaterfallDisplaySettings()
    {
        _waterfallService?.UpdateDisplaySettings(
            WaterfallFloorPercent / 100f,
            WaterfallCeilingPercent / 100f,
            WaterfallZoom);
        UpdateWaterfallBitmap();
    }

    private void UpdateAudioSelectionsFromSettings()
    {
        SelectedRxDevice = FindDevice(RxDeviceOptions, _settings.Audio.RxDeviceId);
        SelectedMicDevice = FindDevice(RxDeviceOptions, _settings.Audio.MicDeviceId);
        SelectedTxDevice = FindDevice(OutputDeviceOptions, _settings.Audio.TxDeviceId);
        SelectedMonitorDevice = FindDevice(OutputDeviceOptions, _settings.Audio.MonitorDeviceId);
        SelectedFreedvMonitorDevice = FindDevice(OutputDeviceOptions, _settings.Audio.FreedvMonitorDeviceId);
        VoiceRxDeviceDisplay = SelectedRxDevice?.FriendlyName ?? "Not configured";
        VoiceTxDeviceDisplay = SelectedTxDevice?.FriendlyName ?? "Not configured";
        VoiceMicDeviceDisplay = SelectedMicDevice?.FriendlyName ?? "Not configured";
        VoiceMonitorDeviceDisplay = SelectedMonitorDevice?.FriendlyName ?? "Not configured";
        FreedvMonitorDeviceDisplay = DescribeFreedvMonitorDevice();
        DiagnosticsAudioRadioRx = VoiceRxDeviceDisplay;
        DiagnosticsAudioRadioTx = VoiceTxDeviceDisplay;
        DiagnosticsAudioPcTx = VoiceMicDeviceDisplay;
        DiagnosticsAudioPcRx = VoiceMonitorDeviceDisplay;
    }

    private void UpdateBandConditionsVisibility()
    {
        ShowBandConditionsPanel = IsOperatingWorkspace && IsBandConditionsEnabled;
    }

    private void UpdateModePanelsVisibility()
    {
        if (!ShowCwPanel && SelectedModePanelTabIndex == 1)
        {
            SelectedModePanelTabIndex = 0;
        }
    }

    private static AudioDeviceInfo? FindDevice(IReadOnlyList<AudioDeviceInfo> devices, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return devices.FirstOrDefault(d => d.IsDefault);
        }

        return devices.FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(d => d.IsDefault);
    }

    private void UpdateWaterfallBitmap()
    {
        var frame = _waterfallRenderSource?.GetLatestFrame();
        if (frame is null || frame.Width <= 0 || frame.Height <= 0 || frame.WaterfallPixels.Length == 0)
        {
            return;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using (var locked = bitmap.Lock())
        {
            Marshal.Copy(frame.WaterfallPixels, 0, locked.Address, frame.WaterfallPixels.Length);
        }

        WaterfallBitmap = bitmap;
        FrequencyMarkers = BuildFrequencyMarkers(frame.CenterFrequencyHz, frame.SpanHz);
    }

    private static string BuildSpectrumPath(IReadOnlyList<float> bins, double width, double height)
    {
        if (bins.Count == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < bins.Count; i++)
        {
            var x = bins.Count == 1 ? 0 : width * i / (bins.Count - 1);
            var y = height - (Math.Clamp(bins[i], 0f, 1f) * height);
            sb.Append(i == 0 ? "M " : " L ");
            sb.Append(x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(' ');
            sb.Append(y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> BuildFrequencyMarkers(long centerFrequencyHz, int spanHz)
    {
        if (centerFrequencyHz <= 0 || spanHz <= 0)
        {
            return ["---", "---", "---", "---", "---"];
        }

        var half = spanHz / 2.0;
        return
        [
            FormatMarker(centerFrequencyHz - (long)half),
            FormatMarker(centerFrequencyHz - (long)(half / 2.0)),
            FormatMarker(centerFrequencyHz),
            FormatMarker(centerFrequencyHz + (long)(half / 2.0)),
            FormatMarker(centerFrequencyHz + (long)half),
        ];
    }

    private static string FormatMarker(long hz) => $"{hz / 1_000_000.0:0.000}";

    private static string FormatTelemetryPercent(int percent)
        => $"{Math.Clamp(percent, 0, 100),3}%";

    private static string FormatCallsign(string callsign) => callsign.Trim().ToUpperInvariant();

    private static string FormatWeakSignalGrid(string gridSquare)
    {
        var normalized = gridSquare.Trim().ToUpperInvariant();
        return normalized.Length >= 4 ? normalized[..4] : normalized;
    }

    private void UpdateWsjtxSessionNotes(WsjtxModeTelemetry? telemetry = null)
    {
        telemetry ??= _lastWsjtxTelemetry;
        if (telemetry is null)
        {
            return;
        }

        WsjtxSessionNotes = $"{telemetry.ActiveWorker}  |  Sync {(telemetry.IsClockSynchronized ? "locked" : "open")}  |  AutoSeq {(WsjtxAutoSequenceEnabled ? "on" : "off")}  |  TX {(telemetry.IsTransmitArmed ? "armed" : "idle")}  |  Decodes {telemetry.DecodeCount}";
    }

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

    private static string FormatCivAddress(int address) => $"{address:X2}h";

    private static (int Ioc, int Lpm) ParseWefaxMode(string modeLabel)
    {
        var normalized = modeLabel?.Trim() ?? string.Empty;
        if (normalized.Contains("288", StringComparison.OrdinalIgnoreCase))
        {
            return (288, normalized.Contains("90", StringComparison.OrdinalIgnoreCase) ? 90 : normalized.Contains("60", StringComparison.OrdinalIgnoreCase) ? 60 : 120);
        }

        if (normalized.Contains("90", StringComparison.OrdinalIgnoreCase))
        {
            return (576, 90);
        }

        if (normalized.Contains("60", StringComparison.OrdinalIgnoreCase))
        {
            return (576, 60);
        }

        return (576, 120);
    }

    private static (int ShiftHz, double BaudRate) ParseRttyProfile(string profileLabel) => profileLabel switch
    {
        "170 Hz / 75 baud" => (170, 75.0),
        "425 Hz / 45.45 baud" => (425, 45.45),
        _ => (170, 45.45),
    };

    private static double ParseRttyAudioCenterHz(string value)
    {
        if (double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hz))
        {
            return Math.Clamp(hz, 300.0, 3200.0);
        }

        return 1700.0;
    }

    private static double ParseKeyboardAudioCenterHz(string value)
    {
        if (double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hz))
        {
            return Math.Clamp(hz, 300.0, 3200.0);
        }

        return 1000.0;
    }

    private static double GetWsjtxCycleLengthSeconds(string modeLabel) =>
        WsjtxModeCatalog.GetMode(modeLabel).CycleLengthSeconds;

    private static string ResolveWsjtxModeSelection(string selectedMode, string frequencyLabel)
    {
        if (!string.IsNullOrWhiteSpace(frequencyLabel))
        {
            foreach (var modeLabel in WsjtxModeCatalog.GetModeLabels())
            {
                if (frequencyLabel.Contains(modeLabel, StringComparison.OrdinalIgnoreCase))
                {
                    return modeLabel;
                }
            }
        }

        return selectedMode;
    }

    private static string DescribeWsjtxMode(string modeLabel)
    {
        var mode = WsjtxModeCatalog.GetMode(modeLabel);
        if (string.Equals(mode.Label, "WSPR", StringComparison.OrdinalIgnoreCase))
        {
            return "WSPR monitor ready. Uses the selected two-minute WSPR band window; TX/QSO rail is hidden for now.";
        }

        if (mode.Label.StartsWith("JS8 ", StringComparison.OrdinalIgnoreCase))
        {
            return $"{mode.Label} monitor ready. Uses JS8Call-compatible cycles and expects a JS8Call jt9.exe via SHACKSTACK_JS8_JT9_PATH or a bundled js8call-tools runtime.";
        }

        var sequenceText = mode.SupportsAutoSequence ? "Auto-sequence capable." : "Manual/semi-manual sequencing expected.";
        var clockText = mode.RequiresAccurateClock ? "Tight UTC discipline matters." : "Clock still matters, but this mode is less timing-sensitive.";
        return $"{mode.Label} ready. {sequenceText} {clockText}";
    }

    private void RebuildWsjtxSuggestedMessages()
    {
        var items = new List<WsjtxSuggestedMessageItem>();
        if (!IsWsjtxQsoMode(WsjtxSelectedMode))
        {
            WsjtxSuggestedMessages = new ObservableCollection<WsjtxSuggestedMessageItem>(items);
            SelectedWsjtxSuggestedMessage = null;
            return;
        }

        var myCall = FormatCallsign(WsjtxOperatorCallsign);
        var myGrid = FormatWeakSignalGrid(WsjtxOperatorGridSquare);
        var selected = FindActiveWsjtxConversationAnchor(myCall) ?? SelectedWsjtxMessage;
        var selectedState = ClassifyWsjtxQsoState(selected?.MessageText, myCall);
        var outboundReport = FormatWsjtxReport(selected?.SnrDb ?? -10);
        var theirCall = selectedState.OtherCall ?? TryExtractCallsign(selected?.MessageText);

        var targetCall = string.IsNullOrWhiteSpace(theirCall) ? "<CALL>" : theirCall;
        var cqText = string.IsNullOrWhiteSpace(myCall)
            ? "CQ <MYCALL> <GRID>"
            : string.IsNullOrWhiteSpace(myGrid)
                ? $"CQ {myCall}"
                : $"CQ {myCall} {myGrid}";
        var answerCqText = string.IsNullOrWhiteSpace(myCall)
            ? $"{targetCall} <MYCALL> <GRID>"
            : string.IsNullOrWhiteSpace(myGrid)
                ? $"{targetCall} {myCall}"
                : $"{targetCall} {myCall} {myGrid}";
        var reportText = string.IsNullOrWhiteSpace(myCall)
            ? $"{targetCall} <MYCALL> {outboundReport}"
            : $"{targetCall} {myCall} {outboundReport}";
        var rogerText = string.IsNullOrWhiteSpace(myCall)
            ? $"{targetCall} <MYCALL> R{outboundReport}"
            : $"{targetCall} {myCall} R{outboundReport}";
        var r73Text = string.IsNullOrWhiteSpace(myCall)
            ? $"{targetCall} <MYCALL> R73"
            : $"{targetCall} {myCall} R73";
        var rr73Text = string.IsNullOrWhiteSpace(myCall)
            ? $"{targetCall} <MYCALL> RR73"
            : $"{targetCall} {myCall} RR73";
        var signoffText = string.IsNullOrWhiteSpace(myCall)
            ? $"{targetCall} <MYCALL> 73"
            : $"{targetCall} {myCall} 73";

        items.Add(new WsjtxSuggestedMessageItem("CQ", cqText, "Call CQ on your current TX offset"));
        items.Add(new WsjtxSuggestedMessageItem("Reply CQ", answerCqText, "Answer the selected CQ with your callsign and grid"));
        items.Add(new WsjtxSuggestedMessageItem("Report", reportText, $"Send signal report {outboundReport}"));
        items.Add(new WsjtxSuggestedMessageItem("Roger", rogerText, $"Roger and send report {outboundReport}"));
        items.Add(new WsjtxSuggestedMessageItem("R73", r73Text, "Wrap the exchange with R73"));
        items.Add(new WsjtxSuggestedMessageItem("RR73", rr73Text, "Wrap the exchange with RR73"));
        items.Add(new WsjtxSuggestedMessageItem("73", signoffText, "Send a final 73"));

        WsjtxSuggestedMessages = new ObservableCollection<WsjtxSuggestedMessageItem>(items);

        var preferredSelected = SelectedWsjtxSuggestedMessage is null
            ? null
            : WsjtxSuggestedMessages.FirstOrDefault(item =>
                string.Equals(item.MessageText, SelectedWsjtxSuggestedMessage.MessageText, StringComparison.Ordinal));
        var preferredQueued = WsjtxQueuedTransmitMessage is null
            ? null
            : WsjtxSuggestedMessages.FirstOrDefault(item =>
                string.Equals(item.MessageText, WsjtxQueuedTransmitMessage.MessageText, StringComparison.Ordinal));

        WsjtxSuggestedMessageItem? preferredByStage = null;
        if (!string.IsNullOrWhiteSpace(theirCall))
        {
            preferredByStage = selectedState.Stage switch
            {
                WsjtxQsoStage.Cq or WsjtxQsoStage.Qrz => WsjtxSuggestedMessages.FirstOrDefault(item => item.Label == "Reply CQ"),
                WsjtxQsoStage.ReportToMe => WsjtxSuggestedMessages.FirstOrDefault(item => item.Label == "Roger"),
                WsjtxQsoStage.RogerToMe => WsjtxSuggestedMessages.FirstOrDefault(item => item.Label == "RR73"),
                WsjtxQsoStage.Rr73ToMe or WsjtxQsoStage.SignoffToMe => WsjtxSuggestedMessages.FirstOrDefault(item => item.Label == "73"),
                _ => null
            };
        }

        SelectedWsjtxSuggestedMessage = preferredSelected
            ?? preferredQueued
            ?? preferredByStage
            ?? WsjtxSuggestedMessages.FirstOrDefault();

        if (preferredQueued is not null && !_wsjtxManualTransmitOverride)
        {
            WsjtxQueuedTransmitMessage = preferredQueued;
            SetWsjtxManualTransmitText(preferredQueued.MessageText, isManualOverride: false);
        }
        OnPropertyChanged(nameof(WsjtxSuggestedMessagePreview));
    }

    private void RebuildJs8SuggestedMessages()
    {
        var items = new List<WsjtxSuggestedMessageItem>();
        var myCall = FormatCallsign(WsjtxOperatorCallsign);
        var myGrid = FormatWeakSignalGrid(WsjtxOperatorGridSquare);
        var target = string.IsNullOrWhiteSpace(Js8TargetCallsign)
            ? TryExtractCallsign(SelectedWsjtxMessage?.MessageText) ?? "<CALL>"
            : Js8TargetCallsign.Trim().ToUpperInvariant();

        var cqText = string.IsNullOrWhiteSpace(myCall)
            ? "CQ CQ CQ DE <MYCALL>"
            : string.IsNullOrWhiteSpace(myGrid)
                ? $"CQ CQ CQ DE {myCall}"
                : $"CQ CQ CQ DE {myCall} {myGrid}";
        var heartbeatText = string.IsNullOrWhiteSpace(myCall)
            ? "<MYCALL>: @HB HEARTBEAT"
            : string.IsNullOrWhiteSpace(myGrid)
                ? $"{myCall}: @HB HEARTBEAT"
                : $"{myCall}: @HB HEARTBEAT {myGrid}";
        var directedReply = string.IsNullOrWhiteSpace(myCall)
            ? $"{target}: <MYCALL> COPY"
            : $"{target}: {myCall} COPY";
        var reportText = string.IsNullOrWhiteSpace(myCall)
            ? $"{target}: <MYCALL> SNR?"
            : $"{target}: {myCall} SNR?";
        var ackText = $"{target}: ACK";
        var seventyThreeText = $"{target}: 73";

        items.Add(new WsjtxSuggestedMessageItem("CQ", cqText, "General JS8 CQ text"));
        items.Add(new WsjtxSuggestedMessageItem("Heartbeat", heartbeatText, "JS8 heartbeat-style presence text"));
        items.Add(new WsjtxSuggestedMessageItem("Reply", directedReply, "Directed reply to the selected station"));
        items.Add(new WsjtxSuggestedMessageItem("SNR?", reportText, "Ask the selected station for a report"));
        items.Add(new WsjtxSuggestedMessageItem("ACK", ackText, "Acknowledge the selected station"));
        items.Add(new WsjtxSuggestedMessageItem("73", seventyThreeText, "Close the JS8 exchange"));

        Js8SuggestedMessages = new ObservableCollection<WsjtxSuggestedMessageItem>(items);
        SelectedJs8SuggestedMessage = Js8SuggestedMessages.FirstOrDefault(item =>
                string.Equals(item.MessageText, SelectedJs8SuggestedMessage?.MessageText, StringComparison.Ordinal))
            ?? Js8SuggestedMessages.FirstOrDefault();
    }

    private static string? TryExtractCallsign(string? messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return null;
        }

        var tokens = messageText
            .ToUpperInvariant()
            .Replace("[ANALYSIS]", string.Empty, StringComparison.Ordinal)
            .Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            if (token.Length < 3 || token.Length > 12)
            {
                continue;
            }

            var hasLetter = token.Any(char.IsLetter);
            var hasDigit = token.Any(char.IsDigit);
            if (!hasLetter || !hasDigit)
            {
                continue;
            }

            if (token.All(ch => char.IsLetterOrDigit(ch) || ch == '/'))
            {
                return token;
            }
        }

        return null;
    }

    private string ExpandCwMacro(string? label)
    {
        var myCall = FormatCallsign(SettingsCallsign);
        if (string.IsNullOrWhiteSpace(myCall))
        {
            myCall = "<MYCALL>";
        }

        var toCall = FormatCallsign(LongwaveLogCallsign);
        if (string.IsNullOrWhiteSpace(toCall))
        {
            toCall = "<CALL>";
        }

        var park = string.IsNullOrWhiteSpace(LongwaveLogParkReference)
            ? SelectedVoiceLongwavePotaSpot?.ParkReference ?? SelectedLongwavePotaSpot?.ParkReference ?? string.Empty
            : LongwaveLogParkReference.Trim().ToUpperInvariant();

        var text = (label ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "CQ" => $"CQ CQ DE {myCall} {myCall} K",
            "DE" => $"{toCall} DE {myCall}",
            "RST" => $"{toCall} DE {myCall} 599 599",
            "TU" => $"{toCall} DE {myCall} TU",
            "73" => $"{toCall} DE {myCall} 73",
            "POTA" => string.IsNullOrWhiteSpace(park)
                ? $"CQ POTA DE {myCall} {myCall} K"
                : $"CQ POTA DE {myCall} {myCall} {park} K",
            _ => string.Empty,
        };

        return text
            .Replace("%MYCALL", myCall, StringComparison.OrdinalIgnoreCase)
            .Replace("%TOCALL", toCall, StringComparison.OrdinalIgnoreCase)
            .Replace("%RST", "599", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static WsjtxQsoState ClassifyWsjtxQsoState(string? messageText, string myCall)
    {
        if (string.IsNullOrWhiteSpace(messageText) || string.IsNullOrWhiteSpace(myCall))
        {
            return new WsjtxQsoState(WsjtxQsoStage.None, null);
        }

        var tokens = messageText
            .ToUpperInvariant()
            .Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            return new WsjtxQsoState(WsjtxQsoStage.None, null);
        }

        if (tokens[0] == "CQ" || tokens[0] == "QRZ")
        {
            return new WsjtxQsoState(tokens[0] == "CQ" ? WsjtxQsoStage.Cq : WsjtxQsoStage.Qrz, TryExtractOtherCall(tokens, myCall));
        }

        if (tokens.Length >= 3 && tokens[0] == myCall)
        {
            var other = tokens[1];
            var tail = tokens[^1];
            if (IsRogerReportToken(tail) || IsSignalReportToken(tail))
            {
                return new WsjtxQsoState(WsjtxQsoStage.ReportToMe, other);
            }

            if (tail == "RR73")
            {
                return new WsjtxQsoState(WsjtxQsoStage.Rr73ToMe, other);
            }

            if (tail == "73")
            {
                return new WsjtxQsoState(WsjtxQsoStage.SignoffToMe, other);
            }
        }

        if (tokens.Length >= 3 && tokens[1] == myCall)
        {
            var other = tokens[0];
            var tail = tokens[^1];
            if (tail == "RRR" || tail == "RR73")
            {
                return new WsjtxQsoState(WsjtxQsoStage.RogerToMe, other);
            }
        }

        return new WsjtxQsoState(WsjtxQsoStage.None, TryExtractOtherCall(tokens, myCall));
    }

    private static string? TryExtractOtherCall(string[] tokens, string myCall)
    {
        foreach (var token in tokens)
        {
            if (token == myCall)
            {
                continue;
            }

            if (LooksLikeCallsign(token))
            {
                return token;
            }
        }

        return null;
    }

    private static bool IsSignalReportToken(string token) =>
        System.Text.RegularExpressions.Regex.IsMatch(token, @"^[+-]\d{2}$");

    private static bool IsRogerReportToken(string token) =>
        System.Text.RegularExpressions.Regex.IsMatch(token, @"^R[+-]\d{2}$");

    private static string FormatWsjtxReport(int snrDb)
    {
        var clamped = Math.Clamp(snrDb, -30, 20);
        return clamped >= 0 ? $"+{clamped:00}" : clamped.ToString("00");
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

    private static bool TryParseWefaxPublishedFrequencyHz(string frequencyLabel, out long hz)
    {
        hz = 0;
        if (string.IsNullOrWhiteSpace(frequencyLabel))
        {
            return false;
        }

        var match = System.Text.RegularExpressions.Regex.Match(frequencyLabel, @"(\d+(?:\.\d+)?)\s*kHz", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        if (!double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var khz))
        {
            return false;
        }

        hz = (long)Math.Round(khz * 1000.0);
        return hz > 0;
    }

    public void RefreshFromRadioSnapshot()
    {
        if (_radioService is null)
        {
            return;
        }

        var snapshot = _radioService.CurrentState;
        ApplyRadioState(snapshot, forceRigBackedControls: true);
    }

    private void ApplyRadioState(RadioState state, bool forceRigBackedControls = false)
    {
        ConnectionState = state.IsConnected ? "Connected" : "Disconnected";
        if (state.FrequencyHz > 0)
        {
            CurrentFrequencyHz = state.FrequencyHz;
        }
        if (state.VfoAFrequencyHz > 0)
        {
            VfoAFrequencyHz = state.VfoAFrequencyHz;
        }
        if (state.VfoBFrequencyHz > 0)
        {
            VfoBFrequencyHz = state.VfoBFrequencyHz;
        }
        IsVfoBActive = state.IsVfoBActive;
        OnPropertyChanged(nameof(IsVfoAActive));
        IsSplitEnabled = state.IsSplitEnabled;

        FrequencyDisplay = state.FrequencyHz > 0 ? $"{state.FrequencyHz:N0} Hz" : "---";
        ModeDisplay = FormatModeDisplay(state.Mode);
        if (SelectedMode != state.Mode)
        {
            SelectedMode = state.Mode;
            AvailableModes = BuildModePresets(state.Mode);
        }

        PttState = state.IsPttActive ? "TX" : "RX";
        FilterWidthDisplay = $"F{state.FilterSlot}  |  {state.FilterWidthHz} Hz";
        SelectedFilterSlot = state.FilterSlot;
        UpdateActiveFilterSlot(state.FilterSlot);
        PreampLevel = state.PreampLevel;
        IsAttenuatorEnabled = state.AttenuatorDb > 0;
        IsTunerEnabled = state.IsTunerEnabled;
        IsNoiseBlankerEnabled = state.IsNoiseBlankerEnabled;
        IsNoiseReductionEnabled = state.IsNoiseReductionEnabled;
        if (DateTimeOffset.UtcNow >= _noiseReductionInteractionUntilUtc || NoiseReductionLevel == state.NoiseReductionLevel)
        {
            _isUpdatingNoiseReductionLevelFromRadio = true;
            NoiseReductionLevel = state.NoiseReductionLevel;
            _isUpdatingNoiseReductionLevelFromRadio = false;
        }
        IsAutoNotchEnabled = state.IsAutoNotchEnabled;
        IsManualNotchEnabled = state.IsManualNotchEnabled;
        ManualNotchWidth = state.ManualNotchWidth;
        SelectedManualNotchWidth = state.ManualNotchWidth switch
        {
            0 => "Wide",
            2 => "Nar",
            _ => "Mid",
        };
        ManualNotchPosition = Math.Clamp(state.ManualNotchPosition, 0, 255);
        IsIpPlusEnabled = state.IsIpPlusEnabled;
        IsFilterShapeSoft = state.IsFilterShapeSoft;
        UpdateActiveBand(state.FrequencyHz);
        UpdateRadioControlBrushes();
        ApplySmeterLevel(state.Smeter);

        if (!state.IsConnected)
        {
            _hasReceivedVoiceRigStateFromRadio = false;
        }

        if (state.IsConnected && (forceRigBackedControls || !_voiceRigSettingsDirty || !_hasReceivedVoiceRigStateFromRadio))
        {
            _isUpdatingVoiceRigSettingsFromRadio = true;
            VoiceMicGainPercent = state.VoiceMicGainPercent;
            VoiceCompressionPercent = state.VoiceCompressionPercent;
            VoiceRfPowerPercent = state.VoiceRfPowerPercent;
            _isUpdatingVoiceRigSettingsFromRadio = false;
            _hasReceivedVoiceRigStateFromRadio = true;
        }

        if (forceRigBackedControls || !_cwRigSettingsDirty)
        {
            _isUpdatingCwRigSettingsFromRadio = true;
            CwPitchHz = state.CwPitchHz;
            CwWpm = state.CwKeyerWpm;
            _isUpdatingCwRigSettingsFromRadio = false;
        }

        RadioStatusSummary = state.IsConnected
            ? $"{ModeDisplay}  |  {FrequencyDisplay}  |  {FilterWidthDisplay}"
            : "Radio idle";
        CanDisconnect = state.IsConnected;
        CanConnect = !state.IsConnected
            && _radioService is not null
            && string.Equals(_settings.Radio.ControlBackend, "direct", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_settings.Radio.CivPort, "auto", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_settings.Radio.CivPort);
        IsBusy = false;
    }

    private static bool TryParseCivAddress(string value, out int address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out address);
        }

        if (trimmed.EndsWith("h", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(trimmed[..^1], System.Globalization.NumberStyles.HexNumber, null, out address);
        }

        return int.TryParse(trimmed, out address);
    }

    private void UpdateActiveBand(long frequencyHz)
    {
        var activeLabel = GetBandLabelForFrequency(frequencyHz);
        if (string.Equals(activeLabel, _activeBandLabel, StringComparison.Ordinal))
        {
            return;
        }

        _activeBandLabel = activeLabel;
        AvailableBands = BuildBandPresets(activeLabel);
    }

    private void UpdateRadioControlBrushes()
    {
        PreampOffBrush = BuildToggleBrush(PreampLevel == 0);
        Preamp1Brush = BuildToggleBrush(PreampLevel == 1);
        Preamp2Brush = BuildToggleBrush(PreampLevel == 2);
        SplitButtonBrush = BuildToggleBrush(IsSplitEnabled);
        AttenuatorButtonBrush = BuildToggleBrush(IsAttenuatorEnabled);
        TunerButtonBrush = BuildToggleBrush(IsTunerEnabled);
        NoiseBlankerButtonBrush = BuildToggleBrush(IsNoiseBlankerEnabled);
        NoiseReductionButtonBrush = BuildToggleBrush(IsNoiseReductionEnabled);
        AutoNotchButtonBrush = BuildToggleBrush(IsAutoNotchEnabled);
        ManualNotchButtonBrush = BuildToggleBrush(IsManualNotchEnabled);
        IpPlusButtonBrush = BuildToggleBrush(IsIpPlusEnabled);
        FilterSharpBrush = BuildToggleBrush(!IsFilterShapeSoft);
        FilterSoftBrush = BuildToggleBrush(IsFilterShapeSoft);
    }

    private int ParseManualNotchWidth(string value) => value switch
    {
        "Wide" => 0,
        "Nar" => 2,
        _ => 1,
    };

    private int ParseManualNotchPosition()
    {
        return Math.Clamp(ManualNotchPosition, 0, 255);
    }

    private void UpdateActiveFilterSlot(int filterSlot)
    {
        var normalized = Math.Clamp(filterSlot, 1, 3);
        if (normalized == _activeFilterSlot)
        {
            return;
        }

        _activeFilterSlot = normalized;
        AvailableFilterWidths = BuildFilterPresets(normalized);
    }

    private static IReadOnlyList<FilterPreset> BuildFilterPresets(int selectedSlot)
    {
        return
        [
            new FilterPreset("F1", 1, BuildToggleBrush(selectedSlot == 1)),
            new FilterPreset("F2", 2, BuildToggleBrush(selectedSlot == 2)),
            new FilterPreset("F3", 3, BuildToggleBrush(selectedSlot == 3)),
        ];
    }

    private static IReadOnlyList<ModePreset> BuildModePresets(RadioMode selectedMode)
    {
        return
        [
            new ModePreset("LSB", RadioMode.Lsb, BuildToggleBrush(selectedMode == RadioMode.Lsb)),
            new ModePreset("LSB-D", RadioMode.LsbData, BuildToggleBrush(selectedMode == RadioMode.LsbData)),
            new ModePreset("USB", RadioMode.Usb, BuildToggleBrush(selectedMode == RadioMode.Usb)),
            new ModePreset("USB-D", RadioMode.UsbData, BuildToggleBrush(selectedMode == RadioMode.UsbData)),
            new ModePreset("CW", RadioMode.Cw, BuildToggleBrush(selectedMode == RadioMode.Cw)),
            new ModePreset("AM", RadioMode.Am, BuildToggleBrush(selectedMode == RadioMode.Am)),
            new ModePreset("FM", RadioMode.Fm, BuildToggleBrush(selectedMode == RadioMode.Fm)),
            new ModePreset("RTTY", RadioMode.Rtty, BuildToggleBrush(selectedMode == RadioMode.Rtty)),
        ];
    }

    private static IReadOnlyList<BandPreset> BuildBandPresets(string activeLabel)
    {
        return
        [
            new BandPreset("160m", 1_900_000, BuildToggleBrush(activeLabel == "160m")),
            new BandPreset("80m", 3_750_000, BuildToggleBrush(activeLabel == "80m")),
            new BandPreset("40m", 7_150_000, BuildToggleBrush(activeLabel == "40m")),
            new BandPreset("30m", 10_125_000, BuildToggleBrush(activeLabel == "30m")),
            new BandPreset("20m", 14_175_000, BuildToggleBrush(activeLabel == "20m")),
            new BandPreset("17m", 18_118_000, BuildToggleBrush(activeLabel == "17m")),
            new BandPreset("15m", 21_225_000, BuildToggleBrush(activeLabel == "15m")),
            new BandPreset("12m", 24_940_000, BuildToggleBrush(activeLabel == "12m")),
            new BandPreset("10m", 28_400_000, BuildToggleBrush(activeLabel == "10m")),
            new BandPreset("6m", 50_250_000, BuildToggleBrush(activeLabel == "6m")),
        ];
    }

    private static IReadOnlyList<WefaxScheduleItem> BuildWefaxScheduleItems(DateTime utcNow)
    {
        var items = new List<WefaxScheduleItem>();
        foreach (var definition in BuildWefaxScheduleDefinitions())
        {
            var start = new DateTime(
                utcNow.Year,
                utcNow.Month,
                utcNow.Day,
                definition.Hour,
                definition.Minute,
                0,
                DateTimeKind.Utc);
            var end = start.AddMinutes(definition.DurationMinutes);
            if (end <= utcNow)
            {
                start = start.AddDays(1);
                end = end.AddDays(1);
            }

            var isOnAir = start <= utcNow && utcNow < end;
            var status = isOnAir ? "ON AIR" : "NEXT";
            var until = isOnAir
                ? $"ends in {FormatWefaxScheduleDelta(end - utcNow)}"
                : $"in {FormatWefaxScheduleDelta(start - utcNow)}";
            items.Add(new WefaxScheduleItem(
                status,
                start.ToString("HH:mm"),
                until,
                definition.Station,
                definition.Product,
                definition.FrequencyLabel,
                "IOC 576 / 120 LPM",
                definition.Source,
                start,
                end,
                BuildWefaxScheduleStatusBrush(isOnAir)));
        }

        return items
            .OrderBy(item => item.Status == "ON AIR" ? 0 : 1)
            .ThenBy(item => item.StartUtc)
            .Take(14)
            .ToList();
    }

    private static IReadOnlyList<WefaxScheduleDefinition> BuildWefaxScheduleDefinitions()
    {
        var schedule = new List<WefaxScheduleDefinition>();

        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 9110.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 0233, "00Z Preliminary Surface Analysis");
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 9110.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 0325, "00Z Surface Analysis Part 1 NE Atlantic", 13);
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 9110.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 0338, "00Z Surface Analysis Part 2 NW Atlantic", 13);
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 9110.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 0805, "24Hr Surface Forecast");
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 9110.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 0845, "48Hr Surface Forecast");
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 12750.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 1453, "12Z Preliminary Surface Analysis");
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 12750.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 1810, "24Hr Surface Forecast");
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 12750.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 1955, "48Hr Surface Forecast");
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 12750.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 2125, "18Z Surface Analysis Part 1 NE Atlantic", 13);
        AddWefax(schedule, "Boston NMF", "NOAA Atlantic 12750.0 kHz USB-D", "NOAA Atlantic hfmarsh.txt", 2138, "18Z Surface Analysis Part 2 NW Atlantic", 13);

        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 8682.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 0143, "NE Pacific GOES IR Satellite Image");
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 8682.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 0305, "Prelim Surface Analysis Part 1 NE Pacific", 13);
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 8682.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 0318, "Prelim Surface Analysis Part 2 NW Pacific", 13);
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 8682.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 0828, "48Hr Surface Forecast");
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 12786.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 1403, "NE Pacific GOES IR Satellite Image");
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 12786.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 1822, "24Hr Surface Forecast");
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 12786.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 2013, "48Hr Surface Forecast");
        AddWefax(schedule, "Pt Reyes NMC", "NOAA Pacific 12786.0 kHz USB-D", "NOAA Pacific hfreyes.txt", 2113, "Pacific GOES IR Satellite Image");

        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 8503.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 0005, "U.S./Tropical Surface Analysis W Half", 15);
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 8503.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 0020, "Tropical Surface Analysis E Half", 15);
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 8503.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 0655, "24Hr Surface Forecast");
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 8503.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 0705, "48Hr Surface Forecast");
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 12789.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 1805, "U.S./Tropical Surface Analysis W Half", 15);
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 12789.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 1820, "Tropical Surface Analysis E Half", 15);
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 12789.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 1855, "24Hr Surface Forecast");
        AddWefax(schedule, "New Orleans NMG", "NOAA Gulf 12789.9 kHz USB-D", "NOAA Gulf hfgulf.txt", 1905, "48Hr Surface Forecast");

        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 11090.0 kHz USB-D", "NOAA Hawaii hfhi.txt", 0535, "Cyclone Danger Area");
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 11090.0 kHz USB-D", "NOAA Hawaii hfhi.txt", 0615, "Surface Analysis");
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 11090.0 kHz USB-D", "NOAA Hawaii hfhi.txt", 0701, "24Hr Surface Forecast");
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 11090.0 kHz USB-D", "NOAA Hawaii hfhi.txt", 0917, "Surface Analysis Part 1 NE Pacific", 13);
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 11090.0 kHz USB-D", "NOAA Hawaii hfhi.txt", 1530, "Surface Analysis Part 1 NE Pacific", 13);
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 9982.5 kHz USB-D", "NOAA Hawaii hfhi.txt", 1719, "Test Pattern");
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 9982.5 kHz USB-D", "NOAA Hawaii hfhi.txt", 1815, "Surface Analysis");
        AddWefax(schedule, "Honolulu KVM70", "NOAA Hawaii 9982.5 kHz USB-D", "NOAA Hawaii hfhi.txt", 1914, "48Hr Surface Forecast");

        return schedule;
    }

    private static void AddWefax(
        List<WefaxScheduleDefinition> schedule,
        string station,
        string frequencyLabel,
        string source,
        int hhmm,
        string product,
        int durationMinutes = 10)
    {
        schedule.Add(new WefaxScheduleDefinition(
            station,
            frequencyLabel,
            source,
            hhmm / 100,
            hhmm % 100,
            durationMinutes,
            product));
    }

    private static string FormatWefaxScheduleDelta(TimeSpan delta)
    {
        var totalMinutes = Math.Max(0, (int)Math.Round(delta.TotalMinutes, MidpointRounding.AwayFromZero));
        if (totalMinutes < 60)
        {
            return $"{totalMinutes}m";
        }

        return $"{totalMinutes / 60}h {totalMinutes % 60:00}m";
    }

    private static IBrush BuildWefaxScheduleStatusBrush(bool isOnAir) =>
        new SolidColorBrush(Color.Parse(isOnAir ? "#1E6F5C" : "#24304A"));

    private static string GetBandLabelForFrequency(long frequencyHz) => frequencyHz switch
    {
        >= 1_800_000 and <= 2_000_000 => "160m",
        >= 3_500_000 and <= 4_000_000 => "80m",
        >= 7_000_000 and <= 7_300_000 => "40m",
        >= 10_100_000 and <= 10_150_000 => "30m",
        >= 14_000_000 and <= 14_350_000 => "20m",
        >= 18_068_000 and <= 18_168_000 => "17m",
        >= 21_000_000 and <= 21_450_000 => "15m",
        >= 24_890_000 and <= 24_990_000 => "12m",
        >= 28_000_000 and <= 29_700_000 => "10m",
        >= 50_000_000 and <= 54_000_000 => "6m",
        _ => string.Empty,
    };

    private static IBrush BuildToggleBrush(bool active) =>
        new SolidColorBrush(Color.Parse(active ? "#1A1D2E" : "#0C1017"));

    private static IBrush BuildConditionBrush(string condition) => new SolidColorBrush(Color.Parse(condition switch
    {
        "Good" => "#12301F",
        "Fair" => "#33290F",
        "Poor" => "#351314",
        "Band Closed" => "#16181D",
        _ => "#16181D",
    }));

    private static IReadOnlyList<int> GetFilterOptions(RadioMode mode) => mode switch
    {
        RadioMode.Cw => [250, 500, 1200],
        RadioMode.Rtty => [250, 500, 1200],
        RadioMode.LsbData => [1800, 2400, 3000],
        RadioMode.UsbData => [1800, 2400, 3000],
        _ => [1800, 2400, 3000],
    };

    private static string FormatModeDisplay(RadioMode mode) => mode switch
    {
        RadioMode.LsbData => "LSB-D",
        RadioMode.UsbData => "USB-D",
        _ => mode.ToString().ToUpperInvariant(),
    };

    private void ApplySmeterLevel(int targetLevel)
    {
        _targetSmeterLevel = Math.Clamp(targetLevel / 25.5, 0.0, 10.0);
        if (_targetSmeterLevel > _displayedSmeterLevel)
        {
            _displayedSmeterLevel = _targetSmeterLevel;
            RenderSmeter();
        }
    }

    private void AnimateSmeter()
    {
        var delta = _targetSmeterLevel - _displayedSmeterLevel;
        if (Math.Abs(delta) < 0.02)
        {
            if (Math.Abs(delta) > 0)
            {
                _displayedSmeterLevel = _targetSmeterLevel;
                RenderSmeter();
            }

            return;
        }

        var step = delta > 0
            ? Math.Max(0.15, delta * 0.45)
            : Math.Min(-0.08, delta * 0.20);

        _displayedSmeterLevel = Math.Clamp(_displayedSmeterLevel + step, 0.0, 10.0);
        RenderSmeter();
    }

    private void RenderSmeter()
    {
        var roundedDisplay = (int)Math.Round(_displayedSmeterLevel, MidpointRounding.AwayFromZero);
        SmeterDisplay = roundedDisplay >= 10 ? "S9+" : $"S{roundedDisplay}";
        SmeterSegmentBrushes = BuildSmeterBrushes(_displayedSmeterLevel);
    }

    private static IReadOnlyList<IBrush> BuildSmeterBrushes(double level)
    {
        var brushes = new IBrush[11];
        var clamped = Math.Clamp(level, 0.0, 10.0);
        for (var i = 0; i < brushes.Length; i++)
        {
            var segmentFill = Math.Clamp(clamped - i, 0.0, 1.0);
            var active = segmentFill > 0.001;
            if (i < 7)
            {
                brushes[i] = BuildMeterSegmentBrush(active ? "#1D9E75" : "#0A1A10", segmentFill);
            }
            else
            {
                brushes[i] = BuildMeterSegmentBrush(active ? "#EF9F27" : "#1A1200", segmentFill);
            }
        }

        return brushes;
    }

    private static IBrush BuildMeterSegmentBrush(string activeColorHex, double fill)
    {
        var active = Color.Parse(activeColorHex);
        var off = Color.Parse(activeColorHex == "#EF9F27" ? "#1A1200" : "#0A1A10");
        var clamped = Math.Clamp(fill, 0.0, 1.0);
        byte Lerp(byte from, byte to) => (byte)Math.Round(from + ((to - from) * clamped));
        return new SolidColorBrush(Color.FromRgb(
            Lerp(off.R, active.R),
            Lerp(off.G, active.G),
            Lerp(off.B, active.B)));
    }

    private sealed record WefaxScheduleDefinition(
        string Station,
        string FrequencyLabel,
        string Source,
        int Hour,
        int Minute,
        int DurationMinutes,
        string Product);

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
