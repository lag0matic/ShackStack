using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface ICwDecoderHost
{
    Task ConfigureAsync(CwDecoderConfiguration configuration, CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task ResetAsync(CancellationToken ct);
    IObservable<CwDecoderTelemetry> TelemetryStream { get; }
    IObservable<CwDecodeChunk> DecodeStream { get; }
}
