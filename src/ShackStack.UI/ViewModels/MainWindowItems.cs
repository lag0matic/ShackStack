using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using ShackStack.Core.Abstractions.Models;

namespace ShackStack.UI.ViewModels;

public sealed record ModePreset(string Label, RadioMode Mode, IBrush Brush);
public sealed record BandPreset(string Label, long FrequencyHz, IBrush Brush);
public sealed record FrequencyMarker(string Label);
public sealed record FilterPreset(string Label, int Slot, IBrush Brush);
public sealed record BandConditionCellViewModel(string BandLabel, string DayCondition, IBrush DayBrush, string NightCondition, IBrush NightBrush);
public sealed record WefaxImageItem(string Label, string Path, DateTime Timestamp, Bitmap Bitmap);
public sealed record WefaxScheduleItem(
    string Status,
    string TimeText,
    string UntilText,
    string Station,
    string Product,
    string FrequencyLabel,
    string ModeLabel,
    string Source,
    DateTime StartUtc,
    DateTime EndUtc,
    IBrush StatusBrush)
{
    public string SummaryText => $"{Station}  |  {Product}";
    public string DetailText => $"{TimeText} UTC  |  {FrequencyLabel}";
}

public sealed record WsjtxMessageItem(
    DateTime TimestampUtc,
    string TimeText,
    string ModeText,
    string SnrText,
    string DtText,
    string HzText,
    string MessageText,
    int FrequencyOffsetHz,
    int SnrDb,
    double DtSeconds,
    bool IsDirectedToMe,
    bool IsCq,
    bool IsOwnTransmit,
    string RowBackground,
    string AccentBrush,
    string MessageBrush,
    string MetaBrush,
    string BadgeText,
    bool ShowBadge,
    string BadgeBackground,
    string BadgeForeground);

public sealed record WsjtxSuggestedMessageItem(string Label, string MessageText, string Intent);
public sealed record WsjtxActiveSession(string OtherCall, int FrequencyOffsetHz, string ModeLabel);
public sealed record WsjtxReplyAutomationModeItem(string Key, string Label, string Summary);

public sealed record LongwaveSpotSummaryItem(
    string Id,
    string ActivatorCallsign,
    string ParkReference,
    double FrequencyKhz,
    string Mode,
    string Band,
    string? Comments,
    string? SpotterCallsign,
    DateTime SpottedAtUtc,
    bool IsLogged)
{
    public string FrequencyText => $"{FrequencyKhz / 1000d:0.000} MHz";
    public string AgeText => $"{Math.Max(0, (int)(DateTime.UtcNow - SpottedAtUtc).TotalMinutes)}m ago";
    public string SummaryText => string.IsNullOrWhiteSpace(Comments)
        ? $"{ActivatorCallsign} @ {ParkReference}"
        : $"{ActivatorCallsign} @ {ParkReference}  |  {Comments}";
    public string BadgeText => IsLogged ? "LOGGED" : Mode;
    public string BadgeBackground => IsLogged ? "#3B4A68" : "#1E6F5C";
    public string BadgeForeground => IsLogged ? "#DDE7FF" : "#F4FFF8";
    public string RowBackground => IsLogged ? "#111723" : "#0C1017";
    public string MessageForeground => IsLogged ? "#8F9BB4" : "#E5ECFF";
}

public sealed record LongwaveLogbookItem(
    string Id,
    string Name,
    string OperatorCallsign,
    string? ParkReference,
    string? ActivationDate,
    string? Notes,
    int ContactCount)
{
    public string DisplayText => ContactCount <= 0 ? Name : $"{Name}  ({ContactCount})";
    public string DetailText => string.IsNullOrWhiteSpace(ParkReference)
        ? $"{OperatorCallsign}"
        : $"{OperatorCallsign}  |  {ParkReference}";
}

public sealed record LongwaveRecentContactItem(
    string Id,
    string LogbookId,
    string StationCallsign,
    string OperatorCallsign,
    string QsoDate,
    string TimeOn,
    string Mode,
    string Band,
    string TimeText,
    string? ParkReference,
    double FrequencyKhz,
    string? RstSent,
    string? RstReceived,
    string? Name,
    string? Qth,
    string? County,
    string? GridSquare,
    string? Country,
    string? State,
    string? Dxcc,
    string? QrzUploadStatus,
    string? QrzUploadDate,
    double? Latitude,
    double? Longitude,
    string? SourceSpotId)
{
    public string FrequencyText => $"{FrequencyKhz / 1000d:0.000} MHz";
    public string QrzUploadText => string.Equals(QrzUploadStatus, "Y", StringComparison.OrdinalIgnoreCase)
        ? string.IsNullOrWhiteSpace(QrzUploadDate) ? "QRZ uploaded" : $"QRZ {QrzUploadDate}"
        : "QRZ pending";
    public string QrzUploadBackground => string.Equals(QrzUploadStatus, "Y", StringComparison.OrdinalIgnoreCase)
        ? "#1E6F5C"
        : "#4A3B18";
    public string QrzUploadForeground => string.Equals(QrzUploadStatus, "Y", StringComparison.OrdinalIgnoreCase)
        ? "#F4FFF8"
        : "#FFE7A6";
    public string SummaryText => string.IsNullOrWhiteSpace(ParkReference)
        ? $"{StationCallsign}  |  {Band} {Mode}"
        : $"{StationCallsign}  |  {Band} {Mode}  |  {ParkReference}";
}

public sealed class FreedvReporterStationItem : ObservableObject
{
    private string _callsign;
    private string _gridSquare;
    private string _frequencyText;
    private string _modeText;
    private string _txText;
    private string _heardText;
    private string _messageText;
    private string _updatedText;
    private long? _frequencyHz;
    private bool _isTransmitting;

    public FreedvReporterStationItem(
        string sid,
        string callsign,
        string gridSquare,
        string frequencyText,
        string modeText,
        string txText,
        string heardText,
        string messageText,
        string updatedText,
        long? frequencyHz,
        bool isTransmitting)
    {
        Sid = sid;
        _callsign = callsign;
        _gridSquare = gridSquare;
        _frequencyText = frequencyText;
        _modeText = modeText;
        _txText = txText;
        _heardText = heardText;
        _messageText = messageText;
        _updatedText = updatedText;
        _frequencyHz = frequencyHz;
        _isTransmitting = isTransmitting;
    }

    public string Sid { get; }
    public string Callsign { get => _callsign; private set => SetProperty(ref _callsign, value); }
    public string GridSquare { get => _gridSquare; private set => SetProperty(ref _gridSquare, value); }
    public string FrequencyText { get => _frequencyText; private set => SetProperty(ref _frequencyText, value); }
    public string ModeText { get => _modeText; private set => SetProperty(ref _modeText, value); }
    public string TxText { get => _txText; private set => SetProperty(ref _txText, value); }
    public string HeardText { get => _heardText; private set => SetProperty(ref _heardText, value); }
    public string MessageText { get => _messageText; private set => SetProperty(ref _messageText, value); }
    public string UpdatedText { get => _updatedText; private set => SetProperty(ref _updatedText, value); }
    public long? FrequencyHz { get => _frequencyHz; private set => SetProperty(ref _frequencyHz, value); }
    public bool IsTransmitting
    {
        get => _isTransmitting;
        private set
        {
            if (SetProperty(ref _isTransmitting, value))
            {
                OnPropertyChanged(nameof(AccentBrush));
                OnPropertyChanged(nameof(RowBackground));
            }
        }
    }

    public string SummaryText => $"{Callsign}  |  {GridSquare}";
    public string AccentBrush => IsTransmitting ? "#FFB000" : "#77D8FF";
    public string RowBackground => IsTransmitting ? "#261B08" : "#0C1017";

    public void UpdateFrom(FreedvReporterStationItem item)
    {
        Callsign = item.Callsign;
        GridSquare = item.GridSquare;
        FrequencyText = item.FrequencyText;
        ModeText = item.ModeText;
        TxText = item.TxText;
        HeardText = item.HeardText;
        MessageText = item.MessageText;
        UpdatedText = item.UpdatedText;
        FrequencyHz = item.FrequencyHz;
        IsTransmitting = item.IsTransmitting;
        OnPropertyChanged(nameof(SummaryText));
    }
}
