param(
    [string]$DotNetDesktopRuntimeVersion = "10.0.7"
)

$ErrorActionPreference = "Stop"

function Test-WingetAvailable {
    return $null -ne (Get-Command winget -ErrorAction SilentlyContinue)
}

function Invoke-WingetInstall {
    param([Parameter(Mandatory = $true)][string]$PackageId)

    if (-not (Test-WingetAvailable)) {
        return $false
    }

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
        if (Test-Path -LiteralPath $root) {
            $v10 = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -like "10.*" }
            if ($v10) {
                return $true
            }
        }
    }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $listed = & dotnet --list-runtimes 2>$null
        if ($listed -match "Microsoft\.WindowsDesktop\.App 10\.") {
            return $true
        }
    }

    return $false
}

function Install-DotNetDesktopRuntimeFromMicrosoft {
    param([string]$Version)

    $url = "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/$Version/windowsdesktop-runtime-$Version-win-x64.exe"
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) "windowsdesktop-runtime-$Version-win-x64.exe"
    Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing
    $process = Start-Process -FilePath $temp -ArgumentList @("/install", "/quiet", "/norestart") -PassThru -Wait -Verb RunAs
    Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue

    if ($null -ne $process.ExitCode -and $process.ExitCode -ne 0 -and $process.ExitCode -ne 3010) {
        throw ".NET Windows Desktop Runtime Installer wurde mit ExitCode $($process.ExitCode) beendet."
    }
}

function Install-WindowsAppRuntime18FromMicrosoft {
    $url = "https://aka.ms/windowsappsdk/1.8/1.8.260209005/windowsappruntimeinstall-x64.exe"
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) "windowsappruntimeinstall-1.8-x64.exe"
    Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing
    $process = Start-Process -FilePath $temp -ArgumentList @("--quiet", "--force") -PassThru -Wait -Verb RunAs
    Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue

    if ($null -ne $process.ExitCode -and $process.ExitCode -ne 0 -and $process.ExitCode -ne 3010) {
        throw "Windows App Runtime Installer wurde mit ExitCode $($process.ExitCode) beendet."
    }
}

function Refresh-PathEnv {
    $machine = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = "$machine;$userPath"
}

if ($null -eq (Get-WindowsAppRuntime18X64)) {
    if (-not (Invoke-WingetInstall -PackageId "Microsoft.WindowsAppRuntime.1.8")) {
        Install-WindowsAppRuntime18FromMicrosoft
    }
}

if (-not (Test-DotNetWindowsDesktopRuntime10)) {
    if (-not (Invoke-WingetInstall -PackageId "Microsoft.DotNet.DesktopRuntime.10")) {
        Install-DotNetDesktopRuntimeFromMicrosoft -Version $DotNetDesktopRuntimeVersion
    }
}

Refresh-PathEnv

if ($null -eq (Get-WindowsAppRuntime18X64)) {
    throw "Windows App Runtime 1.8 x64 wurde nach der Installation nicht gefunden."
}

if (-not (Test-DotNetWindowsDesktopRuntime10)) {
    throw ".NET Windows Desktop Runtime 10.x wurde nach der Installation nicht gefunden."
}
