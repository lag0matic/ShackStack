namespace ShackStack.Core.Abstractions.Models;

public sealed record ModeFrequencyPreset(
    string ModeGroup,
    string DisplayLabel,
    long FrequencyHz);
