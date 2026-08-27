# wStreamAudio - Roadmap und Ist-Stand

Stand: 2026-06-05. Dieses Dokument beschreibt den technischen Ist-Stand der
App. Die [README.md](../README.md) ist die kurze Nutzer- und Build-Doku.

## Kurzfazit

wStreamAudio ist aktuell eine WinUI-3-Tray-App für lokalen LAN-Audiostream.
Der stabile Hauptpfad ist:

1. Windows-Render-Endpoint per WASAPI-Loopback aufnehmen.
2. Audio intern als Stereo-16-bit-PCM verarbeiten.
3. Per HTTP als MP3-Live-Stream unter `/stream.mp3` bereitstellen.
4. LMS/Squeezebox, DLNA-Renderer und AirPlay-1-Empfänger aus der App heraus
   ansteuern.

Sample-genaue Synchronität gilt nur innerhalb der LMS/Squeezebox-Sync-Gruppe.
DLNA und AirPlay werden direkt angesprochen und laufen unabhängig.

## Architektur

```text
Windows-Render-Endpoints
    -> WasapiLoopbackSource (NAudio)
       -> Downmix auf Stereo, 16-bit-PCM
       -> HttpStreamServer
          -> MP3 128 kbit/s CBR, pro HTTP-Client eigener LAME-Encoder
          -> /stream.mp3
             -> LMS / Squeezebox-Player
             -> DLNA/UPnP-Renderer
       -> RaopSender
          -> AirPlay 1 / RAOP via RTSP + RTP
```

Steuerung:

- LMS per JSON-RPC (`serverstatus`, `power`, `mixer volume`, `sync`,
  `playlist play`, `pause`, `stop`)
- DLNA per SSDP-Discovery und UPnP-AVTransport/RenderingControl
- AirPlay per mDNS-Discovery und AirPlay-1/RAOP-Sender
- Tray-Icon per Win32 `Shell_NotifyIcon`
- WinUI-3-Fenster für Mini-Fenster, Einstellungen und Über-Dialog
- Settings-Persistenz mit Debounce und atomischem Replace

## Status pro Bereich

Legende: fertig, eingeschränkt, offen, bewusst nicht geplant

### Capture und Streaming

| Feature | Status | Hinweis |
|---|---|---|
| WASAPI-Endpoint-Loopback | fertig | `WasapiLoopbackSource`, `WindowsAudioEndpointCatalog` |
| Default-Endpoint folgen | fertig | Profil kann dem Windows-Default folgen |
| Konkreter Endpoint, z. B. SPDIF/HDMI | fertig | kein Default-Wechsel nötig |
| Mehrkanal-Downmix auf Stereo | fertig | ITU-ähnlicher Downmix, LFE ignoriert |
| Keep-Alive bei Stille | fertig | Silence-Timer hält HTTP-Clients offen |
| Capture-Pegel | fertig | gedrosselte Level-Events für UI |
| HTTP-Live-Stream | fertig | TCP-Listener, mehrere Clients |
| MP3-Encoding | fertig | 128 kbit/s CBR, LAME pro Client |
| Stream-URL | fertig | `http://<lokale-IP>:<Port>/stream.mp3` |
| DLNA-Prebuffer | fertig | `?buf=<ms>` sendet anfängliche Stille |
| Per-App-Capture | offen | Modell/UI vorhanden, Runtime unterstützt nur Endpoint-Loopback |
| Ziel-Samplerate aus Profil | eingeschränkt | Capture nutzt aktuell das WASAPI-Mixformat; AirPlay resampelt intern einfach auf 44,1 kHz |
| FLAC/WAV als HTTP-Format | bewusst nicht geplant | MP3 ist der aktuelle Kompatibilitätspfad für LMS und DLNA |

### LMS / Squeezebox

| Feature | Status | Hinweis |
|---|---|---|
| LMS-JSON-RPC-Client | fertig | typed `HttpClient`, BaseAddress aus Settings |
| Verbindungstest | fertig | TCP-Test plus `POST /jsonrpc.js` |
| Player laden | fertig | `serverstatus` mit Persistenz/Sortierung |
| Sync-Gruppe | fertig | erster aktiver Player wird Master |
| Stream starten/stoppen | fertig | `playlist play`, `pause`, `stop` |
| Lautstärke setzen | fertig | `mixer volume` |
| Bridge-Erkennung in LMS-Namen | eingeschränkt | einfache Namenserkennung für AirPlay/UPnP-Bridges |
| LMS-Auto-Discovery | offen | Setting existiert, kein mDNS-Listener für LMS implementiert |
| Externe Volume-Änderungen live übernehmen | offen | Event existiert, Polling/Subscribe-Loop fehlt |

### DLNA / UPnP

| Feature | Status | Hinweis |
|---|---|---|
| SSDP-Discovery | fertig | sucht `MediaRenderer:1` |
| Gerätebeschreibung laden | fertig | findet auch verschachtelte MediaRenderer |
| Wiedergabe starten | fertig | `SetAVTransportURI` + `Play` |
| Wiedergabe stoppen | fertig | `Stop` |
| Lautstärke setzen/lesen | fertig | `RenderingControl` falls vorhanden |
| Prebuffer-Hint | fertig | MP3-Stille am Stream-Anfang |
| Sample-Sync mit LMS | bewusst nicht geplant | direkter DLNA-Pfad ist eigenständig |

### AirPlay

| Feature | Status | Hinweis |
|---|---|---|
| mDNS-Discovery | fertig | `_raop._tcp` und `_airplay._tcp`, Audio-Filter |
| AirPlay-1/RAOP-Sender | eingeschränkt | RTSP/RTP, AES-CBC, L16 44,1 kHz |
| AirPlay-Lautstärke | eingeschränkt | `SET_PARAMETER volume` |
| AirPlay 2 | bewusst nicht geplant | kein Pairing/PTP/Curve25519/SRP |
| HomePod/Apple TV | eingeschränkt | Discovery möglich, direkter RAOP-Stream nicht garantiert |
| Retransmission/Control-Port | offen | Requests werden nicht nachgeliefert |
| Robuster Resampler | offen | aktuell einfache lineare Umrechnung auf 44,1 kHz |

### UI und Bedienung

| Feature | Status | Hinweis |
|---|---|---|
| Tray-Icon | fertig | Linksklick Mini-Fenster, Rechtsklick Kontextmenü |
| Mini-Fenster | fertig | Play/Pause, Pin, Schließen, Playerliste, Pegel |
| Mini-Fenster-Position | fertig | Position wird persistiert; Tray-Anker ist weiterhin grob |
| Settings-Fenster | fertig | Allgemein, Audio-Quelle, Dienste, Streaming, Über |
| Capture-Profil-Editor | fertig | Endpoint und vorbereitete Per-App-Felder |
| Gemeinsame Renderer-Karten | fertig | LMS, DLNA und AirPlay nutzen ein Template |
| Sortierung per Drag & Drop | fertig | Persistenz über `SortOrder` |
| Theme-Umschalter | fertig | System/Hell/Dunkel, live auf offene Fenster |
| Sprache Deutsch/Englisch | eingeschränkt | zentrale `Strings`-Klasse, keine `.resw`-Lokalisierung |
| Deutsche UI-Texte | eingeschränkt | viele Texte zentralisiert, XAML enthält noch feste Texte |

### App-Lifecycle und System

| Feature | Status | Hinweis |
|---|---|---|
| Single-Instance | fertig | Named Pipe, zweite Instanz öffnet Mini-Fenster |
| Autostart | fertig | Registry-basierter Windows-Autostart |
| Start minimiert ins Tray | fertig | `LaunchMinimizedToTray` |
| Wiedergabe fortsetzen | fertig | `ResumePlaybackOnStart` + `WasStreamingAtExit` |
| Settings-Persistenz | fertig | Debounce, atomisches Replace |
| Fensterpositionen | fertig | Settings-Fenster und Mini-Fenster |
| Crash-Logging | fertig | `%LOCALAPPDATA%\wStreamAudio\logs\` |
| Firewall-Regel | eingeschränkt | `netsh` elevated, pro App-Session nur einmal |

### Build, Installer und Release

| Feature | Status | Hinweis |
|---|---|---|
| Zentrale Version | fertig | `Directory.Build.props` mit `AppVersion` |
| Build-Skript | fertig | `dotnet publish`, XAML-Artefakte, optional Inno Setup |
| Install-Skript | fertig | Runtime-Prüfung, Update, Startmenü-Link |
| Uninstall-Skript | fertig | App entfernen, optional Nutzerdaten |
| Inno-Setup-Projekt | fertig | `installer/wStreamAudio.iss` |
| GitHub-Sync | fertig | `git-sync.ps1`, optional Release-Upload |

## Priorisierung

### Kurzfristig

1. Per-App-Capture entweder vollständig implementieren oder in der UI klar als
   noch nicht verfügbar sperren.
2. AirPlay-Hinweise in der UI auf AirPlay 1/RAOP korrigieren.
3. Robusteren Resampler für AirPlay und optionale Ziel-Samplerate einbauen.
4. LMS-Auto-Discovery wirklich implementieren oder das Setting entfernen.

### Danach

1. Player-Volume-Polling oder LMS-Subscribe-Loop, damit externe Änderungen live
   ins Mini-Fenster kommen.
2. Tray-Anker-Position über `Shell_NotifyIconGetRect` DPI-genau setzen.
3. Firewall-Regel mit besserem Statusfeedback im UI anzeigen.
4. XAML-Resttexte weiter in `Strings` zentralisieren.

### Nice-to-have

- Stream-Statistik im Mini-Fenster: Clients, Bitrate, Buffer, aktuelle URL.
- Diagnose-Seite für LMS/DLNA/AirPlay mit letzten Fehlern.
- Optionales alternatives HTTP-Format, falls konkrete Geräte MP3 ablehnen.
- Mehr Tests für Settings-Migrationen, Stream-Header und LMS-Adressbereinigung.
