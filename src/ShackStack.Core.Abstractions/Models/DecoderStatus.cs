namespace ShackStack.Core.Abstractions.Models;

public sealed record DecoderStatus(
    string DecoderId,
    bool IsEnabled,
    bool IsHealthy,
    string Summary
);
