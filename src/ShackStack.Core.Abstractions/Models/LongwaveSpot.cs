namespace ShackStack.Core.Abstractions.Models;

public sealed record LongwaveSpot(
    string Id,
    string Source,
    string ActivatorCallsign,
    string ParkReference,
    double FrequencyKhz,
    string Mode,
    string Band,
    string? Comments,
    string? SpotterCallsign,
    DateTime SpottedAtUtc,
    double? Latitude,
    double? Longitude
);
