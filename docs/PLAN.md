# wStreamAudio — Roadmap & Stand

Stand: 2026-05-10. Dieses Dokument ist die Wahrheit über den Ist-Zustand der App.
Die [README.md](../README.md) beschreibt das **Ziel**, nicht den Stand.

> Vorgeschichte: Das Projekt hieß zunächst **wAirPlay** und wurde dann auf
> **wStreamAudio** umbenannt, als klar wurde, dass nicht nur AirPlay-, sondern
> auch DLNA- und Squeeze-Player adressiert werden sollen. Der ursprüngliche
> Plan stammt aus der Chat-Session, mit der das Projekt aus dem Magic-Voice-
> Pattern abgeleitet wurde — dieses Dokument fasst ihn nachträglich zusammen.

## Architektur (Ist)

```
Windows-Audio-Endpoints
    └─► WASAPI-Loopback (IAudioCapture, NAudio-frei)
            └─► PCM-Frames (16-bit interleaved, ggf. resampled)
                    └─► HttpStreamServer  ── PCM-WAV ──► LMS
                                                          └─► echte Squeeze-Player
                                                ── direkt ──► DLNA-Renderer (SSDP/AVTransport)
                                                ── direkt ──► AirPlay-Empfänger (mDNS)
```

Steuerung:
- LMS-JSON-RPC für Player-Status, Volume, Sync, Power, Play.
- Tray-Icon (Win32 Shell_NotifyIcon) als einziger UI-Anker, Quick-Popup +
  Settings-Window als WinUI-3-Fenster.
- System-Lautstärke ↔ Player-Volume optional gekoppelt.

## Status pro Feature

Legende: ✅ fertig · 🟡 funktioniert eingeschränkt · ❌ Stub / fehlt

### Capture & Streaming
| Feature | Status | Hinweis |
|---|---|---|
| WASAPI-Endpoint-Loopback (Default/SPDIF/HDMI) | ✅ | `WasapiLoopbackSource`, `WindowsAudioEndpointCatalog` |
| Per-App-Capture (Process Loopback) | 🟡 | Modell vorhanden; tatsächliche WASAPI-Process-Loopback-Implementierung muss noch geprüft werden |
| Capture-Profile-Editor | ✅ | Dialog für Modus, Endpoint, Prozess, Sample-Rate, Kompression |
| HTTP-Live-Stream | ✅ | TCP-Listener, multi-client |
| **FLAC-Encoder** | ⛔ bewusst weggelassen | Im LAN sind die ~1,4 Mbit/s eines PCM-WAV-Streams kein Problem; LMS spielt WAV nativ. FLAC-Encoder + `libFLAC.dll`-Abhängigkeit + per-Client-Encoding wurden geprüft und wieder entfernt, weil der Nutzen den Aufwand nicht rechtfertigt. Falls jemals Bandbreite eng wird, ist der Re-Add eine kleine Übung. |
| Resampler | 🟡 | rudimentär; zu prüfen, ob 48k-Quellen sauber zu 44.1k-LMS-Konfig laufen |

### LMS-Anbindung
| Feature | Status | Hinweis |
|---|---|---|
| LMS-JSON-RPC-Client | ✅ | `slim.request` mit `serverstatus`, `power`, `mixer volume`, `sync`, `playlist play`, `pause`, `stop` |
| Verbindungstest | ✅ | inkl. Host-Bereinigung (Schema/Pfad/Port aus Eingabe) und Fehlermeldung im UI |
| mDNS-Auto-Discovery | 🟡 | Setting `AutoDiscover` existiert, aber kein konkreter mDNS-Listener — nur manueller Host gilt aktuell |
| Player-Volume-Subscribe | 🟡 | `RaiseVolumeChanged` existiert, aber kein Polling/Subscribe-Loop, der das Event füllt |

### UI / Bedienung
| Feature | Status | Hinweis |
|---|---|---|
| Tray-Icon (Linksklick = Popup, Rechtsklick = Menü) | ✅ | nativer Shell_NotifyIcon |
| Quick-Popup mit Stream-Toggle, Profil, Player-Liste | ✅ | |
| Quick-Popup-Position relativ zum Tray | 🟡 | aktuell pauschal „rechts unten am Primärbildschirm"; DPI-genaue Tray-Anker-Position fehlt |
| Settings-Window (NavigationView) | ✅ | Allgemein, Audio-Quelle, Dienste, Streaming, Über |
| Multiroom-Sync per Toggle pro Player | ✅ | Popup-Toggle ruft live `_lms.SyncAsync` / `UnsyncAsync` |
| Pro-Player-Trim („App steuert Lautstärke") | 🟡 | Settings-Modell + `VolumeMath` da, in Streaming-Page als Checkbox sichtbar; Popup zeigt nur den Lautstärke-Slider |
| Lokalisierung (de/en) | ❌ bewusst zurückgestellt | Sprache-Combobox in der Settings-Page ist deaktiviert mit Hinweis. Echte Lokalisierung würde Resource-Files (.resw) erfordern — erst sinnvoll, wenn die UI-Strings stabil sind |
| Theme-Umschalter (System/Hell/Dunkel) | ✅ | `ThemeService.ApplyTo` setzt `RequestedTheme` auf der Window-Root; bei Umschalten in der Page werden alle offenen Fenster sofort aktualisiert |

### Direkte Geräte-Steuerung
| Feature | Status | Hinweis |
|---|---|---|
| DLNA-Renderer-Discovery (SSDP) | ✅ | `DlnaService.DiscoverRenderersAsync` |
| DLNA-Wiedergabe via AVTransport | ✅ | `DlnaService` (frühere LMS-to-uPnP-Bridge ist damit überflüssig) |
| AirPlay-Empfänger-Discovery (mDNS) | ✅ | `AirPlayDiscovery` (frühere AirConnect-Bridge ist damit überflüssig) |
| AirPlay-Wiedergabe-Sender | ❌ | in Vorbereitung — UI zeigt Liste, Stream-Pfad folgt |

### App-Lifecycle / System
| Feature | Status | Hinweis |
|---|---|---|
| Single-Instance via Named Pipe | ✅ | zweite Instanz signalisiert Popup-Show |
| Autostart | ✅ | Registry-basierter `WindowsAutostartService` |
| `LaunchMinimizedToTray` | ✅ | beim Start ausgewertet (Default jetzt false → Settings-Fenster sichtbar) |
| Settings-Persistenz mit Debounce | ✅ | atomisches Replace via `.tmp` |
| Crash-Logging | ✅ | `StartupCrashLogger` |
| Firewall-Regel für Stream-Port | 🟡 | Setting `SetFirewallRule` existiert; tatsächliche Regel-Anlage noch nicht verifiziert |

## Phasen / Priorisierung

### Phase 1 — bereits geliefert
Alles mit ✅. Reicht für: System-Audio in den LMS streamen, dort Player-Sync,
Tray-Bedienung, direkte DLNA-Wiedergabe.

### Phase 2 — kurzfristig nächste
1. **App-Volume-Trim im Popup**: Toggle „App steuert Lautstärke" pro Player im
   Quick-Popup, zusätzlich zum Slider.
2. **Quick-Popup-Position**: Tray-Anker via `Shell_NotifyIconGetRect` und
   DPI-Korrektur (aktuell: rechts unten am Primärbildschirm mit kleinem Abstand).
3. **AirPlay-Sender** vollständig implementieren (RTSP/RAOP-Pfad).

### Phase 3 — mittelfristig
6. **mDNS-Auto-Discovery für LMS** (`_slimproto._tcp` und `_lms._tcp`).
7. **Player-Volume-Subscribe-Loop**, damit externe Volume-Änderungen ins UI
   kommen (LMS-Subscribe oder Polling alle 2 s).
8. **Lokalisierung** (Resource-Files de/en).
9. **Theme live anwenden**.
10. **Firewall-Rule-Anlage** verifizieren / robust machen.

### Phase 4 — Nice-to-have
- Statistik im Popup (Bitrate, Bufferzustand, Client-Anzahl).
- Eigenes Capture-Format-Profil pro Player (z.B. ein Player will 16-Bit,
  ein anderer 24-Bit).
- Tray-Notification, wenn LMS-Verbindung verloren geht.

