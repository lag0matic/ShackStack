using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IDecoderPlugin
{
    string Id { get; }
    string DisplayName { get; }
    DecoderKind Kind { get; }
    Task StartAsync(IDecoderContext context, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    IObservable<DecoderStatus> StatusStream { get; }
    IObservable<DecoderResult> ResultStream { get; }
}
