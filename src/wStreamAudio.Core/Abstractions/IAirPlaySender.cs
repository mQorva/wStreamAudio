using wStreamAudio.Core.Models;

namespace wStreamAudio.Core.Abstractions;

/// <summary>
/// AirPlay-1-Sender (RAOP). Pro aktivem Gerät baut die Implementierung eine RTSP-Session
/// auf, hält einen verschlüsselten RTP-Audio-Stream und meldet Volumen via SET_PARAMETER.
/// Capture-Frames werden über <see cref="PushPcmFrame"/> in einen Ring-Puffer pro
/// Session geschoben — analog zum HTTP-Stream-Server.
/// </summary>
public interface IAirPlaySender
{
    /// <summary>Baut RTSP-Handshake + RTP-Stream zum Gerät auf.</summary>
    Task PlayAsync(AirPlayDevice device, CancellationToken ct = default);

    /// <summary>Schickt TEARDOWN und schließt die Session.</summary>
    Task StopAsync(AirPlayDevice device, CancellationToken ct = default);

    /// <summary>Setzt Lautstärke 0..100. Wird zu RAOP-dB konvertiert (-30..0; 0 → -144 = mute).</summary>
    Task SetVolumeAsync(AirPlayDevice device, int volumePercent, CancellationToken ct = default);

    /// <summary>
    /// Pusht rohe interleaved-stereo-16-bit-PCM-Bytes in alle aktiven RAOP-Sessions.
    /// Wird von der Pipeline pro Capture-Frame aufgerufen.
    /// </summary>
    void PushPcmFrame(ReadOnlySpan<byte> pcm16LeStereo, int sampleRate);
}
