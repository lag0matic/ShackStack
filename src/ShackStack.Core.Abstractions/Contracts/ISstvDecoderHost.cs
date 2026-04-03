using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface ISstvDecoderHost
{
    Task ConfigureAsync(SstvDecoderConfiguration configuration, CancellationToken ct);
    Task SetManualAlignmentAsync(int manualSlant, int manualOffset, CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task ResetAsync(CancellationToken ct);
    IObservable<SstvDecoderTelemetry> TelemetryStream { get; }
    IObservable<SstvImageFrame> ImageStream { get; }
}
