namespace ShackStack.Core.Abstractions.Models;

public sealed record KeyboardModeDecoderConfiguration(
    string ModeLabel,
    string FrequencyLabel,
    double AudioCenterHz = 1000.0,
    bool ReversePolarity = false);
