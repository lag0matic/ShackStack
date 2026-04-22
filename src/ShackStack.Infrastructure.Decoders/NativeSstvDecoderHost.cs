using System.Diagnostics;
using System.Text;
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
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly IDisposable _audioSubscription;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DecoderWorkerLaunch _workerLaunch;

    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private SstvDecoderConfiguration _configuration = new("Martin 1", "14.230 MHz USB", 0, 0);
    private bool _isRunning;

    public NativeSstvDecoderHost(IAudioService audioService)
    {
        _audioService = audioService;
        _workerLaunch = BundledDecoderWorkerLocator.Resolve("sstv_native_sidecar");

        _audioSubscription = _audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (!_isRunning)
            {
                return;
            }

            _ = SendAudioAsync(buffer);
        }));

        _telemetry.OnNext(new SstvDecoderTelemetry(
            false,
            _workerLaunch.Exists ? "Native SSTV worker ready to launch" : $"Worker missing: {_workerLaunch.DisplayPath}",
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
            manualSlant = configuration.ManualSlant,
            manualOffset = configuration.ManualOffset,
        }, ct).ConfigureAwait(false);
    }

    public async Task SetManualAlignmentAsync(int manualSlant, int manualOffset, CancellationToken ct)
    {
        _configuration = _configuration with
        {
            ManualSlant = manualSlant,
            ManualOffset = manualOffset,
        };
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new
        {
            type = "manual_alignment",
            manualSlant,
            manualOffset,
        }, ct).ConfigureAwait(false);
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
            _telemetry.OnNext(new SstvDecoderTelemetry(
                false,
                $"Worker missing: {_workerLaunch.DisplayPath}",
                "Native SSTV sidecar",
                0,
                _configuration.Mode));
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
                    _telemetry.OnNext(new SstvDecoderTelemetry(
                        root.TryGetProperty("isRunning", out var runningEl) && runningEl.GetBoolean(),
                        root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
                        root.TryGetProperty("activeWorker", out var workerEl) ? workerEl.GetString() ?? "Native SSTV sidecar" : "Native SSTV sidecar",
                        root.TryGetProperty("signalLevelPercent", out var levelEl) ? levelEl.GetInt32() : 0,
                        root.TryGetProperty("detectedMode", out var modeEl) ? modeEl.GetString() ?? _configuration.Mode : _configuration.Mode));
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

            _telemetry.OnNext(new SstvDecoderTelemetry(
                _isRunning,
                $"Worker stderr: {line}",
                "Native SSTV sidecar",
                0,
                _configuration.Mode));
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
        DecoderHostProcessCleanup.Shutdown(_process, _stdin, _stdoutTask, _stderrTask, _writeGate);
        _writeGate.Dispose();
    }
}
