namespace wStreamAudio.Core.Models;

/// <summary>
/// Aktueller Online-Zustand eines Players (nicht persistiert).
/// Wird vom LMS-Client gefüllt und mit PersistedPlayer kombiniert für die UI.
/// </summary>
public sealed class PlayerSnapshot
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public PlayerKind Kind { get; init; } = PlayerKind.Squeeze;
    public bool IsConnected { get; init; }
    public bool IsPowered { get; init; }
    public bool IsPlaying { get; init; }
    public int Volume { get; init; }
    public string? SyncMaster { get; init; }
    /// <summary>IP-Adresse des Players (ohne Port). Wird verwendet, um den eigenen
    /// Rechner als „Loopback-Gerät" zu erkennen und aus der UI auszublenden.</summary>
    public string? Ip { get; init; }
}
