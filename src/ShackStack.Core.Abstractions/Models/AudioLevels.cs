namespace ShackStack.Core.Abstractions.Models;

public sealed record AudioLevels(
    float RxLevel,
    float TxLevel,
    float MicLevel
);
