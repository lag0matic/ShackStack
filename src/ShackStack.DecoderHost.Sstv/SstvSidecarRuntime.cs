using System.Text.Json;
using ShackStack.DecoderHost.Sstv.Core;
using ShackStack.DecoderHost.Sstv.Protocol;

namespace ShackStack.DecoderHost.Sstv;

internal sealed class SstvSidecarRuntime
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NativeSstvReceiver _receiver = new();

    public void EmitStartup()
    {
        EmitTelemetry("Native SSTV worker ready");
        EmitImage("No image captured yet", null);
    }

    public void Handle(DecoderCommand command)
    {
        switch ((command.Type ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "configure":
                _receiver.Configure(command.Mode, command.FrequencyLabel, command.ManualSlant, command.ManualOffset);
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
            case "manual_alignment":
                _receiver.SetManualAlignment(command.ManualSlant ?? 0, command.ManualOffset ?? 0);
                if (!string.IsNullOrWhiteSpace(_receiver.LatestImagePath))
                {
                    EmitImage($"Adjusted preview: {Path.GetFileName(_receiver.LatestImagePath)}", _receiver.LatestImagePath);
                }

                EmitTelemetry(BuildConfiguredStatus("Manual alignment updated"));
                break;
            case "force_start":
                EmitTelemetry(_receiver.ForceStartConfiguredMode());
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
            var status = _receiver.HandleAudio(working, out var imageUpdated);
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

    private string BuildConfiguredStatus(string prefix = "Configured")
    {
        if (_receiver.ConfiguredMode.Equals("Auto Detect", StringComparison.OrdinalIgnoreCase))
        {
            return $"{prefix} Auto Detect | {_receiver.FrequencyLabel} | slant {_receiver.ManualSlant} | offset {_receiver.ManualOffset}";
        }

        return MmsstvModeCatalog.TryResolve(_receiver.ConfiguredMode, out var profile)
            ? $"{prefix} {MmsstvTimingEngine.Summarize(profile, SstvWorkingConfig.WorkingSampleRate)} | {_receiver.FrequencyLabel} | slant {_receiver.ManualSlant} | offset {_receiver.ManualOffset}"
            : $"{prefix} {_receiver.ConfiguredMode} | {_receiver.FrequencyLabel} | slant {_receiver.ManualSlant} | offset {_receiver.ManualOffset}";
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
            _receiver.DetectedMode));
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
