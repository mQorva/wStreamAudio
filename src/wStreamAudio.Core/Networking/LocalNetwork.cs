using System.Net;
using System.Net.NetworkInformation;

namespace wStreamAudio.Core.Networking;

/// <summary>
/// Helper, um zu erkennen, ob eine IP-Adresse zum eigenen Rechner gehört.
/// Wird genutzt, um den lokalen Player (Squeezelite o.ä. auf dieser Maschine) aus der
/// Player-Liste auszublenden — Wiedergabe auf der eigenen Audioquelle wäre eine Schleife.
/// </summary>
public static class LocalNetwork
{
    private static readonly Lazy<HashSet<string>> LocalIps = new(LoadLocalIps);

    public static bool IsLocal(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;
        if (ip == "127.0.0.1" || ip == "::1") return true;
        return LocalIps.Value.Contains(ip);
    }

    private static HashSet<string> LoadLocalIps()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    set.Add(addr.Address.ToString());
                }
            }
            try
            {
                foreach (var addr in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    set.Add(addr.ToString());
            }
            catch { /* DNS kann scheitern, egal */ }
        }
        catch { /* best-effort */ }
        return set;
    }
}
