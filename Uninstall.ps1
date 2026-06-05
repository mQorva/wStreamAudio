param(
    [switch]$RemoveUserData
)

$ErrorActionPreference = "Stop"

$target = Join-Path $env:LOCALAPPDATA "Programs\wStreamAudio"
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\wStreamAudio.lnk"
$desktop = [Environment]::GetFolderPath("DesktopDirectory")
$desktopLink = Join-Path $desktop "wStreamAudio.lnk"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$userData = Join-Path $env:LOCALAPPDATA "wStreamAudio"

# Laufenden Prozess beenden, sonst lassen sich Dateien nicht löschen.
Get-Process -Name "wStreamAudio" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

Remove-Item -Path $startMenu -Force -ErrorAction SilentlyContinue
Remove-Item -Path $desktopLink -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $runKey -Name "wStreamAudio" -ErrorAction SilentlyContinue

if (Test-Path $target) { Remove-Item -Path $target -Recurse -Force }

if ($RemoveUserData -and (Test-Path $userData)) {
    Remove-Item -Path $userData -Recurse -Force
}

Write-Host "wStreamAudio wurde deinstalliert. Nutzerdaten wurden nur mit -RemoveUserData entfernt."
