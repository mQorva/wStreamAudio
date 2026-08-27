namespace wStreamAudio.Core.Volume;

/// <summary>
/// Reine Logik für direkte Player-Lautstärken. Keine Windows- oder Netzwerk-Abhängigkeiten.
/// </summary>
public static class VolumeMath
{
    public static int ClampVolume(int v) => Math.Clamp(v, 0, 100);
}
