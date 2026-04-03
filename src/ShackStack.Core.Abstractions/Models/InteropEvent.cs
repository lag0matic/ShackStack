namespace ShackStack.Core.Abstractions.Models;

public sealed record InteropEvent(
    string Source,
    string Message
);
