namespace wStreamAudio.Core.Abstractions;

/// <summary>
/// Lightweight In-Process-Bus für Player-Zustandsänderungen, die im UI auflaufen.
/// Wer einen Wert ändert (Slider, Toggle, Tray-Menü), ruft <see cref="RaisePlayerChanged"/>
/// auf — alle anderen UI-Komponenten, die denselben Player anzeigen, hören auf
/// <see cref="PlayerChanged"/> und aktualisieren sich sofort. Kein Polling.
/// </summary>
public interface IPlayerStateBus
{
    event EventHandler<PlayerChangedEventArgs>? PlayerChanged;
    void RaisePlayerChanged(PlayerChangedEventArgs args);
}

public sealed class PlayerChangedEventArgs : EventArgs
{
    public required string PlayerId { get; init; }
    public PlayerChangeKind Kind { get; init; }
    public int? Volume { get; init; }
    public bool? Powered { get; init; }
    public bool? Enabled { get; init; }
}

public enum PlayerChangeKind
{
    Volume,
    Power,
    SyncGroup,
    NameOrSettings,
    Enabled,
}
