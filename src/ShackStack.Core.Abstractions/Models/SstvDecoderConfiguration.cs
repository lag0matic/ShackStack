namespace ShackStack.Core.Abstractions.Models;

public sealed record SstvDecoderConfiguration(
    string Mode,
    string FrequencyLabel,
    int ManualSlant,
    int ManualOffset
);
