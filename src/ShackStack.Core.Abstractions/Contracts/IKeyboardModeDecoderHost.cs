using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IKeyboardModeDecoderHost
{
    Task ConfigureAsync(KeyboardModeDecoderConfiguration configuration, CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task ResetAsync(CancellationToken ct);
    IObservable<KeyboardModeDecoderTelemetry> TelemetryStream { get; }
    IObservable<KeyboardModeDecodeChunk> DecodeStream { get; }
}
