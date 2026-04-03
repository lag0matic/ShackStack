using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IWefaxDecoderHost
{
    Task ConfigureAsync(WefaxDecoderConfiguration configuration, CancellationToken ct);
    Task SetManualSlantAsync(int manualSlant, CancellationToken ct);
    Task SetManualOffsetAsync(int manualOffset, CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StartNowAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task ResetAsync(CancellationToken ct);
    IObservable<WefaxDecoderTelemetry> TelemetryStream { get; }
    IObservable<WefaxImageFrame> ImageStream { get; }
}
