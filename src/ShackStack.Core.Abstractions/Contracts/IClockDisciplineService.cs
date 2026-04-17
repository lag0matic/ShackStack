using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IClockDisciplineService
{
    ClockDisciplineSnapshot Current { get; }
    IObservable<ClockDisciplineSnapshot> SnapshotStream { get; }
}
