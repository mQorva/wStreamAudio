# wStreamAudio

wStreamAudio überträgt den Ton eines Windows-Wiedergabegeräts als lokalen
MP3-Live-Stream an Logitech Media Server (LMS), DLNA/UPnP-Renderer und
kompatible AirPlay-1-Empfänger. Die App läuft im Windows-Infobereich und
ändert das Windows-Standard-Wiedergabegerät nicht.

> **Projektstatus:** wStreamAudio ist eine Vorabversion. LMS ist der
> Hauptpfad. Direkte DLNA- und insbesondere AirPlay-1-Wiedergabe hängt vom
> jeweiligen Empfänger ab und ist noch nicht auf einer breiten Gerätebasis
> verifiziert.

## Installation unter Windows

1. Auf der [GitHub-Release-Seite](https://github.com/mQorva/wStreamAudio/releases)
   den neuesten `wStreamAudio-Setup-<Version>.exe` herunterladen.
2. Die Setup-Datei starten und den Anweisungen folgen.
3. wStreamAudio anschließend über das Startmenü oder direkt nach Abschluss
   des Setups öffnen.

Das Setup installiert wStreamAudio für den aktuellen Benutzer unter
`%LOCALAPPDATA%\Programs\wStreamAudio`. Fehlende Microsoft-Komponenten werden
bei Bedarf nachinstalliert:

- .NET Windows Desktop Runtime 10.x
- Windows App Runtime 1.8 x64

Für ein Update wird einfach das Setup einer neueren Version ausgeführt. Eine
laufende wStreamAudio-Instanz wird dabei beendet. Einstellungen und Logs unter
`%LOCALAPPDATA%\wStreamAudio` bleiben erhalten.

> **Windows-Sicherheitshinweis:** Die derzeit veröffentlichten Setup-Dateien
> sind noch nicht digital signiert. Windows kann deshalb eine
> SmartScreen-Warnung anzeigen. Releases ausschließlich aus diesem Repository
> herunterladen.

## Funktionen

- Aufnahme eines aktiven Windows-Render-Endpoints per WASAPI-Loopback, zum
  Beispiel Standardgerät, SPDIF oder HDMI
- Downmix von Mono oder Mehrkanal auf internes Stereo-PCM mit 16 Bit
- lokaler MP3-Live-Stream mit 128 kbit/s CBR unter `/stream.mp3`
- LMS-Steuerung per JSON-RPC: Player laden, ein- und ausschalten, Lautstärke,
  Synchronisationsgruppe, Wiedergabe, Pause und Stopp
- direkte DLNA/UPnP-Steuerung per SSDP, AVTransport und RenderingControl
- experimentelle direkte AirPlay-1-Ausgabe per RAOP/RTSP/RTP
- Mini-Fenster und Tray-Menü zur Steuerung von Stream und Wiedergabegeräten
- Autostart, Start ins Tray und optionales Fortsetzen der letzten Wiedergabe
- System-, helles und dunkles Farbschema
- deutsche Oberfläche; eine englische Übersetzung ist teilweise vorhanden

Innerhalb einer LMS/Squeezebox-Synchronisationsgruppe übernimmt LMS die
zeitliche Synchronisierung. Direkt angesteuerte DLNA- und AirPlay-Geräte sind
nicht mit dieser Gruppe samplegenau synchronisiert.

## Bekannte Einschränkungen

- Per-App-Capture ist in Datenmodell und Oberfläche vorbereitet, wird von der
  Aufnahmeruntime aber noch nicht unterstützt. Verwendbar ist ausschließlich
  Endpoint-Loopback.
- Die im Capture-Profil wählbare Ziel-Samplerate wird vom Endpoint-Pfad noch
  nicht angewendet. Aufgenommen wird im WASAPI-Mixformat.
- AirPlay 2 wird nicht unterstützt. HomePod und Apple TV sind deshalb keine
  verlässlich unterstützten Ziele.
- Direkte DLNA- und AirPlay-Wiedergabe ist nicht mit einer LMS-Gruppe
  synchronisiert.
- Der HTTP-Ausgabepfad stellt MP3 bereit, nicht WAV oder FLAC.
- Teile der Oberfläche sind noch ausschließlich deutsch.

Der ausführlichere, nach Implementierung und Verifikation getrennte Stand
steht in [docs/PLAN.md](docs/PLAN.md).

## Bedienung

- Linksklick auf das Tray-Icon öffnet das Mini-Fenster.
- Rechtsklick öffnet das Kontextmenü.
- Der zentrale Wiedergabeschalter startet oder beendet Aufnahme und Stream.
- Auf der Streaming-Seite werden sichtbare und beim Start mitlaufende Geräte
  ausgewählt.
- Einstellungen werden automatisch gespeichert.

LMS muss im Netzwerk erreichbar sein. Host und Port werden in den
Einstellungen eingetragen. Der Verbindungstest prüft die TCP-Erreichbarkeit
und eine Anfrage an `/jsonrpc.js`.

## Voraussetzungen

Für die installierte App:

- Windows 10 Build 19041 oder neuer beziehungsweise Windows 11, x64
- Netzwerkzugriff auf die gewünschten Empfänger
- für Squeezebox-Player ein erreichbarer Logitech Media Server

Die benötigten Microsoft-Runtimes werden vom Setup geprüft.

## Entwicklung

Erforderlich sind das in `global.json` festgelegte .NET-10-SDK und für den
Setup-Build optional Inno Setup 6.

```powershell
# Projekt bauen
dotnet build wStreamAudio.sln -c Debug -p:Platform=x64

# Tests ausführen
dotnet test wStreamAudio.sln

# App im Debug-Modus starten
dotnet run --project src\wStreamAudio\wStreamAudio.csproj -c Debug -p:Platform=x64

# Release-Payload und, wenn Inno Setup vorhanden ist, Setup erzeugen
.\Build.ps1

# Nur den veröffentlichbaren Anwendungsordner erzeugen
.\Build.ps1 -SkipInstaller
```

`Install.ps1` bleibt als manueller Installationsweg für lokale Builds und
kopierte Payload-Ordner verfügbar:

```powershell
.\Install.ps1
.\Install.ps1 -SourceDir "D:\Deploy\wStreamAudio"
.\Uninstall.ps1 -RemoveUserData
```

## Release für Maintainer

Die Version steht zentral als `AppVersion` in `Directory.Build.props`. Vor
einer Veröffentlichung müssen Build, Tests und der erzeugte Installer geprüft
werden.

```powershell
.\Build.ps1 -Clean
.\git-sync.ps1 -Release
```

Das Release-Skript verwendet den Tag `v<Version>` und das Asset
`artifacts\installer\wStreamAudio-Setup-<Version>.exe`. Ein vorhandener Tag
wird nicht auf einen anderen Commit verschoben. Für jede veröffentlichte
Version muss daher ein eigener, zur Versionsnummer passender Commit vorhanden
sein.

## Lokale Daten und Datenschutz

- Einstellungen: `%LOCALAPPDATA%\wStreamAudio\settings.json`
- Logs: `%LOCALAPPDATA%\wStreamAudio\logs\`

Während der Wiedergabe werden Audio- und Steuerdaten nur im lokalen Netzwerk
an die konfigurierten beziehungsweise gefundenen Geräte übertragen. Die App
sendet keine Nutzungs- oder Audiodaten an einen Cloud-Dienst. Setup und
PowerShell-Installation können Microsoft-Dienste kontaktieren, um fehlende
Runtimes herunterzuladen.

## Lizenz

MIT, siehe [LICENSE](LICENSE). Hinweise zu Komponenten Dritter stehen in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Copyright 2026 mQorva
