namespace ShackStack.Core.Abstractions.Models;

public sealed record BandConditionCell(
    string BandLabel,
    string DayCondition,
    string NightCondition
);
