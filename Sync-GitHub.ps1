<#
.SYNOPSIS
    Synchronisiert wStreamAudio mit dem konfigurierten Git-Remote (z. B. GitHub).

.DESCRIPTION
    Fehlt "origin", wird es mit -OriginUrl angelegt (Vorgabe:
    CannonRS/wStreamAudio). Danach wie gewohnt Pull/Push; Upstream setzt -u beim
    ersten Push.

    Es wird immer auf -Branch gewechselt (Vorgabe: main); Pull/Push ohne
    Upstream nutzt ausdrücklich diesen Branch (origin/<Branch>).

    Ohne gemeinsamen Vorfahren mit dem Remote-Branch erkennt das Skript das und
    nutzt automatisch --allow-unrelated-histories beim Pull (GitHub-README/LICENSE
    + lokaler erster Commit). -AllowUnrelatedHistories erzwingt das zusätzlich.

.PARAMETER Action
    Pull, Push oder PullPush (Standard: erst pull, dann push).

.PARAMETER SkipPull
    Nur bei PullPush: kein pull, nur push (kann fehlschlagen, wenn Remote voraus ist).

.PARAMETER Branch
    Branch für git switch und für explizite pull/push origin/<Branch> (Vorgabe: main).

.PARAMETER Rebase
    Bei Pull: git pull --rebase (wird ignoriert, wenn -AllowUnrelatedHistories gesetzt).

.PARAMETER AllowUnrelatedHistories
    Bei Pull: erzwingt --allow-unrelated-histories (selten nötig; meist automatisch).

.PARAMETER PushForceWithLease
    Bei Push: git push --force-with-lease (nur mit Absicht).

.PARAMETER OriginUrl
    URL für "git remote add origin", falls origin noch fehlt (HTTPS oder SSH).

.PARAMETER ReleaseSetup
    Erstellt oder aktualisiert nach dem Push ein GitHub Release und lädt den
    Setup-Installer aus artifacts\installer hoch. Benötigt GitHub CLI (gh).

.PARAMETER ReleaseVersion
    Version für Tag/Release/Setup-Dateiname. Leer = AppVersion aus Directory.Build.props.

.PARAMETER ReleaseTag
    Git-Tag für das Release. Leer = v<ReleaseVersion>.

.PARAMETER ReleaseTitle
    Titel des GitHub Releases. Leer = wStreamAudio <ReleaseVersion>.

.PARAMETER ReleaseNotes
    Release-Notizen. Leer = Setup-Installer für wStreamAudio <ReleaseVersion>.

.PARAMETER SetupPath
    Pfad zur Setup-Datei. Leer = artifacts\installer\wStreamAudio-Setup-<ReleaseVersion>.exe.

.PARAMETER DraftRelease
    Release als Draft erstellen.

.PARAMETER Prerelease
    Release als Pre-Release markieren.

.PARAMETER ForceReleaseAsset
    Überschreibt bei einem bereits bestehenden Release das Setup-Asset. Ohne diesen
    Schalter bricht -ReleaseSetup ab, wenn das Release schon existiert.
#>
param(
    [ValidateSet("Pull", "Push", "PullPush")]
    [string]$Action = "PullPush",
    [string]$Branch = "main",
    [switch]$Rebase,
    [switch]$AllowUnrelatedHistories,
    [switch]$PushForceWithLease,
    [switch]$SkipPull,
    [string]$OriginUrl = "https://github.com/CannonRS/wStreamAudio.git",
    [switch]$ReleaseSetup,
    [string]$ReleaseVersion = "",
    [string]$ReleaseTag = "",
    [string]$ReleaseTitle = "",
    [string]$ReleaseNotes = "",
    [string]$SetupPath = "",
    [switch]$DraftRelease,
    [switch]$Prerelease,
    [switch]$ForceReleaseAsset
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Branch)) {
    throw "Branch darf nicht leer sein (Vorgabe: main)."
}

$syncBranch = $Branch.Trim()

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "git wurde nicht gefunden. Git for Windows installieren und die Sitzung neu starten."
}

function Invoke-RepoGit {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host ("git " + ($Arguments -join " "))
    & git -C $repoRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git-Befehl ist fehlgeschlagen (Exit $LASTEXITCODE)."
    }
}

function Assert-GitRepo {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot ".git"))) {
        throw "Kein Git-Repository (.git fehlt unter $repoRoot)."
    }
}

function Get-CurrentBranchName {
    $name = (& git -C $repoRoot branch --show-current 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($name)) {
        throw "Kein Branch ermittelbar (detached HEAD?). Bitte einen Branch auschecken."
    }

    return $name.Trim()
}

function Ensure-TargetBranch {
    Invoke-RepoGit @("switch", $syncBranch)
    $current = Get-CurrentBranchName
    if ($current -ne $syncBranch) {
        throw "Erwartet Branch '$syncBranch', ausgecheckt ist '$current'."
    }
}

function Test-UpstreamConfigured {
    $null = & git -C $repoRoot rev-parse --abbrev-ref '@{upstream}' 2>$null
    return ($LASTEXITCODE -eq 0)
}

function Test-OriginExists {
    $null = & git -C $repoRoot remote get-url origin 2>$null
    return ($LASTEXITCODE -eq 0)
}

function Ensure-Origin {
    param([Parameter(Mandatory = $true)][string]$Url)

    if (Test-OriginExists) {
        $current = (& git -C $repoRoot remote get-url origin).Trim()
        Write-Host "Remote origin: $current"
        return
    }

    if ([string]::IsNullOrWhiteSpace($Url)) {
        throw "Kein Remote 'origin' - bitte -OriginUrl setzen (oder einmalig manuell: git remote add origin ...)."
    }

    Invoke-RepoGit @("remote", "add", "origin", $Url.Trim())
}

function Show-Context {
    Write-Host "Synchronisations-Branch: $syncBranch | Repo: $repoRoot"

    if (Test-UpstreamConfigured) {
        $upstream = (& git -C $repoRoot rev-parse --abbrev-ref '@{upstream}').Trim()
        Write-Host "Upstream: $upstream"
    }
    else {
        Write-Host "Kein Upstream - Pull/Push nutzen origin/$syncBranch (Tracking nach erstem Push -u)."
    }
}

function Invoke-RepoPull {
    param(
        [switch]$UseRebase,
        [switch]$AllowUnrelated
    )

    $useUnrelatedMerge = [bool]$AllowUnrelated

    if (-not $useUnrelatedMerge) {
        $otherRef = $null
        if (Test-UpstreamConfigured) {
            $otherRef = (& git -C $repoRoot rev-parse --abbrev-ref '@{upstream}').Trim()
        }
        elseif (Test-OriginExists) {
            $candidate = "origin/$syncBranch"
            & git -C $repoRoot rev-parse --verify $candidate 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) {
                $otherRef = $candidate
            }
        }

        if ($null -ne $otherRef) {
            & git -C $repoRoot merge-base HEAD $otherRef 2>$null | Out-Null
            if ($LASTEXITCODE -ne 0) {
                $useUnrelatedMerge = $true
                Write-Host "Hinweis: kein gemeinsamer Vorfahr mit $otherRef - Pull mit --allow-unrelated-histories."
            }
        }
    }

    if ($useUnrelatedMerge) {
        if ($UseRebase) {
            Write-Host "Hinweis: -Rebase wird bei unrelated merge ignoriert (Merge)."
        }

        if (Test-UpstreamConfigured) {
            Invoke-RepoGit @("pull", "--allow-unrelated-histories")
        }
        else {
            Invoke-RepoGit @("pull", "origin", $syncBranch, "--allow-unrelated-histories")
        }

        return
    }

    if (Test-UpstreamConfigured) {
        if ($UseRebase) {
            Invoke-RepoGit @("pull", "--rebase")
        }
        else {
            Invoke-RepoGit @("pull")
        }

        return
    }

    if (-not (Test-OriginExists)) {
        throw "Intern: origin fehlt nach Ensure-Origin."
    }

    if ($UseRebase) {
        Invoke-RepoGit @("pull", "--rebase", "origin", $syncBranch)
    }
    else {
        Invoke-RepoGit @("pull", "origin", $syncBranch)
    }
}

function Invoke-RepoPush {
    param([switch]$ForceWithLease)

    if (Test-UpstreamConfigured) {
        if ($ForceWithLease) {
            Invoke-RepoGit @("push", "--force-with-lease")
        }
        else {
            Invoke-RepoGit @("push")
        }

        return
    }

    if (-not (Test-OriginExists)) {
        throw "Intern: origin fehlt nach Ensure-Origin."
    }

    if ($ForceWithLease) {
        Invoke-RepoGit @("push", "--force-with-lease", "-u", "origin", $syncBranch)
    }
    else {
        Invoke-RepoGit @("push", "-u", "origin", $syncBranch)
    }
}

function Get-AppVersion {
    $propsPath = Join-Path $repoRoot "Directory.Build.props"
    [xml]$props = Get-Content -LiteralPath $propsPath
    $version = $props.Project.PropertyGroup.AppVersion
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "AppVersion wurde in Directory.Build.props nicht gefunden."
    }

    return $version.Trim()
}

function Test-GitHubReleaseExists {
    param([Parameter(Mandatory = $true)][string]$Tag)

    $null = & gh release view $Tag 2>$null
    return ($LASTEXITCODE -eq 0)
}

function Invoke-GitHubReleaseSetup {
    if ($Action -eq "Pull") {
        throw "-ReleaseSetup ist nur mit -Action Push oder PullPush sinnvoll."
    }

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI 'gh' wurde nicht gefunden. Installieren: winget install GitHub.cli"
    }

    $version = if ([string]::IsNullOrWhiteSpace($ReleaseVersion)) { Get-AppVersion } else { $ReleaseVersion.Trim() }
    $tag = if ([string]::IsNullOrWhiteSpace($ReleaseTag)) { "v$version" } else { $ReleaseTag.Trim() }
    $title = if ([string]::IsNullOrWhiteSpace($ReleaseTitle)) { "wStreamAudio $version" } else { $ReleaseTitle.Trim() }
    $notes = if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) { "Setup-Installer für wStreamAudio $version." } else { $ReleaseNotes }
    $setup = if ([string]::IsNullOrWhiteSpace($SetupPath)) {
        Join-Path $repoRoot "artifacts\installer\wStreamAudio-Setup-$version.exe"
    }
    elseif ([System.IO.Path]::IsPathRooted($SetupPath)) {
        $SetupPath
    }
    else {
        Join-Path $repoRoot $SetupPath
    }

    if (-not (Test-Path -LiteralPath $setup)) {
        throw "Setup-Datei wurde nicht gefunden: $setup`nVorher .\Build.ps1 ausführen oder -SetupPath setzen."
    }

    if (Test-GitHubReleaseExists -Tag $tag) {
        if (-not $ForceReleaseAsset) {
            throw "GitHub Release $tag existiert bereits. Version erhöhen oder bewusst mit -ForceReleaseAsset überschreiben."
        }

        $head = (& git -C $repoRoot rev-parse HEAD).Trim()
        Invoke-RepoGit @("tag", "-f", $tag, $head)
        Invoke-RepoGit @("push", "origin", $tag, "--force")
        Write-Host "GitHub Release $tag existiert - lade Setup-Asset wegen -ForceReleaseAsset neu hoch."
        & gh release upload $tag $setup --clobber
        if ($LASTEXITCODE -ne 0) {
            throw "gh release upload ist fehlgeschlagen."
        }
        return
    }

    $head = (& git -C $repoRoot rev-parse HEAD).Trim()
    Invoke-RepoGit @("tag", "-f", $tag, $head)
    Invoke-RepoGit @("push", "origin", $tag, "--force")

    $args = @("release", "create", $tag, $setup, "--title", $title, "--notes", $notes, "--target", $syncBranch)
    if ($DraftRelease) {
        $args += "--draft"
    }
    if ($Prerelease) {
        $args += "--prerelease"
    }

    Write-Host ("gh " + ($args -join " "))
    & gh @args
    if ($LASTEXITCODE -ne 0) {
        throw "gh release create ist fehlgeschlagen."
    }
}

Assert-GitRepo
Ensure-Origin -Url $OriginUrl
Ensure-TargetBranch
Show-Context

switch ($Action) {
    "Pull" {
        Invoke-RepoPull -UseRebase:$Rebase -AllowUnrelated:$AllowUnrelatedHistories
    }
    "Push" {
        Invoke-RepoPush -ForceWithLease:$PushForceWithLease
    }
    "PullPush" {
        if (-not $SkipPull) {
            Invoke-RepoPull -UseRebase:$Rebase -AllowUnrelated:$AllowUnrelatedHistories
        }

        Invoke-RepoPush -ForceWithLease:$PushForceWithLease
    }
}

if ($ReleaseSetup) {
    Invoke-GitHubReleaseSetup
}

Write-Host "Fertig ($Action)."
