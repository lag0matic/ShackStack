namespace ShackStack.Core.Abstractions.Models;

public sealed record CwDecoderTelemetry(
    bool IsRunning,
    string Status,
    string ActiveWorker,
    double Confidence,
    int EstimatedPitchHz,
    int EstimatedWpm
);
