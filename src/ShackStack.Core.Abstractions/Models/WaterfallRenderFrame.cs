namespace ShackStack.Core.Abstractions.Models;

public sealed record WaterfallRenderFrame(
    int Width,
    int Height,
    long CenterFrequencyHz,
    int SpanHz,
    float[] SpectrumBins,
    byte[] WaterfallPixels
);
