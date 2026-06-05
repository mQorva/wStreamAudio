# Assets

Icons der App. Werden automatisch in den Build kopiert (`Content Include="Assets\*.ico"`,
`Assets\*.png` und `Assets\*.svg` in `wStreamAudio.csproj`).

- `App.ico` — Haupt-Icon der Exe (`<ApplicationIcon>` in der csproj). Wird in
  Datei-Explorer, Alt-Tab, Taskbar und Verknüpfungen verwendet. Multi-Size
  (16/24/32/48/64/128/256).
- `TrayIdle.ico` — Tray-Icon im Ruhezustand. Multi-Size (16/20/24/32/40/48).
- `TrayActive.ico` — Tray-Icon während Streaming (gleiches helles Motiv +
  grüner Live-Punkt oben rechts). Multi-Size (16/20/24/32/40/48).
- `App16.png` bis `App512.png` — PNG-Varianten für About-Dialog, Store/Packaging,
  Installer, Dokumentation und spätere UI-Verwendung.
- `TrayIdle16.png` bis `TrayIdle48.png`, `TrayActive16.png` bis `TrayActive48.png`
  — PNG-Varianten der Tray-Zustände.
- `App.svg` — skalierbare Quelle des Hauptsymbols.

Die Tray-Klasse ([`TrayIconController`](../Tray/TrayIconController.cs)) fällt auf
das Standard-Windows-Anwendungssymbol zurück, wenn diese Dateien fehlen — der
Build funktioniert also auch ohne sie.

## Neu generieren

```powershell
python tools\generate-icons.py
```

Das Skript liegt unter [`tools/generate-icons.py`](../../../../tools/generate-icons.py)
und benutzt Pillow. Pixelgrößen, Geometrie und Farben sind dort als Konstanten
am Anfang änderbar. Das Motiv ist ein randfüllendes, transparentes,
S-förmiges Audio-Stream-Zeichen mit Pegelmarken und Knotenpunkten; der aktive
Tray-Zustand ergänzt nur den grünen Live-Punkt.
