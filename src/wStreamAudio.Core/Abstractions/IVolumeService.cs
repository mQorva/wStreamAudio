namespace wStreamAudio.Core.Abstractions;

public interface IVolumeService
{
    /// <summary>Aktuelle System-Lautstärke des Capture-Endpoints in 0–100.</summary>
    int SystemVolumePercent { get; }

    /// <summary>Stellt die direkte LMS-Lautstärke für einen Player ein. Bei Windows-Mute wird 0 gesendet.</summary>
    Task SetTrimAsync(string playerId, int volumePercent, CancellationToken ct = default);

    /// <summary>Legacy-Schalter für gespeicherte Settings; die UI steuert Player-Lautstärke direkt.</summary>
    Task SetAppControlAsync(string playerId, bool enabled, CancellationToken ct = default);

    /// <summary>Wendet Windows-Mute bzw. gespeicherte direkte Lautstärken auf aktive Player an.</summary>
    Task ApplyAllAsync(CancellationToken ct = default);
}

public interface IAutostartService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

public interface ISingleInstance : IDisposable
{
    bool IsFirstInstance { get; }
    /// <summary>
    /// Versucht, der laufenden Instanz <paramref name="command"/> zu schicken.
    /// true = Signal angekommen; false = laufende Instanz nicht erreichbar (Zombie / hängt).
    /// In dem Fall darf die neue Instanz übernehmen.
    /// </summary>
    Task<bool> SignalRunningInstanceAsync(string command, CancellationToken ct = default);
    event EventHandler<string>? CommandReceived;
    Task StartListeningAsync(CancellationToken ct = default);
}

public interface IFirewallService
{
    Task EnsureInboundRuleAsync(string ruleName, int port, CancellationToken ct = default);
    Task RemoveRuleAsync(string ruleName, CancellationToken ct = default);
}
