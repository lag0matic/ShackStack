namespace ShackStack.Core.Abstractions.Models;

public sealed record WsjtxFrequencyPreset(
    string ModeLabel,
    string DisplayLabel,
    long FrequencyHz);
