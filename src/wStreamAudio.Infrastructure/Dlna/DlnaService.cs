using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using wStreamAudio.Core.Abstractions;

namespace wStreamAudio.Infrastructure.Dlna;

/// <summary>
/// Native DLNA-Steuerung via SOAP über HTTP. Discover liefert MediaRenderer im LAN,
/// PlayUrl/Stop/SetVolume rufen die jeweiligen UPnP-Actions auf.
/// </summary>
public sealed class DlnaService : IDlnaService
{
    private const string AvTransportUrn = "urn:schemas-upnp-org:service:AVTransport:1";
    private const string RenderingControlUrn = "urn:schemas-upnp-org:service:RenderingControl:1";
    private const string MediaRendererSt = "urn:schemas-upnp-org:device:MediaRenderer:1";

    private readonly HttpClient _http;
    private readonly ILogger<DlnaService> _log;

    public DlnaService(HttpClient http, ILogger<DlnaService> log)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(8);
        _log = log;
    }

    public async Task<IReadOnlyList<DlnaRenderer>> DiscoverRenderersAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var responses = await SsdpClient.SearchAsync(MediaRendererSt, timeout, ct).ConfigureAwait(false);
        var renderers = new List<DlnaRenderer>();

        foreach (var response in responses)
        {
            try
            {
                var renderer = await FetchDeviceAsync(response.Location, ct).ConfigureAwait(false);
                if (renderer is not null) renderers.Add(renderer);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Konnte Geräte-Beschreibung nicht laden: {Location}", response.Location);
            }
        }

        // Eindeutigkeit per UDN — manchmal antworten Geräte mehrfach.
        return renderers.GroupBy(r => r.Udn).Select(g => g.First()).ToList();
    }

    private async Task<DlnaRenderer?> FetchDeviceAsync(Uri descriptionUrl, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(descriptionUrl, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        var xml = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        // Manche Hersteller (Denon, Sonos) verschachteln den MediaRenderer als Sub-Device
        // im Top-Level-Device (z.B. AiosDevice). Wir suchen daher in ALLEN Device-Elementen
        // nach dem ersten, dessen deviceType „MediaRenderer" enthält.
        XElement? device = null;
        foreach (var d in doc.Descendants(ns + "device"))
        {
            var dt = d.Element(ns + "deviceType")?.Value ?? string.Empty;
            if (dt.Contains("MediaRenderer", StringComparison.OrdinalIgnoreCase))
            {
                device = d;
                break;
            }
        }
        if (device is null) return null;

        var deviceType = device.Element(ns + "deviceType")?.Value ?? string.Empty;

        var udn = device.Element(ns + "UDN")?.Value;
        var friendlyName = device.Element(ns + "friendlyName")?.Value;
        var manufacturer = device.Element(ns + "manufacturer")?.Value;
        var modelName = device.Element(ns + "modelName")?.Value;
        if (string.IsNullOrEmpty(udn) || string.IsNullOrEmpty(friendlyName)) return null;

        var baseUrl = ResolveUrlBase(doc, ns, descriptionUrl);

        Uri? avTransportControl = null;
        Uri? renderingControl = null;
        foreach (var svc in device.Descendants(ns + "service"))
        {
            var serviceType = svc.Element(ns + "serviceType")?.Value ?? string.Empty;
            var controlUrlRel = svc.Element(ns + "controlURL")?.Value;
            if (string.IsNullOrEmpty(controlUrlRel)) continue;

            if (serviceType.Equals(AvTransportUrn, StringComparison.OrdinalIgnoreCase))
                avTransportControl = new Uri(baseUrl, controlUrlRel);
            else if (serviceType.Equals(RenderingControlUrn, StringComparison.OrdinalIgnoreCase))
                renderingControl = new Uri(baseUrl, controlUrlRel);
        }

        if (avTransportControl is null) return null;

        return new DlnaRenderer
        {
            Udn = udn,
            FriendlyName = friendlyName.Trim(),
            AvTransportControlUrl = avTransportControl,
            RenderingControlUrl = renderingControl,
            Manufacturer = manufacturer,
            ModelName = modelName,
        };
    }

    private static Uri ResolveUrlBase(XDocument doc, XNamespace ns, Uri descriptionUrl)
    {
        var urlBase = doc.Root?.Element(ns + "URLBase")?.Value;
        if (!string.IsNullOrEmpty(urlBase) && Uri.TryCreate(urlBase, UriKind.Absolute, out var b)) return b;
        return new Uri(descriptionUrl.GetLeftPart(UriPartial.Authority) + "/");
    }

    public async Task PlayUrlAsync(DlnaRenderer renderer, string streamUrl, string mimeType = "audio/mpeg", string title = "wStreamAudio Live", int prebufferMs = 0, CancellationToken ct = default)
    {
        // Buffer-Hint als Query-Parameter — der HttpStreamServer liest ihn und sendet vor
        // dem eigentlichen Audio entsprechend viel MP3-Stille, sodass der Renderer-Puffer
        // sofort voll ist und der Stream nicht erst ein paar Sekunden später startet.
        var urlWithBuf = prebufferMs > 0
            ? AppendQuery(streamUrl, "buf", prebufferMs.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : streamUrl;

        var didl = BuildDidlLite(urlWithBuf, mimeType, title);
        var setUriArgs = new (string, string)[]
        {
            ("InstanceID", "0"),
            ("CurrentURI", urlWithBuf),
            ("CurrentURIMetaData", didl),
        };
        await SoapAsync(renderer.AvTransportControlUrl, AvTransportUrn, "SetAVTransportURI", setUriArgs, ct).ConfigureAwait(false);

        var playArgs = new (string, string)[]
        {
            ("InstanceID", "0"),
            ("Speed", "1"),
        };
        await SoapAsync(renderer.AvTransportControlUrl, AvTransportUrn, "Play", playArgs, ct).ConfigureAwait(false);
    }

    private static string AppendQuery(string url, string key, string value)
    {
        var sep = url.Contains('?') ? '&' : '?';
        return $"{url}{sep}{key}={Uri.EscapeDataString(value)}";
    }

    public Task StopAsync(DlnaRenderer renderer, CancellationToken ct = default)
    {
        var args = new (string, string)[] { ("InstanceID", "0") };
        return SoapAsync(renderer.AvTransportControlUrl, AvTransportUrn, "Stop", args, ct);
    }

    public Task SetVolumeAsync(DlnaRenderer renderer, int percent, CancellationToken ct = default)
    {
        if (renderer.RenderingControlUrl is null) return Task.CompletedTask;
        var clamped = Math.Clamp(percent, 0, 100);
        var args = new (string, string)[]
        {
            ("InstanceID", "0"),
            ("Channel", "Master"),
            ("DesiredVolume", clamped.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        return SoapAsync(renderer.RenderingControlUrl, RenderingControlUrn, "SetVolume", args, ct);
    }

    public async Task<int?> GetVolumeAsync(DlnaRenderer renderer, CancellationToken ct = default)
    {
        if (renderer.RenderingControlUrl is null) return null;
        var args = new (string, string)[]
        {
            ("InstanceID", "0"),
            ("Channel", "Master"),
        };
        try
        {
            var resp = await SoapWithResponseAsync(renderer.RenderingControlUrl, RenderingControlUrn, "GetVolume", args, ct).ConfigureAwait(false);
            // SOAP-Response enthält <CurrentVolume>NN</CurrentVolume>.
            var doc = XDocument.Parse(resp);
            var volEl = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "CurrentVolume");
            if (volEl is null) return null;
            if (int.TryParse(volEl.Value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                return Math.Clamp(v, 0, 100);
            return null;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "GetVolume an {Renderer} fehlgeschlagen", renderer.FriendlyName);
            return null;
        }
    }

    private async Task<string> SoapWithResponseAsync(Uri controlUrl, string serviceType, string action, IReadOnlyList<(string Name, string Value)> args, CancellationToken ct)
    {
        var sb = new StringBuilder(512);
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">");
        sb.Append("<s:Body><u:").Append(action).Append(" xmlns:u=\"").Append(serviceType).Append("\">");
        foreach (var (name, value) in args)
        {
            sb.Append('<').Append(name).Append('>').Append(EscapeXml(value)).Append("</").Append(name).Append('>');
        }
        sb.Append("</u:").Append(action).Append("></s:Body></s:Envelope>");

        using var req = new HttpRequestMessage(HttpMethod.Post, controlUrl)
        {
            Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/xml"),
        };
        req.Headers.Add("SOAPACTION", $"\"{serviceType}#{action}\"");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private async Task SoapAsync(Uri controlUrl, string serviceType, string action, IReadOnlyList<(string Name, string Value)> args, CancellationToken ct)
    {
        var sb = new StringBuilder(512);
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">");
        sb.Append("<s:Body><u:").Append(action).Append(" xmlns:u=\"").Append(serviceType).Append("\">");
        foreach (var (name, value) in args)
        {
            sb.Append('<').Append(name).Append('>');
            sb.Append(EscapeXml(value));
            sb.Append("</").Append(name).Append('>');
        }
        sb.Append("</u:").Append(action).Append("></s:Body></s:Envelope>");

        using var req = new HttpRequestMessage(HttpMethod.Post, controlUrl)
        {
            Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/xml"),
        };
        req.Headers.Add("SOAPACTION", $"\"{serviceType}#{action}\"");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"DLNA-Action '{action}' an {controlUrl} schlug fehl: HTTP {(int)resp.StatusCode}. Body: {Truncate(body, 400)}");
        }
    }

    private static string BuildDidlLite(string streamUrl, string mimeType, string title)
    {
        // Minimaler DIDL-Lite-Datensatz für audioBroadcast.
        // Wichtige DLNA-Extras im protocolInfo: ORG_PN für das Audio-Format-Profil,
        // ORG_OP=01 = Bytes-Range-Seek nicht unterstützt aber Play schon,
        // ORG_FLAGS=0c500000... = streaming flag set. Ohne diese Hints lehnen viele
        // Renderer (Denon, Sonos) den Stream stumm ab.
        var orgPn = mimeType switch
        {
            "audio/mpeg" => "MP3",
            "audio/wav" or "audio/x-wav" => "LPCM",
            "audio/flac" => "FLAC",
            _ => null,
        };
        var protocolInfo = orgPn is null
            ? $"http-get:*:{mimeType}:*"
            : $"http-get:*:{mimeType}:DLNA.ORG_PN={orgPn};DLNA.ORG_OP=01;DLNA.ORG_FLAGS=01700000000000000000000000000000";
        var sb = new StringBuilder(512);
        sb.Append("<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\">");
        sb.Append("<item id=\"1\" parentID=\"0\" restricted=\"1\">");
        sb.Append("<dc:title>").Append(EscapeXml(title)).Append("</dc:title>");
        sb.Append("<upnp:class>object.item.audioItem.audioBroadcast</upnp:class>");
        sb.Append("<res protocolInfo=\"").Append(EscapeXml(protocolInfo)).Append("\">");
        sb.Append(EscapeXml(streamUrl));
        sb.Append("</res>");
        sb.Append("</item>");
        sb.Append("</DIDL-Lite>");
        return sb.ToString();
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
