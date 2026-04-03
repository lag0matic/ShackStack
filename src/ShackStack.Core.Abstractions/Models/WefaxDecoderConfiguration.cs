namespace ShackStack.Core.Abstractions.Models;

public sealed record WefaxDecoderConfiguration(
    string ModeLabel,
    int Ioc,
    int Lpm,
    string FrequencyLabel,
    int ManualSlant,
    int ManualOffset
);
