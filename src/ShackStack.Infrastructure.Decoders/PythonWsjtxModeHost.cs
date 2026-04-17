using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Channels;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class PythonWsjtxModeHost : IWsjtxModeHost, IDisposable
{
    private const int WsjtxInputSampleRate = 12_000;
    private static readonly string[] IgnoredWorkerStderrFragments =
    [
        "PyInstaller\\loader\\pyimod02_importers.py:384",
        "pkg_resources is deprecated as an API",
        "https://setuptools.pypa.io/en/latest/pkg_resources.html",
        "The pkg_resources package is slated for removal as early as 2025-11-30",
        "Refrain from using this package or pin to Setuptools<81."
    ];
    private readonly IAudioService _audioService;
    private readonly IClockDisciplineService _clockDisciplineService;
    private readonly SimpleSubject<WsjtxModeTelemetry> _telemetry = new();
    private readonly SimpleSubject<WsjtxDecodeMessage> _decode = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly IDisposable _audioSubscription;
    private readonly DecoderWorkerLaunch _workerLaunch;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WsjtxExternalWaveformPort _waveformPort = WsjtxExternalWaveformPort.CreateDefault();
    private readonly object _sync = new();
    private readonly Channel<AudioBuffer> _audioQueue = Channel.CreateBounded<AudioBuffer>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest,
    });
    private readonly CancellationTokenSource _audioPumpCts = new();
    private readonly Task _audioPumpTask;
    private readonly string _statusLogPath;
    private readonly bool _statusLoggingEnabled;
    private static readonly Regex DecodeSummaryRegex = new(@"^(?<mode>[A-Z0-9/]+)\s+cycle\s+\d+:\s+(?<decoder>[^\|]+)\|\s+(?<decodes>\d+)\s+decodes\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private WsjtxModeConfiguration _configuration = new("FT8", "20m FT8 14.074 MHz USB-D", true, false, false, false, false, 15.0, true, string.Empty, string.Empty, false);
    private string _workerStatus = "WSJT sidecar ready";
    private string _lastHealthyStatus = "WSJT sidecar ready";
    private string? _lastLoggedStatus;
    private string _activeWorker = "WSJT sidecar";
    private int _decodeCount;
    private readonly Dictionary<string, DateTime> _recentDecodeKeys = new(StringComparer.Ordinal);
    private bool _isTransmitArmed;
    private bool _autoSequenceEnabled = true;
    private bool _isRunning;

    public PythonWsjtxModeHost(IAudioService audioService, IClockDisciplineService clockDisciplineService)
    {
        _audioService = audioService;
        _clockDisciplineService = clockDisciplineService;
        _workerLaunch = BundledDecoderWorkerLocator.ResolvePreferred(
            "wsjtx_gpl_sidecar",
            "wsjtx_sidecar_worker");
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "ShackStack",
            "logs");
        _statusLoggingEnabled = IsStatusLoggingEnabled();
        if (_statusLoggingEnabled)
        {
            Directory.CreateDirectory(logDirectory);
        }
        _statusLogPath = Path.Combine(logDirectory, "wsjtx-status.log");
        _audioPumpTask = Task.Run(() => AudioPumpLoopAsync(_audioPumpCts.Token));

        _audioSubscription = _audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (!_isRunning)
            {
                return;
            }

            var copiedSamples = new float[buffer.Samples.Length];
            Array.Copy(buffer.Samples, copiedSamples, copiedSamples.Length);
            _audioQueue.Writer.TryWrite(new AudioBuffer(copiedSamples, buffer.SampleRate, buffer.Channels));
        }));

        _activeWorker = _workerLaunch.DisplayPath;
        PublishHealthyTelemetry(_workerLaunch.Exists
            ? $"WSJT worker ready: {_workerLaunch.DisplayPath}"
            : $"Worker missing: {_workerLaunch.DisplayPath}");
    }

    public IObservable<WsjtxModeTelemetry> TelemetryStream => _telemetry;

    public IObservable<WsjtxDecodeMessage> DecodeStream => _decode;

    public async Task ConfigureAsync(WsjtxModeConfiguration configuration, CancellationToken ct)
    {
        var restartForModeChange = false;
        lock (_sync)
        {
            restartForModeChange = !string.Equals(_configuration.ModeLabel, configuration.ModeLabel, StringComparison.OrdinalIgnoreCase);
            _configuration = configuration;
            _autoSequenceEnabled = configuration.AutoSequenceEnabled;
            _isTransmitArmed = configuration.CallCQEnabled;
        }

        if (restartForModeChange)
        {
            await RestartWorkerAsync().ConfigureAwait(false);
        }

        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new
        {
            type = "configure",
            modeLabel = configuration.ModeLabel,
            frequencyLabel = configuration.FrequencyLabel,
            autoSequenceEnabled = configuration.AutoSequenceEnabled,
            callCQEnabled = configuration.CallCQEnabled,
            ft8SubtractionEnabled = configuration.Ft8SubtractionEnabled,
            ft8ApEnabled = configuration.Ft8ApEnabled,
            ft8OsdEnabled = configuration.Ft8OsdEnabled,
            cycleLengthSeconds = configuration.CycleLengthSeconds,
            requiresAccurateClock = configuration.RequiresAccurateClock,
            stationCallsign = configuration.StationCallsign,
            stationGridSquare = configuration.StationGridSquare,
            transmitFirstEnabled = configuration.TransmitFirstEnabled,
        }, ct).ConfigureAwait(false);
        PublishHealthyTelemetry($"WSJT-style sidecar configured for {configuration.ModeLabel}");
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        WsjtxModeConfiguration configuration;
        lock (_sync)
        {
            configuration = _configuration;
        }

        await SendMessageAsync(new
        {
            type = "start",
            modeLabel = configuration.ModeLabel,
            frequencyLabel = configuration.FrequencyLabel,
            ft8SubtractionEnabled = configuration.Ft8SubtractionEnabled,
            ft8ApEnabled = configuration.Ft8ApEnabled,
            ft8OsdEnabled = configuration.Ft8OsdEnabled,
            stationCallsign = configuration.StationCallsign,
            stationGridSquare = configuration.StationGridSquare,
        }, ct).ConfigureAwait(false);
        _isRunning = true;
        StartLoop(ct);
        PublishHealthyTelemetry($"Listening for {configuration.ModeLabel} weak-signal traffic");
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _isRunning = false;
        StopLoop();

        if (_process is null || _process.HasExited)
        {
            PublishHealthyTelemetry("Weak-signal digital receive stopped");
            return;
        }

        await SendMessageAsync(new { type = "stop" }, ct).ConfigureAwait(false);
        PublishHealthyTelemetry("Weak-signal digital receive stopped");
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        _decodeCount = 0;
        lock (_sync)
        {
            _recentDecodeKeys.Clear();
        }
        if (_process is not null && !_process.HasExited)
        {
            await SendMessageAsync(new { type = "reset" }, ct).ConfigureAwait(false);
        }

        PublishHealthyTelemetry("Weak-signal session reset");
    }

    public Task<WsjtxPreparedTransmitResult> PrepareTransmitAsync(string modeLabel, string messageText, int txAudioFrequencyHz, CancellationToken ct)
    {
        WsjtxModeConfiguration configuration;
        lock (_sync)
        {
            configuration = _configuration;
        }

        return _waveformPort.PrepareAsync(modeLabel, messageText, txAudioFrequencyHz, configuration.CycleLengthSeconds, ct);
    }

    private Task EnsureProcessAsync(CancellationToken ct)
    {
        if (!_workerLaunch.Exists)
        {
            PublishHealthyTelemetry($"Worker missing: {_workerLaunch.DisplayPath}");
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

    private async Task RestartWorkerAsync()
    {
        _isRunning = false;
        StopLoop();
        _audioQueue.Writer.TryWrite(new AudioBuffer([], WsjtxInputSampleRate, 1));
        lock (_sync)
        {
            _recentDecodeKeys.Clear();
        }

        var process = _process;
        var stdin = _stdin;
        var stdoutTask = _stdoutTask;
        var stderrTask = _stderrTask;

        _process = null;
        _stdin = null;
        _stdoutTask = null;
        _stderrTask = null;

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited && stdin is not null)
            {
                try
                {
                    await stdin.WriteLineAsync("{\"type\":\"shutdown\"}").ConfigureAwait(false);
                    await stdin.FlushAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                if (!process.WaitForExit(500))
                {
                    process.Kill(true);
                }
            }
        }
        catch
        {
        }
        finally
        {
            stdin?.Dispose();
            process.Dispose();
        }

        if (stdoutTask is not null)
        {
            try { await stdoutTask.ConfigureAwait(false); } catch { }
        }

        if (stderrTask is not null)
        {
            try { await stderrTask.ConfigureAwait(false); } catch { }
        }
    }

    private void StartLoop(CancellationToken ct)
    {
        if (_loopTask is not null && !_loopTask.IsCompleted)
        {
            return;
        }

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => TelemetryLoopAsync(_loopCts.Token), _loopCts.Token);
    }

    private void StopLoop()
    {
        if (_loopCts is null)
        {
            return;
        }

        _loopCts.Cancel();
        _loopCts.Dispose();
        _loopCts = null;
        _loopTask = null;
    }

    private async Task TelemetryLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            PublishTelemetry(_workerStatus);
        }
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
                    var telemetryMode = root.TryGetProperty("modeLabel", out var telemetryModeEl)
                        ? telemetryModeEl.GetString() ?? _configuration.ModeLabel
                        : _configuration.ModeLabel;
                    lock (_sync)
                    {
                        _workerStatus = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? _workerStatus : _workerStatus;
                        if (!string.Equals(telemetryMode, _configuration.ModeLabel, StringComparison.OrdinalIgnoreCase))
                        {
                            _workerStatus = $"Mode mismatch: host {_configuration.ModeLabel}, worker {telemetryMode} | {_workerStatus}";
                        }
                        if (!_workerStatus.StartsWith("Worker stderr:", StringComparison.OrdinalIgnoreCase))
                        {
                            _lastHealthyStatus = _workerStatus;
                        }
                        _activeWorker = root.TryGetProperty("activeWorker", out var workerEl) ? workerEl.GetString() ?? _activeWorker : _activeWorker;
                        _decodeCount = root.TryGetProperty("decodeCount", out var decodeEl) ? decodeEl.GetInt32() : _decodeCount;
                        _autoSequenceEnabled = root.TryGetProperty("autoSequenceEnabled", out var autoSeqEl) ? autoSeqEl.GetBoolean() : _autoSequenceEnabled;
                        _isTransmitArmed = root.TryGetProperty("isTransmitArmed", out var txEl) ? txEl.GetBoolean() : _isTransmitArmed;
                    }
                    PublishTelemetry(_workerStatus);
                }
                else if (string.Equals(type, "decode", StringComparison.OrdinalIgnoreCase))
                {
                    var messageText = root.TryGetProperty("messageText", out var textEl) ? textEl.GetString() ?? string.Empty : string.Empty;
                    var decodedMode = root.TryGetProperty("modeLabel", out var decodedModeEl) ? decodedModeEl.GetString() ?? _configuration.ModeLabel : _configuration.ModeLabel;
                    var isDirectedToMe = root.TryGetProperty("isDirectedToMe", out var directEl) && directEl.GetBoolean();
                    var isCq = root.TryGetProperty("isCq", out var cqEl) && cqEl.GetBoolean();
                    if (!isDirectedToMe)
                    {
                        var myCall = _configuration.StationCallsign?.Trim().ToUpperInvariant();
                        if (!string.IsNullOrWhiteSpace(myCall))
                        {
                            var tokens = messageText
                                .ToUpperInvariant()
                                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            isDirectedToMe = tokens.Skip(1).Take(2).Any(t => t == myCall);
                        }
                    }

                    var message = new WsjtxDecodeMessage(
                        TimestampUtc: root.TryGetProperty("timestampUtc", out var timeEl) && timeEl.TryGetDateTime(out var parsedTime)
                            ? DateTime.SpecifyKind(parsedTime, DateTimeKind.Utc)
                            : DateTime.UtcNow,
                        ModeLabel: decodedMode,
                        FrequencyOffsetHz: root.TryGetProperty("frequencyOffsetHz", out var hzEl) ? hzEl.GetInt32() : 0,
                        SnrDb: root.TryGetProperty("snrDb", out var snrEl) ? snrEl.GetInt32() : 0,
                        DeltaTimeSeconds: root.TryGetProperty("deltaTimeSeconds", out var dtEl) ? dtEl.GetDouble() : 0,
                        MessageText: messageText,
                        Confidence: root.TryGetProperty("confidence", out var confEl) ? confEl.GetDouble() : 0,
                        IsDirectedToMe: isDirectedToMe,
                        IsCq: isCq);

                    if (IsDuplicateDecode(message))
                    {
                        continue;
                    }

                    _decode.OnNext(message);
                    lock (_sync)
                    {
                        _decodeCount += 1;
                        if (!string.Equals(decodedMode, _configuration.ModeLabel, StringComparison.OrdinalIgnoreCase))
                        {
                            _workerStatus = $"Mode mismatch: host {_configuration.ModeLabel}, worker {decodedMode}";
                        }
                        if (_workerStatus.StartsWith("Worker stderr:", StringComparison.OrdinalIgnoreCase))
                        {
                            _workerStatus = _lastHealthyStatus;
                        }
                    }
                    PublishTelemetry(_workerStatus);
                }
            }
            catch (Exception ex)
            {
                if (_statusLoggingEnabled)
                {
                    PublishTelemetry($"Decoder output parse error: {ex.Message}");
                }
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

            if (IsIgnorableWorkerStderr(line))
            {
                continue;
            }

            PublishTelemetry($"Worker stderr: {line}");
        }
    }

    private static bool IsIgnorableWorkerStderr(string line)
    {
        foreach (var fragment in IgnoredWorkerStderrFragments)
        {
            if (line.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task SendAudioAsync(AudioBuffer buffer)
    {
        if (_process is null || _process.HasExited || _stdin is null)
        {
            return;
        }

        var adapted = AdaptForWsjtx(buffer);
        if (adapted.Samples.Length == 0)
        {
            return;
        }

        var bytes = new byte[adapted.Samples.Length * sizeof(float)];
        Buffer.BlockCopy(adapted.Samples, 0, bytes, 0, bytes.Length);
        await SendMessageAsync(new
        {
            type = "audio",
            sampleRate = adapted.SampleRate,
            channels = adapted.Channels,
            samples = Convert.ToBase64String(bytes),
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private static AudioBuffer AdaptForWsjtx(AudioBuffer buffer)
    {
        if (buffer.Samples.Length == 0 || buffer.SampleRate <= 0 || buffer.Channels <= 0)
        {
            return new AudioBuffer([], WsjtxInputSampleRate, 1);
        }

        var mono = buffer.Channels == 1
            ? buffer.Samples
            : DownmixToMono(buffer.Samples, buffer.Channels);

        if (buffer.SampleRate == WsjtxInputSampleRate)
        {
            return new AudioBuffer(mono, WsjtxInputSampleRate, 1);
        }

        var resampled = ResampleLinear(mono, buffer.SampleRate, WsjtxInputSampleRate);
        return new AudioBuffer(resampled, WsjtxInputSampleRate, 1);
    }

    private bool IsDuplicateDecode(WsjtxDecodeMessage message)
    {
        var normalizedText = message.MessageText.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return true;
        }

        var roundedHz = (int)Math.Round(message.FrequencyOffsetHz / 25.0, MidpointRounding.AwayFromZero) * 25;
        var key = $"{message.ModeLabel}|{roundedHz}|{normalizedText}";
        var now = message.TimestampUtc;
        var cutoff = now.AddSeconds(-GetDuplicateWindowSeconds(message.ModeLabel));

        lock (_sync)
        {
            if (_recentDecodeKeys.Count > 0)
            {
                var expired = _recentDecodeKeys
                    .Where(pair => pair.Value < cutoff)
                    .Select(pair => pair.Key)
                    .ToArray();
                foreach (var expiredKey in expired)
                {
                    _recentDecodeKeys.Remove(expiredKey);
                }
            }

            if (_recentDecodeKeys.TryGetValue(key, out var seenAt) && seenAt >= cutoff)
            {
                return true;
            }

            _recentDecodeKeys[key] = now;
            return false;
        }
    }

    private static double GetDuplicateWindowSeconds(string modeLabel) => modeLabel.Trim().ToUpperInvariant() switch
    {
        "WSPR" or "FST4W" => 180,
        "JT65" or "JT9" or "JT4" => 120,
        _ => 90,
    };

    private static float[] DownmixToMono(float[] interleaved, int channels)
    {
        var frameCount = interleaved.Length / channels;
        var mono = new float[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var offset = frame * channels;
            double sum = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += interleaved[offset + channel];
            }

            mono[frame] = (float)(sum / channels);
        }

        return mono;
    }

    private static float[] ResampleLinear(float[] samples, int sourceSampleRate, int targetSampleRate)
    {
        if (samples.Length == 0 || sourceSampleRate <= 0 || targetSampleRate <= 0)
        {
            return [];
        }

        if (sourceSampleRate == targetSampleRate)
        {
            return samples;
        }

        var outputLength = (int)Math.Round(samples.Length * (targetSampleRate / (double)sourceSampleRate), MidpointRounding.AwayFromZero);
        if (outputLength <= 1)
        {
            return [];
        }

        var output = new float[outputLength];
        var step = sourceSampleRate / (double)targetSampleRate;
        for (var i = 0; i < outputLength; i++)
        {
            var sourceIndex = i * step;
            var left = (int)Math.Floor(sourceIndex);
            var right = Math.Min(left + 1, samples.Length - 1);
            var fraction = sourceIndex - left;
            if (left >= samples.Length)
            {
                output[i] = samples[^1];
                continue;
            }

            output[i] = (float)((samples[left] * (1.0 - fraction)) + (samples[right] * fraction));
        }

        return output;
    }

    private async Task AudioPumpLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _audioQueue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_audioQueue.Reader.TryRead(out var buffer))
                {
                    if (!_isRunning)
                    {
                        continue;
                    }

                    await SendAudioAsync(buffer).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
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

    private void PublishTelemetry(string status)
    {
        WsjtxModeConfiguration configuration;
        string activeWorker;
        int decodeCount;
        bool autoSequenceEnabled;
        bool isTransmitArmed;
        string displayStatus;
        lock (_sync)
        {
            configuration = _configuration;
            activeWorker = _activeWorker;
            decodeCount = _decodeCount;
            autoSequenceEnabled = _autoSequenceEnabled;
            isTransmitArmed = _isTransmitArmed;
            displayStatus = NormalizeStatusForDisplay(_configuration.ModeLabel, status);
            _workerStatus = displayStatus;
        }

        LogStatusIfChanged(configuration.ModeLabel, status);

        var cycleLength = configuration.CycleLengthSeconds > 0 ? configuration.CycleLengthSeconds : 15.0;
        var utcNow = DateTimeOffset.UtcNow;
        var cyclePosition = utcNow.ToUnixTimeMilliseconds() / (cycleLength * 1000.0);
        var secondsToNextCycle = cycleLength - ((cyclePosition - Math.Floor(cyclePosition)) * cycleLength);
        if (secondsToNextCycle >= cycleLength)
        {
            secondsToNextCycle = 0;
        }

        var clock = _clockDisciplineService.Current;
        var clockStatus = configuration.RequiresAccurateClock
            ? $"{clock.Status} | Source {clock.SourceLabel}"
            : $"Clock source {clock.SourceLabel}";

        _telemetry.OnNext(new WsjtxModeTelemetry(
            _isRunning,
            displayStatus,
            activeWorker,
            configuration.ModeLabel,
            clockStatus,
            clock.IsSynchronized,
            clock.OffsetMs,
            cycleLength,
            secondsToNextCycle,
            decodeCount,
            autoSequenceEnabled,
            isTransmitArmed));
    }

    private void LogStatusIfChanged(string modeLabel, string status)
    {
        if (!_statusLoggingEnabled)
        {
            return;
        }

        lock (_sync)
        {
            if (string.Equals(_lastLoggedStatus, status, StringComparison.Ordinal))
            {
                return;
            }

            _lastLoggedStatus = status;
        }

        try
        {
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{modeLabel}] {status}";
            File.AppendAllText(_statusLogPath, line + Environment.NewLine);
        }
        catch
        {
            // Status logging is best-effort and must never interfere with live decode telemetry.
        }
    }

    private static bool IsStatusLoggingEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("SHACKSTACK_WSJTX_STATUS_LOG");
        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeStatusForDisplay(string modeLabel, string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return $"{modeLabel} decoder ready";
        }

        if (status.StartsWith("WSJT worker ready:", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("WSJT-style sidecar configured for", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Configured ", StringComparison.OrdinalIgnoreCase))
        {
            return $"{modeLabel} decoder ready";
        }

        if (status.StartsWith("Listening for ", StringComparison.OrdinalIgnoreCase))
        {
            return $"Listening for {modeLabel}";
        }

        if (status.StartsWith("WSJT-X GPL sidecar ready", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("WSJT-X GPL sidecar started", StringComparison.OrdinalIgnoreCase))
        {
            return $"{modeLabel} decoder active";
        }

        if (status.Contains("waiting for cycle boundary", StringComparison.OrdinalIgnoreCase))
        {
            return $"{modeLabel} waiting for cycle boundary";
        }

        var decodeSummary = DecodeSummaryRegex.Match(status);
        if (decodeSummary.Success)
        {
            var summaryMode = decodeSummary.Groups["mode"].Value.ToUpperInvariant();
            var decodes = int.TryParse(decodeSummary.Groups["decodes"].Value, out var parsedDecodes) ? parsedDecodes : 0;
            return decodes > 0
                ? $"{summaryMode} receiving decodes"
                : $"{summaryMode} sync open";
        }

        if (status.StartsWith("Worker stderr:", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("WSJT-X GPL sidecar error:", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Decoder output parse error:", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Mode mismatch:", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Worker missing:", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("WSJT worker exited:", StringComparison.OrdinalIgnoreCase))
        {
            return status;
        }

        if (status.StartsWith("Weak-signal digital receive stopped", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Weak-signal session reset", StringComparison.OrdinalIgnoreCase))
        {
            return status;
        }

        return $"{modeLabel} decoder active";
    }

    private void PublishHealthyTelemetry(string status)
    {
        lock (_sync)
        {
            _lastHealthyStatus = status;
        }

        PublishTelemetry(status);
    }

    private void OnWorkerExited(object? sender, EventArgs e)
    {
        _isRunning = false;
        StopLoop();
        PublishTelemetry($"WSJT worker exited: {_workerLaunch.DisplayPath}");
    }

    public void Dispose()
    {
        _isRunning = false;
        StopLoop();
        _audioPumpCts.Cancel();
        _audioQueue.Writer.TryComplete();
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
        _audioPumpCts.Dispose();
        _writeGate.Dispose();
    }
}
