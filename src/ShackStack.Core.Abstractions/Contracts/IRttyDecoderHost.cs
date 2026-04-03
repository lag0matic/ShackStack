using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IRttyDecoderHost
{
    Task ConfigureAsync(RttyDecoderConfiguration configuration, CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task ResetAsync(CancellationToken ct);
    IObservable<RttyDecoderTelemetry> TelemetryStream { get; }
    IObservable<RttyDecodeChunk> DecodeStream { get; }
}
