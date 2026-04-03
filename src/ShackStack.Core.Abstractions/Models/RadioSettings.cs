namespace ShackStack.Core.Abstractions.Models;

public sealed record RadioSettings(
    string ControlBackend,
    string CivPort,
    int CivBaud,
    int CivAddress,
    int PollIntervalMs
);
