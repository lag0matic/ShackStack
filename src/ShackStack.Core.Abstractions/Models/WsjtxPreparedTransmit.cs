namespace ShackStack.Core.Abstractions.Models;

public sealed record WsjtxPreparedTransmit(
    string ModeLabel,
    string MessageText,
    int TxAudioFrequencyHz,
    string GeneratorName,
    string WaveFilePath,
    DateTime PreparedUtc);

public sealed record WsjtxPreparedTransmitResult(
    bool Success,
    string Status,
    WsjtxPreparedTransmit? PreparedTransmit,
    Pcm16AudioClip? PreparedClip);
