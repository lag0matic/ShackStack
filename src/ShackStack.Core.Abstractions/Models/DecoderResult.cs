namespace ShackStack.Core.Abstractions.Models;

public sealed record DecoderResult(
    string DecoderId,
    string Text,
    float Confidence,
    DateTimeOffset Timestamp
);
