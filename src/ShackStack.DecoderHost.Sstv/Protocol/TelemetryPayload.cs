namespace ShackStack.DecoderHost.Sstv.Protocol;

internal sealed record TelemetryPayload(
    string Type,
    bool IsRunning,
    string Status,
    string ActiveWorker,
    int SignalLevelPercent,
    string DetectedMode,
    string? FskIdCallsign = null);
