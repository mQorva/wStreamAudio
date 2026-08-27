# wStreamAudio

wStreamAudio ist eine Windows-Tray-App, die den Ton eines ausgewählten
Windows-Wiedergabegeräts per WASAPI-Loopback abgreift und als lokalen
MP3-Live-Stream im LAN bereitstellt. Die App kann den Stream automatisch an
Logitech Media Server (LMS) / Squeezebox-Player, DLNA/UPnP-Renderer und
AirPlay-1-Empfänger weitergeben.

> Hinweis: Diese App entstand unter intensivem KI-Einsatz auf Basis eines
> bestehenden Projekt-Patterns (Magic-Voice von Ronny Schulz).

## Aktueller Stand

- Tray-App für Windows 10/11 mit WinUI-3-Einstellungen und Mini-Fenster
- Endpoint-Loopback für Default-Device, SPDIF, HDMI und andere aktive
  Render-Endpoints, ohne den Windows-Default ändern zu müssen
- Downmix von Mono/Mehrkanal auf Stereo, Ausgabe als 16-bit-PCM intern
- HTTP-Live-Stream unter `/stream.mp3` mit MP3 128 kbit/s CBR
- LMS-Anbindung per JSON-RPC: Player laden, Power, Lautstärke, Sync,
  Play/Pause/Stop und Stream-URL automatisch starten
- Sample-synchrone Wiedergabe innerhalb der LMS/Squeezebox-Sync-Gruppe
- Direkte DLNA/UPnP-Steuerung per SSDP und AVTransport
- Direkte AirPlay-1-Ausgabe per RAOP/RTSP/RTP für kompatible Empfänger
- Lautstärke-Kopplung mit System-Lautstärke und pro Player/Renderer
  einstellbarem Pegel
- Autostart, Start ins Tray, Wiedergabe beim nächsten Start fortsetzen
- Hell/Dunkel/System-Theme und deutsche/englische UI-Texte
- Einstellungen unter `%LOCALAPPDATA%\wStreamAudio\settings.json`
- Logs unter `%LOCALAPPDATA%\wStreamAudio\logs\`

Nicht fertig oder bewusst begrenzt:

- Per-App-Capture ist im Modell und in der UI vorbereitet, aber die Runtime
  nutzt aktuell nur Endpoint-Loopback.
- AirPlay 2, HomePod und Apple TV werden nicht als echter AirPlay-2-Sender
  unterstützt. Der direkte Sender ist AirPlay 1/RAOP.
- DLNA und AirPlay laufen direkt gegen die Geräte und sind nicht sample-genau
  mit der LMS/Squeezebox-Gruppe synchron.
- FLAC/WAV-Ausgabe ist aktuell nicht der Standardpfad. Der HTTP-Stream ist MP3,
  weil das mit LMS und vielen DLNA-Renderern ohne zusätzliche Transcoding-
  Konfiguration funktioniert.

## Voraussetzungen

- Windows 10 Build 19041+ oder Windows 11, x64
- .NET 10 SDK laut `global.json` zum Entwickeln und Bauen
- .NET Windows Desktop Runtime 10.x auf Zielrechnern
- Windows App Runtime 1.8 x64 auf Zielrechnern
- Für LMS/Squeezebox: ein erreichbarer Logitech Media Server im LAN
- Optional für Setup-Installer: Inno Setup 6
- Optional für Releases: GitHub CLI (`gh`), angemeldet

`Install.ps1` kann die .NET Desktop Runtime 10.x und Windows App Runtime 1.8
bei Bedarf per `winget` oder Direkt-Download installieren.

## Bauen und starten

```powershell
# Build
dotnet build wStreamAudio.sln -c Debug -p:Platform=x64

# App starten (Debug)
dotnet run --project src\wStreamAudio\wStreamAudio.csproj -c Debug -p:Platform=x64

# Tests
dotnet test wStreamAudio.sln
```

## Build, Installation und Update

```powershell
# Release-Payload und, falls Inno Setup vorhanden ist, Setup-Installer erzeugen
.\Build.ps1

# Nur veröffentlichbaren Ordner bauen, ohne Setup-Installer
.\Build.ps1 -SkipInstaller

# Installation aus artifacts\release\wStreamAudio
.\Install.ps1

# Installation aus einem kopierten Payload-Ordner
.\Install.ps1 -SourceDir "D:\Deploy\wStreamAudio"

# Deinstallation, optional mit Nutzerdaten
.\Uninstall.ps1 -RemoveUserData
```

Installiert wird nach `%LOCALAPPDATA%\Programs\wStreamAudio`. Vor Updates
beendet `Install.ps1` eine laufende wStreamAudio-Instanz, räumt den Zielordner
auf und kopiert den neuen Payload. Die Autostart-Einstellung wird ausschließlich
in der App verwaltet.

## GitHub-Sync und Release

```powershell
# Änderungen mit GitHub synchronisieren
.\git-sync.ps1

# Ohne Pull pushen
.\git-sync.ps1 -SkipPull

# Setup-Datei als GitHub-Release-Asset hochladen
.\Build.ps1
.\git-sync.ps1 -Release
```

Die Release-Version kommt aus `Directory.Build.props` (`AppVersion`). Das
Release-Skript erstellt oder aktualisiert den Tag `v<Version>` und lädt
`artifacts\installer\wStreamAudio-Setup-<Version>.exe` hoch.

## Bedienung

- Linksklick auf das Tray-Icon öffnet das Mini-Fenster.
- Rechtsklick auf das Tray-Icon öffnet das Kontextmenü.
- Im Mini-Fenster startet oder stoppt der Play-Button die Pipeline.
- Im Mini-Fenster steuerst du Wiedergabe und Pegel pro sichtbarem Gerät.
- Auf der Streaming-Seite steuerst du, welche Geräte sichtbar sind und welche
  beim Stream-Start mitlaufen.
- In den Einstellungen werden Capture-Profile, Dienste, Stream-Port,
  Firewall-Regel, Autostart, Theme und Sprache verwaltet.
- Änderungen werden automatisch gespeichert.

## Architektur in Kürze

```text
Windows-Render-Endpoint
    -> WASAPI-Loopback (NAudio)
    -> Stereo-16-bit-PCM
    -> HttpStreamServer (/stream.mp3, MP3 128 kbit/s)
       -> LMS / Squeezebox per JSON-RPC und HTTP-Stream
       -> DLNA-Renderer per SSDP/AVTransport
       -> AirPlay-1-Empfänger per RAOP
```

LMS muss im Netz erreichbar sein. Der Host wird in den Einstellungen gepflegt;
der Verbindungstest prüft TCP-Erreichbarkeit und `POST /jsonrpc.js`.

## Datenschutz

wStreamAudio arbeitet lokal im LAN. Die App sendet keine Nutzungs- oder
Audiodaten an Cloud-Dienste. Netzwerkkommunikation findet nur zu den
konfigurierten oder gefundenen Geräten statt: LMS per HTTP/JSON-RPC,
DLNA/UPnP per SSDP/SOAP, AirPlay per mDNS/RAOP sowie der lokale HTTP-
Audio-Stream.

## Lizenz

MIT - siehe [LICENSE](LICENSE). Komponenten Dritter siehe
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Autor: mQorva, Copyright 2026
