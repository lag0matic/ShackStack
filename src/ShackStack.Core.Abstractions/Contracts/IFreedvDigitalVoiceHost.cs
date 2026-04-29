using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IFreedvDigitalVoiceHost
{
    Task ConfigureAsync(FreedvDigitalVoiceConfiguration configuration, CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task StartTransmitAsync(AudioRoute route, CancellationToken ct);
    Task StopTransmitAsync(CancellationToken ct);
    Task ResetAsync(CancellationToken ct);
    IObservable<FreedvDigitalVoiceTelemetry> TelemetryStream { get; }
    IObservable<Pcm16AudioClip> SpeechStream { get; }
}
