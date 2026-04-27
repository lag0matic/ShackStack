using System.Text.Json;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class PythonCwDecoderHost : ICwDecoderHost, IDisposable
{
    private readonly IAudioService _audioService;
    private readonly SimpleSubject<CwDecoderTelemetry> _telemetry = new();
    private readonly SimpleSubject<CwDecodeChunk> _decode = new();
    private readonly IDisposable _audioSubscription;
    private readonly DecoderWorkerProcess _workerProcess;
    private readonly DecoderAudioPump _audioPump;

    private CwDecoderConfiguration _configuration = new(700, 20, "Sidecar");
    private bool _isRunning;

    public PythonCwDecoderHost(IAudioService audioService)
    {
        _audioService = audioService;
        _workerProcess = new DecoderWorkerProcess(BundledDecoderWorkerLocator.Resolve("cw_sidecar_worker"));
        _audioPump = new DecoderAudioPump(SendAudioAsync, () => _isRunning);

        _audioSubscription = _audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (!_isRunning)
            {
                return;
            }

            _audioPump.Enqueue(buffer);
        }));

        _telemetry.OnNext(new CwDecoderTelemetry(
            false,
            _workerProcess.Exists ? "Python CW worker ready to launch" : $"Worker missing: {_workerProcess.DisplayPath}",
            "Python CW sidecar",
            0.0,
            _configuration.PitchHz,
            _configuration.Wpm));
    }

    public IObservable<CwDecoderTelemetry> TelemetryStream => _telemetry;

    public IObservable<CwDecodeChunk> DecodeStream => _decode;

    public async Task ConfigureAsync(CwDecoderConfiguration configuration, CancellationToken ct)
    {
        _configuration = configuration;
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new
        {
            type = "configure",
            pitchHz = configuration.PitchHz,
            wpm = configuration.Wpm,
            profile = configuration.Profile,
            bandwidthHz = configuration.BandwidthHz,
            matchedFilterEnabled = configuration.MatchedFilterEnabled,
            trackingEnabled = configuration.TrackingEnabled,
            trackingRangeWpm = configuration.TrackingRangeWpm,
            lowerWpmLimit = configuration.LowerWpmLimit,
            upperWpmLimit = configuration.UpperWpmLimit,
            attack = configuration.Attack,
            decay = configuration.Decay,
            noiseCharacter = configuration.NoiseCharacter,
            autoToneSearchEnabled = configuration.AutoToneSearchEnabled,
            afcEnabled = configuration.AfcEnabled,
            toneSearchSpanHz = configuration.ToneSearchSpanHz,
            squelch = configuration.Squelch,
            spacing = configuration.Spacing,
        }, ct).ConfigureAwait(false);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new { type = "start" }, ct).ConfigureAwait(false);
        _isRunning = true;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _isRunning = false;
        if (!_workerProcess.IsStarted)
        {
            return;
        }

        await SendMessageAsync(new { type = "stop" }, ct).ConfigureAwait(false);
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        if (!_workerProcess.IsStarted)
        {
            return;
        }

        await SendMessageAsync(new { type = "reset" }, ct).ConfigureAwait(false);
    }

    private Task EnsureProcessAsync(CancellationToken ct)
    {
        if (!_workerProcess.Exists)
        {
            _telemetry.OnNext(new CwDecoderTelemetry(
                false,
                $"Worker missing: {_workerProcess.DisplayPath}",
                "Python CW sidecar",
                0.0,
                _configuration.PitchHz,
                _configuration.Wpm));
            return Task.CompletedTask;
        }

        return _workerProcess.EnsureStartedAsync(HandleStdoutLineAsync, HandleStderrLineAsync, () => OnWorkerExited(null, EventArgs.Empty), ct);
    }

    private Task HandleStdoutLineAsync(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            if (string.Equals(type, "telemetry", StringComparison.OrdinalIgnoreCase))
            {
                _telemetry.OnNext(new CwDecoderTelemetry(
                    root.TryGetProperty("isRunning", out var runningEl) && runningEl.GetBoolean(),
                    root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("activeWorker", out var workerEl) ? workerEl.GetString() ?? "Python CW sidecar" : "Python CW sidecar",
                    root.TryGetProperty("confidence", out var confEl) ? confEl.GetDouble() : 0.0,
                    root.TryGetProperty("estimatedPitchHz", out var pitchEl) ? pitchEl.GetInt32() : _configuration.PitchHz,
                    root.TryGetProperty("estimatedWpm", out var wpmEl) ? wpmEl.GetInt32() : _configuration.Wpm));
            }
            else if (string.Equals(type, "decode", StringComparison.OrdinalIgnoreCase))
            {
                _decode.OnNext(new CwDecodeChunk(
                    root.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("confidence", out var confEl) ? confEl.GetDouble() : 0.0));
            }
        }
        catch (Exception ex)
        {
            _telemetry.OnNext(new CwDecoderTelemetry(
                _isRunning,
                $"Decoder output parse error: {ex.Message}",
                "Python CW sidecar",
                0.0,
                _configuration.PitchHz,
                _configuration.Wpm));
        }

        return Task.CompletedTask;
    }

    private Task HandleStderrLineAsync(string line)
    {
        _telemetry.OnNext(new CwDecoderTelemetry(
            _isRunning,
            $"Worker stderr: {line}",
            "Python CW sidecar",
            0.0,
            _configuration.PitchHz,
            _configuration.Wpm));
        return Task.CompletedTask;
    }

    private async Task SendAudioAsync(AudioBuffer buffer, CancellationToken ct)
    {
        if (!_workerProcess.IsStarted)
        {
            return;
        }

        var bytes = new byte[buffer.Samples.Length * sizeof(float)];
        Buffer.BlockCopy(buffer.Samples, 0, bytes, 0, bytes.Length);
        await SendMessageAsync(new
        {
            type = "audio",
            sampleRate = buffer.SampleRate,
            channels = buffer.Channels,
            samples = Convert.ToBase64String(bytes),
        }, ct).ConfigureAwait(false);
    }

    private Task SendMessageAsync<T>(T payload, CancellationToken ct)
        => _workerProcess.SendJsonAsync(payload, ct);

    private void OnWorkerExited(object? sender, EventArgs e)
    {
        _isRunning = false;
        _telemetry.OnNext(new CwDecoderTelemetry(
            false,
            "Python CW worker exited",
            "Python CW sidecar",
            0.0,
            _configuration.PitchHz,
            _configuration.Wpm));
    }

    public void Dispose()
    {
        _isRunning = false;
        _audioSubscription.Dispose();
        _audioPump.Dispose();
        _workerProcess.Dispose();
    }
}
