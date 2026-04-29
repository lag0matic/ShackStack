using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface IAudioService
{
    Task<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(CancellationToken ct);
    Task StartReceiveAsync(AudioRoute route, CancellationToken ct);
    Task StartReceiveCaptureAsync(AudioRoute route, CancellationToken ct);
    Task StopReceiveAsync(CancellationToken ct);
    Task StartTransmitAsync(AudioRoute route, CancellationToken ct);
    Task StartMicCaptureAsync(AudioRoute route, CancellationToken ct);
    Task StartTransmitPcmAsync(AudioRoute route, Pcm16AudioClip clip, CancellationToken ct);
    Task PlayTransmitPcmAsync(AudioRoute route, Pcm16AudioClip clip, CancellationToken ct);
    Task StopTransmitAsync(CancellationToken ct);
    Task PlayMonitorPcmAsync(AudioRoute route, Pcm16AudioClip clip, CancellationToken ct);
    Task PlayDecodedMonitorPcmAsync(AudioRoute route, Pcm16AudioClip clip, CancellationToken ct);
    Task SetMonitorVolumeAsync(float volume, CancellationToken ct);
    Task SetDecodedMonitorVolumeAsync(float volume, CancellationToken ct);
    Task SetMicGainAsync(float gain, CancellationToken ct);
    Task SetVoiceCompressionAsync(float amount, CancellationToken ct);
    Task SetMicMonitorAsync(bool enabled, float level, CancellationToken ct);
    IObservable<AudioBuffer> ReceiveStream { get; }
    IObservable<AudioBuffer> MicStream { get; }
    IObservable<AudioLevels> LevelStream { get; }
}
