using System.Text.Json;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class NativeSstvDecoderHost : ISstvDecoderHost, IDisposable
{
    private readonly IAudioService _audioService;
    private readonly SimpleSubject<SstvDecoderTelemetry> _telemetry = new();
    private readonly SimpleSubject<SstvImageFrame> _images = new();
    private readonly IDisposable _audioSubscription;
    private readonly DecoderWorkerProcess _workerProcess;
    private readonly DecoderAudioPump _audioPump;

    private SstvDecoderConfiguration _configuration = new("Martin 1", "14.230 MHz USB");
    private bool _isRunning;

    public NativeSstvDecoderHost(IAudioService audioService)
    {
        _audioService = audioService;
        _workerProcess = new DecoderWorkerProcess(BundledDecoderWorkerLocator.Resolve("sstv_native_sidecar"));
        _audioPump = new DecoderAudioPump(SendAudioAsync, () => _isRunning);

        _audioSubscription = _audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (!_isRunning)
            {
                return;
            }

            _audioPump.Enqueue(buffer);
        }));

        _telemetry.OnNext(new SstvDecoderTelemetry(
            false,
            _workerProcess.Exists ? "Native SSTV worker ready to launch" : $"Worker missing: {_workerProcess.DisplayPath}",
            "Native SSTV sidecar",
            0,
            _configuration.Mode));
        _images.OnNext(new SstvImageFrame("No image captured yet", null));
    }

    public IObservable<SstvDecoderTelemetry> TelemetryStream => _telemetry;

    public IObservable<SstvImageFrame> ImageStream => _images;

    public async Task ConfigureAsync(SstvDecoderConfiguration configuration, CancellationToken ct)
    {
        _configuration = configuration;
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new
        {
            type = "configure",
            mode = configuration.Mode,
            frequencyLabel = configuration.FrequencyLabel,
        }, ct).ConfigureAwait(false);
    }

    public async Task ApplyPostReceiveSlantCorrectionAsync(CancellationToken ct)
    {
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new { type = "post_receive_slant" }, ct).ConfigureAwait(false);
    }

    public async Task ForceStartAsync(CancellationToken ct)
    {
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new { type = "force_start" }, ct).ConfigureAwait(false);
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
            _telemetry.OnNext(new SstvDecoderTelemetry(
                false,
                $"Worker missing: {_workerProcess.DisplayPath}",
                "Native SSTV sidecar",
                0,
                _configuration.Mode));
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
                _telemetry.OnNext(new SstvDecoderTelemetry(
                    root.TryGetProperty("isRunning", out var runningEl) && runningEl.GetBoolean(),
                    root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("activeWorker", out var workerEl) ? workerEl.GetString() ?? "Native SSTV sidecar" : "Native SSTV sidecar",
                    root.TryGetProperty("signalLevelPercent", out var levelEl) ? levelEl.GetInt32() : 0,
                    root.TryGetProperty("detectedMode", out var modeEl) ? modeEl.GetString() ?? _configuration.Mode : _configuration.Mode,
                    root.TryGetProperty("fskIdCallsign", out var fskEl) ? fskEl.GetString() : null));
            }
            else if (string.Equals(type, "image", StringComparison.OrdinalIgnoreCase))
            {
                _images.OnNext(new SstvImageFrame(
                    root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("imagePath", out var imageEl) ? imageEl.GetString() : null));
            }
        }
        catch (Exception ex)
        {
            _telemetry.OnNext(new SstvDecoderTelemetry(
                _isRunning,
                $"Decoder output parse error: {ex.Message}",
                "Native SSTV sidecar",
                0,
                _configuration.Mode));
        }

        return Task.CompletedTask;
    }

    private Task HandleStderrLineAsync(string line)
    {
        _telemetry.OnNext(new SstvDecoderTelemetry(
            _isRunning,
            $"Worker stderr: {line}",
            "Native SSTV sidecar",
            0,
            _configuration.Mode));
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
        _telemetry.OnNext(new SstvDecoderTelemetry(
            false,
            "Native SSTV worker exited",
            "Native SSTV sidecar",
            0,
            _configuration.Mode));
    }

    public void Dispose()
    {
        _isRunning = false;
        _audioSubscription.Dispose();
        _audioPump.Dispose();
        _workerProcess.Dispose();
    }
}
