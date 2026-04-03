using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IInteropService
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    IObservable<InteropEvent> Events { get; }
}
