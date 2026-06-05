using System.Net;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Core.Models;

namespace wStreamAudio.Infrastructure.AirPlay;

/// <summary>
/// mDNS/Bonjour-Browser für AirPlay-Receiver auf Basis von Makaretu.Dns.Multicast.
/// Wir abonnieren <c>_raop._tcp</c> (AirPlay 1) und <c>_airplay._tcp</c> (AirPlay 2),
/// sammeln über eine konfigurierbare Scan-Zeit alle Antworten, lösen Hostname → IP auf
/// und mergen pro Host zu einem Eintrag.
/// </summary>
public sealed class AirPlayDiscovery : IAirPlayDiscovery
{
    private const string RaopService = "_raop._tcp";
    private const string AirPlay2Service = "_airplay._tcp";

    private readonly ILogger<AirPlayDiscovery> _log;

    public AirPlayDiscovery(ILogger<AirPlayDiscovery> log) { _log = log; }

    public async Task<IReadOnlyList<AirPlayDevice>> DiscoverAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var scanTime = timeout ?? TimeSpan.FromSeconds(6);
        Streaming.StreamLog.Write($"AirPlay-Discovery: scanne {scanTime.TotalSeconds}s nach {RaopService} und {AirPlay2Service}");

        // Pro Instanz-Name (z.B. „Bad._raop._tcp.local") die gesammelten DNS-Records sammeln.
        var instances = new Dictionary<string, InstanceRecords>(StringComparer.OrdinalIgnoreCase);

        using var sd = new ServiceDiscovery();
        sd.ServiceInstanceDiscovered += (s, e) =>
        {
            var name = e.ServiceInstanceName.ToString();
            // Wir bekommen pro Antwort die zugehörigen Records mitgeliefert.
            lock (instances)
            {
                if (!instances.TryGetValue(name, out var rec))
                {
                    rec = new InstanceRecords { Name = name };
                    instances[name] = rec;
                }
                MergeRecords(rec, e.Message);
            }
        };

        // Beide Service-Typen anfragen — Makaretu broadcastet die mDNS-Query.
        try { sd.QueryServiceInstances(RaopService); } catch (Exception ex) { Streaming.StreamLog.Write($"Query {RaopService} fail: {ex.Message}"); }
        try { sd.QueryServiceInstances(AirPlay2Service); } catch (Exception ex) { Streaming.StreamLog.Write($"Query {AirPlay2Service} fail: {ex.Message}"); }

        try { await Task.Delay(scanTime, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }

        // Antworten verdichten zu AirPlayDevice-Instanzen, gruppiert nach Host-IP.
        var byHost = new Dictionary<string, AirPlayDevice>(StringComparer.OrdinalIgnoreCase);
        lock (instances)
        {
            foreach (var rec in instances.Values)
            {
                var dev = MapInstance(rec);
                if (dev is null) continue;
                if (byHost.TryGetValue(dev.Host, out var existing))
                {
                    byHost[dev.Host] = new AirPlayDevice
                    {
                        Id = existing.Id,
                        FriendlyName = existing.FriendlyName,
                        Host = existing.Host,
                        Port = dev.Port > 0 ? dev.Port : existing.Port,
                        SupportsAirPlay1 = existing.SupportsAirPlay1 || dev.SupportsAirPlay1,
                        SupportsAirPlay2 = existing.SupportsAirPlay2 || dev.SupportsAirPlay2,
                        Model = existing.Model ?? dev.Model,
                        Manufacturer = existing.Manufacturer ?? dev.Manufacturer,
                    };
                }
                else
                {
                    byHost[dev.Host] = dev;
                }
            }
        }

        var result = byHost.Values.OrderBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase).ToList();
        Streaming.StreamLog.Write($"AirPlay-Discovery: {result.Count} Geräte (über {instances.Count} Instanzen)");
        foreach (var d in result)
        {
            Streaming.StreamLog.Write($"  → {d.FriendlyName} @ {d.Host}:{d.Port}  AP2={d.SupportsAirPlay2}");
        }
        return result;
    }

    private static void MergeRecords(InstanceRecords rec, Message msg)
    {
        foreach (var r in msg.Answers.Concat(msg.AdditionalRecords))
        {
            switch (r)
            {
                case SRVRecord srv:
                    rec.Port = srv.Port;
                    rec.Target = srv.Target.ToString();
                    break;
                case TXTRecord txt:
                    foreach (var s in txt.Strings)
                    {
                        var eq = s.IndexOf('=');
                        if (eq > 0)
                        {
                            var k = s[..eq];
                            var v = s[(eq + 1)..];
                            rec.Txt[k] = v;
                        }
                    }
                    break;
                case ARecord a:
                    rec.Hosts[a.Name.ToString()] = a.Address.ToString();
                    break;
                case AAAARecord aaaa:
                    rec.Hosts.TryAdd(aaaa.Name.ToString(), aaaa.Address.ToString());
                    break;
            }
        }
    }

    private static AirPlayDevice? MapInstance(InstanceRecords rec)
    {
        // Name: „MACMACMACMAC@FriendlyName._raop._tcp.local" oder „FriendlyName._airplay._tcp.local"
        var n = rec.Name.TrimEnd('.');
        bool isAirPlay2 = n.EndsWith("._airplay._tcp.local", StringComparison.OrdinalIgnoreCase);

        // Audio-Filter: RAOP-Einträge sind per Definition Audio (Remote Audio Output Protocol).
        // Bei _airplay._tcp werden auch Video-/Mirroring-/Photo-Receiver oder Random-Geräte
        // gefunden — die filtern wir per features-TXT-Bitmap raus. Bit 9 (0x200) signalisiert
        // Audio-Unterstützung in der AirPlay-2-Spec.
        if (isAirPlay2 && !HasAudioFeature(rec))
        {
            return null;
        }
        // Service-Suffix entfernen.
        int dot = n.IndexOf("._", StringComparison.Ordinal);
        var instance = dot > 0 ? n[..dot] : n;
        string id, friendly;
        var at = instance.IndexOf('@');
        if (at > 0) { id = Unescape(instance[..at]); friendly = Unescape(instance[(at + 1)..]); }
        else { id = Unescape(instance); friendly = Unescape(instance); }

        // Stabile Id bevorzugen — AirPlay-2 hat „pi" (Public-Key-Hash) im TXT.
        if (rec.Txt.TryGetValue("pi", out var pi) && !string.IsNullOrEmpty(pi)) id = pi;

        // Host-IP über Target-Hostname auflösen.
        string? ip = null;
        if (!string.IsNullOrEmpty(rec.Target) && rec.Hosts.TryGetValue(rec.Target!.TrimEnd('.') + ".", out var byTarget))
        {
            ip = byTarget;
        }
        else if (rec.Hosts.Count > 0)
        {
            ip = rec.Hosts.Values.First();
        }
        if (string.IsNullOrEmpty(ip)) return null;

        rec.Txt.TryGetValue("model", out var model);
        rec.Txt.TryGetValue("am", out var am);
        rec.Txt.TryGetValue("manufacturer", out var manufacturer);

        return new AirPlayDevice
        {
            Id = id,
            FriendlyName = friendly,
            Host = ip,
            Port = rec.Port,
            SupportsAirPlay1 = !isAirPlay2,  // _raop._tcp → AirPlay 1
            SupportsAirPlay2 = isAirPlay2,   // _airplay._tcp → AirPlay 2
            Model = model ?? am,
            Manufacturer = manufacturer,
        };
    }

    /// <summary>Liest aus dem TXT-Feld „features" (Format: „0xHEX_LOW,0xHEX_HIGH") die Audio-Bit-Maske
    /// aus. Bit 9 (Mask 0x200) der unteren 32 Bit = „Supports Audio". Falls features fehlen, lassen wir
    /// das Gerät durch — manche Receiver melden den Wert nicht zuverlässig.</summary>
    private static bool HasAudioFeature(InstanceRecords rec)
    {
        if (!rec.Txt.TryGetValue("features", out var feats) || string.IsNullOrEmpty(feats))
        {
            // Sekundär: wenn „am"/„model" auf ein Audio-Modell schließen lässt, akzeptieren.
            // Wir sind hier konservativ und lassen es durch — Audio-Filter via features bevorzugt.
            return true;
        }

        try
        {
            var parts = feats.Split(',');
            var rawLow = parts[0].Trim();
            if (rawLow.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) rawLow = rawLow[2..];
            if (!uint.TryParse(rawLow, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var low))
            {
                return true;
            }
            const uint AudioBit = 0x200; // Bit 9: Supports Audio
            return (low & AudioBit) != 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Wandelt das mDNS-DNS-Sicherheits-Escape (z. B. \032 für Space) zurück.</summary>
    private static string Unescape(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 3 < s.Length &&
                char.IsDigit(s[i + 1]) && char.IsDigit(s[i + 2]) && char.IsDigit(s[i + 3]))
            {
                var code = int.Parse(s.AsSpan(i + 1, 3));
                sb.Append((char)code);
                i += 3;
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString().TrimEnd();
    }

    private sealed class InstanceRecords
    {
        public required string Name { get; init; }
        public int Port { get; set; }
        public string? Target { get; set; }
        public Dictionary<string, string> Txt { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Hosts { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
