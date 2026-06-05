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
        throw "Version '$Value' ist ungültig. Erwartet wird z. B. 0.1.1."
    }
}

$propsPath = Join-Path $PSScriptRoot "Directory.Build.props"
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
    throw "Für 'set' muss eine Version angegeben werden: .\mqversion.ps1 set 0.1.1"
}

Test-Version -Value $Version

$content = Get-Content -LiteralPath $propsPath -Raw
$content = [regex]::Replace($content, '<AppVersion>[^<]+</AppVersion>', "<AppVersion>$Version</AppVersion>")
Set-Content -LiteralPath $propsPath -Value $content -NoNewline

Write-Output $Version
