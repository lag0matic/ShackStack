namespace ShackStack.Core.Abstractions.Models;

public sealed record AudioSettings(
    string RxDeviceId,
    string TxDeviceId,
    string MicDeviceId,
    string MonitorDeviceId,
    int SampleRate,
    int BlockSize,
    int MonitorVolumePercent = 75,
    int MicGainPercent = 100,
    int VoiceCompressionPercent = 0,
    bool MicMonitorEnabled = false,
    int MicMonitorPercent = 50,
    string FreedvMonitorDeviceId = "",
    int FreedvMonitorVolumePercent = 75
);
