using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using wStreamAudio.Core.Models;

namespace wStreamAudio.Core.Settings;

public enum AppTheme { System, Light, Dark }

public sealed class GeneralSettings : INotifyPropertyChanged
{
    private bool _autostart;
    private bool _launchMinimizedToTray;
    private bool _resumePlaybackOnStart;
    private bool _autoActivateNewDevices = true;
    private AppTheme _theme = AppTheme.System;
    private string _languageCode = "de";

    public bool Autostart { get => _autostart; set => Set(ref _autostart, value); }
    public bool LaunchMinimizedToTray { get => _launchMinimizedToTray; set => Set(ref _launchMinimizedToTray, value); }
    /// <summary>Wenn true und beim letzten Beenden lief der Stream: beim Start wieder anwerfen.</summary>
    public bool ResumePlaybackOnStart { get => _resumePlaybackOnStart; set => Set(ref _resumePlaybackOnStart, value); }
    /// <summary>Wenn true: neu entdeckte Player/Renderer/AirPlay-Empfänger landen mit IsEnabled=true
    /// in den Settings und tauchen sofort im Mini-Fenster auf. Wenn false: bleiben deaktiviert,
    /// der User muss sie auf der Streaming-Seite manuell aktivieren.</summary>
    public bool AutoActivateNewDevices { get => _autoActivateNewDevices; set => Set(ref _autoActivateNewDevices, value); }
    public AppTheme Theme { get => _theme; set => Set(ref _theme, value); }
    public string LanguageCode { get => _languageCode; set => Set(ref _languageCode, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class LmsServerSettings : INotifyPropertyChanged
{
    private bool _autoDiscover = true;
    private string _host = "lms.local";
    private int _port = Defaults.LmsHttpPort;

    public bool AutoDiscover { get => _autoDiscover; set => Set(ref _autoDiscover, value); }
    public string Host { get => _host; set => Set(ref _host, value); }
    public int Port { get => _port; set => Set(ref _port, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class StreamingSettings : INotifyPropertyChanged
{
    private int _httpPort = Defaults.StreamHttpPort;
    private bool _setFirewallRule = true;
    private bool _playersFollowSystemVolume = true;

    public int HttpPort { get => _httpPort; set => Set(ref _httpPort, value); }
    public bool SetFirewallRule { get => _setFirewallRule; set => Set(ref _setFirewallRule, value); }
    public bool PlayersFollowSystemVolume { get => _playersFollowSystemVolume; set => Set(ref _playersFollowSystemVolume, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// Master-Schalter für die drei Ausgabe-Dienste. Wer z.B. nur DLNA nutzt, kann LMS hier aus
/// machen — dann verschwinden die zugehörigen Sektionen in der UI und die Pipeline spart sich
/// Discovery/Streaming-Versuche.
/// </summary>
public sealed class ServicesSettings : INotifyPropertyChanged
{
    private bool _squeezeBox = true;
    private bool _dlna = true;
    private bool _airPlay = true;
    private int _dlnaBufferMs = 3000;
    private bool _dlnaAutoDiscover = true;
    private bool _airPlayAutoDiscover = true;

    public bool SqueezeBox { get => _squeezeBox; set => Set(ref _squeezeBox, value); }
    public bool Dlna { get => _dlna; set => Set(ref _dlna, value); }
    public bool AirPlay { get => _airPlay; set => Set(ref _airPlay, value); }

    /// <summary>Puffergröße für DLNA-Renderer in ms. Höhere Werte stabilisieren gegen
    /// Netzwerk-Aussetzer, niedrigere reduzieren die Start-Latenz. Default 3000 ms.</summary>
    public int DlnaBufferMs { get => _dlnaBufferMs; set => Set(ref _dlnaBufferMs, value); }

    /// <summary>Beim Öffnen der Streaming-Seite automatisch SSDP-Discovery anstoßen.</summary>
    public bool DlnaAutoDiscover { get => _dlnaAutoDiscover; set => Set(ref _dlnaAutoDiscover, value); }

    /// <summary>Beim Öffnen der Streaming-Seite automatisch mDNS-Discovery anstoßen.</summary>
    public bool AirPlayAutoDiscover { get => _airPlayAutoDiscover; set => Set(ref _airPlayAutoDiscover, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class SettingsModel
{
    public GeneralSettings General { get; set; } = new();
    public LmsServerSettings Lms { get; set; } = new();
    public StreamingSettings Streaming { get; set; } = new();
    public ServicesSettings Services { get; set; } = new();

    public ObservableCollection<CaptureProfile> CaptureProfiles { get; set; } = new();
    public string? ActiveCaptureProfileId { get; set; }

    /// <summary>Persistierte Player-Settings, identifiziert per Id (MAC).</summary>
    public ObservableCollection<PersistedPlayer> Players { get; set; } = new();

    /// <summary>Persistierte direkt-angesteuerte DLNA-Renderer (ohne LMS-Sync).</summary>
    public ObservableCollection<PersistedDlnaRenderer> DlnaRenderers { get; set; } = new();

    /// <summary>Persistierte AirPlay-Empfänger (ohne LMS-Sync). Beim Start per mDNS aktualisiert.</summary>
    public ObservableCollection<PersistedAirPlayDevice> AirPlayDevices { get; set; } = new();

    /// <summary>Letzte Position/Größe des Settings-Fensters.</summary>
    public WindowPlacement SettingsWindow { get; set; } = new();

    /// <summary>Letzte Position des Quick-Popups. Größe ist fix.</summary>
    public WindowPlacement QuickPopupPlacement { get; set; } = new();

    /// <summary>
    /// Pin-Icon im Mini-Fenster: wenn true, bleibt das Mini-Fenster immer im Vordergrund
    /// (Always-on-top). Der Auto-Hide-bei-Fokus-Verlust ist generell aus — das Fenster
    /// bleibt also offen, egal wohin der User klickt. Pin steuert nur noch die Z-Order.
    /// </summary>
    public bool QuickPopupSticky { get; set; }

    /// <summary>Beim letzten Beenden war das Mini-Fenster offen? Wird beim Start ausgewertet,
    /// um es automatisch wieder hochzufahren. Wird vom Schließen-Button und vom
    /// „Mini-Fenster anzeigen"-Toggle in Allgemein gepflegt.</summary>
    public bool QuickPopupOpen { get; set; }

    /// <summary>Wird beim Beenden auf den aktuellen Streaming-Zustand gesetzt; beim Start
    /// zusammen mit <see cref="GeneralSettings.ResumePlaybackOnStart"/> ausgewertet.</summary>
    public bool WasStreamingAtExit { get; set; }
}

public sealed class PersistedAirPlayDevice
{
    public required string Id { get; set; }
    public required string FriendlyName { get; set; }
    public required string Host { get; set; }
    public int Port { get; set; }
    public bool SupportsAirPlay2 { get; set; }
    public string? Model { get; set; }
    public string? Manufacturer { get; set; }
    public DateTimeOffset? LastSeenUtc { get; set; }
    public string? CustomName { get; set; }
    /// <summary>„aktiv"-CheckBox = im Mini-Fenster sichtbar.</summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>Lautsprecher-Toggle = Stream läuft auf diesem Empfänger mit.</summary>
    public bool IsPlayActive { get; set; }
    public int VolumePercent { get; set; } = 50;
    /// <summary>Reihenfolge der Karte im Hauptfenster. 0 = noch nicht durchnummeriert (Migration).</summary>
    public int SortOrder { get; set; }
}

public sealed class PersistedDlnaRenderer
{
    public required string Udn { get; set; }
    public required string FriendlyName { get; set; }
    public required string AvTransportControlUrl { get; set; }
    public string? RenderingControlUrl { get; set; }
    public string? Manufacturer { get; set; }
    public string? ModelName { get; set; }
    public DateTimeOffset? LastSeenUtc { get; set; }
    public string? CustomName { get; set; }
    /// <summary>„aktiv"-CheckBox = im Mini-Fenster sichtbar.</summary>
    public bool IsEnabled { get; set; } = true;
    /// <summary>Lautsprecher-Toggle = Stream läuft auf diesem Renderer mit.</summary>
    public bool IsPlayActive { get; set; }
    public int VolumePercent { get; set; } = 50;
    /// <summary>Reihenfolge der Karte im Hauptfenster. 0 = noch nicht durchnummeriert (Migration).</summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// Position und Größe eines Fensters in Pixeln (DPI-abhängig). Felder sind nullable —
/// null bedeutet "noch nie gespeichert", dann nimmt das Fenster Default-Werte.
/// </summary>
public sealed class WindowPlacement
{
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
}
