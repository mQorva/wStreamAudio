using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace wStreamAudio.Infrastructure.Dlna;

/// <summary>
/// Sehr schlanker SSDP-Client: schickt M-SEARCH und sammelt Antworten.
/// Auf Mehrfach-NIC-Hosts wird auf jeder aktiven IPv4-Schnittstelle gesucht,
/// damit Renderer in allen erreichbaren Netzen gefunden werden.
/// </summary>
internal static class SsdpClient
{
    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("239.255.255.250"), 1900);

    /// <summary>
    /// Schickt M-SEARCH für den angegebenen Service-Type und sammelt LOCATION-Header
    /// aus den Antworten.
    /// </summary>
    public static async Task<IReadOnlyList<SsdpResponse>> SearchAsync(string searchTarget, TimeSpan timeout, CancellationToken ct = default)
    {
        var responses = new List<SsdpResponse>();
        var seenLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sockets = new List<UdpClient>();
        try
        {
            foreach (var localAddress in EnumerateLocalIPv4Addresses())
            {
                UdpClient? udp = null;
                try
                {
                    udp = new UdpClient(new IPEndPoint(localAddress, 0));
                    udp.MulticastLoopback = false;
                    udp.JoinMulticastGroup(MulticastEndpoint.Address, localAddress);
                }
                catch
                {
                    udp?.Dispose();
                    continue;
                }
                sockets.Add(udp);

                var msearch = BuildMSearch(searchTarget, mxSeconds: (int)Math.Min(5, Math.Max(1, timeout.TotalSeconds)));
                var bytes = Encoding.ASCII.GetBytes(msearch);
                try { await udp.SendAsync(bytes, MulticastEndpoint, ct).ConfigureAwait(false); }
                catch { /* einzelne NIC-Fehler ignorieren */ }
            }

            using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadlineCts.CancelAfter(timeout);

            var receiveTasks = sockets.Select(s => ReceiveLoopAsync(s, responses, seenLocations, deadlineCts.Token)).ToArray();
            try { await Task.WhenAll(receiveTasks).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* erwartet bei Timeout */ }
        }
        finally
        {
            foreach (var s in sockets) { try { s.Dispose(); } catch { /* ignore */ } }
        }

        return responses;
    }

    private static async Task ReceiveLoopAsync(UdpClient udp, List<SsdpResponse> sink, HashSet<string> seen, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await udp.ReceiveAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch { continue; }

            var text = Encoding.ASCII.GetString(result.Buffer);
            var parsed = ParseResponse(text, result.RemoteEndPoint);
            if (parsed is null) continue;
            lock (sink)
            {
                if (seen.Add(parsed.Location.AbsoluteUri))
                {
                    sink.Add(parsed);
                }
            }
        }
    }

    private static IEnumerable<IPAddress> EnumerateLocalIPv4Addresses()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ua.Address)) continue;
                yield return ua.Address;
            }
        }
    }

    private static string BuildMSearch(string searchTarget, int mxSeconds)
    {
        var sb = new StringBuilder(256);
        sb.Append("M-SEARCH * HTTP/1.1\r\n");
        sb.Append("HOST: 239.255.255.250:1900\r\n");
        sb.Append("MAN: \"ssdp:discover\"\r\n");
        sb.Append("MX: ").Append(mxSeconds).Append("\r\n");
        sb.Append("ST: ").Append(searchTarget).Append("\r\n");
        sb.Append("\r\n");
        return sb.ToString();
    }

    private static SsdpResponse? ParseResponse(string text, IPEndPoint remoteEp)
    {
        if (!text.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase)) return null;
        string? location = null;
        string? usn = null;
        string? st = null;
        foreach (var line in text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = line.IndexOf(':');
            if (idx < 1) continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (key.Equals("LOCATION", StringComparison.OrdinalIgnoreCase)) location = value;
            else if (key.Equals("USN", StringComparison.OrdinalIgnoreCase)) usn = value;
            else if (key.Equals("ST", StringComparison.OrdinalIgnoreCase)) st = value;
        }
        if (string.IsNullOrEmpty(location)) return null;
        if (!Uri.TryCreate(location, UriKind.Absolute, out var locUri)) return null;
        return new SsdpResponse(locUri, usn, st, remoteEp);
    }
}

internal sealed record SsdpResponse(Uri Location, string? Usn, string? St, IPEndPoint RemoteEndPoint);
