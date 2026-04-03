namespace ShackStack.Core.Abstractions.Models;

public sealed record SpectrumFrame(
    float[] Bins,
    long CenterFrequencyHz,
    int SpanHz
);
