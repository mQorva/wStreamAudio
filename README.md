# wStreamAudio

Tray-Tool für Windows 11, das das System-Audio (auch vom **SPDIF-Endpoint
ohne Default-Wechsel**) abgreift und an die Multiroom-Welten **Squeezebox /
LMS**, **AirPlay** und **DLNA/UPnP** gleichzeitig verteilt — sample-genau
synchron, weil ein vorhandener Logitech Media Server (LMS) als zentraler
Sync-Master genutzt wird.

> Hinweis: Diese App entstand unter intensivem KI-Einsatz auf Basis eines
> bestehenden Projekt-Patterns (Magic-Voice von Ronny Schulz).

## Funktionsumfang (Ziel)

- Loopback-Capture eines beliebigen Render-Endpoints (Default, **SPDIF**,
  HDMI, …) ohne den System-Default zu ändern
- **Per-App-Capture** (WASAPI Process Loopback, Windows 10 Build 19041+) —
  z.B. nur Spotify, nur Browser
- PCM-WAV-HTTP-Live-Stream als LMS-Quelle (für LAN-Bandbreite ausreichend;
  FLAC bewusst nicht implementiert)
- Multiroom-Verbindung beliebiger LMS-Player im Quick-Popup
- AirPlay- und DLNA-Geräte (z.B. Denon-AVR, HomePod, Smart-TV) werden direkt
  per mDNS bzw. SSDP gefunden und ohne externe Brücken angesteuert
- Optionale Lautstärke-Kopplung pro Player („App steuert Lautstärke" mit
  Trim relativ zur System-Lautstärke)
- Tray-Bedienung: Linksklick = Quick-Popup, Rechtsklick = Kontextmenü mit
  Stream/Pause/Capture-Profil/Einstellungen
- Autostart mit Windows, direkt unsichtbar ins Tray
- Settings auto-gespeichert in `%LOCALAPPDATA%\wStreamAudio\settings.json`

## Voraussetzungen

- Windows 10 (Build 19041+) oder Windows 11 — x64
- .NET 10 SDK (siehe `global.json`)
- Visual Studio 2026 oder neuer mit „Windows App SDK"-Workload, oder
  `dotnet`-CLI mit installiertem WindowsAppSDK 1.8 Bootstrapper
- Vorhandener Logitech Media Server (LMS) im LAN — die App ist Sender, nicht
  Server

## Bauen und starten

```powershell
# Build
dotnet build wStreamAudio.sln -c Debug -p:Platform=x64

# App starten (Debug)
dotnet run --project src\wStreamAudio\wStreamAudio.csproj -c Debug

# Tests
dotnet test wStreamAudio.sln
```

## Verteilung / Installer

```powershell
# Build-Artefakte erzeugen
.\Build.ps1

# Installation am Zielrechner
.\Install.ps1

# Deinstallation
.\Uninstall.ps1 -RemoveUserData
```

`Install.ps1` legt die App nach `%LOCALAPPDATA%\Programs\wStreamAudio` und
installiert (sofern fehlend) die nötigen Microsoft-Runtimes über `winget`
oder per Direkt-Download. Die Einstellung „mit Windows starten“ wird
ausschließlich in der App verwaltet und dauerhaft gespeichert.

## GitHub-Sync und Release

```powershell
# Änderungen mit GitHub synchronisieren
.\Sync-GitHub.ps1

# Nur pushen, wenn kein Pull gewünscht ist
.\Sync-GitHub.ps1 -SkipPull
```

Für GitHub-Veröffentlichungen wird die Setup-Datei als Release-Asset
hochgeladen, damit Nutzer nicht das komplette Repository laden müssen:

```powershell
.\Build.ps1
.\Sync-GitHub.ps1 -Release
```

Das nutzt die `AppVersion` aus `Directory.Build.props`, erstellt/aktualisiert
den Tag `v<Version>` und lädt
`artifacts\installer\wStreamAudio-Setup-<Version>.exe` in das GitHub Release
hoch. Das Skript fragt `Draft?` und `Pre-Release?` direkt mit ja/nein ab.
Voraussetzung: GitHub CLI (`gh`) ist installiert und angemeldet.

Ein bestehendes Release wird dabei nur überschrieben, wenn die Nachfrage im
Skript bestätigt wird.

## Bedienung

- **Linksklick auf Tray-Icon** → Quick-Popup mit Stream-Toggle,
  Capture-Profil-Wahl, Player-Liste und Multiroom-Sync.
- **Rechtsklick auf Tray-Icon** → Stream starten/stoppen, Pause,
  Capture-Profil wechseln, Einstellungen, Beenden.
- **Doppelklick** → bewusst keine Aktion (verhindert versehentliches
  Toggle).
- **Settings-Fenster** → Allgemein, Audio-Quelle (Profile),
  Dienste, Streaming, Über. Änderungen werden sofort
  gespeichert (debounced 300 ms).

## Architektur in Kürze

```
Windows-Audio (SPDIF / Default / Per-App)
    → WASAPI-Loopback → Resampler → PCM/WAV-Encoder
    → Kestrel HTTP-Server (stream.wav)
    → LMS auf NAS/Pi (sample-synchroner Multiroom-Master)
        → echte Squeeze-Player
    → DLNA-Renderer (Smart-TV, AVR) direkt via SSDP/AVTransport
    → AirPlay-Empfänger direkt via mDNS
```

LMS muss im Netz erreichbar sein. Die App findet den Server per mDNS
(`_slimproto._tcp`) oder über den manuell eingetragenen Host in den
Einstellungen.

## Datenschutz

wStreamAudio ist ausschließlich lokal. Es werden keine Daten an Server von
Anthropic, Microsoft oder Dritten gesendet. Kommunikation erfolgt nur im
LAN: zum LMS und zu AirPlay-/UPnP-Geräten via mDNS/SSDP. Logs liegen unter
`%LOCALAPPDATA%\wStreamAudio\logs\`.

## Lizenz

MIT — siehe [LICENSE](LICENSE). Komponenten Dritter siehe
[THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES.md).

Autor: Ronny Schulz · © 2026
