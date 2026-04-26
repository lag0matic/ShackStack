namespace ShackStack.Core.Abstractions.Models;

public sealed record SstvDecoderTelemetry(
    bool IsRunning,
    string Status,
    string ActiveWorker,
    int SignalLevelPercent,
    string DetectedMode,
    string? FskIdCallsign = null
);
