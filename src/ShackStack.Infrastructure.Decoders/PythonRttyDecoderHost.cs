using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class PythonRttyDecoderHost : IRttyDecoderHost, IDisposable
{
    private readonly IAudioService _audioService;
    private readonly SimpleSubject<RttyDecoderTelemetry> _telemetry = new();
    private readonly SimpleSubject<RttyDecodeChunk> _decode = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly IDisposable _audioSubscription;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DecoderWorkerLaunch _workerLaunch;

    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private RttyDecoderConfiguration _configuration = new("170 Hz / 45.45 baud", 170, 45.45, "14.080 MHz USB");
    private bool _isRunning;

    public PythonRttyDecoderHost(IAudioService audioService)
    {
        _audioService = audioService;
        _workerLaunch = BundledDecoderWorkerLocator.Resolve("rtty_sidecar_worker");

        _audioSubscription = _audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (!_isRunning)
            {
                return;
            }

            _ = SendAudioAsync(buffer);
        }));

        _telemetry.OnNext(new RttyDecoderTelemetry(
            false,
            _workerLaunch.Exists ? "Python RTTY worker ready to launch" : $"Worker missing: {_workerLaunch.DisplayPath}",
            "Python RTTY sidecar",
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
            _telemetry.OnNext(new RttyDecoderTelemetry(
                false,
                $"Worker missing: {_workerLaunch.DisplayPath}",
                "Python RTTY sidecar",
                0,
                _configuration.ShiftHz,
                _configuration.BaudRate,
                _configuration.ProfileLabel));
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
                    _telemetry.OnNext(new RttyDecoderTelemetry(
                        root.TryGetProperty("isRunning", out var runningEl) && runningEl.GetBoolean(),
                        root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
                        root.TryGetProperty("activeWorker", out var workerEl) ? workerEl.GetString() ?? "Python RTTY sidecar" : "Python RTTY sidecar",
                        root.TryGetProperty("signalLevelPercent", out var levelEl) ? levelEl.GetInt32() : 0,
                        root.TryGetProperty("estimatedShiftHz", out var shiftEl) ? shiftEl.GetInt32() : _configuration.ShiftHz,
                        root.TryGetProperty("estimatedBaud", out var baudEl) ? baudEl.GetDouble() : _configuration.BaudRate,
                        root.TryGetProperty("profileLabel", out var profileEl) ? profileEl.GetString() ?? _configuration.ProfileLabel : _configuration.ProfileLabel));
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
                    "Python RTTY sidecar",
                    0,
                    _configuration.ShiftHz,
                    _configuration.BaudRate,
                    _configuration.ProfileLabel));
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

            _telemetry.OnNext(new RttyDecoderTelemetry(
                _isRunning,
                $"Worker stderr: {line}",
                "Python RTTY sidecar",
                0,
                _configuration.ShiftHz,
                _configuration.BaudRate,
                _configuration.ProfileLabel));
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
        _telemetry.OnNext(new RttyDecoderTelemetry(
            false,
            "Python RTTY worker exited",
            "Python RTTY sidecar",
            0,
            _configuration.ShiftHz,
            _configuration.BaudRate,
            _configuration.ProfileLabel));
    }

    public void Dispose()
    {
        _isRunning = false;
        _audioSubscription.Dispose();
        try
        {
            _stdin?.Dispose();
        }
        catch
        {
        }

        try
        {
            if (_process is not null && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        _process?.Dispose();
        _writeGate.Dispose();
    }
}
