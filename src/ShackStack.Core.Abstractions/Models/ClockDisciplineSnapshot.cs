namespace ShackStack.Core.Abstractions.Models;

public sealed record ClockDisciplineSnapshot(
    string Status,
    bool IsSynchronized,
    double OffsetMs,
    string SourceLabel,
    DateTimeOffset LastCheckedUtc);
