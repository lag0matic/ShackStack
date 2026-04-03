namespace ShackStack.Core.Abstractions.Models;

public sealed record RttyDecoderConfiguration(
    string ProfileLabel,
    int ShiftHz,
    double BaudRate,
    string FrequencyLabel);
