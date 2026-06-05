#define AppName "wStreamAudio"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#ifndef SourceDir
#define SourceDir "..\artifacts\release\wStreamAudio"
#endif
#ifndef OutputDir
#define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{A289F143-2BA9-4E84-9ADE-D62EE17B8522}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Ronny Schulz
AppPublisherURL=https://github.com/CannonRS/wStreamAudio
AppSupportURL=https://github.com/CannonRS/wStreamAudio/issues
AppUpdatesURL=https://github.com/CannonRS/wStreamAudio/releases
DefaultDirName={localappdata}\Programs\wStreamAudio
DefaultGroupName=wStreamAudio
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=wStreamAudio-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
SetupIconFile=..\src\wStreamAudio\Assets\App.ico
UninstallDisplayIcon={app}\wStreamAudio.exe
CloseApplications=yes
CloseApplicationsFilter=wStreamAudio.exe
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=Ronny Schulz
VersionInfoDescription=wStreamAudio Installer
VersionInfoProductName=wStreamAudio
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Verknüpfungen:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Prerequisites.ps1"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\wStreamAudio"; Filename: "{app}\wStreamAudio.exe"; WorkingDir: "{app}"; IconFilename: "{app}\wStreamAudio.exe"
Name: "{autodesktop}\wStreamAudio"; Filename: "{app}\wStreamAudio.exe"; WorkingDir: "{app}"; IconFilename: "{app}\wStreamAudio.exe"; Tasks: desktopicon

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{tmp}\Prerequisites.ps1"" -DotNetDesktopRuntimeVersion ""10.0.7"""; StatusMsg: "Voraussetzungen werden geprüft ..."; Flags: runhidden waituntilterminated
Filename: "{app}\wStreamAudio.exe"; Description: "wStreamAudio starten"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-Process -Name 'wStreamAudio' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue"""; Flags: runhidden; RunOnceId: "StopWStreamAudio"
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'wStreamAudio' -ErrorAction SilentlyContinue"""; Flags: runhidden; RunOnceId: "RemoveWStreamAudioAutostart"
