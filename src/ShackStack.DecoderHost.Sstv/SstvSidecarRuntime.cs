using System.Text.Json;
using System.Runtime.InteropServices;
using ShackStack.DecoderHost.Sstv.Core;
using ShackStack.DecoderHost.Sstv.Protocol;

namespace ShackStack.DecoderHost.Sstv;

internal sealed class SstvSidecarRuntime
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NativeSstvReceiver _receiver = new();
    private readonly bool _diagnosticsEnabled =
        string.Equals(Environment.GetEnvironmentVariable("SHACKSTACK_SSTV_DIAGNOSTICS"), "1", StringComparison.Ordinal);
    private readonly string _diagnosticsLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "ShackStack",
        "logs",
        "sstv-diagnostics.jsonl");
    private readonly string _diagnosticsAudioPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "ShackStack",
        "logs",
        "sstv-live-working-audio.rawf32");

    public void EmitStartup()
    {
        EmitTelemetry("Native SSTV worker ready");
        if (_diagnosticsEnabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_diagnosticsLogPath)!);
            File.AppendAllText(_diagnosticsLogPath, JsonSerializer.Serialize(new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                eventName = "startup",
                worker = "ShackStack SSTV native sidecar",
                workingSampleRate = SstvWorkingConfig.WorkingSampleRate,
                audioCapturePath = _diagnosticsAudioPath
            }, _jsonOptions) + Environment.NewLine);
            File.WriteAllBytes(_diagnosticsAudioPath, []);
            EmitTelemetry($"Native SSTV diagnostics logging to {_diagnosticsLogPath}");
        }

        EmitImage("No image captured yet", null);
    }

    public void Handle(DecoderCommand command)
    {
            switch ((command.Type ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "configure":
                    _receiver.Configure(command.Mode, command.FrequencyLabel);
                    WriteDiagnosticsEvent("configure", new
                    {
                        requestedMode = command.Mode,
                        configuredMode = _receiver.ConfiguredMode,
                        detectedMode = _receiver.DetectedMode,
                        frequencyLabel = _receiver.FrequencyLabel
                    });
                    EmitTelemetry(BuildConfiguredStatus());
                    break;
            case "start":
                _receiver.Start();
                EmitTelemetry("Receiver started");
                break;
            case "stop":
                _receiver.Stop();
                EmitTelemetry("Receiver stopped");
                break;
            case "reset":
                _receiver.Reset();
                EmitImage("No image captured yet", null);
                EmitTelemetry("Native SSTV session reset");
                break;
                case "force_start":
                    EmitTelemetry(_receiver.ForceStartConfiguredMode());
                    break;
            case "post_receive_slant":
                ApplyPostReceiveSlantCorrection();
                break;
            case "audio":
                HandleAudio(command);
                break;
            case "shutdown":
                _receiver.Stop();
                EmitTelemetry("Native SSTV worker shutting down");
                break;
        }
    }

    private void ApplyPostReceiveSlantCorrection()
    {
        if (_receiver.ApplyMmsstvPostReceiveSlantCorrection() && !string.IsNullOrWhiteSpace(_receiver.LatestImagePath))
        {
            EmitImage($"MMSSTV post-receive slant applied: {Path.GetFileName(_receiver.LatestImagePath)}", _receiver.LatestImagePath);
            EmitTelemetry(_receiver.LastMmsstvSlantDebug ?? "MMSSTV post-receive slant applied");
            return;
        }

        EmitTelemetry(_receiver.LastMmsstvSlantDebug ?? "No completed SSTV image is available for MMSSTV slant correction");
    }

    private void HandleAudio(DecoderCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Samples))
        {
            EmitTelemetry(BuildConfiguredStatus("Audio packet missing samples"));
            return;
        }

        try
        {
            var floatSamples = SstvAudioMath.DecodeBase64FloatSamples(command.Samples);
            var mono = SstvAudioMath.ToMono(floatSamples, command.Channels ?? 1);
            var working = SstvAudioMath.Resample(mono, command.SampleRate ?? SstvWorkingConfig.WorkingSampleRate, SstvWorkingConfig.WorkingSampleRate);
            WriteDiagnosticsAudio(working);
            var status = _receiver.HandleAudio(working, out var imageUpdated);
            WriteDiagnostics(command, mono, working, status);
            if (imageUpdated && !string.IsNullOrWhiteSpace(_receiver.LatestImagePath))
            {
                EmitImage(status, _receiver.LatestImagePath);
            }

            EmitTelemetry(status);
        }
        catch (FormatException ex)
        {
            EmitTelemetry(BuildConfiguredStatus($"Audio payload decode error: {ex.Message}"));
        }
    }

    private void WriteDiagnostics(DecoderCommand command, ReadOnlySpan<float> mono, ReadOnlySpan<float> working, string status)
    {
        if (!_diagnosticsEnabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_diagnosticsLogPath)!);
            var sampleRate = command.SampleRate ?? SstvWorkingConfig.WorkingSampleRate;
            var toneSampleRate = SstvWorkingConfig.WorkingSampleRate;
            var toneWindow = working.Length > 0
                ? working[^Math.Min(working.Length, toneSampleRate / 5)..]
                : ReadOnlySpan<float>.Empty;
            var tone1200 = SstvAudioMath.TonePower(toneWindow, toneSampleRate, 1200.0);
            var tone1500 = SstvAudioMath.TonePower(toneWindow, toneSampleRate, 1500.0);
            var tone1900 = SstvAudioMath.TonePower(toneWindow, toneSampleRate, 1900.0);
            var tone2300 = SstvAudioMath.TonePower(toneWindow, toneSampleRate, 2300.0);
            var dominantTone = DominantTone((tone1200, tone1500, tone1900, tone2300));
            var payload = new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                eventName = "audio",
                inputSampleRate = sampleRate,
                inputChannels = command.Channels ?? 1,
                monoSamples = mono.Length,
                workingSamples = working.Length,
                monoRms = Rms(mono),
                monoPeak = PeakAbs(mono),
                workingRms = Rms(working),
                workingPeak = PeakAbs(working),
                signalLevelPercent = _receiver.SignalLevelPercent,
                dominantToneHz = dominantTone.FrequencyHz,
                dominantTonePower = dominantTone.Power,
                tone1200,
                tone1500,
                tone1900,
                tone2300,
                status,
                detectedMode = _receiver.DetectedMode,
                syncStatus = _receiver.SyncStatus,
                sessionOrigin = _receiver.SessionOrigin,
                fskIdCallsign = _receiver.LastFskIdCallsign
            };

            File.AppendAllText(_diagnosticsLogPath, JsonSerializer.Serialize(payload, _jsonOptions) + Environment.NewLine);
        }
        catch
        {
        }
    }

    private void WriteDiagnosticsAudio(ReadOnlySpan<float> working)
    {
        if (!_diagnosticsEnabled || working.Length == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_diagnosticsAudioPath)!);
            var bytes = new byte[working.Length * sizeof(float)];
            MemoryMarshal.AsBytes(working).CopyTo(bytes);
            using var stream = new FileStream(_diagnosticsAudioPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
        }
        catch
        {
        }
    }

    private void WriteDiagnosticsEvent(string eventName, object details)
    {
        if (!_diagnosticsEnabled)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_diagnosticsLogPath)!);
            var payload = new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                eventName,
                details
            };
            File.AppendAllText(_diagnosticsLogPath, JsonSerializer.Serialize(payload, _jsonOptions) + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static (int FrequencyHz, double Power) DominantTone((double Tone1200, double Tone1500, double Tone1900, double Tone2300) tones)
    {
        var bestFrequency = 1200;
        var bestPower = tones.Tone1200;
        if (tones.Tone1500 > bestPower)
        {
            bestFrequency = 1500;
            bestPower = tones.Tone1500;
        }

        if (tones.Tone1900 > bestPower)
        {
            bestFrequency = 1900;
            bestPower = tones.Tone1900;
        }

        if (tones.Tone2300 > bestPower)
        {
            bestFrequency = 2300;
            bestPower = tones.Tone2300;
        }

        return (bestFrequency, bestPower);
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return 0.0;
        }

        var sum = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }

        return Math.Sqrt(sum / samples.Length);
    }

    private static double PeakAbs(ReadOnlySpan<float> samples)
    {
        var peak = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }

        return peak;
    }

    private string BuildConfiguredStatus(string prefix = "Configured")
    {
        if (_receiver.ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase))
        {
            return $"{prefix} Auto Detect | {_receiver.FrequencyLabel}";
        }

        return MmsstvModeCatalog.TryResolve(_receiver.ConfiguredMode, out var profile)
            ? $"{prefix} {MmsstvTimingEngine.Summarize(profile, SstvWorkingConfig.WorkingSampleRate)} | {_receiver.FrequencyLabel}"
            : $"{prefix} {_receiver.ConfiguredMode} | {_receiver.FrequencyLabel}";
    }

    private void EmitTelemetry(string status)
    {
        var syncDetails = _receiver.SyncStatus;
        var prominenceText = _receiver.LastSyncProminence > 0.0
            ? $" | {_receiver.SessionOrigin} | sync {_receiver.LastSyncProminence:0.00}x"
            : $" | {_receiver.SessionOrigin}";
        var richStatus = string.IsNullOrWhiteSpace(syncDetails) || string.Equals(syncDetails, status, StringComparison.Ordinal)
            ? $"{status}{prominenceText}"
            : $"{status} | {syncDetails}{prominenceText}";

        Emit(new TelemetryPayload(
            "telemetry",
            _receiver.IsRunning,
            richStatus,
            "ShackStack SSTV native sidecar",
            _receiver.SignalLevelPercent,
            _receiver.DetectedMode,
            _receiver.LastFskIdCallsign));
    }

    private void EmitImage(string status, string? imagePath)
    {
        Emit(new ImagePayload("image", status, imagePath));
    }

    private void Emit<T>(T payload)
    {
        Console.WriteLine(JsonSerializer.Serialize(payload, _jsonOptions));
    }
}
