namespace ShackStack.Core.Abstractions.Models;

public sealed record KeyboardModeDecodeChunk(
    string Text,
    double Confidence);
