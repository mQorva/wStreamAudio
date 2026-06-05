using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace wStreamAudio.Infrastructure.Audio;

/// <summary>
/// Ein leichter MMNotificationClient-Wrapper. Feuert Events, wenn der Default-
/// Render-Endpoint wechselt oder ein Endpoint deaktiviert/entfernt wird.
/// </summary>
public sealed class EndpointMonitor : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public event EventHandler<string>? DefaultEndpointChanged;
    public event EventHandler<string>? EndpointStateChanged;
    public event EventHandler<string>? EndpointRemoved;

    public EndpointMonitor() { _enumerator.RegisterEndpointNotificationCallback(this); }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
        => EndpointStateChanged?.Invoke(this, deviceId);

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { /* nicht relevant */ }
    void IMMNotificationClient.OnDeviceRemoved(string deviceId)
        => EndpointRemoved?.Invoke(this, deviceId);
    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia)
        {
            DefaultEndpointChanged?.Invoke(this, defaultDeviceId);
        }
    }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { /* nicht relevant */ }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { /* ignore */ }
        _enumerator.Dispose();
    }
}
