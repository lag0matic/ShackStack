using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface ICwSkimmerDecoderHost
{
    Task ConfigureAsync(CwSkimmerConfiguration configuration, CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task ResetAsync(CancellationToken ct);
    IObservable<CwSkimmerTelemetry> TelemetryStream { get; }
    IObservable<CwSkimmerCandidate> CandidateStream { get; }
}
