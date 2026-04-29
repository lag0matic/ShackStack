namespace ShackStack.Core.Abstractions.Models;

public sealed record LongwavePotaSpotDraft(
    string ActivatorCallsign,
    string ParkReference,
    double FrequencyKhz,
    string Mode,
    string Band,
    string? Comments,
    string? SpotterCallsign);
