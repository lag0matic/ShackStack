using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using ShackStack.Core.Abstractions.Utilities;

namespace ShackStack.Infrastructure.Audio.WindowsAudio;

internal sealed class AudioDeviceNotificationMonitor : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly SimpleSubject<string> _deviceEvents = new();

    public AudioDeviceNotificationMonitor()
    {
        _enumerator = new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public IObservable<string> DeviceEvents => _deviceEvents;

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        _deviceEvents.OnNext($"state:{deviceId}:{newState}");
    }

    public void OnDeviceAdded(string pwstrDeviceId)
    {
        _deviceEvents.OnNext($"added:{pwstrDeviceId}");
    }

    public void OnDeviceRemoved(string deviceId)
    {
        _deviceEvents.OnNext($"removed:{deviceId}");
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        _deviceEvents.OnNext($"default:{flow}:{role}:{defaultDeviceId}");
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        _deviceEvents.OnNext($"property:{pwstrDeviceId}:{key.formatId}:{key.propertyId}");
    }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(this);
        _enumerator.Dispose();
    }
}
