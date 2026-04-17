namespace ShackStack.Core.Abstractions.Models;

public sealed record WsjtxModeConfiguration(
    string ModeLabel,
    string FrequencyLabel,
    bool AutoSequenceEnabled,
    bool CallCQEnabled,
    bool Ft8SubtractionEnabled,
    bool Ft8ApEnabled,
    bool Ft8OsdEnabled,
    double CycleLengthSeconds,
    bool RequiresAccurateClock,
    string StationCallsign,
    string StationGridSquare,
    bool TransmitFirstEnabled);
