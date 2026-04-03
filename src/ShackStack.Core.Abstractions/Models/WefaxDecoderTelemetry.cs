namespace ShackStack.Core.Abstractions.Models;

public sealed record WefaxDecoderTelemetry(
    bool IsRunning,
    string Status,
    string ActiveWorker,
    int LinesReceived,
    int AlignedOffset,
    double StartConfidence,
    double StopConfidence,
    string ModeLabel
);
