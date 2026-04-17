namespace ShackStack.Core.Abstractions.Models;

public sealed record LongwaveLogbook(
    string Id,
    string Name,
    string OperatorCallsign,
    string? ParkReference,
    string? ActivationDate,
    string? Notes,
    int ContactCount
);
