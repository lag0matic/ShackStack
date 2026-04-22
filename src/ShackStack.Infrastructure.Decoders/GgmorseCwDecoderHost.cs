using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Decoders;

public sealed class GgmorseCwDecoderHost : ICwDecoderHost, IDisposable
{
    private const float SilenceRmsThreshold = 0.0025f;
    private const float SilencePeakThreshold = 0.0125f;
    private const int SilenceResetFrames = 6;
    private const float CandidateLockCostThreshold = 0.82f;
    private const float CandidateSignalThreshold = 0.08f;
    private const float CandidateActivityRatioThreshold = 0.035f;
    private const float CandidatePeakThreshold = 0.04f;
    private const int StableLockFrames = 3;
    private const int LockDropFrames = 2;

    private readonly IAudioService audioService;
    private readonly SimpleSubject<CwDecoderTelemetry> telemetry = new();
    private readonly SimpleSubject<CwDecodeChunk> decode = new();
    private readonly IDisposable audioSubscription;
    private readonly Lock sync = new();

    private GgmorseNative.GgmorseInstance? instance;
    private CwDecoderConfiguration configuration = new(700, 20, "Adaptive");
    private bool isRunning;
    private int currentSampleRate;
    private int consecutiveSilentFrames;
    private int consecutiveLockFrames;
    private int consecutiveUnlockFrames;
    private int preprocessSampleRate;
    private float dcPreviousInput;
    private float dcPreviousOutput;
    private float bandX1;
    private float bandX2;
    private float bandY1;
    private float bandY2;
    private float envelopeState;

    public GgmorseCwDecoderHost(IAudioService audioService)
    {
        this.audioService = audioService;

        audioSubscription = audioService.ReceiveStream.Subscribe(new Observer<AudioBuffer>(buffer =>
        {
            if (!isRunning)
            {
                return;
            }

            HandleAudio(buffer);
        }));

        var available = GgmorseNative.TryGetAvailability(out var status);
        telemetry.OnNext(new CwDecoderTelemetry(
            false,
            status,
            available ? "ggmorse native" : "ggmorse bridge missing",
            0.0,
            configuration.PitchHz,
            configuration.Wpm));
    }

    public IObservable<CwDecoderTelemetry> TelemetryStream => telemetry;

    public IObservable<CwDecodeChunk> DecodeStream => decode;

    public Task ConfigureAsync(CwDecoderConfiguration configuration, CancellationToken ct)
    {
        this.configuration = configuration;

        lock (sync)
        {
            ApplyConfiguration(instance);
        }

        EmitTelemetry("Configured ggmorse");
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct)
    {
        isRunning = true;
        consecutiveSilentFrames = 0;
        consecutiveLockFrames = 0;
        consecutiveUnlockFrames = 0;
        ResetPreprocessor();
        EmitTelemetry("Listening for CW audio");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        isRunning = false;
        consecutiveSilentFrames = 0;
        consecutiveLockFrames = 0;
        consecutiveUnlockFrames = 0;
        ResetPreprocessor();
        EmitTelemetry("Decoder stopped");
        return Task.CompletedTask;
    }

    public Task ResetAsync(CancellationToken ct)
    {
        lock (sync)
        {
            instance?.Reset();
            ApplyConfiguration(instance);
        }

        consecutiveSilentFrames = 0;
        consecutiveLockFrames = 0;
        consecutiveUnlockFrames = 0;
        ResetPreprocessor();
        EmitTelemetry("Decoder reset");
        return Task.CompletedTask;
    }

    private void HandleAudio(AudioBuffer buffer)
    {
        var mono = PreprocessMono(DownmixToMono(buffer), buffer.SampleRate, configuration.PitchHz);
        if (mono.Length == 0)
        {
            return;
        }

        var activity = MeasureSignal(mono);
        var rms = activity.Rms;
        var peak = activity.Peak;
        var hasSignal = rms >= SilenceRmsThreshold || peak >= SilencePeakThreshold;

        lock (sync)
        {
            if (!EnsureInstance(buffer.SampleRate))
            {
                return;
            }

            if (instance is null)
            {
                return;
            }

            if (!hasSignal)
            {
                consecutiveSilentFrames++;
                consecutiveUnlockFrames++;
                consecutiveLockFrames = 0;
                if (consecutiveSilentFrames >= SilenceResetFrames)
                {
                    instance.Reset();
                    ApplyConfiguration(instance);
                }

                EmitTelemetry("Listening for CW tone");
                return;
            }

            consecutiveSilentFrames = 0;

            if (!instance.PushAudio(mono))
            {
                telemetry.OnNext(new CwDecoderTelemetry(
                    isRunning,
                    "ggmorse bridge rejected audio buffer",
                    "ggmorse native",
                    0.0,
                    configuration.PitchHz,
                    configuration.Wpm));
                return;
            }

            var text = instance.TakeText();
            var hasStats = instance.TryGetStats(out var stats);
            var hasCandidateLock = hasStats && IsCandidateLock(stats, activity);
            if (hasCandidateLock)
            {
                consecutiveLockFrames++;
                consecutiveUnlockFrames = 0;
            }
            else
            {
                consecutiveUnlockFrames++;
                if (consecutiveUnlockFrames >= LockDropFrames)
                {
                    consecutiveLockFrames = 0;
                }
            }

            var hasStableLock = consecutiveLockFrames >= StableLockFrames;
            var hasText = hasStableLock && !string.IsNullOrWhiteSpace(text);

            if (hasText)
            {
                var confidence = ComputeConfidence(instance);
                decode.OnNext(new CwDecodeChunk(text, confidence));
            }

            EmitTelemetry(hasText
                ? "Receiving text"
                : hasStableLock
                    ? "Locked on CW tone"
                    : hasCandidateLock
                        ? "Tracking CW tone"
                    : "Listening for CW tone");
        }
    }

    private bool EnsureInstance(int sampleRate)
    {
        if (instance is not null && currentSampleRate == sampleRate)
        {
            return true;
        }

        instance?.Dispose();
        instance = null;
        currentSampleRate = sampleRate;
        ResetPreprocessor();

        if (!GgmorseNative.TryCreateInstance(sampleRate, out instance, out var status) || instance is null)
        {
            telemetry.OnNext(new CwDecoderTelemetry(
                false,
                status,
                "ggmorse bridge missing",
                0.0,
                configuration.PitchHz,
                configuration.Wpm));
            return false;
        }

        ApplyConfiguration(instance);
        return true;
    }

    private void ApplyConfiguration(GgmorseNative.GgmorseInstance? target)
    {
        target?.Configure(configuration.PitchHz, configuration.Wpm, autoPitch: true, autoSpeed: true);
    }

    private void EmitTelemetry(string status)
    {
        double confidence = 0.0;
        var pitch = configuration.PitchHz;
        var wpm = configuration.Wpm;

        lock (sync)
        {
            if (instance is not null && instance.TryGetStats(out var stats))
            {
                confidence = ComputeConfidence(stats);
                if (stats.CostFunction < 1.0f && stats.EstimatedPitchHz > 0)
                {
                    pitch = (int)Math.Round(stats.EstimatedPitchHz);
                }

                if (stats.CostFunction < 1.0f && stats.EstimatedSpeedWpm > 0)
                {
                    wpm = (int)Math.Round(stats.EstimatedSpeedWpm);
                }
            }
        }

        telemetry.OnNext(new CwDecoderTelemetry(
            isRunning,
            status,
            "ggmorse native",
            confidence,
            pitch,
            wpm));
    }

    private static double ComputeConfidence(GgmorseNative.GgmorseInstance instance)
    {
        return instance.TryGetStats(out var stats) ? ComputeConfidence(stats) : 0.0;
    }

    private static double ComputeConfidence(GgmorseNative.GgmorseStats stats)
    {
        if (stats.CostFunction >= 1.0f)
        {
            return 0.0;
        }

        var cost = 1.0 - Math.Clamp(stats.CostFunction, 0.0f, 1.0f);
        var threshold = Math.Clamp(stats.SignalThreshold, 0.0f, 1.0f);
        return Math.Clamp(cost * 0.85 + threshold * 0.15, 0.0, 1.0);
    }

    private static bool IsCandidateLock(GgmorseNative.GgmorseStats stats, SignalActivity activity)
    {
        return stats.CostFunction > 0.0f
            && stats.CostFunction < CandidateLockCostThreshold
            && stats.SignalThreshold >= CandidateSignalThreshold
            && stats.EstimatedPitchHz >= 250.0f
            && stats.EstimatedPitchHz <= 1100.0f
            && activity.ActiveRatio >= CandidateActivityRatioThreshold
            && activity.Peak >= CandidatePeakThreshold;
    }

    private static float[] DownmixToMono(AudioBuffer buffer)
    {
        if (buffer.Samples.Length == 0)
        {
            return Array.Empty<float>();
        }

        if (buffer.Channels <= 1)
        {
            return buffer.Samples;
        }

        var frameCount = buffer.Samples.Length / buffer.Channels;
        if (frameCount <= 0)
        {
            return Array.Empty<float>();
        }

        var mono = new float[frameCount];
        var src = buffer.Samples;
        var channels = buffer.Channels;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var sum = 0.0f;
            var baseIndex = frame * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                sum += src[baseIndex + ch];
            }

            mono[frame] = sum / channels;
        }

        return mono;
    }

    private float[] PreprocessMono(float[] samples, int sampleRate, int configuredPitchHz)
    {
        if (samples.Length == 0 || sampleRate <= 0)
        {
            return samples;
        }

        if (preprocessSampleRate != sampleRate)
        {
            ResetPreprocessor();
            preprocessSampleRate = sampleRate;
        }

        var output = new float[samples.Length];
        var targetPitch = Math.Clamp(configuredPitchHz > 0 ? configuredPitchHz : 700, 250, 1100);
        var omega = 2.0 * Math.PI * targetPitch / sampleRate;
        var q = 8.0;
        var alpha = Math.Sin(omega) / (2.0 * q);
        var cosOmega = Math.Cos(omega);

        var b0 = alpha;
        var b1 = 0.0;
        var b2 = -alpha;
        var a0 = 1.0 + alpha;
        var a1 = -2.0 * cosOmega;
        var a2 = 1.0 - alpha;

        var nb0 = (float)(b0 / a0);
        var nb1 = (float)(b1 / a0);
        var nb2 = (float)(b2 / a0);
        var na1 = (float)(a1 / a0);
        var na2 = (float)(a2 / a0);

        var rmsAccumulator = 0.0;

        for (var i = 0; i < samples.Length; i++)
        {
            // Simple DC blocker to keep the band-pass from reacting to offset / hum bias.
            var dcBlocked = samples[i] - dcPreviousInput + (0.995f * dcPreviousOutput);
            dcPreviousInput = samples[i];
            dcPreviousOutput = dcBlocked;

            // Narrow band-pass around the expected CW note.
            var filtered = (nb0 * dcBlocked)
                         + (nb1 * bandX1)
                         + (nb2 * bandX2)
                         - (na1 * bandY1)
                         - (na2 * bandY2);

            bandX2 = bandX1;
            bandX1 = dcBlocked;
            bandY2 = bandY1;
            bandY1 = filtered;

            output[i] = filtered;
            rmsAccumulator += filtered * filtered;
        }

        var rms = (float)Math.Sqrt(rmsAccumulator / samples.Length);
        if (rms > 0.0001f)
        {
            var gain = Math.Clamp(0.08f / rms, 0.5f, 8.0f);
            var gateThreshold = Math.Max(rms * 0.9f, 0.01f);
            const float attack = 0.35f;
            const float release = 0.995f;

            for (var i = 0; i < output.Length; i++)
            {
                var normalized = Math.Clamp(output[i] * gain, -1.0f, 1.0f);
                var magnitude = MathF.Abs(normalized);
                envelopeState = magnitude > envelopeState
                    ? ((1.0f - attack) * envelopeState) + (attack * magnitude)
                    : release * envelopeState;

                if (envelopeState <= gateThreshold)
                {
                    output[i] = 0.0f;
                    continue;
                }

                var retained = MathF.Max(0.0f, magnitude - gateThreshold);
                var scaled = retained / MathF.Max(0.0001f, 1.0f - gateThreshold);
                output[i] = Math.Clamp(MathF.CopySign(scaled, normalized), -1.0f, 1.0f);
            }
        }

        return output;
    }

    private void ResetPreprocessor()
    {
        preprocessSampleRate = 0;
        dcPreviousInput = 0.0f;
        dcPreviousOutput = 0.0f;
        bandX1 = 0.0f;
        bandX2 = 0.0f;
        bandY1 = 0.0f;
        bandY2 = 0.0f;
        envelopeState = 0.0f;
    }

    private static SignalActivity MeasureSignal(float[] samples)
    {
        if (samples.Length == 0)
        {
            return new SignalActivity(0.0f, 0.0f, 0.0f);
        }

        double sumSquares = 0.0;
        var peak = 0.0f;
        var activeCount = 0;

        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            var abs = MathF.Abs(sample);
            if (abs > peak)
            {
                peak = abs;
            }

            if (abs >= 0.02f)
            {
                activeCount++;
            }

            sumSquares += sample * sample;
        }

        var rms = (float)Math.Sqrt(sumSquares / samples.Length);
        var activeRatio = (float)activeCount / samples.Length;
        return new SignalActivity(rms, peak, activeRatio);
    }

    private readonly record struct SignalActivity(float Rms, float Peak, float ActiveRatio);

    public void Dispose()
    {
        isRunning = false;
        audioSubscription.Dispose();
        lock (sync)
        {
            instance?.Dispose();
            instance = null;
        }
    }
}
