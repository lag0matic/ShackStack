namespace ShackStack.Core.Abstractions.Models;

public sealed record LongwaveCallsignLookup(
    string Callsign,
    string? Name,
    string? Qth,
    string? County,
    string? GridSquare,
    string? Country,
    string? State,
    string? Dxcc,
    double? Latitude,
    double? Longitude,
    string? QrzUrl
);
