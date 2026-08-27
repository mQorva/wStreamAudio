# Third-Party Notices

wStreamAudio nutzt Komponenten Dritter. Diese Komponenten unterliegen ihren
eigenen Lizenzen, die in den jeweiligen Originalpaketen enthalten sind.

## NuGet-Pakete

- **Microsoft.WindowsAppSDK** - MIT License - Copyright Microsoft Corporation
- **Microsoft.Extensions.DependencyInjection**, **Microsoft.Extensions.Http**,
  **Microsoft.Extensions.Logging**, **Microsoft.Extensions.Logging.Debug** und
  **Microsoft.Extensions.Logging.Abstractions** - MIT License - Copyright .NET Foundation
- **CommunityToolkit.WinUI.Controls.SettingsControls** - MIT License -
  Copyright .NET Foundation und Contributors
- **NAudio** - MIT License - Copyright Mark Heath und Contributors
- **NAudio.Lame** - MIT License (Wrapper) - Copyright Corey Murtagh
- **Makaretu.Dns.Multicast** - Copyright Richard Schneider; die lokale NuGet-
  Nuspec enthält keine explizite Lizenzangabe. Das Paket wird für
  mDNS/Bonjour-Discovery von AirPlay-Empfängern genutzt.
- **Microsoft.NET.Test.Sdk**, **xunit**, **xunit.runner.visualstudio** und
  **coverlet.collector** - Test-Toolchain, MIT/Apache-2.0 je nach Paket

`NAudio.Lame` bindet die native **libmp3lame** ein. Die LAME-Library bleibt
unter ihrer eigenen Lizenz; Informationen und Quellcode liegen unter
https://lame.sourceforge.io/ vor.

Konkrete Paketversionen stehen in den `*.csproj`-Dateien unter `src\` und
`tests\`.

## Protokolle und Plattformen

LMS (Logitech Media Server), Squeezebox, DLNA/UPnP und AirPlay sind unabhängige
Ökosysteme. wStreamAudio kommuniziert über deren lokal erreichbare Netzwerk-
und Streaming-Schnittstellen und enthält keine Server- oder Gerätekomponenten
dieser Projekte.
