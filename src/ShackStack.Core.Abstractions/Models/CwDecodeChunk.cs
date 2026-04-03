namespace ShackStack.Core.Abstractions.Models;

public sealed record CwDecodeChunk(
    string Text,
    double Confidence
);
