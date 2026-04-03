using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class NullCwDecoderHost : ICwDecoderHost
{
    private readonly SimpleSubject<CwDecoderTelemetry> _telemetry = new();
    private readonly SimpleSubject<CwDecodeChunk> _decode = new();
    private CwDecoderConfiguration _configuration = new(700, 20, "Sidecar");

    public NullCwDecoderHost()
    {
        _telemetry.OnNext(new CwDecoderTelemetry(
            false,
            "Decoder sidecar not connected yet",
            "None",
            0.0,
            _configuration.PitchHz,
            _configuration.Wpm));
    }

    public IObservable<CwDecoderTelemetry> TelemetryStream => _telemetry;

    public IObservable<CwDecodeChunk> DecodeStream => _decode;

    public Task ConfigureAsync(CwDecoderConfiguration configuration, CancellationToken ct)
    {
        _configuration = configuration;
        _telemetry.OnNext(new CwDecoderTelemetry(
            false,
            $"Configured for {_configuration.PitchHz} Hz / {_configuration.Wpm} WPM",
            "None",
            0.0,
            _configuration.PitchHz,
            _configuration.Wpm));
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _telemetry.OnNext(new CwDecoderTelemetry(
            true,
            "Waiting for decoder worker",
            "CW sidecar",
            0.0,
            _configuration.PitchHz,
            _configuration.Wpm));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _telemetry.OnNext(new CwDecoderTelemetry(
            false,
            "Decoder stopped",
            "CW sidecar",
            0.0,
            _configuration.PitchHz,
            _configuration.Wpm));
        return Task.CompletedTask;
    }

    public Task ResetAsync(CancellationToken ct)
    {
        _telemetry.OnNext(new CwDecoderTelemetry(
            false,
            "Decoder reset",
            "CW sidecar",
            0.0,
            _configuration.PitchHz,
            _configuration.Wpm));
        return Task.CompletedTask;
    }
}
