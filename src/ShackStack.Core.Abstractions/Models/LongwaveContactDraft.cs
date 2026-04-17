namespace ShackStack.Core.Abstractions.Models;

public sealed record LongwaveContactDraft(
    string LogbookId,
    string StationCallsign,
    string OperatorCallsign,
    string QsoDate,
    string TimeOn,
    string Band,
    string Mode,
    double FrequencyKhz,
    string? ParkReference,
    string? RstSent,
    string? RstReceived,
    string? Name,
    string? Qth,
    string? County,
    string? GridSquare,
    string? Country,
    string? State,
    string? Dxcc,
    double? Latitude,
    double? Longitude,
    string? SourceSpotId
);
