# Third-Party Notices

wStreamAudio nutzt Komponenten Dritter. Diese Komponenten unterliegen ihren
eigenen Lizenzen, die in den jeweiligen Originalpaketen enthalten sind.

## NuGet-Pakete

- **Microsoft.WindowsAppSDK** — MIT License — © Microsoft Corporation
- **Microsoft.Extensions.* (DependencyInjection, Logging, Http, Configuration, Hosting)** — MIT License — © .NET Foundation
- **NAudio** — MIT License — © Mark Heath, contributors
- **NAudio.Lame** — MIT License (Wrapper) — © Corey Murtagh
  Bündelt die native **libmp3lame** (LAME) — © The LAME Project, LGPL-2.1.
  LAME wird dynamisch eingebunden, der Quellcode der LAME-Library liegt unter
  https://lame.sourceforge.io/ vor. Die LGPL-Lizenz bleibt für die libmp3lame-
  DLL erhalten und überträgt sich nicht auf den wStreamAudio-Quellcode.
- **CommunityToolkit.WinUI.Controls.SettingsControls** — MIT License — © .NET Foundation, contributors
- **CommunityToolkit.Mvvm** — MIT License — © .NET Foundation, contributors
- **Zeroconf** — MIT License — © Oren Novotny, contributors
- **System.Text.Json** — MIT License — © .NET Foundation
- **xunit**, **xunit.runner.visualstudio**, **Microsoft.NET.Test.Sdk**,
  **coverlet.collector** — MIT/Apache-2.0 — Test-Toolchain

Konkrete Versionen siehe `*.csproj`-Dateien in `src\` und `tests\`.

## Squeezebox-/LMS-Protokoll

LMS (Logitech Media Server) und das Squeezebox-Ökosystem sind unabhängige
Open-Source-Projekte. wStreamAudio kommuniziert ausschließlich über deren
öffentlich dokumentierte JSON-RPC- und Streaming-Schnittstellen.
