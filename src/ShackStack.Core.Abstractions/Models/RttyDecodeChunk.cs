namespace ShackStack.Core.Abstractions.Models;

public sealed record RttyDecodeChunk(
    string Text,
    double Confidence);
