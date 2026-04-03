namespace ShackStack.Core.Abstractions.Models;

public sealed record AudioBuffer(
    float[] Samples,
    int SampleRate,
    int Channels
);
