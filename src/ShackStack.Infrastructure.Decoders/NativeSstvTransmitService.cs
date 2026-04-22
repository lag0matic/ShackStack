using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.DecoderHost.Sstv.Core;

namespace ShackStack.Infrastructure.Decoders;

public sealed class NativeSstvTransmitService : ISstvTransmitService
{
    private readonly SstvTransmitClipBuilder _builder = new();

    public Task<Pcm16AudioClip> BuildTransmitClipAsync(string mode, byte[] rgb24, int width, int height, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var clip = _builder.Build(mode, rgb24, width, height);
        return Task.FromResult(new Pcm16AudioClip(clip.PcmBytes, clip.SampleRate, clip.Channels));
    }
}
