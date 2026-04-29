namespace ShackStack.Core.Abstractions.Models;

public sealed record FreedvDigitalVoiceTelemetry(
    bool IsRunning,
    string Status,
    string ActiveWorker,
    string ModeLabel,
    int SignalLevelPercent,
    int SyncPercent,
    double SnrDb,
    int SpeechSampleRate,
    int ModemSampleRate,
    bool IsCodec2RuntimeLoaded,
    string RadeCallsign = "");
