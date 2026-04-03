namespace ShackStack.Core.Abstractions.Models;

public sealed record AudioRoute(
    string RxDeviceId,
    string TxDeviceId,
    string MicDeviceId,
    string MonitorDeviceId
);
