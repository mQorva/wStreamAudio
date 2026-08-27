using System.Globalization;

namespace wStreamAudio.Localization;

/// <summary>
/// Zentrale UI-Texte mit DE/EN. Pages und Fenster rufen <see cref="ApplyTexts"/> in
/// ihrem Ctor und re-applyen via <see cref="LanguageChanged"/>-Event nach Sprachwechsel.
/// </summary>
public static class Strings
{
    public static event EventHandler? LanguageChanged;

    private static string _code = "de";
    public static string Code => _code;

    /// <summary>Setzt den Sprachcode ("de"/"en") und feuert <see cref="LanguageChanged"/>.</summary>
    public static void SetLanguage(string code)
    {
        var normalized = string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "de";
        if (_code == normalized) return;
        _code = normalized;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Liefert "de" für deutsche Systeme, sonst "en". Für Erststart.</summary>
    public static string DetectFromSystem()
    {
        try
        {
            var ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return string.Equals(ui, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
        }
        catch { return "de"; }
    }

    private static bool De => _code == "de";

    // ===== Settings Window — Nav =====
    public static string NavGeneral => De ? "Allgemein" : "General";
    public static string NavAudio => De ? "Audio-Quelle" : "Audio source";
    public static string NavLms => De ? "Dienste" : "Services";
    public static string NavStreaming => De ? "Streaming" : "Streaming";
    public static string NavAbout => De ? "Über" : "About";
    public static string WindowTitle => De ? "wStreamAudio — Einstellungen" : "wStreamAudio — Settings";

    // ===== Common =====
    public static string On => De ? "an" : "on";
    public static string Off => De ? "aus" : "off";
    public static string Save => De ? "speichern" : "save";
    public static string Cancel => De ? "abbrechen" : "cancel";

    // ===== General page =====
    public static string GenAutostartHeader => De ? "mit Windows starten" : "Start with Windows";
    public static string GenAutostartDesc => De ? "wStreamAudio läuft automatisch beim Login." : "wStreamAudio runs automatically at login.";
    public static string GenLaunchHiddenHeader => De ? "beim Start minimieren" : "Start minimized";
    public static string GenLaunchHiddenDesc => De ? "Kein Fenster beim Start öffnen, direkt ins Tray." : "Don't open a window at launch — go straight to the tray.";
    public static string GenAutoActivateHeader => De ? "neue Geräte automatisch aktivieren" : "Auto-activate new devices";
    public static string GenAutoActivateDesc => De
        ? "Frisch entdeckte Player, DLNA-Renderer und AirPlay-Empfänger landen sofort im Mini-Fenster."
        : "Newly discovered players, DLNA renderers and AirPlay receivers show up in the mini window immediately.";
    public static string GenResumePlaybackHeader => De ? "beim Start abspielen fortsetzen" : "Resume playback on start";
    public static string GenResumePlaybackDesc => De
        ? "Wenn beim Beenden gestreamt wurde, läuft der Stream beim nächsten Start automatisch wieder los."
        : "If the stream was running at exit, it resumes automatically on next start.";
    public static string GenMiniWindowHeader => De ? "Mini-Fenster anzeigen" : "Show mini window";
    public static string GenMiniWindowDesc => De
        ? "Blendet das Mini-Fenster ein und fixiert es. Synchron mit dem Tray-Menü."
        : "Shows the mini window and pins it. Synced with the tray menu.";
    public static string GenThemeHeader => De ? "Theme" : "Theme";
    public static string GenThemeDesc => De ? "System / Hell / Dunkel" : "System / Light / Dark";
    public static string GenThemeSystem => De ? "System" : "System";
    public static string GenThemeLight => De ? "Hell" : "Light";
    public static string GenThemeDark => De ? "Dunkel" : "Dark";
    public static string GenLanguageHeader => De ? "Sprache" : "Language";
    public static string GenLanguageDesc => De ? "Deutsch oder Englisch." : "German or English.";
    public static string GenLanguageDe => De ? "Deutsch" : "German";
    public static string GenLanguageEn => De ? "Englisch" : "English";

    // ===== Streaming page =====
    public static string StreamUrlHeader => De ? "Stream-URL (so erreicht LMS uns)" : "Stream URL (how LMS reaches us)";
    public static string StreamUrlDesc => De
        ? "Diese URL wird LMS beim Klick auf abspielen automatisch übergeben — auf LMS-Seite musst du nichts konfigurieren."
        : "LMS gets this URL automatically when you press play — no LMS-side config required.";
    public static string OpenInBrowser => De ? "im Browser öffnen" : "open in browser";
    public static string TestLocally => De ? "lokal testen" : "test locally";
    public static string HttpPortHeader => De ? "HTTP-Port für Audio-Stream" : "HTTP port for audio stream";
    public static string HttpPortDesc => De
        ? "Auf diesem Port lauscht wStreamAudio — LMS holt sich hier den Audio-Stream ab."
        : "wStreamAudio listens on this port — LMS pulls the audio stream from here.";
    public static string FirewallHeader => De ? "Firewall-Regel automatisch setzen" : "Set firewall rule automatically";
    public static string FirewallDesc => De ? "Eingehender TCP-Port für LMS — UAC erforderlich." : "Inbound TCP port for LMS — requires UAC.";
    public static string LevelHeader => De ? "Audio-Pegel (Capture)" : "Audio level (capture)";
    public static string LevelDesc => De
        ? "Spitzenpegel der laufenden Aufnahme. Bewegt sich der Balken nicht, kommt aus dem gewählten Endpoint kein Audio."
        : "Peak level of the running capture. If the bar doesn't move, the selected endpoint isn't producing audio.";
    public static string LevelInactive => De ? "Stream nicht aktiv." : "Stream not active.";
    public static string KnownPlayers => De ? "bekannte Player" : "known players";
    public static string ReloadPlayers => De ? "Player vom LMS neu laden" : "Reload players from LMS";
    public static string DlnaHeader => De ? "DLNA-Renderer (direkt)" : "DLNA renderers (direct)";
    public static string DlnaDesc => De
        ? "Direkt-Wiedergabe ohne LMS — gut für einzelne Geräte (Smart-TV, AVR), aber NICHT sample-synchron mit Squeeze-Playern."
        : "Direct playback without LMS — good for single devices (smart TV, AVR), but NOT sample-synchronous with Squeeze players.";
    public static string DiscoverRenderers => De ? "Renderer im LAN suchen" : "Discover renderers on LAN";

    // ===== LMS page =====
    public static string LmsAutoDiscoverHeader => De ? "Auto-Discover" : "Auto-discover";
    public static string LmsAutoDiscoverDesc => De ? "LMS im LAN automatisch finden." : "Find LMS on LAN automatically.";
    public static string LmsHostHeader => De ? "Host" : "Host";
    public static string LmsPortHeader => De ? "Port" : "Port";
    public static string TestConnection => De ? "Verbindung testen" : "test connection";

    // ===== About page =====
    public static string AboutHeader => De ? "Über wStreamAudio" : "About wStreamAudio";
}
