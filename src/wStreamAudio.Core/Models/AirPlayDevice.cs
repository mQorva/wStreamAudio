namespace wStreamAudio.Core.Models;

/// <summary>
/// Ein im LAN per mDNS/Bonjour gefundener AirPlay-Empfänger
/// (HomePod, Apple TV, AirPort Express, AirPlay-AVR usw.).
/// </summary>
public sealed class AirPlayDevice
{
    /// <summary>Stabiler Identifier — bei RAOP der „deviceid" (MAC), bei AirPlay-2 die „pi" (Public-Key-Hash).</summary>
    public required string Id { get; init; }

    /// <summary>Anzeigename ohne MAC-Prefix („Wohnzimmer" statt „001122334455@Wohnzimmer").</summary>
    public required string FriendlyName { get; init; }

    /// <summary>IPv4 oder IPv6 des Geräts.</summary>
    public required string Host { get; init; }

    /// <summary>TCP-Port für das RTSP-Handshake.</summary>
    public int Port { get; init; }

    /// <summary>true, wenn das Gerät den klassischen RAOP-Service (`_raop._tcp`) annonciert.</summary>
    public bool SupportsAirPlay1 { get; init; }

    /// <summary>true, wenn das Gerät den AirPlay-2-Service (`_airplay._tcp`) annonciert.</summary>
    public bool SupportsAirPlay2 { get; init; }

    /// <summary>Modellbezeichnung aus dem TXT-Record (z.B. „AppleTV5,3", „AudioAccessory1,1").</summary>
    public string? Model { get; init; }

    /// <summary>Hersteller, falls im TXT-Record enthalten.</summary>
    public string? Manufacturer { get; init; }

    public DateTimeOffset LastSeenUtc { get; init; } = DateTimeOffset.UtcNow;
}
