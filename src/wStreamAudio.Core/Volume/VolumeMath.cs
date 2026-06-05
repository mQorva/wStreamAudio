using wStreamAudio.Core.Models;

namespace wStreamAudio.Core.Volume;

/// <summary>
/// Reine Logik für die Trim-basierte Lautstärke-Kopplung. Keine Windows- oder
/// Netzwerk-Abhängigkeiten — voll testbar.
/// </summary>
public static class VolumeMath
{
    /// <summary>
    /// Berechnet die effektive Player-Lautstärke aus System-Lautstärke (0–100) und Trim (0–150).
    /// Ergebnis in 0–100 (LMS-Skala) gerundet.
    /// </summary>
    public static int EffectiveVolume(int systemVolumePercent, int trimPercent)
    {
        var raw = systemVolumePercent * (double)trimPercent / 100.0;
        return (int)Math.Round(Math.Clamp(raw, 0, 100), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Aus einer am Player direkt gesetzten Lautstärke den passenden Trim
    /// zurückrechnen (für die LMS-Subscribe-Rückrechnung).
    /// Rückgabe in den persistierten Trim-Bereich (0–150) gechompt.
    /// </summary>
    public static int RecoverTrim(int playerVolumePercent, int systemVolumePercent)
    {
        if (systemVolumePercent <= 0)
        {
            return Defaults.PlayerTrimDefault;
        }

        var raw = playerVolumePercent * 100.0 / systemVolumePercent;
        return (int)Math.Round(Math.Clamp(raw, Defaults.PlayerTrimMin, Defaults.PlayerTrimMax),
            MidpointRounding.AwayFromZero);
    }

    public static int ClampTrim(int trim) => Math.Clamp(trim, Defaults.PlayerTrimMin, Defaults.PlayerTrimMax);
    public static int ClampVolume(int v) => Math.Clamp(v, 0, 100);
}
