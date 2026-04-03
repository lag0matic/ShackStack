using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IBandConditionsService
{
    IObservable<BandConditionsSnapshot> SnapshotStream { get; }
    Task StartAsync(CancellationToken ct);
}
