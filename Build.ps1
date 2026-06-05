param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("x64", "x86", "arm64")]
    [string]$Platform = "x64",
    [switch]$SkipInstaller,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot "src\wStreamAudio\wStreamAudio.csproj"
$publishDir = Join-Path $repoRoot "artifacts\release\wStreamAudio"
$installerScript = Join-Path $repoRoot "installer\wStreamAudio.iss"
$installerOutputDir = Join-Path $repoRoot "artifacts\installer"
$buildOutputDir = Join-Path $repoRoot "src\wStreamAudio\bin\$Platform\$Configuration\net10.0-windows10.0.19041.0\win-$Platform"

function Get-AppVersion {
    $propsPath = Join-Path $repoRoot "Directory.Build.props"
    [xml]$props = Get-Content -LiteralPath $propsPath
    $version = $props.Project.PropertyGroup.AppVersion
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "AppVersion wurde in Directory.Build.props nicht gefunden."
    }

    return $version.Trim()
}

function Get-InnoSetupCompiler {
    $iscc = Get-Command iscc -ErrorAction SilentlyContinue
    if ($iscc) {
        return $iscc.Source
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return $null
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet wurde nicht gefunden. .NET SDK laut global.json installieren und die Sitzung neu starten."
}

if ($Clean) {
    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    if (Test-Path -LiteralPath $installerOutputDir) {
        Remove-Item -LiteralPath $installerOutputDir -Recurse -Force
    }

    Write-Host "dotnet clean ..."
    & dotnet clean $project -c $Configuration -p:Platform=$Platform
    if ($LASTEXITCODE -ne 0) { throw "dotnet clean ist fehlgeschlagen." }
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host "dotnet publish ($Configuration / $Platform) -> $publishDir"
& dotnet publish $project `
    -c $Configuration `
    -r "win-$Platform" `
    --self-contained false `
    -p:Platform=$Platform `
    -p:WindowsAppSDKSelfContained=false `
    -p:WindowsPackageType=None `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish ist fehlgeschlagen." }

# WinUI: .xbf und .pri (dotnet publish lässt sie oft aus; ohne sie crasht WinUI/XAML).
if (Test-Path -LiteralPath $buildOutputDir) {
    Get-ChildItem -Path $buildOutputDir -Filter "*.xbf" -ErrorAction SilentlyContinue |
        Copy-Item -Destination $publishDir -Force
    Get-ChildItem -Path $buildOutputDir -Filter "*.pri" -ErrorAction SilentlyContinue |
        Copy-Item -Destination $publishDir -Force
}

$exe = Join-Path $publishDir "wStreamAudio.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Build unvollständig: $exe fehlt."
}

$appVersion = Get-AppVersion
$iscc = Get-InnoSetupCompiler
if (-not $SkipInstaller -and $Configuration -eq "Release" -and $Platform -eq "x64" -and $null -ne $iscc) {
    New-Item -ItemType Directory -Force -Path $installerOutputDir | Out-Null

    Write-Host ""
    Write-Host "Inno Setup -> $installerOutputDir"
    & $iscc $installerScript "/DAppVersion=$appVersion" "/DSourceDir=$publishDir" "/DOutputDir=$installerOutputDir"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup ist fehlgeschlagen." }
}
elseif (-not $SkipInstaller -and $Configuration -eq "Release" -and $Platform -eq "x64") {
    Write-Host ""
    Write-Host "Inno Setup wurde nicht gefunden; Setup-Installer wurde übersprungen."
    Write-Host "Installieren: winget install JRSoftware.InnoSetup"
}
elseif ($SkipInstaller) {
    Write-Host ""
    Write-Host "Setup-Installer wurde wegen -SkipInstaller übersprungen."
}
else {
    Write-Host ""
    Write-Host "Setup-Installer wird nur für Release|x64 gebaut."
}

Write-Host ""
Write-Host "Fertig. Den kompletten Ordner auf den Zielrechner kopieren (oder dort .\Install.ps1 -SourceDir <Pfad> nutzen):"
Write-Host "  $publishDir"
if (Test-Path -LiteralPath $installerOutputDir) {
    $setup = Join-Path $installerOutputDir "wStreamAudio-Setup-$appVersion.exe"
    if (Test-Path -LiteralPath $setup) {
        Write-Host "Setup-Installer:"
        Write-Host "  $setup"
    }
}
