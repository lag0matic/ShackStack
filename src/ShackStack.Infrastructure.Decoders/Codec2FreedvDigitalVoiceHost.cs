using System.Text.Json;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class Codec2FreedvDigitalVoiceHost : IFreedvDigitalVoiceHost, IDisposable
{
    private readonly SimpleSubject<FreedvDigitalVoiceTelemetry> _telemetry = new();
    private readonly SimpleSubject<Pcm16AudioClip> _speech = new();
    private readonly IDisposable _audioSubscription;
    private readonly IDisposable _micSubscription;
    private readonly DecoderWorkerProcess _workerProcess;
    private readonly DecoderAudioPump _audioPump;
    private readonly DecoderAudioPump _speechPump;
    private readonly IAudioService _audioService;

    private FreedvDigitalVoiceConfiguration _configuration = new("700D", "Current radio frequency", true);
    private AudioRoute? _transmitRoute;
    private bool _isRunning;
    private bool _isTransmitting;

    public Codec2FreedvDigitalVoiceHost(IAudioService audioService)
    {
        _audioService = audioService;
        _workerProcess = new DecoderWorkerProcess(BundledDecoderWorkerLocator.Resolve("freedv_codec2_sidecar"));
        _audioPump = new DecoderAudioPump(SendAudioAsync, () => _isRunning);
        _speechPump = new DecoderAudioPump(SendSpeechAsync, () => _isTransmitting);
        _audioSubscription = audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (_isRunning)
            {
                _audioPump.Enqueue(buffer);
            }
        }));
        _micSubscription = audioService.MicStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (_isTransmitting)
            {
                _speechPump.Enqueue(buffer);
            }
        }));

        _telemetry.OnNext(new FreedvDigitalVoiceTelemetry(
            false,
            _workerProcess.Exists
                ? "Codec2 FreeDV worker ready to launch"
                : $"Worker missing: {_workerProcess.DisplayPath}",
            "Codec2 FreeDV sidecar",
            _configuration.ModeLabel,
            0,
            0,
            0,
            8000,
            8000,
            false));
    }

    public IObservable<FreedvDigitalVoiceTelemetry> TelemetryStream => _telemetry;

    public IObservable<Pcm16AudioClip> SpeechStream => _speech;

    public async Task ConfigureAsync(FreedvDigitalVoiceConfiguration configuration, CancellationToken ct)
    {
        _configuration = configuration;
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new
        {
            type = "configure",
            modeLabel = configuration.ModeLabel,
            frequencyLabel = configuration.FrequencyLabel,
            useCurrentRadioFrequency = configuration.UseCurrentRadioFrequency,
            rxFrequencyOffsetHz = configuration.RxFrequencyOffsetHz,
            transmitCallsign = configuration.TransmitCallsign,
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

    public async Task StartTransmitAsync(AudioRoute route, CancellationToken ct)
    {
        _transmitRoute = route;
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new { type = "startTx" }, ct).ConfigureAwait(false);
        _isTransmitting = true;
        await _audioService.StartMicCaptureAsync(route, ct).ConfigureAwait(false);
    }

    public async Task StopTransmitAsync(CancellationToken ct)
    {
        _isTransmitting = false;
        if (_workerProcess.IsStarted)
        {
            await SendMessageAsync(new { type = "stopTx" }, ct).ConfigureAwait(false);
        }

        await _audioService.StopTransmitAsync(ct).ConfigureAwait(false);
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
            _telemetry.OnNext(new FreedvDigitalVoiceTelemetry(
                false,
                $"Worker missing: {_workerProcess.DisplayPath}",
                "Codec2 FreeDV sidecar",
                _configuration.ModeLabel,
                0,
                0,
                0,
                8000,
                8000,
                false));
            return Task.CompletedTask;
        }

        return _workerProcess.EnsureStartedAsync(HandleStdoutLineAsync, HandleStderrLineAsync, () => OnWorkerExited(null, EventArgs.Empty), ct);
    }

    private async Task HandleStdoutLineAsync(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            if (string.Equals(type, "telemetry", StringComparison.OrdinalIgnoreCase))
            {
                _telemetry.OnNext(new FreedvDigitalVoiceTelemetry(
                    root.TryGetProperty("isRunning", out var runningEl) && runningEl.GetBoolean(),
                    root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("activeWorker", out var workerEl) ? workerEl.GetString() ?? "Codec2 FreeDV sidecar" : "Codec2 FreeDV sidecar",
                    root.TryGetProperty("modeLabel", out var modeEl) ? modeEl.GetString() ?? _configuration.ModeLabel : _configuration.ModeLabel,
                    root.TryGetProperty("signalLevelPercent", out var levelEl) ? levelEl.GetInt32() : 0,
                    root.TryGetProperty("syncPercent", out var syncEl) ? syncEl.GetInt32() : 0,
                    root.TryGetProperty("snrDb", out var snrEl) ? snrEl.GetDouble() : 0,
                    root.TryGetProperty("speechSampleRate", out var speechRateEl) ? speechRateEl.GetInt32() : 8000,
                    root.TryGetProperty("modemSampleRate", out var modemRateEl) ? modemRateEl.GetInt32() : 8000,
                    root.TryGetProperty("isCodec2RuntimeLoaded", out var loadedEl) && loadedEl.GetBoolean(),
                    root.TryGetProperty("radeCallsign", out var callsignEl) ? callsignEl.GetString() ?? string.Empty : string.Empty));
            }
            else if (string.Equals(type, "speech", StringComparison.OrdinalIgnoreCase))
            {
                var sampleRate = root.TryGetProperty("sampleRate", out var rateEl) ? rateEl.GetInt32() : 8000;
                var channels = root.TryGetProperty("channels", out var channelsEl) ? channelsEl.GetInt32() : 1;
                var bytes = root.TryGetProperty("pcm16", out var pcmEl)
                    ? Convert.FromBase64String(pcmEl.GetString() ?? string.Empty)
                    : [];
                if (bytes.Length > 0)
                {
                    _speech.OnNext(new Pcm16AudioClip(bytes, sampleRate, channels));
                }
            }
            else if (string.Equals(type, "modem", StringComparison.OrdinalIgnoreCase))
            {
                var sampleRate = root.TryGetProperty("sampleRate", out var rateEl) ? rateEl.GetInt32() : 8000;
                var channels = root.TryGetProperty("channels", out var channelsEl) ? channelsEl.GetInt32() : 1;
                var bytes = root.TryGetProperty("pcm16", out var pcmEl)
                    ? Convert.FromBase64String(pcmEl.GetString() ?? string.Empty)
                    : [];
                if (bytes.Length > 0 && _transmitRoute is not null)
                {
                    await _audioService.PlayTransmitPcmAsync(_transmitRoute, new Pcm16AudioClip(bytes, sampleRate, channels), CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _telemetry.OnNext(new FreedvDigitalVoiceTelemetry(
                _isRunning,
                $"FreeDV output parse error: {ex.Message}",
                "Codec2 FreeDV sidecar",
                _configuration.ModeLabel,
                0,
                0,
                0,
                8000,
                8000,
                false));
        }

    }

    private Task HandleStderrLineAsync(string line)
    {
        _telemetry.OnNext(new FreedvDigitalVoiceTelemetry(
            _isRunning,
            $"Worker stderr: {line}",
            "Codec2 FreeDV sidecar",
            _configuration.ModeLabel,
            0,
            0,
            0,
            8000,
            8000,
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

    private async Task SendSpeechAsync(AudioBuffer buffer, CancellationToken ct)
    {
        if (!_workerProcess.IsStarted)
        {
            return;
        }

        var bytes = new byte[buffer.Samples.Length * sizeof(float)];
        Buffer.BlockCopy(buffer.Samples, 0, bytes, 0, bytes.Length);
        await SendMessageAsync(new
        {
            type = "speech",
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
        _isTransmitting = false;
        _ = _audioService.StopTransmitAsync(CancellationToken.None);
        _telemetry.OnNext(new FreedvDigitalVoiceTelemetry(
            false,
            "Codec2 FreeDV worker exited",
            "Codec2 FreeDV sidecar",
            _configuration.ModeLabel,
            0,
            0,
            0,
            8000,
            8000,
            false));
    }

    public void Dispose()
    {
        _isRunning = false;
        _isTransmitting = false;
        _audioSubscription.Dispose();
        _micSubscription.Dispose();
        _audioPump.Dispose();
        _speechPump.Dispose();
        _workerProcess.Dispose();
    }
}
