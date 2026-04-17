namespace ShackStack.Core.Abstractions.Models;

public sealed record WsjtxDecodeMessage(
    DateTime TimestampUtc,
    string ModeLabel,
    int FrequencyOffsetHz,
    int SnrDb,
    double DeltaTimeSeconds,
    string MessageText,
    double Confidence,
    bool IsDirectedToMe = false,
    bool IsCq = false);
