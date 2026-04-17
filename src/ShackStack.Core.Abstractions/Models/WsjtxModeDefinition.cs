namespace ShackStack.Core.Abstractions.Models;

public sealed record WsjtxModeDefinition(
    string Label,
    double CycleLengthSeconds,
    bool RequiresAccurateClock,
    bool SupportsAutoSequence);
