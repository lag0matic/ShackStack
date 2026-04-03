using NAudio.CoreAudioApi;
using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Infrastructure.Audio.WindowsAudio;

internal sealed class WindowsAudioDeviceCatalog
{
    public IReadOnlyList<AudioDeviceInfo> Enumerate()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<AudioDeviceInfo>();

        var defaultCaptureId = TryGetDefaultId(enumerator, DataFlow.Capture);
        var defaultRenderId = TryGetDefaultId(enumerator, DataFlow.Render);

        devices.AddRange(EnumerateByFlow(enumerator, DataFlow.Capture, defaultCaptureId));
        devices.AddRange(EnumerateByFlow(enumerator, DataFlow.Render, defaultRenderId));

        return devices
            .OrderByDescending(d => d.IsDefault)
            .ThenBy(d => d.IsInput ? 0 : 1)
            .ThenBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<AudioDeviceInfo> EnumerateByFlow(MMDeviceEnumerator enumerator, DataFlow flow, string? defaultId)
    {
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            yield return new AudioDeviceInfo(
                DeviceId: device.ID,
                FriendlyName: device.FriendlyName,
                IsDefault: string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase),
                IsInput: flow == DataFlow.Capture,
                IsOutput: flow == DataFlow.Render
            );
        }
    }

    private static string? TryGetDefaultId(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID;
        }
        catch
        {
            return null;
        }
    }
}
