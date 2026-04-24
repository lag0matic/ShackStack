namespace ShackStack.Core.Abstractions.Models;

public sealed record SstvTransmitOptions(
    bool CwIdEnabled = false,
    string? CwIdText = null,
    int CwIdFrequencyHz = 1000,
    int CwIdWpm = 28);
