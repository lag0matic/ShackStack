namespace ShackStack.Core.Abstractions.Models;

public sealed record InteropSettings(
    bool FlrigEnabled,
    string FlrigHost,
    int FlrigPort
);
