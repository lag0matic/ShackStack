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
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ShackStack.UI.ViewModels;

public sealed record ModePreset(string Label, RadioMode Mode, IBrush Brush);
public sealed record BandPreset(string Label, long FrequencyHz, IBrush Brush);
public sealed record FrequencyMarker(string Label);
public sealed record FilterPreset(string Label, int Slot, IBrush Brush);
public sealed record BandConditionCellViewModel(string BandLabel, string DayCondition, IBrush DayBrush, string NightCondition, IBrush NightBrush);
public sealed record SstvImageItem(string Label, string Path, DateTime Timestamp, Bitmap Bitmap);
public sealed record SstvOverlayTemplateFile(string Name, IReadOnlyList<SstvOverlayTemplateItemFile> Items);
public sealed record SstvOverlayTemplateItemFile(string Text, double X, double Y, double FontSize, string FontFamily, string Color);
public sealed record SstvTemplateItem(string Name, string Path, DateTime Timestamp);
public sealed record WefaxImageItem(string Label, string Path, DateTime Timestamp, Bitmap Bitmap);

public sealed class SstvOverlayItemViewModel : ObservableObject
{
    private string _text = "W8STR DE KE9CRR - 599!";
    private double _x = 160;
    private double _y = 210;
    private double _fontSize = 18;
    private string _fontFamilyName = "Segoe UI";
    private int _red = 245;
    private int _green = 247;
    private int _blue = 255;

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value);
    }

    public string FontFamilyName
    {
        get => _fontFamilyName;
        set
        {
            if (SetProperty(ref _fontFamilyName, value))
            {
                OnPropertyChanged(nameof(PreviewFontFamily));
            }
        }
    }

    public int Red
    {
        get => _red;
        set
        {
            var clamped = Math.Clamp(value, 0, 255);
            if (SetProperty(ref _red, clamped))
            {
                OnColorChanged();
            }
        }
    }

    public int Green
    {
        get => _green;
        set
        {
            var clamped = Math.Clamp(value, 0, 255);
            if (SetProperty(ref _green, clamped))
            {
                OnColorChanged();
            }
        }
    }

    public int Blue
    {
        get => _blue;
        set
        {
            var clamped = Math.Clamp(value, 0, 255);
            if (SetProperty(ref _blue, clamped))
            {
                OnColorChanged();
            }
        }
    }

    public string ColorHex => $"#{Red:X2}{Green:X2}{Blue:X2}";

    public IBrush PreviewBrush => new SolidColorBrush(Color.FromRgb((byte)Red, (byte)Green, (byte)Blue));

    public FontFamily PreviewFontFamily => new(FontFamilyName);

    public void SetColorFromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return;
        }

        try
        {
            var parsed = Color.Parse(hex);
            _red = parsed.R;
            _green = parsed.G;
            _blue = parsed.B;
            OnPropertyChanged(nameof(Red));
            OnPropertyChanged(nameof(Green));
            OnPropertyChanged(nameof(Blue));
            OnColorChanged();
        }
        catch
        {
        }
    }

    private void OnColorChanged()
    {
        OnPropertyChanged(nameof(ColorHex));
        OnPropertyChanged(nameof(PreviewBrush));
    }
}

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private int _displayedSmeterLevel;
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
    private readonly ISstvDecoderHost? _sstvDecoderHost;
    private readonly IWefaxDecoderHost? _wefaxDecoderHost;
    private readonly IDisposable? _radioSubscription;
    private readonly IDisposable? _audioLevelSubscription;
    private readonly IDisposable? _spectrumSubscription;
    private readonly IDisposable? _bandConditionsSubscription;
    private readonly IDisposable? _interopSubscription;
    private readonly IDisposable? _cwTelemetrySubscription;
    private readonly IDisposable? _cwDecodeSubscription;
    private readonly IDisposable? _rttyTelemetrySubscription;
    private readonly IDisposable? _rttyDecodeSubscription;
    private readonly IDisposable? _sstvTelemetrySubscription;
    private readonly IDisposable? _sstvImageSubscription;
    private readonly IDisposable? _wefaxTelemetrySubscription;
    private readonly IDisposable? _wefaxImageSubscription;
    private readonly string _sstvReceivedDirectory;
    private readonly string _sstvReplyDirectory;
    private readonly string _sstvTemplateDirectory;
    private readonly string _wefaxReceivedDirectory;
    private bool _isUpdatingCwRigSettingsFromRadio;
    private bool _cwRigSettingsDirty;
    private bool _isUpdatingVoiceRigSettingsFromRadio;
    private bool _voiceRigSettingsDirty;
    private bool _hasReceivedVoiceRigStateFromRadio;
    private bool _suppressRuntimeUiStatePersistence;

    public MainWindowViewModel(
        AppSettings settings,
        string settingsPath,
        IRadioService? radioService = null,
        IAppSettingsStore? settingsStore = null,
        IAudioService? audioService = null,
        IWaterfallService? waterfallService = null,
        IWaterfallRenderSource? waterfallRenderSource = null,
        IBandConditionsService? bandConditionsService = null,
        IInteropService? interopService = null,
        ICwDecoderHost? cwDecoderHost = null,
        IRttyDecoderHost? rttyDecoderHost = null,
        ISstvDecoderHost? sstvDecoderHost = null,
        IWefaxDecoderHost? wefaxDecoderHost = null)
    {
        _settings = settings;
        _settingsPath = settingsPath;
        _radioService = radioService;
        _settingsStore = settingsStore;
        _audioService = audioService;
        _waterfallService = waterfallService;
        _waterfallRenderSource = waterfallRenderSource;
        _bandConditionsService = bandConditionsService;
        _interopService = interopService;
        _cwDecoderHost = cwDecoderHost;
        _rttyDecoderHost = rttyDecoderHost;
        _sstvDecoderHost = sstvDecoderHost;
        _wefaxDecoderHost = wefaxDecoderHost;
        _sstvReceivedDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "sstv");
        _sstvReplyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "sstv-reply");
        _sstvTemplateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "sstv-templates");
        _wefaxReceivedDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ShackStack", "wefax");
        Directory.CreateDirectory(_sstvReceivedDirectory);
        Directory.CreateDirectory(_sstvReplyDirectory);
        Directory.CreateDirectory(_sstvTemplateDirectory);
        Directory.CreateDirectory(_wefaxReceivedDirectory);
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
        SettingsRadioBackend = settings.Radio.ControlBackend;
        SettingsCivPort = settings.Radio.CivPort;
        SettingsCivBaud = settings.Radio.CivBaud.ToString();
        SettingsCivAddress = FormatCivAddress(settings.Radio.CivAddress);
        SettingsTheme = settings.Ui.Theme;
        SettingsBandConditionsEnabled = settings.Ui.BandConditionsEnabled;
        SettingsShowExperimentalCw = true;
        IsBandConditionsEnabled = settings.Ui.BandConditionsEnabled;
        ShowCwPanel = true;
        SettingsFlrigEnabled = settings.Interop.FlrigEnabled;
        SettingsFlrigHost = settings.Interop.FlrigHost;
        SettingsFlrigPort = settings.Interop.FlrigPort.ToString();
        MonitorVolumePercent = Math.Clamp(settings.Audio.MonitorVolumePercent, 0, 100);
        WaterfallFloorPercent = Math.Clamp(settings.Ui.WaterfallFloorPercent, 0, 95);
        WaterfallCeilingPercent = Math.Clamp(settings.Ui.WaterfallCeilingPercent, WaterfallFloorPercent + 1, 100);
        VoiceMicGainPercent = 50;
        VoiceCompressionPercent = 0;
        VoiceRfPowerPercent = 100;
        CwPitchHz = 700;
        CwWpm = 20;
        CwDecoderProfile = "Adaptive";
        _voiceRigSettingsDirty = false;
        _cwRigSettingsDirty = false;
        SettingsStatusMessage = "Settings loaded";
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

                    CwDecodedText = string.IsNullOrWhiteSpace(CwDecodedText)
                        ? chunk.Text
                        : $"{CwDecodedText} {chunk.Text}".Trim();
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
                    RttySessionNotes = $"{telemetry.ActiveWorker}  |  Signal {telemetry.SignalLevelPercent}%  |  Shift {telemetry.EstimatedShiftHz} Hz  |  Baud {telemetry.EstimatedBaud:0.##}";
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

        if (sstvDecoderHost is not null)
        {
            _sstvTelemetrySubscription = sstvDecoderHost.TelemetryStream.Subscribe(new Observer<SstvDecoderTelemetry>(telemetry =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SstvRxStatus = telemetry.Status;
                    SstvSessionNotes = $"{telemetry.ActiveWorker}  |  Signal {telemetry.SignalLevelPercent}%  |  Mode {telemetry.DetectedMode}  |  Slant {SstvManualSlant}  |  Offset {SstvManualOffset}";
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
                    WefaxSessionNotes = $"{telemetry.ActiveWorker}  |  Lines {telemetry.LinesReceived}  |  Auto {telemetry.AlignedOffset}  |  Slant {WefaxManualSlant}  |  Offset {WefaxManualOffset}  |  Start {telemetry.StartConfidence:P0}";
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
    private bool settingsFlrigEnabled = true;

    [ObservableProperty]
    private string settingsFlrigHost = "127.0.0.1";

    [ObservableProperty]
    private string settingsFlrigPort = "12345";

    [ObservableProperty]
    private string settingsStatusMessage = string.Empty;

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
    private string voiceRxDeviceDisplay = "Not configured";

    [ObservableProperty]
    private string voiceTxDeviceDisplay = "Not configured";

    [ObservableProperty]
    private string voiceMicDeviceDisplay = "Not configured";

    [ObservableProperty]
    private string voiceMonitorDeviceDisplay = "Not configured";

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
    private string cwDecoderProfile = "Adaptive";

    [ObservableProperty]
    private IReadOnlyList<string> cwDecoderProfiles = ["Adaptive", "Hybrid", "Minimal", "External"];

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
    private string rttyRxStatus = "RTTY receiver scaffold ready";

    [ObservableProperty]
    private string rttySessionNotes = "Select a common shift/baud profile, then start receive. This is the live worker scaffold for the real RTTY demod path.";

    [ObservableProperty]
    private string rttyDecodedText = string.Empty;

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
    private int sstvManualSlant;

    [ObservableProperty]
    private int sstvManualOffset;

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
    private string sstvRxStatus = "Receiver scaffold ready";

    [ObservableProperty]
    private string sstvImageStatus = "No image captured yet";

    [ObservableProperty]
    private string sstvSessionNotes = "Auto Detect is the recommended default. Lock a mode only when you know the signal family. Correction tools are preview-side for now.";

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
    private ObservableCollection<SstvTemplateItem> sstvReplyLayoutTemplates = [];

    [ObservableProperty]
    private SstvTemplateItem? selectedSstvReplyLayoutTemplate;

    [ObservableProperty]
    private string sstvReplyTemplateName = "Default Reply";

    [ObservableProperty]
    private string sstvReplyTemplateStatus = "Save a layout template to reuse overlay positioning later.";

    [ObservableProperty]
    private IReadOnlyList<string> sstvReplyTemplates =
    [
        "W8STR DE KE9CRR - 599!",
        "TNX SSTV - 73 DE KE9CRR",
        "QSL W8STR DE KE9CRR",
    ];

    public Bitmap? SstvSelectedReceivedBitmap => SelectedSstvReceivedImage?.Bitmap;

    public string SstvSelectedReceivedPath => SelectedSstvReceivedImage?.Path ?? "No saved image selected";

    public Bitmap? SstvReplyPreviewBitmap => SelectedSstvReplyBaseImage?.Bitmap;

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
    ];

    [ObservableProperty]
    private string wefaxSelectedFrequency = "NOAA Atlantic 12750.0 kHz USB-D";

    [ObservableProperty]
    private int wefaxManualSlant;

    [ObservableProperty]
    private int wefaxManualOffset;

    [ObservableProperty]
    private string wefaxRxStatus = "WeFAX receiver ready";

    [ObservableProperty]
    private string wefaxImageStatus = "No WeFAX image captured yet";

    [ObservableProperty]
    private string wefaxSessionNotes = "Auto slant correction is enabled. Start RX for normal operation, or Start Now if you joined the broadcast late.";

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
                MonitorVolumePercent = MonitorVolumePercent,
                MicGainPercent = VoiceMicGainPercent,
                VoiceCompressionPercent = VoiceCompressionPercent,
            },
            Interop = _settings.Interop with
            {
                FlrigEnabled = SettingsFlrigEnabled,
                FlrigHost = SettingsFlrigHost.Trim(),
                FlrigPort = flrigPort,
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
    private async Task StartCwDecoderAsync()
    {
        if (_cwDecoderHost is null)
        {
            CwDecoderStatus = "CW decoder host unavailable";
            return;
        }

        var config = new CwDecoderConfiguration(Math.Clamp(CwPitchHz, 300, 1200), Math.Clamp(CwWpm, 5, 60), CwDecoderProfile);
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
    private void ClearCwDecodedText() => CwDecodedText = string.Empty;

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

        RttyRxStatus = "RTTY receiver scaffold ready";
        RttySessionNotes = "Select a common shift/baud profile, then start receive. This is the live worker scaffold for the real RTTY demod path.";
        RttyDecodedText = string.Empty;
    }

    [RelayCommand]
    private void ClearRttyDecodedText() => RttyDecodedText = string.Empty;

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
    private void ResetSstvSession()
    {
        if (_sstvDecoderHost is not null)
        {
            _ = _sstvDecoderHost.ResetAsync(CancellationToken.None);
        }

        SstvRxStatus = "Receiver scaffold ready";
        SstvImageStatus = "No image captured yet";
        SstvSessionNotes = "Auto Detect is the recommended default. Lock a mode only when you know the signal family. Correction tools are preview-side for now.";
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
        WefaxSessionNotes = "Auto slant correction is enabled. Start RX for normal operation, or Start Now if you joined the broadcast late.";
        UpdateWefaxPreview(null);
    }

    [RelayCommand]
    private void RefreshWefaxArchive()
    {
        LoadWefaxArchiveImages();
    }

    [RelayCommand]
    private void ApplySstvReplyTemplate(string template)
    {
        if (!string.IsNullOrWhiteSpace(template) && SelectedSstvReplyOverlayItem is not null)
        {
            SelectedSstvReplyOverlayItem.Text = template;
        }
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
                item.ColorHex)).ToArray());

        try
        {
            File.WriteAllText(fullPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            SstvReplyTemplateStatus = $"Saved template '{normalizedName}'";
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
            if (payload is null || payload.Items.Count == 0)
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
            SstvReplyTemplateName = payload.Name;
            SstvReplyTemplateStatus = $"Loaded template '{payload.Name}'";
        }
        catch (Exception ex)
        {
            SstvReplyTemplateStatus = $"Template load failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddSstvReplyOverlay()
    {
        var item = new SstvOverlayItemViewModel
        {
            Text = "W8STR DE KE9CRR - 599!",
            X = 160 + (SstvReplyOverlayItems.Count * 12),
            Y = 210 + (SstvReplyOverlayItems.Count * 12),
            FontSize = 18,
            FontFamilyName = "Segoe UI",
        };
        SstvReplyOverlayItems.Add(item);
        SelectedSstvReplyOverlayItem = item;
    }

    [RelayCommand]
    private void RemoveSstvReplyOverlay()
    {
        if (SelectedSstvReplyOverlayItem is null)
        {
            return;
        }

        var item = SelectedSstvReplyOverlayItem;
        SstvReplyOverlayItems.Remove(item);
        SelectedSstvReplyOverlayItem = SstvReplyOverlayItems.FirstOrDefault();
    }

    private async Task StartSstvReceiveCoreAsync()
    {
        if (_sstvDecoderHost is null)
        {
            return;
        }

        await TuneRadioForSstvAsync(SstvSelectedFrequency);
        var config = new SstvDecoderConfiguration(NormalizeSstvModeSelection(SstvSelectedMode), SstvSelectedFrequency, SstvManualSlant, SstvManualOffset);
        await _sstvDecoderHost.ConfigureAsync(config, CancellationToken.None);
        await _sstvDecoderHost.StartAsync(CancellationToken.None);
    }

    private async Task StartRttyReceiveCoreAsync()
    {
        if (_rttyDecoderHost is null)
        {
            return;
        }

        await TuneRadioForRttyAsync(RttySelectedFrequency);
        var (shiftHz, baudRate) = ParseRttyProfile(RttySelectedProfile);
        var config = new RttyDecoderConfiguration(RttySelectedProfile, shiftHz, baudRate, RttySelectedFrequency);
        await _rttyDecoderHost.ConfigureAsync(config, CancellationToken.None);
        await _rttyDecoderHost.StartAsync(CancellationToken.None);
    }

    private async Task StartWefaxReceiveCoreAsync(bool forceNow)
    {
        if (_wefaxDecoderHost is null)
        {
            return;
        }

        var (ioc, lpm) = ParseWefaxMode(WefaxSelectedMode);
        await TuneRadioForWefaxAsync(WefaxSelectedFrequency);
        var config = new WefaxDecoderConfiguration(WefaxSelectedMode, ioc, lpm, WefaxSelectedFrequency, WefaxManualSlant, WefaxManualOffset);
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
            await _radioService.SetFrequencyAsync(dialHz, CancellationToken.None);
            RadioStatusSummary = $"WeFAX tuned: {dialHz:N0} Hz {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"WeFAX tune failed: {ex.Message}";
        }
    }

    private async Task TuneRadioForSstvAsync(string frequencyLabel)
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
            RadioStatusSummary = $"SSTV tuned: {hz:N0} Hz {FormatModeDisplay(mode)}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"SSTV tune failed: {ex.Message}";
        }
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
                ? RadioMode.Lsb
                : RadioMode.Usb;
            await _radioService.SetModeAsync(mode, CancellationToken.None);
            await _radioService.SetFrequencyAsync(hz, CancellationToken.None);
            RadioStatusSummary = $"RTTY tuned: {hz:N0} Hz {mode.ToString().ToUpperInvariant()}";
        }
        catch (Exception ex)
        {
            RadioStatusSummary = $"RTTY tune failed: {ex.Message}";
        }
    }

    private static string NormalizeSstvModeSelection(string selection) => selection switch
    {
        "Lock Martin M1" => "Martin M1",
        "Lock Martin M2" => "Martin M2",
        "Lock Scottie 1" => "Scottie 1",
        "Lock Scottie 2" => "Scottie 2",
        "Lock Robot 36" => "Robot 36",
        "Lock PD 120" => "PD 120",
        _ => "Auto Detect",
    };

    [RelayCommand]
    private async Task ApplySstvAlignmentAsync()
    {
        if (_sstvDecoderHost is null)
        {
            return;
        }

        try
        {
            await _sstvDecoderHost.SetManualAlignmentAsync(SstvManualSlant, SstvManualOffset, CancellationToken.None);
            SstvRxStatus = $"SSTV alignment set: slant {SstvManualSlant}, offset {SstvManualOffset}";
        }
        catch (Exception ex)
        {
            SstvRxStatus = $"SSTV alignment apply failed: {ex.Message}";
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
        Directory.CreateDirectory(_sstvReceivedDirectory);
        Directory.CreateDirectory(_sstvReplyDirectory);
        Directory.CreateDirectory(_sstvTemplateDirectory);

        var receivedItems = new List<SstvImageItem>();
        foreach (var file in new DirectoryInfo(_sstvReceivedDirectory)
                     .EnumerateFiles("*.png", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(f => f.LastWriteTimeUtc)
                     .Take(40))
        {
            try
            {
                receivedItems.Add(new SstvImageItem(
                    Path.GetFileNameWithoutExtension(file.Name),
                    file.FullName,
                    file.LastWriteTime,
                    new Bitmap(file.FullName)));
            }
            catch
            {
            }
        }

        var replyItems = new List<SstvImageItem>();
        foreach (var file in new DirectoryInfo(_sstvReplyDirectory)
                     .EnumerateFiles("*.png", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(f => f.LastWriteTimeUtc)
                     .Take(40))
        {
            try
            {
                replyItems.Add(new SstvImageItem(
                    Path.GetFileNameWithoutExtension(file.Name),
                    file.FullName,
                    file.LastWriteTime,
                    new Bitmap(file.FullName)));
            }
            catch
            {
            }
        }

        var templateItems = new List<SstvTemplateItem>();
        foreach (var file in new DirectoryInfo(_sstvTemplateDirectory)
                     .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(f => f.LastWriteTimeUtc)
                     .Take(50))
        {
            templateItems.Add(new SstvTemplateItem(
                Path.GetFileNameWithoutExtension(file.Name),
                file.FullName,
                file.LastWriteTime));
        }

        SstvReceivedImages = new ObservableCollection<SstvImageItem>(receivedItems);
        SstvReplyImages = new ObservableCollection<SstvImageItem>(replyItems);
        SstvReplyLayoutTemplates = new ObservableCollection<SstvTemplateItem>(templateItems);
        SelectedSstvReceivedImage ??= SstvReceivedImages.FirstOrDefault();
        SelectedSstvReplyBaseImage ??= SstvReplyImages.FirstOrDefault();
        SelectedSstvReplyLayoutTemplate ??= SstvReplyLayoutTemplates.FirstOrDefault();
        if (SstvReplyOverlayItems.Count == 0)
        {
            var defaultOverlay = new SstvOverlayItemViewModel();
            SstvReplyOverlayItems.Add(defaultOverlay);
            SelectedSstvReplyOverlayItem = defaultOverlay;
        }
        OnPropertyChanged(nameof(SstvSelectedReceivedBitmap));
        OnPropertyChanged(nameof(SstvSelectedReceivedPath));
        OnPropertyChanged(nameof(SstvReplyPreviewBitmap));
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
                var file = new FileInfo(imagePath);
                existing = new SstvImageItem(
                    Path.GetFileNameWithoutExtension(file.Name),
                    file.FullName,
                    file.LastWriteTime,
                    new Bitmap(file.FullName));
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

    partial void OnNoiseReductionLevelChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 15);
        if (clamped != value)
        {
            NoiseReductionLevel = clamped;
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
        OnPropertyChanged(nameof(SstvReplyHasBaseImage));
        OnPropertyChanged(nameof(SstvReplyShowPlaceholder));
    }

    partial void OnSelectedSstvReplyLayoutTemplateChanged(SstvTemplateItem? value)
    {
        if (value is not null)
        {
            SstvReplyTemplateName = value.Name;
        }
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
            await _radioService.ConnectAsync(
                new RadioConnectionOptions(
                    _settings.Radio.CivPort,
                    _settings.Radio.CivBaud,
                    _settings.Radio.CivAddress),
                CancellationToken.None);
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

    partial void OnCanConnectChanged(bool value) => ConnectCommand.NotifyCanExecuteChanged();

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

        UpdateHeaderCallsign();
    }

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
        ShowCwPanel = true;
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
        SettingsShowExperimentalCw = true;
        IsBandConditionsEnabled = settings.Ui.BandConditionsEnabled;
        ShowCwPanel = true;
        SettingsFlrigEnabled = settings.Interop.FlrigEnabled;
        SettingsFlrigHost = settings.Interop.FlrigHost;
        SettingsFlrigPort = settings.Interop.FlrigPort.ToString();
        MonitorVolumePercent = Math.Clamp(settings.Audio.MonitorVolumePercent, 0, 100);
        WaterfallFloorPercent = Math.Clamp(settings.Ui.WaterfallFloorPercent, 0, 95);
        WaterfallCeilingPercent = Math.Clamp(settings.Ui.WaterfallCeilingPercent, WaterfallFloorPercent + 1, 100);
        _voiceRigSettingsDirty = false;
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
        VoiceRxDeviceDisplay = SelectedRxDevice?.FriendlyName ?? "Not configured";
        VoiceTxDeviceDisplay = SelectedTxDevice?.FriendlyName ?? "Not configured";
        VoiceMicDeviceDisplay = SelectedMicDevice?.FriendlyName ?? "Not configured";
        VoiceMonitorDeviceDisplay = SelectedMonitorDevice?.FriendlyName ?? "Not configured";
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

    private static bool TryParseUiFrequencyHz(string frequencyLabel, out long hz)
    {
        hz = 0;
        if (string.IsNullOrWhiteSpace(frequencyLabel))
        {
            return false;
        }

        var match = System.Text.RegularExpressions.Regex.Match(frequencyLabel, @"(\d+(?:\.\d+)?)\s*MHz", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        if (!double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mhz))
        {
            return false;
        }

        hz = (long)Math.Round(mhz * 1_000_000.0);
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
        NoiseReductionLevel = state.NoiseReductionLevel;
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
        var clampedTarget = Math.Clamp(targetLevel, 0, 10);
        if (clampedTarget > _displayedSmeterLevel)
        {
            _displayedSmeterLevel = clampedTarget;
        }
        else
        {
            var delta = _displayedSmeterLevel - clampedTarget;
            var decayStep = delta >= 4 ? 3 : delta >= 2 ? 2 : 1;
            _displayedSmeterLevel = Math.Max(clampedTarget, _displayedSmeterLevel - decayStep);
        }

        SmeterDisplay = _displayedSmeterLevel >= 10 ? "S9+" : $"S{_displayedSmeterLevel}";
        SmeterSegmentBrushes = BuildSmeterBrushes(_displayedSmeterLevel);
    }

    private static IReadOnlyList<IBrush> BuildSmeterBrushes(int level)
    {
        var brushes = new IBrush[11];
        var clamped = Math.Clamp(level, 0, 10);
        for (var i = 0; i < brushes.Length; i++)
        {
            var active = i < clamped;
            if (i < 7)
            {
                brushes[i] = new SolidColorBrush(Color.Parse(active ? "#1D9E75" : "#0A1A10"));
            }
            else
            {
                brushes[i] = new SolidColorBrush(Color.Parse(active ? "#EF9F27" : "#1A1200"));
            }
        }

        return brushes;
    }

    public void Dispose()
    {
        _cwSendCts?.Cancel();
        _radioSubscription?.Dispose();
        _audioLevelSubscription?.Dispose();
        _spectrumSubscription?.Dispose();
        _bandConditionsSubscription?.Dispose();
        _interopSubscription?.Dispose();
        _cwTelemetrySubscription?.Dispose();
        _cwDecodeSubscription?.Dispose();
        _rttyTelemetrySubscription?.Dispose();
        _rttyDecodeSubscription?.Dispose();
        _sstvTelemetrySubscription?.Dispose();
        _sstvImageSubscription?.Dispose();
        _wefaxTelemetrySubscription?.Dispose();
        _wefaxImageSubscription?.Dispose();
        _cwSendCts?.Dispose();
    }
}
