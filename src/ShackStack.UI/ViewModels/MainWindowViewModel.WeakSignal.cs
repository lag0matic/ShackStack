using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShackStack.Core.Abstractions.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ShackStack.UI.ViewModels;

public partial class MainWindowViewModel
{
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
    private ObservableCollection<WsjtxMessageItem> filteredWsjtxMessages = [];

    [ObservableProperty]
    private IReadOnlyList<string> wsjtxBandActivityFilterOptions = ["All", "CQ", "POTA", "DX"];

    [ObservableProperty]
    private string selectedWsjtxBandActivityFilter = "All";

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

    public bool WsjtxHasMessages => FilteredWsjtxMessages.Count > 0;

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
        FilteredWsjtxMessages.Clear();
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
        FilteredWsjtxMessages.Clear();
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
        FilteredWsjtxMessages.Clear();
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

        RebuildFilteredWsjtxMessages();
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

        RebuildFilteredWsjtxMessages();
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

    private void RebuildFilteredWsjtxMessages()
    {
        FilteredWsjtxMessages.Clear();
        foreach (var item in WsjtxMessages)
        {
            if (ShouldShowWsjtxBandActivityMessage(item))
            {
                FilteredWsjtxMessages.Add(item);
            }
        }

        OnPropertyChanged(nameof(WsjtxHasMessages));
    }

    private bool ShouldShowWsjtxBandActivityMessage(WsjtxMessageItem item)
    {
        if (item.IsOwnTransmit || IsWsjtxMessageDirectedToOperator(item))
        {
            return true;
        }

        var filter = SelectedWsjtxBandActivityFilter?.Trim();
        if (string.IsNullOrWhiteSpace(filter) || string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized = NormalizeWsjtxMessageText(item.MessageText);
        return filter.ToUpperInvariant() switch
        {
            "CQ" => item.IsCq || normalized.StartsWith("CQ ", StringComparison.OrdinalIgnoreCase),
            "POTA" => normalized.Contains(" POTA ", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("CQ POTA ", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(" POTA", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains(" P2P ", StringComparison.OrdinalIgnoreCase),
            "DX" => normalized.StartsWith("CQ DX ", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains(" CQ DX ", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith(" DX", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }

    private bool IsWsjtxMessageDirectedToOperator(WsjtxMessageItem item)
    {
        if (item.IsDirectedToMe)
        {
            return true;
        }

        var myCall = FormatCallsign(WsjtxOperatorCallsign);
        return !string.IsNullOrWhiteSpace(myCall) && WsjtxMessageMentionsCall(item.MessageText, myCall);
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

    partial void OnSelectedWsjtxBandActivityFilterChanged(string value)
    {
        RebuildFilteredWsjtxMessages();
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
        RebuildFilteredWsjtxMessages();
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
}
