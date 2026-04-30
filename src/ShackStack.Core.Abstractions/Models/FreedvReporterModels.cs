namespace ShackStack.Core.Abstractions.Models;

public sealed record FreedvReporterConfiguration(
    string Hostname,
    string Callsign,
    string GridSquare,
    string Software,
    bool ReportStation,
    bool ReceiveOnly);

public sealed record FreedvReporterStation(
    string Sid,
    string Callsign,
    string GridSquare,
    string Version,
    bool ReceiveOnly,
    DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? LastUpdatedUtc,
    long? FrequencyHz,
    string Mode,
    bool IsTransmitting,
    DateTimeOffset? LastTransmitUtc,
    string Message,
    string LastHeardCallsign,
    double? LastHeardSnrDb,
    string LastHeardMode)
{
    public static FreedvReporterStation Empty(string sid) => new(
        sid,
        string.Empty,
        string.Empty,
        string.Empty,
        false,
        null,
        null,
        null,
        string.Empty,
        false,
        null,
        string.Empty,
        string.Empty,
        null,
        string.Empty);
}

public sealed record FreedvReporterSnapshot(
    bool IsConnected,
    string Status,
    IReadOnlyList<FreedvReporterStation> Stations);
