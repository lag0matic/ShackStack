using System.Text.Json;
using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class PythonCwSkimmerDecoderHost : ICwSkimmerDecoderHost, IDisposable
{
    private readonly IAudioService _audioService;
    private readonly SimpleSubject<CwSkimmerTelemetry> _telemetry = new();
    private readonly SimpleSubject<CwSkimmerCandidate> _candidates = new();
    private readonly IDisposable _audioSubscription;
    private readonly DecoderWorkerProcess _workerProcess;
    private readonly DecoderAudioPump _audioPump;

    private CwSkimmerConfiguration _configuration = new();
    private bool _isRunning;

    public PythonCwSkimmerDecoderHost(IAudioService audioService)
    {
        _audioService = audioService;
        _workerProcess = new DecoderWorkerProcess(BundledDecoderWorkerLocator.Resolve("cw_skimmer_sidecar_worker"));
        _audioPump = new DecoderAudioPump(SendAudioAsync, () => _isRunning);

        _audioSubscription = _audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (!_isRunning)
            {
                return;
            }

            _audioPump.Enqueue(buffer);
        }));

        _telemetry.OnNext(new CwSkimmerTelemetry(
            false,
            _workerProcess.Exists ? "CW skimmer scout ready to launch" : $"Worker missing: {_workerProcess.DisplayPath}",
            "CW skimmer scout",
            0,
            0.0,
            0.0,
            0));
    }

    public IObservable<CwSkimmerTelemetry> TelemetryStream => _telemetry;

    public IObservable<CwSkimmerCandidate> CandidateStream => _candidates;

    public async Task ConfigureAsync(CwSkimmerConfiguration configuration, CancellationToken ct)
    {
        _configuration = configuration;
        await EnsureProcessAsync(ct).ConfigureAwait(false);
        await SendMessageAsync(new
        {
            type = "configure",
            lowerAudioHz = configuration.LowerAudioHz,
            upperAudioHz = configuration.UpperAudioHz,
            laneSpacingHz = configuration.LaneSpacingHz,
            maxLanes = configuration.MaxLanes,
            wpmHint = configuration.WpmHint,
            neuralSecondPassEnabled = configuration.NeuralSecondPassEnabled,
            scoutEngine = configuration.ScoutEngine,
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
            _telemetry.OnNext(new CwSkimmerTelemetry(
                false,
                $"Worker missing: {_workerProcess.DisplayPath}",
                "CW skimmer scout",
                0,
                0.0,
                0.0,
                0));
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
                _telemetry.OnNext(new CwSkimmerTelemetry(
                    root.TryGetProperty("isRunning", out var runningEl) && runningEl.GetBoolean(),
                    root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("activeWorker", out var workerEl) ? workerEl.GetString() ?? "CW skimmer scout" : "CW skimmer scout",
                    root.TryGetProperty("candidateCount", out var countEl) ? countEl.GetInt32() : 0,
                    root.TryGetProperty("strongestToneHz", out var toneEl) ? toneEl.GetDouble() : 0.0,
                    root.TryGetProperty("strongestSnrDb", out var snrEl) ? snrEl.GetDouble() : 0.0,
                    root.TryGetProperty("sampleRate", out var sampleRateEl) ? sampleRateEl.GetInt32() : 0));
            }
            else if (string.Equals(type, "candidate", StringComparison.OrdinalIgnoreCase))
            {
                _candidates.OnNext(new CwSkimmerCandidate(
                    root.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("confidence", out var confEl) ? confEl.GetDouble() : 0.0,
                    root.TryGetProperty("audioToneHz", out var toneEl) ? toneEl.GetDouble() : 0.0,
                    root.TryGetProperty("estimatedWpm", out var wpmEl) ? wpmEl.GetInt32() : _configuration.WpmHint,
                    root.TryGetProperty("scoutText", out var scoutEl) ? scoutEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("source", out var sourceEl) ? sourceEl.GetString() ?? "Spectral Scout" : "Spectral Scout",
                    root.TryGetProperty("laneId", out var laneIdEl) ? laneIdEl.GetInt32() : 0,
                    root.TryGetProperty("laneAge", out var laneAgeEl) ? laneAgeEl.GetInt32() : 0,
                    root.TryGetProperty("toneProbability", out var toneProbEl) ? toneProbEl.GetDouble() : 0.0,
                    root.TryGetProperty("envelopeLevel", out var envelopeEl) ? envelopeEl.GetDouble() : 0.0,
                    root.TryGetProperty("sidebandCount", out var sidebandEl) ? sidebandEl.GetInt32() : 0,
                    root.TryGetProperty("occupiedBandwidthHz", out var bandwidthEl) ? bandwidthEl.GetDouble() : 0.0,
                    root.TryGetProperty("timingHint", out var timingHintEl) ? timingHintEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("timingPattern", out var timingPatternEl) ? timingPatternEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("estimatedElementMs", out var elementMsEl) ? elementMsEl.GetDouble() : 0.0,
                    root.TryGetProperty("laneTranscript", out var transcriptEl) ? transcriptEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("isProvisionalText", out var provisionalEl) && provisionalEl.GetBoolean(),
                    root.TryGetProperty("laneDurationSeconds", out var durationEl) ? durationEl.GetDouble() : 0.0,
                    root.TryGetProperty("committedCharacterCount", out var commitCountEl) ? commitCountEl.GetInt32() : 0,
                    root.TryGetProperty("copyRateCharactersPerSecond", out var copyRateEl) ? copyRateEl.GetDouble() : 0.0,
                    root.TryGetProperty("neuralText", out var neuralTextEl) ? neuralTextEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("neuralTokenText", out var neuralTokenTextEl) ? neuralTokenTextEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("neuralBadLaneScore", out var neuralBadLaneEl) ? neuralBadLaneEl.GetDouble() : 0.0,
                    root.TryGetProperty("neuralTokenCandidates", out var neuralTokenCandidatesEl) ? neuralTokenCandidatesEl.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("neuralStatus", out var neuralStatusEl) ? neuralStatusEl.GetString() ?? string.Empty : string.Empty));
            }
        }
        catch (Exception ex)
        {
            _telemetry.OnNext(new CwSkimmerTelemetry(
                _isRunning,
                $"Skimmer output parse error: {ex.Message}",
                "CW skimmer scout",
                0,
                0.0,
                0.0,
                0));
        }

        return Task.CompletedTask;
    }

    private Task HandleStderrLineAsync(string line)
    {
        _telemetry.OnNext(new CwSkimmerTelemetry(
            _isRunning,
            $"Worker stderr: {line}",
            "CW skimmer scout",
            0,
            0.0,
            0.0,
            0));
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
        _telemetry.OnNext(new CwSkimmerTelemetry(
            false,
            "CW skimmer worker exited",
            "CW skimmer scout",
            0,
            0.0,
            0.0,
            0));
    }

    public void Dispose()
    {
        _isRunning = false;
        _audioSubscription.Dispose();
        _audioPump.Dispose();
        _workerProcess.Dispose();
    }
}
