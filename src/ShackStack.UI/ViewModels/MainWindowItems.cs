using Avalonia.Media;
using Avalonia.Media.Imaging;
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

public sealed record LongwaveLogbookItem(string Id, string Name, string OperatorCallsign, string? Notes)
{
    public string DisplayText => Name;
}

public sealed record LongwaveRecentContactItem(
    string Id,
    string StationCallsign,
    string Mode,
    string Band,
    string TimeText,
    string? ParkReference,
    double FrequencyKhz)
{
    public string FrequencyText => $"{FrequencyKhz / 1000d:0.000} MHz";
    public string SummaryText => string.IsNullOrWhiteSpace(ParkReference)
        ? $"{StationCallsign}  |  {Band} {Mode}"
        : $"{StationCallsign}  |  {Band} {Mode}  |  {ParkReference}";
}
