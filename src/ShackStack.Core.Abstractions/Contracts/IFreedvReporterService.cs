using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IFreedvReporterService
{
    IObservable<FreedvReporterSnapshot> SnapshotStream { get; }
    Task ConnectAsync(FreedvReporterConfiguration configuration, CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    Task UpdateFrequencyAsync(long frequencyHz, CancellationToken ct);
    Task UpdateTransmitAsync(string mode, bool isTransmitting, CancellationToken ct);
    Task UpdateMessageAsync(string message, CancellationToken ct);
    Task ReportReceiveAsync(string callsign, string mode, double snrDb, CancellationToken ct);
}
