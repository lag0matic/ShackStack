using System.Diagnostics;
using System.Text;
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
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly IDisposable _audioSubscription;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DecoderWorkerLaunch _workerLaunch;

    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private WefaxDecoderConfiguration _configuration = new("IOC 576 / 120 LPM", 576, 120, "NOAA Atlantic 12750.0 kHz USB-D", 0, 0);
    private bool _isRunning;

    public PythonWefaxDecoderHost(IAudioService audioService)
    {
        _audioService = audioService;
        _workerLaunch = BundledDecoderWorkerLocator.Resolve("wefax_sidecar_worker");

        _audioSubscription = _audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (!_isRunning)
            {
                return;
            }

            _ = SendAudioAsync(buffer);
        }));

        _telemetry.OnNext(new WefaxDecoderTelemetry(
            false,
            _workerLaunch.Exists ? "Python WeFAX worker ready to launch" : $"Worker missing: {_workerLaunch.DisplayPath}",
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
        if (_process is null || _process.HasExited)
        {
            return;
        }

        await SendMessageAsync(new { type = "stop" }, ct).ConfigureAwait(false);
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        if (_process is null || _process.HasExited)
        {
            return;
        }

        await SendMessageAsync(new { type = "reset" }, ct).ConfigureAwait(false);
    }

    private Task EnsureProcessAsync(CancellationToken ct)
    {
        if (!_workerLaunch.Exists)
        {
            _telemetry.OnNext(new WefaxDecoderTelemetry(
                false,
                $"Worker missing: {_workerLaunch.DisplayPath}",
                "Python WeFAX sidecar",
                0,
                0,
                0,
                0,
                _configuration.ModeLabel));
            return Task.CompletedTask;
        }

        if (_process is not null && !_process.HasExited && _stdin is not null)
        {
            return Task.CompletedTask;
        }

        var startInfo = BundledDecoderWorkerLocator.CreateStartInfo(_workerLaunch);

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        _process.Exited += OnWorkerExited;
        _process.Start();
        _stdin = _process.StandardInput;
        _stdin.AutoFlush = true;
        _stdoutTask = Task.Run(ReadStdoutLoopAsync, ct);
        _stderrTask = Task.Run(ReadStderrLoopAsync, ct);
        return Task.CompletedTask;
    }

    private async Task ReadStdoutLoopAsync()
    {
        if (_process is null)
        {
            return;
        }

        while (await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

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
        }
    }

    private async Task ReadStderrLoopAsync()
    {
        if (_process is null)
        {
            return;
        }

        while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            _telemetry.OnNext(new WefaxDecoderTelemetry(
                _isRunning,
                $"Worker stderr: {line}",
                "Python WeFAX sidecar",
                0,
                0,
                0,
                0,
                _configuration.ModeLabel));
        }
    }

    private async Task SendAudioAsync(AudioBuffer buffer)
    {
        if (_process is null || _process.HasExited || _stdin is null)
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
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task SendMessageAsync<T>(T payload, CancellationToken ct)
    {
        if (_stdin is null)
        {
            return;
        }

        var line = JsonSerializer.Serialize(payload, _jsonOptions);
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stdin.WriteLineAsync(line).ConfigureAwait(false);
            await _stdin.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

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
        DecoderHostProcessCleanup.Shutdown(_process, _stdin, _stdoutTask, _stderrTask, _writeGate);
        _writeGate.Dispose();
    }
}
