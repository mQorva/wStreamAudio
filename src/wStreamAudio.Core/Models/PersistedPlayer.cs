namespace wStreamAudio.Core.Models;

/// <summary>
/// Persistierte Settings eines bekannten LMS-Players. Identifikation primär per
/// MAC-Adresse (in LMS stabil); Settings bleiben erhalten, auch wenn der Player
/// gerade offline ist.
/// </summary>
public sealed class PersistedPlayer
{
    /// <summary>LMS-Player-ID (MAC, z.B. "00:04:20:11:22:33").</summary>
    public required string Id { get; set; }

    /// <summary>Vom LMS gemeldeter Name beim letzten Discovery.</summary>
    public string? LastSeenName { get; set; }

    /// <summary>Optional vom Nutzer überschriebener Anzeigename.</summary>
    public string? CustomName { get; set; }

    /// <summary>Wenn true, kontrolliert wStreamAudio die Lautstärke dieses Players via Trim.</summary>
    public bool AppControlsVolume { get; set; }

    /// <summary>Trim in Prozent (0–150). Multipliziert mit System-Lautstärke.</summary>
    public int TrimPercent { get; set; } = Defaults.PlayerTrimDefault;

    /// <summary>UTC-Zeitstempel des letzten Online-Sichtens (für UI-Anzeige).</summary>
    public DateTimeOffset? LastSeenUtc { get; set; }

    /// <summary>Wenn true, ist der Player Mitglied der zuletzt aktiven Multiroom-Gruppe.</summary>
    public bool InActiveSyncGroup { get; set; }

    /// <summary>
    /// Wird auf true gesetzt, wenn der Player auf demselben Rechner wie wStreamAudio läuft
    /// (gleiche IP-Adresse). Solche Player sind eine Audio-Schleife — werden aus der UI
    /// herausgefiltert. Wird bei jedem Snapshot-Merge aus der Live-IP aktualisiert.
    /// </summary>
    public bool IsLocalDevice { get; set; }

    /// <summary>
    /// Vom User ein-/ausschaltbar. Wenn false: Mini-Fenster blendet den Player komplett aus,
    /// in den Einstellungen wird die Karte grau dargestellt und reagiert nicht auf Klicks.
    /// Default true.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Reihenfolge der Karte im Hauptfenster (Drag&amp;Drop-Reorder). 0 = noch nicht durchnummeriert
    /// (Migration alter Settings); beim ersten Rebuild der Liste wird 1, 2, 3, … vergeben.
    /// Neue Geräte hängen sich ans Ende (max + 1). Verschwundene Geräte behalten ihren Slot.
    /// </summary>
    public int SortOrder { get; set; }
}
