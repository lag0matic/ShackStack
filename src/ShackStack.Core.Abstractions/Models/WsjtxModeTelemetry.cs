namespace ShackStack.Core.Abstractions.Models;

public sealed record WsjtxModeTelemetry(
    bool IsRunning,
    string Status,
    string ActiveWorker,
    string ModeLabel,
    string ClockDisciplineStatus,
    bool IsClockSynchronized,
    double ClockOffsetMs,
    double CycleLengthSeconds,
    double SecondsToNextCycle,
    int DecodeCount,
    bool AutoSequenceEnabled,
    bool IsTransmitArmed);
