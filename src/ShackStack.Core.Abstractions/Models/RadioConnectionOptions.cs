namespace ShackStack.Core.Abstractions.Models;

public sealed record RadioConnectionOptions(
    string PortName,
    int BaudRate,
    int RadioAddress
);
