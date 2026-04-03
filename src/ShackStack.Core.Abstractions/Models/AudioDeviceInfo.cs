namespace ShackStack.Core.Abstractions.Models;

public sealed record AudioDeviceInfo(
    string DeviceId,
    string FriendlyName,
    bool IsDefault,
    bool IsInput,
    bool IsOutput
);
