using System.Text.Json;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class FldigiRttyDecoderHost : IRttyDecoderHost, IDisposable
{
    private readonly IAudioService _audioService;
    private readonly SimpleSubject<RttyDecoderTelemetry> _telemetry = new();
    private readonly SimpleSubject<RttyDecodeChunk> _decode = new();
    private readonly IDisposable _audioSubscription;
    private readonly DecoderWorkerProcess _workerProcess;
    private readonly DecoderAudioPump _audioPump;

    private RttyDecoderConfiguration _configuration = new("170 Hz / 45.45 baud", 170, 45.45, "14.080 MHz USB", 1700.0, false);
    private bool _isRunning;

    public FldigiRttyDecoderHost(IAudioService audioService)
    {
        _audioService = audioService;
        _workerProcess = new DecoderWorkerProcess(BundledDecoderWorkerLocator.Resolve("rtty_sidecar_worker"));
        _audioPump = new DecoderAudioPump(SendAudioAsync, () => _isRunning);

        _audioSubscription = _audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (!_isRunning)
            {
                return;
            }

            _audioPump.Enqueue(buffer);
        }));

        _telemetry.OnNext(new RttyDecoderTelemetry(
            false,
            _workerProcess.Exists ? "fldigi GPL RTTY worker ready to launch" : $"Worker missing: {_workerProcess.DisplayPath}",
            "fldigi GPL RTTY sidecar",
            0,
            _configuration.ShiftHz,
            _configuration.BaudRate,
            _configuration.ProfileLabel));
    }

    public IObservable<RttyDecoderTelemetry> TelemetryStream => _telemetry;

    public IObservable<RttyDecodeChunk> DecodeStream => _decode;

    public async Task ConfigureAsync(RttyDecoderConfiguration configuration, CancellationToken ct)
    {
        _configuration = configuration;
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new
        {
            type = "configure",
            profileLabel = configuration.ProfileLabel,
            shiftHz = configuration.ShiftHz,
            baudRate = configuration.BaudRate,
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
            _telemetry.OnNext(new RttyDecoderTelemetry(
                false,
                $"Worker missing: {_workerProcess.DisplayPath}",
                "fldigi GPL RTTY sidecar",
                0,
                _configuration.ShiftHz,
                _configuration.BaudRate,
                _configuration.ProfileLabel));
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
                _telemetry.OnNext(new RttyDecoderTelemetry(
                    root.TryGetProperty("isRunning", out var runningEl) && runningEl.GetBoolean(),
                    root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("activeWorker", out var workerEl) ? workerEl.GetString() ?? "fldigi GPL RTTY sidecar" : "fldigi GPL RTTY sidecar",
                    root.TryGetProperty("signalLevelPercent", out var levelEl) ? levelEl.GetInt32() : 0,
                    root.TryGetProperty("estimatedShiftHz", out var shiftEl) ? shiftEl.GetInt32() : _configuration.ShiftHz,
                    root.TryGetProperty("estimatedBaud", out var baudEl) ? baudEl.GetDouble() : _configuration.BaudRate,
                    root.TryGetProperty("profileLabel", out var profileEl) ? profileEl.GetString() ?? _configuration.ProfileLabel : _configuration.ProfileLabel,
                    root.TryGetProperty("suggestedAudioCenterHz", out var centerEl) ? centerEl.GetDouble() : _configuration.AudioCenterHz,
                    root.TryGetProperty("tuneConfidence", out var confidenceEl) ? confidenceEl.GetDouble() : 0.0,
                    root.TryGetProperty("isCarrierLocked", out var lockedEl) && lockedEl.GetBoolean()));
            }
            else if (string.Equals(type, "decode", StringComparison.OrdinalIgnoreCase))
            {
                _decode.OnNext(new RttyDecodeChunk(
                    root.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("confidence", out var confEl) ? confEl.GetDouble() : 0));
            }
        }
        catch (Exception ex)
        {
            _telemetry.OnNext(new RttyDecoderTelemetry(
                _isRunning,
                $"Decoder output parse error: {ex.Message}",
                "fldigi GPL RTTY sidecar",
                0,
                _configuration.ShiftHz,
                _configuration.BaudRate,
                _configuration.ProfileLabel));
        }

        return Task.CompletedTask;
    }

    private Task HandleStderrLineAsync(string line)
    {
        _telemetry.OnNext(new RttyDecoderTelemetry(
            _isRunning,
            $"Worker stderr: {line}",
            "fldigi GPL RTTY sidecar",
            0,
            _configuration.ShiftHz,
            _configuration.BaudRate,
            _configuration.ProfileLabel));
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
        _telemetry.OnNext(new RttyDecoderTelemetry(
            false,
            "fldigi GPL RTTY worker exited",
            "fldigi GPL RTTY sidecar",
            0,
            _configuration.ShiftHz,
            _configuration.BaudRate,
            _configuration.ProfileLabel));
    }

    public void Dispose()
    {
        _isRunning = false;
        _audioSubscription.Dispose();
        _audioPump.Dispose();
        _workerProcess.Dispose();
    }
}
