namespace wStreamAudio.Core.Abstractions;

/// <summary>
/// Native DLNA/UPnP-Anbindung — sucht <c>MediaRenderer</c>-Geräte im LAN per SSDP
/// und steuert sie via AVTransport-SOAP. Direkte Wiedergabe ohne LMS
/// (und damit ohne Multiroom-Sync mit Squeeze-Playern).
/// </summary>
public interface IDlnaService
{
    /// <summary>
    /// Sucht Renderer im LAN. Sammelt SSDP-Antworten für die angegebene Dauer
    /// und parst die Geräte-Beschreibungen.
    /// </summary>
    Task<IReadOnlyList<DlnaRenderer>> DiscoverRenderersAsync(TimeSpan timeout, CancellationToken ct = default);

    /// <summary>Schickt SetAVTransportURI + Play an den Renderer.</summary>
    Task PlayUrlAsync(DlnaRenderer renderer, string streamUrl, string mimeType = "audio/mpeg", string title = "wStreamAudio Live", int prebufferMs = 0, CancellationToken ct = default);

    /// <summary>Stopp am Renderer.</summary>
    Task StopAsync(DlnaRenderer renderer, CancellationToken ct = default);

    /// <summary>Lautstärke 0..100 setzen (sofern <c>RenderingControl</c> vorhanden).</summary>
    Task SetVolumeAsync(DlnaRenderer renderer, int percent, CancellationToken ct = default);

    /// <summary>Aktuelle Lautstärke 0..100 abfragen. Null, wenn nicht ermittelbar.</summary>
    Task<int?> GetVolumeAsync(DlnaRenderer renderer, CancellationToken ct = default);
}

public sealed class DlnaRenderer
{
    /// <summary>UPnP Unique Device Name. Stabil über Sessions hinweg.</summary>
    public required string Udn { get; init; }

    public required string FriendlyName { get; init; }

    /// <summary>Vollständige URL des AVTransport-Service-Endpoints.</summary>
    public required Uri AvTransportControlUrl { get; init; }

    /// <summary>Optional: URL des RenderingControl-Service. Null, wenn der Renderer das nicht meldet.</summary>
    public Uri? RenderingControlUrl { get; init; }

    public string? Manufacturer { get; init; }
    public string? ModelName { get; init; }
}
