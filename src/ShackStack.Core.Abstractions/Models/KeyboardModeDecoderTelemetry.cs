namespace ShackStack.Core.Abstractions.Models;

public sealed record KeyboardModeDecoderTelemetry(
    bool IsRunning,
    string Status,
    string ActiveWorker,
    string ModeLabel,
    int SignalLevelPercent,
    double AudioCenterHz,
    double TrackedAudioCenterHz,
    double SuggestedAudioCenterHz,
    double SuggestedAudioScoreDb,
    double FrequencyErrorHz,
    bool IsDcdOpen);
