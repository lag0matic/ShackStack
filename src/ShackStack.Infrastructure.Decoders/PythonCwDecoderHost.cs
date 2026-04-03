using System.Diagnostics;
using System.Text;
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
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly IDisposable _audioSubscription;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _workerPath;

    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private CwDecoderConfiguration _configuration = new(700, 20, "Sidecar");
    private bool _isRunning;

    public PythonCwDecoderHost(IAudioService audioService)
    {
        _audioService = audioService;
        _workerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "ShackStack - Codex",
            "Tools",
            "cw_sidecar_worker.py");

        _audioSubscription = _audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (!_isRunning)
            {
                return;
            }

            _ = SendAudioAsync(buffer);
        }));

        _telemetry.OnNext(new CwDecoderTelemetry(
            false,
            File.Exists(_workerPath) ? "Python CW worker ready to launch" : $"Worker missing: {_workerPath}",
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
        if (!File.Exists(_workerPath))
        {
            _telemetry.OnNext(new CwDecoderTelemetry(
                false,
                $"Worker missing: {_workerPath}",
                "Python CW sidecar",
                0.0,
                _configuration.PitchHz,
                _configuration.Wpm));
            return Task.CompletedTask;
        }

        if (_process is not null && !_process.HasExited && _stdin is not null)
        {
            return Task.CompletedTask;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{_workerPath}\"",
            WorkingDirectory = Path.GetDirectoryName(_workerPath)!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

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

            _telemetry.OnNext(new CwDecoderTelemetry(
                _isRunning,
                $"Worker stderr: {line}",
                "Python CW sidecar",
                0.0,
                _configuration.PitchHz,
                _configuration.Wpm));
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

        try
        {
            if (_process is not null && !_process.HasExited)
            {
                _ = SendMessageAsync(new { type = "shutdown" }, CancellationToken.None);
                if (!_process.WaitForExit(500))
                {
                    _process.Kill(true);
                }
            }
        }
        catch
        {
        }

        _stdin?.Dispose();
        _process?.Dispose();
        _writeGate.Dispose();
    }
}
