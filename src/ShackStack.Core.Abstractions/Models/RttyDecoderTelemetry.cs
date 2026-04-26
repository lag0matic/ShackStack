namespace ShackStack.Core.Abstractions.Models;

public sealed record RttyDecoderTelemetry(
    bool IsRunning,
    string Status,
    string ActiveWorker,
    int SignalLevelPercent,
    int EstimatedShiftHz,
    double EstimatedBaud,
    string ProfileLabel,
    double SuggestedAudioCenterHz = 0.0,
    double TuneConfidence = 0.0,
    bool IsCarrierLocked = false);
