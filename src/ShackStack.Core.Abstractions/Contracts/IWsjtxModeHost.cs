using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IWsjtxModeHost
{
    Task ConfigureAsync(WsjtxModeConfiguration configuration, CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task ResetAsync(CancellationToken ct);
    Task<WsjtxPreparedTransmitResult> PrepareTransmitAsync(string modeLabel, string messageText, int txAudioFrequencyHz, CancellationToken ct);
    IObservable<WsjtxModeTelemetry> TelemetryStream { get; }
    IObservable<WsjtxDecodeMessage> DecodeStream { get; }
}
