namespace ShackStack.Core.Abstractions.Models;

public sealed record Pcm16AudioClip(
    byte[] PcmBytes,
    int SampleRate,
    int Channels);
