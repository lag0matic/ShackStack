using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface ISstvTransmitService
{
    Task<Pcm16AudioClip> BuildTransmitClipAsync(string mode, byte[] rgb24, int width, int height, CancellationToken ct);
}
