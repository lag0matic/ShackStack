namespace ShackStack.Core.Abstractions.Models;

public sealed record CwSkimmerTelemetry(
    bool IsRunning,
    string Status,
    string ActiveWorker,
    int CandidateCount,
    double StrongestToneHz,
    double StrongestSnrDb,
    int SampleRate
);
