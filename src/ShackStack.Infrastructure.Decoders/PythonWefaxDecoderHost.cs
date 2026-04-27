using System.Text.Json;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class PythonWefaxDecoderHost : IWefaxDecoderHost, IDisposable
{
    private readonly IAudioService _audioService;
    private readonly SimpleSubject<WefaxDecoderTelemetry> _telemetry = new();
    private readonly SimpleSubject<WefaxImageFrame> _images = new();
    private readonly IDisposable _audioSubscription;
    private readonly DecoderWorkerProcess _workerProcess;
    private readonly DecoderAudioPump _audioPump;

    private WefaxDecoderConfiguration _configuration = new(
        "IOC 576 / 120 LPM",
        576,
        120,
        "NOAA Atlantic 12750.0 kHz USB-D",
        0,
        0,
        1900,
        800,
        1500,
        "Medium",
        true,
        30,
        10,
        500,
        0.05,
        15,
        false,
        false,
        128,
        false,
        24,
        1);
    private bool _isRunning;

    public PythonWefaxDecoderHost(IAudioService audioService)
    {
        _audioService = audioService;
        _workerProcess = new DecoderWorkerProcess(BundledDecoderWorkerLocator.Resolve("wefax_sidecar_worker"));
        _audioPump = new DecoderAudioPump(SendAudioAsync, () => _isRunning);

        _audioSubscription = _audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (!_isRunning)
            {
                return;
            }

            _audioPump.Enqueue(buffer);
        }));

        _telemetry.OnNext(new WefaxDecoderTelemetry(
            false,
            _workerProcess.Exists ? "Python WeFAX worker ready to launch" : $"Worker missing: {_workerProcess.DisplayPath}",
            "Python WeFAX sidecar",
            0,
            0,
            0,
            0,
            _configuration.ModeLabel));
        _images.OnNext(new WefaxImageFrame("No WeFAX image captured yet", null));
    }

    public IObservable<WefaxDecoderTelemetry> TelemetryStream => _telemetry;

    public IObservable<WefaxImageFrame> ImageStream => _images;

    public async Task ConfigureAsync(WefaxDecoderConfiguration configuration, CancellationToken ct)
    {
        _configuration = configuration;
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new
        {
            type = "configure",
            modeLabel = configuration.ModeLabel,
            ioc = configuration.Ioc,
            lpm = configuration.Lpm,
            frequencyLabel = configuration.FrequencyLabel,
            manualSlant = configuration.ManualSlant,
            manualOffset = configuration.ManualOffset,
            centerHz = configuration.CenterHz,
            shiftHz = configuration.ShiftHz,
            maxRows = configuration.MaxRows,
            filterName = configuration.FilterName,
            autoAlign = configuration.AutoAlign,
            autoAlignAfterRows = configuration.AutoAlignAfterRows,
            autoAlignEveryRows = configuration.AutoAlignEveryRows,
            autoAlignStopRows = configuration.AutoAlignStopRows,
            correlationThreshold = configuration.CorrelationThreshold,
            correlationRows = configuration.CorrelationRows,
            invertImage = configuration.InvertImage,
            binaryImage = configuration.BinaryImage,
            binaryThreshold = configuration.BinaryThreshold,
            noiseRemoval = configuration.NoiseRemoval,
            noiseThreshold = configuration.NoiseThreshold,
            noiseMargin = configuration.NoiseMargin,
        }, ct).ConfigureAwait(false);
    }

    public async Task SetManualSlantAsync(int manualSlant, CancellationToken ct)
    {
        _configuration = _configuration with { ManualSlant = manualSlant };
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new
        {
            type = "manual_slant",
            manualSlant,
        }, ct).ConfigureAwait(false);
    }

    public async Task SetManualOffsetAsync(int manualOffset, CancellationToken ct)
    {
        _configuration = _configuration with { ManualOffset = manualOffset };
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new
        {
            type = "manual_offset",
            manualOffset,
        }, ct).ConfigureAwait(false);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new { type = "start" }, ct).ConfigureAwait(false);
        _isRunning = true;
    }

    public async Task StartNowAsync(CancellationToken ct)
    {
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new { type = "start_now" }, ct).ConfigureAwait(false);
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
            _telemetry.OnNext(new WefaxDecoderTelemetry(
                false,
                $"Worker missing: {_workerProcess.DisplayPath}",
                "Python WeFAX sidecar",
                0,
                0,
                0,
                0,
                _configuration.ModeLabel));
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
                _telemetry.OnNext(new WefaxDecoderTelemetry(
                    root.TryGetProperty("isRunning", out var runningEl) && runningEl.GetBoolean(),
                    root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("activeWorker", out var workerEl) ? workerEl.GetString() ?? "Python WeFAX sidecar" : "Python WeFAX sidecar",
                    root.TryGetProperty("linesReceived", out var linesEl) ? linesEl.GetInt32() : 0,
                    root.TryGetProperty("alignedOffset", out var offsetEl) ? offsetEl.GetInt32() : 0,
                    root.TryGetProperty("startConfidence", out var startEl) ? startEl.GetDouble() : 0,
                    root.TryGetProperty("stopConfidence", out var stopEl) ? stopEl.GetDouble() : 0,
                    root.TryGetProperty("modeLabel", out var modeEl) ? modeEl.GetString() ?? _configuration.ModeLabel : _configuration.ModeLabel));
            }
            else if (string.Equals(type, "image", StringComparison.OrdinalIgnoreCase))
            {
                _images.OnNext(new WefaxImageFrame(
                    root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("imagePath", out var imageEl) ? imageEl.GetString() : null));
            }
        }
        catch (Exception ex)
        {
            _telemetry.OnNext(new WefaxDecoderTelemetry(
                _isRunning,
                $"Decoder output parse error: {ex.Message}",
                "Python WeFAX sidecar",
                0,
                0,
                0,
                0,
                _configuration.ModeLabel));
        }

        return Task.CompletedTask;
    }

    private Task HandleStderrLineAsync(string line)
    {
        _telemetry.OnNext(new WefaxDecoderTelemetry(
            _isRunning,
            $"Worker stderr: {line}",
            "Python WeFAX sidecar",
            0,
            0,
            0,
            0,
            _configuration.ModeLabel));
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
        _telemetry.OnNext(new WefaxDecoderTelemetry(
            false,
            "Python WeFAX worker exited",
            "Python WeFAX sidecar",
            0,
            0,
            0,
            0,
            _configuration.ModeLabel));
    }

    public void Dispose()
    {
        _isRunning = false;
        _audioSubscription.Dispose();
        _audioPump.Dispose();
        _workerProcess.Dispose();
    }
}
