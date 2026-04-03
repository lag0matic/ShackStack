namespace ShackStack.Core.Abstractions.Models;

public sealed record RttyDecoderTelemetry(
    bool IsRunning,
    string Status,
    string ActiveWorker,
    int SignalLevelPercent,
    int EstimatedShiftHz,
    double EstimatedBaud,
    string ProfileLabel);
