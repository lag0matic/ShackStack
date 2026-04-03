namespace ShackStack.Core.Abstractions.Models;

public sealed record WaterfallRow(
    float[] Bins,
    long CenterFrequencyHz,
    int SpanHz
);
