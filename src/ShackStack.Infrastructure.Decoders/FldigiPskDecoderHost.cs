using System.Text.Json;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class FldigiPskDecoderHost : IKeyboardModeDecoderHost, IDisposable
{
    private readonly SimpleSubject<KeyboardModeDecoderTelemetry> _telemetry = new();
    private readonly SimpleSubject<KeyboardModeDecodeChunk> _decode = new();
    private readonly IDisposable _audioSubscription;
    private readonly DecoderWorkerProcess _workerProcess;
    private readonly DecoderAudioPump _audioPump;

    private KeyboardModeDecoderConfiguration _configuration = new("BPSK31", "Current radio frequency", 1000.0, false);
    private bool _isRunning;

    public FldigiPskDecoderHost(IAudioService audioService)
    {
        _workerProcess = new DecoderWorkerProcess(BundledDecoderWorkerLocator.Resolve("psk_sidecar_worker"));
        _audioPump = new DecoderAudioPump(SendAudioAsync, () => _isRunning);
        _audioSubscription = audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (_isRunning)
            {
                _audioPump.Enqueue(buffer);
            }
        }));

        _telemetry.OnNext(new KeyboardModeDecoderTelemetry(
            false,
            _workerProcess.Exists ? "fldigi GPL PSK worker ready to launch" : $"Worker missing: {_workerProcess.DisplayPath}",
            "fldigi GPL PSK sidecar",
            _configuration.ModeLabel,
            0,
            _configuration.AudioCenterHz,
            _configuration.AudioCenterHz,
            _configuration.AudioCenterHz,
            0,
            0,
            false));
    }

    public IObservable<KeyboardModeDecoderTelemetry> TelemetryStream => _telemetry;

    public IObservable<KeyboardModeDecodeChunk> DecodeStream => _decode;

    public async Task ConfigureAsync(KeyboardModeDecoderConfiguration configuration, CancellationToken ct)
    {
        _configuration = configuration;
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new
        {
            type = "configure",
            modeLabel = configuration.ModeLabel,
            frequencyLabel = configuration.FrequencyLabel,
            audioCenterHz = configuration.AudioCenterHz,
            reversePolarity = configuration.ReversePolarity,
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
        if (_workerProcess.IsStarted)
        {
            await SendMessageAsync(new { type = "stop" }, ct).ConfigureAwait(false);
        }
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        if (_workerProcess.IsStarted)
        {
            await SendMessageAsync(new { type = "reset" }, ct).ConfigureAwait(false);
        }
    }

    private Task EnsureProcessAsync(CancellationToken ct)
    {
        if (!_workerProcess.Exists)
        {
            _telemetry.OnNext(new KeyboardModeDecoderTelemetry(
                false,
                $"Worker missing: {_workerProcess.DisplayPath}",
                "fldigi GPL PSK sidecar",
                _configuration.ModeLabel,
                0,
                _configuration.AudioCenterHz,
                _configuration.AudioCenterHz,
                _configuration.AudioCenterHz,
                0,
                0,
                false));
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
                _telemetry.OnNext(new KeyboardModeDecoderTelemetry(
                    root.TryGetProperty("isRunning", out var runningEl) && runningEl.GetBoolean(),
                    root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("activeWorker", out var workerEl) ? workerEl.GetString() ?? "fldigi GPL PSK sidecar" : "fldigi GPL PSK sidecar",
                    root.TryGetProperty("modeLabel", out var modeEl) ? modeEl.GetString() ?? _configuration.ModeLabel : _configuration.ModeLabel,
                    root.TryGetProperty("signalLevelPercent", out var levelEl) ? levelEl.GetInt32() : 0,
                    root.TryGetProperty("audioCenterHz", out var centerEl) ? centerEl.GetDouble() : _configuration.AudioCenterHz,
                    root.TryGetProperty("trackedAudioCenterHz", out var trackedEl) ? trackedEl.GetDouble() : _configuration.AudioCenterHz,
                    root.TryGetProperty("suggestedAudioCenterHz", out var suggestedEl) ? suggestedEl.GetDouble() : _configuration.AudioCenterHz,
                    root.TryGetProperty("suggestedAudioScoreDb", out var scoreEl) ? scoreEl.GetDouble() : 0.0,
                    root.TryGetProperty("frequencyErrorHz", out var errorEl) ? errorEl.GetDouble() : 0.0,
                    root.TryGetProperty("isDcdOpen", out var dcdEl) && dcdEl.GetBoolean()));
            }
            else if (string.Equals(type, "decode", StringComparison.OrdinalIgnoreCase))
            {
                _decode.OnNext(new KeyboardModeDecodeChunk(
                    root.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("confidence", out var confEl) ? confEl.GetDouble() : 0));
            }
        }
        catch (Exception ex)
        {
            _telemetry.OnNext(new KeyboardModeDecoderTelemetry(
                _isRunning,
                $"Decoder output parse error: {ex.Message}",
                "fldigi GPL PSK sidecar",
                _configuration.ModeLabel,
                0,
                _configuration.AudioCenterHz,
                _configuration.AudioCenterHz,
                _configuration.AudioCenterHz,
                0,
                0,
                false));
        }

        return Task.CompletedTask;
    }

    private Task HandleStderrLineAsync(string line)
    {
        _telemetry.OnNext(new KeyboardModeDecoderTelemetry(
            _isRunning,
            $"Worker stderr: {line}",
            "fldigi GPL PSK sidecar",
            _configuration.ModeLabel,
            0,
            _configuration.AudioCenterHz,
            _configuration.AudioCenterHz,
            _configuration.AudioCenterHz,
            0,
            0,
            false));
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
        _telemetry.OnNext(new KeyboardModeDecoderTelemetry(
            false,
            "fldigi GPL PSK worker exited",
            "fldigi GPL PSK sidecar",
            _configuration.ModeLabel,
            0,
            _configuration.AudioCenterHz,
            _configuration.AudioCenterHz,
            _configuration.AudioCenterHz,
            0,
            0,
            false));
    }

    public void Dispose()
    {
        _isRunning = false;
        _audioSubscription.Dispose();
        _audioPump.Dispose();
        _workerProcess.Dispose();
    }
}
