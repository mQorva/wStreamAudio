param(
    [switch]$DesktopShortcut,
    [switch]$SkipPrerequisites,
    [string]$DotNetDesktopRuntimeVersion = "10.0.7",
    # Leer = Repo-Standard artifacts\release\wStreamAudio (Inhalt von .\Build.ps1).
    # Auf anderem Rechner: z. B. -SourceDir "D:\Deploy\wStreamAudio" (kompletter Ordner inkl. wStreamAudio.exe).
    [string]$SourceDir = ""
)

$ErrorActionPreference = "Stop"

function Test-WingetAvailable { return $null -ne (Get-Command winget -ErrorAction SilentlyContinue) }

function Invoke-WingetInstall {
    param([Parameter(Mandatory = $true)][string]$PackageId)
    if (-not (Test-WingetAvailable)) { return $false }
    Write-Host "winget install $PackageId ..."
    & winget install --id $PackageId -e --accept-package-agreements --accept-source-agreements --disable-interactivity
    return $LASTEXITCODE -eq 0
}

function Get-WindowsAppRuntime18X64 {
    return Get-AppxPackage -Name "Microsoft.WindowsAppRuntime.1.8" -ErrorAction SilentlyContinue |
        Where-Object { $_.Architecture -eq "X64" } |
        Sort-Object Version -Descending |
        Select-Object -First 1
}

function Test-DotNetWindowsDesktopRuntime10 {
    $desktopRoots = @(
        (Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.WindowsDesktop.App"),
        (Join-Path ${env:ProgramFiles(x86)} "dotnet\shared\Microsoft.WindowsDesktop.App")
    )
    foreach ($root in $desktopRoots) {
        if (Test-Path $root) {
            $v10 = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like "10.*" }
            if ($v10) { return $true }
        }
    }
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $listed = & dotnet --list-runtimes 2>$null
        if ($listed -match "Microsoft\.WindowsDesktop\.App 10\.") { return $true }
    }
    return $false
}

function Install-DotNetDesktopRuntimeFromMicrosoft {
    param([string]$Version)
    $url = "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/$Version/windowsdesktop-runtime-$Version-win-x64.exe"
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) "windowsdesktop-runtime-$Version-win-x64.exe"
    Write-Host "Lade .NET Windows Desktop Runtime ${Version}: $url"
    Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing
    Write-Host "Starte Runtime-Installer (UAC möglich) ..."
    $p = Start-Process -FilePath $temp -ArgumentList @("/install", "/quiet", "/norestart") -PassThru -Wait -Verb RunAs
    if ($p.ExitCode -ne 0 -and $null -ne $p.ExitCode) {
        Write-Warning "Installer beendete mit ExitCode $($p.ExitCode)."
    }
    Remove-Item -Path $temp -Force -ErrorAction SilentlyContinue
}

function Install-WindowsAppRuntime18FromMicrosoft {
    $url = "https://aka.ms/windowsappsdk/1.8/1.8.260209005/windowsappruntimeinstall-x64.exe"
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) "windowsappruntimeinstall-1.8-x64.exe"
    Write-Host "Lade Windows App Runtime 1.8 (x64): $url"
    Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing
    Write-Host "Starte Windows-App-Runtime-Installer (UAC möglich, still: --quiet --force) ..."
    $p = Start-Process -FilePath $temp -ArgumentList @("--quiet", "--force") -PassThru -Wait -Verb RunAs
    if ($p.ExitCode -ne 0 -and $null -ne $p.ExitCode) { Write-Warning "Installer beendete mit ExitCode $($p.ExitCode)." }
    Remove-Item -Path $temp -Force -ErrorAction SilentlyContinue
}

function Refresh-PathEnv {
    $machine = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = "$machine;$userPath"
}

function Ensure-WindowsAppRuntime18 {
    if ($null -ne (Get-WindowsAppRuntime18X64)) { return }
    Write-Host "Windows App Runtime 1.8 (x64) fehlt - Installation wird versucht."
    if (-not (Invoke-WingetInstall -PackageId "Microsoft.WindowsAppRuntime.1.8")) {
        Write-Host "winget nicht verfügbar oder fehlgeschlagen - versuche direkten Download (Microsoft, x64)."
        Install-WindowsAppRuntime18FromMicrosoft
    }
    Refresh-PathEnv
    Start-Sleep -Seconds 2
    if ($null -eq (Get-WindowsAppRuntime18X64)) {
        throw "Windows App Runtime 1.8 x64 nach Installation weiterhin nicht gefunden. Bitte PC neu starten und Install.ps1 erneut ausführen."
    }
}

function Ensure-DotNetWindowsDesktopRuntime10 {
    param([string]$PreferredVersion)
    if (Test-DotNetWindowsDesktopRuntime10) { return }
    Write-Host ".NET Windows Desktop Runtime 10.x fehlt - Installation wird versucht."
    if (-not (Invoke-WingetInstall -PackageId "Microsoft.DotNet.DesktopRuntime.10")) {
        Write-Host "winget nicht verfügbar oder fehlgeschlagen - versuche direkten Download Version $PreferredVersion."
        Install-DotNetDesktopRuntimeFromMicrosoft -Version $PreferredVersion
    }
    Refresh-PathEnv
    Start-Sleep -Seconds 2
    if (-not (Test-DotNetWindowsDesktopRuntime10)) {
        throw ".NET Windows Desktop Runtime 10.x nach Installation weiterhin nicht gefunden."
    }
}

function Resolve-InstallPayloadDirectory {
    param([Parameter(Mandatory = $true)][string]$RepoRoot, [string]$SourceDir)
    if ([string]::IsNullOrWhiteSpace($SourceDir)) {
        return Join-Path $RepoRoot "artifacts\release\wStreamAudio"
    }
    if ([System.IO.Path]::IsPathRooted($SourceDir)) { return $SourceDir.TrimEnd('\', '/') }
    return (Join-Path $RepoRoot $SourceDir.Trim()).TrimEnd('\', '/')
}

function Stop-WStreamAudioForInstallUpdate {
    # wStreamAudio selbst beenden, damit das Update Dateien überschreiben kann.
    $names = @("wStreamAudio")
    for ($i = 0; $i -lt 4; $i++) {
        $running = Get-Process -Name $names -ErrorAction SilentlyContinue
        if (-not $running) { return }
        if ($i -eq 0) {
            $list = ($running | Select-Object -ExpandProperty Name -Unique) -join ", "
            Write-Host "Beende laufende Prozesse vor Installation/Update: $list"
        }
        $running | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 600
    }
    $stillRunning = Get-Process -Name $names -ErrorAction SilentlyContinue
    if ($stillRunning) {
        $list = ($stillRunning | Select-Object -ExpandProperty Name -Unique) -join ", "
        throw "Folgende Prozesse blockieren weiterhin Dateien: $list. Bitte App manuell beenden und Install.ps1 erneut ausführen."
    }
}

# --- Hauptablauf ---
$repoRoot = $PSScriptRoot
$payloadDir = Resolve-InstallPayloadDirectory -RepoRoot $repoRoot -SourceDir $SourceDir
$target = Join-Path $env:LOCALAPPDATA "Programs\wStreamAudio"
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\wStreamAudio.lnk"
$exe = Join-Path $target "wStreamAudio.exe"
$sourceExe = Join-Path $payloadDir "wStreamAudio.exe"

if (-not $SkipPrerequisites) {
    Ensure-WindowsAppRuntime18
    Ensure-DotNetWindowsDesktopRuntime10 -PreferredVersion $DotNetDesktopRuntimeVersion
}
else {
    if ($null -eq (Get-WindowsAppRuntime18X64)) {
        throw "Windows App Runtime 1.8 x64 wurde nicht gefunden. Ohne -SkipPrerequisites installiert Install.ps1 sie automatisch."
    }
    if (-not (Test-DotNetWindowsDesktopRuntime10)) {
        throw ".NET Windows Desktop Runtime 10.x wurde nicht gefunden."
    }
}

if (-not (Test-Path $sourceExe)) {
    $hint = Join-Path $repoRoot "artifacts\release\wStreamAudio"
    throw "wStreamAudio.exe nicht gefunden: $sourceExe`n`nAuf dem Entwicklungsrechner zuerst .\Build.ps1 ausführen.`nVerteilbarer Ordner (komplett kopieren): $hint"
}

Stop-WStreamAudioForInstallUpdate
New-Item -ItemType Directory -Force -Path $target | Out-Null

# Zielordner komplett aufräumen — wStreamAudio nutzt keine externen Bridges mehr.
Get-ChildItem -Path $target -Force -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force

Copy-Item -Path (Join-Path $payloadDir "*") -Destination $target -Recurse -Force

if (-not (Test-Path $exe)) {
    throw "Installation unvollständig: $exe wurde nach dem Kopieren nicht gefunden."
}

$shell = New-Object -ComObject WScript.Shell
$startMenuDirectory = Split-Path $startMenu -Parent
New-Item -ItemType Directory -Force -Path $startMenuDirectory | Out-Null
$shortcut = $shell.CreateShortcut($startMenu)
$shortcut.TargetPath = $exe
$shortcut.Arguments = ""
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = $exe
$shortcut.Save()

if ($DesktopShortcut) {
    $desktop = [Environment]::GetFolderPath("DesktopDirectory")
    $desktopLink = Join-Path $desktop "wStreamAudio.lnk"
    $deskShortcut = $shell.CreateShortcut($desktopLink)
    $deskShortcut.TargetPath = $exe
    $deskShortcut.Arguments = ""
    $deskShortcut.WorkingDirectory = $target
    $deskShortcut.IconLocation = $exe
    $deskShortcut.Save()
}

Write-Host "wStreamAudio wurde installiert: $target"
Write-Host "Startmenü-Verknüpfung: $startMenu"
Write-Host "Ziel: $exe"
