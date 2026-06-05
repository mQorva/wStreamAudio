using wStreamAudio.Core.Models;

namespace wStreamAudio.Core.Abstractions;

/// <summary>
/// Findet AirPlay-Receiver per mDNS/Bonjour. Default ist eine einmalige Suche;
/// für längere Beobachtung kann ein Continuous-Mode angeboten werden.
/// </summary>
public interface IAirPlayDiscovery
{
    /// <summary>Einmalige Suche im LAN. Wartet bis zur Timeout-Frist und liefert dann das Ergebnis.</summary>
    Task<IReadOnlyList<AirPlayDevice>> DiscoverAsync(TimeSpan? timeout = null, CancellationToken ct = default);
}
