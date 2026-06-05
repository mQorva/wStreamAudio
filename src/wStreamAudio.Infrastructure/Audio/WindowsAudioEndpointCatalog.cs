using NAudio.CoreAudioApi;
using wStreamAudio.Core.Abstractions;

namespace wStreamAudio.Infrastructure.Audio;

public sealed class WindowsAudioEndpointCatalog : IAudioEndpointCatalog
{
    public IReadOnlyList<AudioEndpointInfo> EnumerateRenderEndpoints()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = SafeDefaultId(enumerator);
        var list = new List<AudioEndpointInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            list.Add(new AudioEndpointInfo(device.ID, device.FriendlyName, device.ID == defaultId));
            device.Dispose();
        }
        return list;
    }

    public AudioEndpointInfo? GetDefaultRenderEndpoint()
    {
        using var enumerator = new MMDeviceEnumerator();
        try
        {
            using var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return new AudioEndpointInfo(d.ID, d.FriendlyName, true);
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeDefaultId(MMDeviceEnumerator enumerator)
    {
        try
        {
            using var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return d.ID;
        }
        catch
        {
            return null;
        }
    }
}
