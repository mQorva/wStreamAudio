param(
    [Parameter(Position = 0)]
    [ValidateSet("get", "set")]
    [string]$Command = "get",

    [Parameter(Position = 1)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

function Test-Version {
    param([string]$Value)

    if ($Value -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version '$Value' ist ungültig. Erwartet wird z. B. 0.2.1."
    }
}

function Set-TextFileVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Pattern,
        [Parameter(Mandatory = $true)]
        [string]$Replacement
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Versionsziel nicht gefunden: $Path"
    }

    $content = Get-Content -LiteralPath $Path -Raw
    if (-not [regex]::IsMatch($content, $Pattern)) {
        throw "Versionsmuster wurde in $Path nicht gefunden: $Pattern"
    }

    $updated = [regex]::Replace($content, $Pattern, $Replacement)
    Set-Content -LiteralPath $Path -Value $updated -NoNewline
}

$propsPath = Join-Path $PSScriptRoot "Directory.Build.props"
$installerPath = Join-Path $PSScriptRoot "installer\wStreamAudio.iss"
$manifestPath = Join-Path $PSScriptRoot "src\wStreamAudio\app.manifest"

if (-not (Test-Path -LiteralPath $propsPath)) {
    throw "Versionsdatei nicht gefunden: $propsPath"
}

if ($Command -eq "get") {
    [xml]$xml = Get-Content -LiteralPath $propsPath
    $node = $xml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='AppVersion']")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "AppVersion wurde in $propsPath nicht gefunden."
    }

    Write-Output $node.InnerText.Trim()
    exit 0
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Für 'set' muss eine Version angegeben werden: .\mqversion.ps1 set 0.2.1"
}

Test-Version -Value $Version

Set-TextFileVersion `
    -Path $propsPath `
    -Pattern '<AppVersion>[^<]+</AppVersion>' `
    -Replacement "<AppVersion>$Version</AppVersion>"

Set-TextFileVersion `
    -Path $installerPath `
    -Pattern '#define AppVersion "\d+\.\d+\.\d+"' `
    -Replacement "#define AppVersion `"$Version`""

Set-TextFileVersion `
    -Path $manifestPath `
    -Pattern 'version="\d+\.\d+\.\d+\.\d+" name="wStreamAudio\.app"' `
    -Replacement "version=`"$Version.0`" name=`"wStreamAudio.app`""

Write-Output $Version
