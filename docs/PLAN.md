# Technischer Stand und Roadmap

Stand: 27. August 2026

Dieses Dokument trennt zwischen im Quellcode vorhandener Implementierung und
tatsächlich durchgeführter Verifikation. Ein grüner Build oder Unit-Test ist
kein Nachweis für die Wiedergabe auf realen LMS-, DLNA- oder AirPlay-Geräten.

## Verifikationsstand

Aktuell nachgewiesen:

- Debug-Build der vollständigen Solution unter Windows x64
- sechs Core- und zwei Infrastructure-Tests
- statischer Abgleich der beschriebenen Pfade mit dem aktuellen Quellcode

Noch nicht als aktuelle Akzeptanz nachgewiesen:

- Installation und Update mit dem veröffentlichten Setup auf einem sauberen
  Zielsystem
- längerer Audiostream unter CPU- und Netzwerklast
- Wiedergabe und Synchronisierung auf realen LMS-/Squeezebox-Playern
- direkte Wiedergabe auf einer repräsentativen Auswahl von DLNA-Renderern
- direkte AirPlay-1-Wiedergabe auf den in der Oberfläche gefundenen Geräten
- vollständige englische Oberfläche

## Implementierter Hauptpfad

```text
Windows-Render-Endpoint
    -> WasapiLoopbackSource
    -> Stereo-PCM, 16 Bit
    -> HttpStreamServer
       -> MP3, 128 kbit/s CBR
       -> /stream.mp3
          -> LMS / Squeezebox
          -> DLNA / UPnP
    -> RaopSender
       -> experimentelle direkte AirPlay-1-Ausgabe
```

`WasapiLoopbackSource` verarbeitet ausschließlich Endpoint-Loopback. Ein
Profil kann dem Windows-Standardgerät folgen oder einen konkreten aktiven
Render-Endpoint verwenden. Mehrkanalquellen werden auf Stereo heruntergemischt.

Der HTTP-Server verwendet einen eigenen LAME-Encoder je Client. Der Parameter
`?buf=<ms>` erzeugt nur anfängliche Stille für die Startphase eines
DLNA-Renderers. Er ist kein fortlaufender Puffer gegen später auftretende
CPU- oder Netzwerkengpässe.

## Funktionsstand

### Aufnahme und HTTP-Stream

| Funktion | Implementierung | Aktuelle Akzeptanz |
|---|---|---|
| Endpoint-Loopback | vorhanden | Build; kein aktueller Gerätetest |
| Standardgerät verfolgen | vorhanden | nicht aktuell manuell geprüft |
| konkreter aktiver Endpoint | vorhanden | nicht aktuell manuell geprüft |
| Mono-/Mehrkanal-Downmix | vorhanden | kein eigener Audioreferenztest |
| Stille-Keep-Alive | vorhanden | kein Langzeittest |
| MP3-HTTP-Stream | vorhanden | kein aktueller Last- oder Dauertest |
| Per-App-Capture | nur Modell und UI | Runtime weist diesen Modus zurück |
| Profil-Zielsamplerate | nur Modell und UI | Endpoint-Capture nutzt Mixformat |
| WAV/FLAC-Ausgabe | nicht vorhanden | nicht geplant, solange kein Bedarf belegt ist |

### LMS / Squeezebox

Vorhanden sind JSON-RPC-Aufrufe für Playerliste, Power, Lautstärke,
Synchronisierung, Wiedergabe, Pause und Stopp. LMS übernimmt die
Synchronisierung innerhalb seiner Playergruppe; wStreamAudio garantiert keine
Synchronität mit direkt angesteuerten DLNA- oder AirPlay-Geräten.

Nicht vorhanden sind eine funktionierende LMS-Erkennung per mDNS und eine
laufende Übernahme externer Lautstärkeänderungen. Das vorhandene
`AutoDiscover`-Setting und das interne Lautstärke-Event dürfen nicht als
fertige Funktionen dokumentiert werden.

### DLNA / UPnP

SSDP-Suche, Gerätebeschreibung, `SetAVTransportURI`, `Play`, `Stop` und – falls
vom Gerät angeboten – `RenderingControl` sind implementiert. Verhalten und
Kompatibilität hängen vom Renderer ab und benötigen Gerätetests.

Der konfigurierbare DLNA-Wert fügt dem Beginn einer HTTP-Verbindung Stille
hinzu. Er stabilisiert keine bereits laufende Übertragung.

### AirPlay

mDNS-Suche und ein eigener AirPlay-1-/RAOP-Sender sind implementiert. Der
Sender verwendet RTSP/RTP, L16 mit 44,1 kHz und eine einfache interne
Sampleratenumrechnung. Retransmission und AirPlay 2 fehlen. Der Pfad ist daher
experimentell; aus erfolgreicher Discovery folgt nicht, dass ein Gerät den
Stream annimmt.

### Oberfläche und Einstellungen

Vorhanden sind Tray-Menü, Mini-Fenster, Einstellungsfenster,
Capture-Profile, Gerätekarten, Sortierung, Autostart, Fensterpositionen sowie
automatisch gespeicherte JSON-Einstellungen.

System-, helles und dunkles Farbschema sind implementiert. Eine Umschaltung
zwischen Deutsch und Englisch existiert, zahlreiche XAML-Texte sind aber noch
fest deutsch. Die Oberfläche ist deshalb nicht vollständig lokalisiert.

### Installation und Release

Der empfohlene Nutzerweg ist der mit Inno Setup erzeugte
`wStreamAudio-Setup-<Version>.exe`. `Install.ps1` dient als manueller Weg für
lokale Builds. Beide installieren benutzerbezogen nach
`%LOCALAPPDATA%\Programs\wStreamAudio` und prüfen die benötigten Microsoft-
Runtimes.

Die Setup-Datei ist derzeit nicht digital signiert. Ein Release darf daher
nicht als ohne Windows-Warnung installierbar beschrieben werden.

## Nächste sinnvolle Schritte

1. Setup-Installation und Update auf einem sauberen Windows-System prüfen.
2. LMS-Hauptpfad mit mindestens zwei synchronisierten Playern testen.
3. HTTP-Pipeline unter CPU-Last instrumentieren und als Dauertest prüfen.
4. Direkte DLNA- und AirPlay-Unterstützung anhand einer dokumentierten
   Geräteliste klassifizieren.
5. Per-App-Capture entweder implementieren oder die Bedienelemente deaktivieren.
6. Unbenutzte LMS-Auto-Discovery entfernen oder vollständig implementieren.
7. Verbleibende fest deutsche UI-Texte lokalisieren.
8. Installer signieren, bevor ein stabiles Release veröffentlicht wird.

## Release-Gate für eine stabile Version

Ein Release sollte erst als stabil markiert werden, wenn mindestens folgende
Nachweise vorliegen:

- reproduzierbarer Release-Build aus dem veröffentlichten Tag
- übereinstimmende Version in Quellcode, Tag, App und Setup
- bestandene automatisierte Tests
- bestandener Setup-/Update-/Deinstallations-Test
- bestandener LMS-Dauertest auf realer Hardware
- dokumentierte Aussage zum unterstützten DLNA-/AirPlay-Umfang
- digital signierter Installer oder eine ausdrücklich beschlossene Ausnahme
