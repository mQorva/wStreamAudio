<#
.SYNOPSIS
    Synchronisiert mit dem konfigurierten Git-Remote (z. B. GitHub).

.DESCRIPTION
    Fehlt "origin", wird es mit -OriginUrl angelegt (Vorgabe: öffentliches Repo
    mQorva/wStreamAudio). Danach wie gewohnt Pull/Push; Upstream setzt -u beim
    ersten Push.

    Es wird immer auf -Branch gewechselt (Vorgabe: main); Pull/Push ohne Upstream
    nutzt ausdrücklich diesen Branch (origin/<Branch>).

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

.PARAMETER NoAutoCommit
    Bei Push/PullPush: lokale Änderungen nicht automatisch committen.

.PARAMETER CommitMessage
    Commit-Nachricht für den automatischen Commit bei Push/PullPush.
    Ohne Angabe wird sie aus den vorgemerkten Änderungen abgeleitet.

.PARAMETER OriginUrl
    URL für "git remote add origin", falls origin noch fehlt (HTTPS oder SSH).

.PARAMETER ReleaseSetup
    Erstellt oder aktualisiert nach dem Push ein GitHub Release und lädt den
    Setup-Installer aus artifacts\installer hoch. Installiert GitHub CLI (gh)
    automatisch, falls sie fehlt.
    Fragt interaktiv Draft/Pre-Release und bei Bedarf Überschreiben ab. Alias: -Release.

.PARAMETER ReleaseNotesPath
    Optionale Markdown-Datei für die Release-Notizen. Ohne Angabe wird zuerst
    docs\releases\<Version>.md verwendet und ansonsten ein kurzer Standardtext.
#>
param(
    [ValidateSet("Pull", "Push", "PullPush")]
    [string]$Action = "PullPush",
    [string]$Branch = "main",
    [switch]$Rebase,
    [switch]$AllowUnrelatedHistories,
    [switch]$PushForceWithLease,
    [switch]$SkipPull,
    [switch]$NoAutoCommit,
    [string]$CommitMessage = "",
    [string]$OriginUrl = "https://github.com/mQorva/wStreamAudio.git",
    [string]$ReleaseNotesPath = "",
    [Alias("Release")]
    [switch]$ReleaseSetup
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
$script:GitHubCliCommand = $null

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

function Get-AppVersion {
    $propsPath = Join-Path $repoRoot "Directory.Build.props"
    if (-not (Test-Path -LiteralPath $propsPath)) {
        throw "Directory.Build.props fehlt im Repo-Root - -ReleaseSetup steht in diesem Repository nicht zur Verfügung."
    }

    [xml]$props = Get-Content -LiteralPath $propsPath
    $version = $props.Project.PropertyGroup.AppVersion
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "AppVersion wurde in Directory.Build.props nicht gefunden."
    }

    return $version.Trim()
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

# Commit-Nachricht aus dem Staging-Bereich ableiten: Bereiche + Art der Änderungen.
function New-SyncMessage {
    $rows = @(& git -C $repoRoot diff --cached --name-status)
    if (-not $rows) { return "chore: Arbeitsstand synchronisiert" }

    $added = 0; $modified = 0; $deleted = 0; $renamed = 0
    $areas = New-Object System.Collections.Generic.List[string]
    $files = New-Object System.Collections.Generic.List[string]

    foreach ($row in $rows) {
        $parts = $row -split "`t"
        $code = $parts[0]
        $path = $parts[-1]
        switch -Regex ($code) {
            '^A' { $added++ }
            '^D' { $deleted++ }
            '^R' { $renamed++ }
            '^C' { $added++ }
            default { $modified++ }
        }
        $files.Add($path)
        $seg = ($path -split '/')[0]
        if ($seg -eq $path) { $seg = "Repo-Root" }
        if (-not $areas.Contains($seg)) { $areas.Add($seg) }
    }

    $total = $rows.Count
    if ($total -eq 1) {
        $scope = $files[0]
    }
    elseif ($areas.Count -le 3) {
        $scope = ($areas -join ', ')
    }
    else {
        $scope = "{0}, {1}, {2} und {3} weitere Bereiche" -f $areas[0], $areas[1], $areas[2], ($areas.Count - 3)
    }

    $bits = @()
    if ($added) { $bits += "$added neu" }
    if ($modified) { $bits += "$modified geändert" }
    if ($deleted) { $bits += "$deleted gelöscht" }
    if ($renamed) { $bits += "$renamed verschoben" }

    $summary = "chore: $scope aktualisiert ({0})" -f ($bits -join ', ')
    if ($summary.Length -gt 72 -and $total -gt 1) {
        $summary = "chore: $($areas.Count) Bereiche aktualisiert ({0} Dateien: {1})" -f $total, ($bits -join ', ')
    }

    return $summary
}

function Test-WorkingTreeHasChanges {
    $status = (& git -C $repoRoot status --porcelain)
    if ($LASTEXITCODE -ne 0) {
        throw "Git-Status konnte nicht ermittelt werden."
    }

    return ($null -ne $status -and $status.Count -gt 0)
}

function Invoke-AutoCommitLocalChanges {
    if ($NoAutoCommit) {
        Write-Host "Auto-Commit ist deaktiviert (-NoAutoCommit)."
        return
    }

    if (-not (Test-WorkingTreeHasChanges)) {
        Write-Host "Keine lokalen Änderungen für Auto-Commit."
        return
    }

    Invoke-RepoGit @("add", "-A")

    & git -C $repoRoot diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Keine committbaren Änderungen nach git add -A."
        return
    }
    elseif ($LASTEXITCODE -ne 1) {
        throw "Git-Diff gegen den Index ist fehlgeschlagen (Exit $LASTEXITCODE)."
    }

    $message = $CommitMessage.Trim()
    if ([string]::IsNullOrWhiteSpace($message)) {
        $message = New-SyncMessage
        Write-Host "Commit-Nachricht abgeleitet: $message"
    }

    Invoke-RepoGit @("commit", "-m", $message)
}

function Test-RemoteBranchExists {
    # Ein frisch angelegtes Repo kennt den Branch noch nicht; ein Pull bricht dann ab.
    $refs = (& git -C $repoRoot ls-remote --heads origin $syncBranch 2>$null)
    if ($LASTEXITCODE -ne 0) { return $false }

    return -not [string]::IsNullOrWhiteSpace(($refs | Out-String).Trim())
}

function Invoke-RepoPull {
    param(
        [switch]$UseRebase,
        [switch]$AllowUnrelated
    )

    if (-not (Test-UpstreamConfigured) -and -not (Test-RemoteBranchExists)) {
        Write-Host "Remote kennt den Branch '$syncBranch' noch nicht - Pull wird uebersprungen."
        return
    }

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

function Add-DirectoryToPath {
    param([Parameter(Mandatory = $true)][string]$Directory)

    if (-not (Test-Path -LiteralPath $Directory)) {
        return
    }

    $pathParts = @($env:PATH -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if (-not ($pathParts | Where-Object { $_ -ieq $Directory })) {
        $env:PATH = "$Directory;$env:PATH"
    }

    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $userParts = @($userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if (-not ($userParts | Where-Object { $_ -ieq $Directory })) {
        $newUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $Directory } else { "$Directory;$userPath" }
        [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
    }
}

function Get-GitHubCliCommand {
    if (-not [string]::IsNullOrWhiteSpace($script:GitHubCliCommand) -and (Test-Path -LiteralPath $script:GitHubCliCommand)) {
        return $script:GitHubCliCommand
    }

    $command = Get-Command gh -ErrorAction SilentlyContinue
    if ($command) {
        $script:GitHubCliCommand = $command.Source
        return $script:GitHubCliCommand
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles "GitHub CLI\gh.exe"),
        (Join-Path $env:ProgramFiles "GitHub CLI\bin\gh.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\GitHub CLI\gh.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\GitHub CLI\bin\gh.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\GitHub CLI Portable\gh.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\GitHub CLI Portable\bin\gh.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            Add-DirectoryToPath -Directory (Split-Path -Parent $candidate)
            $script:GitHubCliCommand = $candidate
            return $script:GitHubCliCommand
        }
    }

    return $null
}

function Install-GitHubCliWithWinget {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        return $false
    }

    Write-Host "GitHub CLI fehlt - installiere über winget ..."
    & winget install --id GitHub.cli --source winget --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        Write-Host "winget-Installation ist fehlgeschlagen (Exit $LASTEXITCODE) - nutze portablen Fallback."
        return $false
    }

    return ($null -ne (Get-GitHubCliCommand))
}

function Install-GitHubCliPortable {
    $installRoot = Join-Path $env:LOCALAPPDATA "Programs\GitHub CLI Portable"
    $zipPath = Join-Path $env:TEMP "gh-windows-amd64.zip"
    $extractRoot = Join-Path $env:TEMP ("gh-portable-" + [guid]::NewGuid().ToString("N"))

    Write-Host "GitHub CLI fehlt - installiere portabel nach $installRoot ..."
    New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null

    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/cli/cli/releases/latest" -Headers @{ "User-Agent" = "wStreamAudio-Git-Sync" }
        $asset = $release.assets | Where-Object { $_.name -match "windows_amd64\.zip$" } | Select-Object -First 1
        if ($null -eq $asset) {
            throw "Kein windows_amd64.zip Asset im aktuellen GitHub CLI Release gefunden."
        }

        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force

        $gh = Get-ChildItem -Path $extractRoot -Recurse -Filter gh.exe | Select-Object -First 1
        if ($null -eq $gh) {
            throw "gh.exe wurde im heruntergeladenen Archiv nicht gefunden."
        }

        Copy-Item -LiteralPath $gh.FullName -Destination (Join-Path $installRoot "gh.exe") -Force
        $license = Get-ChildItem -Path (Split-Path -Parent $gh.FullName) -Filter LICENSE* -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($license) {
            Copy-Item -LiteralPath $license.FullName -Destination $installRoot -Force
        }

        Add-DirectoryToPath -Directory $installRoot
        $script:GitHubCliCommand = Join-Path $installRoot "gh.exe"
        return $script:GitHubCliCommand
    }
    finally {
        Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Ensure-GitHubCli {
    $command = Get-GitHubCliCommand
    if ($command) {
        return $command
    }

    if (Install-GitHubCliWithWinget) {
        return (Get-GitHubCliCommand)
    }

    return (Install-GitHubCliPortable)
}

function Ensure-GitHubCliAuthentication {
    # "gh auth status" meldet einen Fehler, sobald irgendein gespeichertes Konto
    # ungültig ist, auch wenn das aktive Konto verwendbar ist. Entscheidend für
    # den Release ist deshalb ein erfolgreicher authentifizierter API-Aufruf.
    $null = & $script:GitHubCliCommand api user --jq .login 2>$null
    if ($LASTEXITCODE -eq 0) {
        return
    }

    $credentialInput = "protocol=https`nhost=github.com`n`n"
    $filled = $credentialInput | git credential fill 2>$null
    $token = ($filled | Where-Object { $_ -like "password=*" } | Select-Object -First 1) -replace "^password=", ""

    if (-not [string]::IsNullOrWhiteSpace($token)) {
        $env:GH_TOKEN = $token
        $null = & $script:GitHubCliCommand api user --jq .login 2>$null
        if ($LASTEXITCODE -eq 0) {
            return
        }
    }

    throw "GitHub CLI ist installiert, aber nicht angemeldet. Einmalig ausführen: gh auth login"
}

function Test-GitHubReleaseExists {
    param([Parameter(Mandatory = $true)][string]$Tag)

    $null = & $script:GitHubCliCommand release view $Tag 2>$null
    return ($LASTEXITCODE -eq 0)
}

function Read-YesNo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Question,
        [bool]$Default = $false
    )

    $suffix = if ($Default) { "[J/n]" } else { "[j/N]" }

    while ($true) {
        $answer = (Read-Host "$Question $suffix").Trim().ToLowerInvariant()
        if ([string]::IsNullOrWhiteSpace($answer)) {
            return $Default
        }

        switch ($answer) {
            { $_ -in @("j", "ja", "y", "yes") } { return $true }
            { $_ -in @("n", "nein", "no") } { return $false }
            default { Write-Host "Bitte 'j' oder 'n' eingeben." }
        }
    }
}

function Get-ReleaseOptions {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version,
        [Parameter(Mandatory = $true)]
        [string]$DefaultSetupPath
    )

    $title = "wStreamAudio $Version"
    $defaultNotesPath = Join-Path $repoRoot "docs\releases\$Version.md"
    $notesPath = if ([string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
        $defaultNotesPath
    }
    elseif ([System.IO.Path]::IsPathRooted($ReleaseNotesPath)) {
        $ReleaseNotesPath
    }
    else {
        Join-Path $repoRoot $ReleaseNotesPath
    }
    $notes = if (Test-Path -LiteralPath $notesPath) {
        Get-Content -LiteralPath $notesPath -Raw
    }
    else {
        "Windows-Setup-Installer für wStreamAudio $Version."
    }
    $setup = $DefaultSetupPath
    $draft = $false
    $prerelease = $false

    Write-Host "Release-Version: $Version"
    Write-Host "Release-Tag: v$Version"
    Write-Host "Setup-Datei: $DefaultSetupPath"
    if (Test-Path -LiteralPath $notesPath) {
        Write-Host "Release-Notizen: $notesPath"
    }

    $draft = Read-YesNo "Release als Draft erstellen?"
    $prerelease = Read-YesNo "Release als Pre-Release markieren?"

    [pscustomobject]@{
        Title = $title
        Notes = $notes
        SetupPath = $setup
        Draft = $draft
        Prerelease = $prerelease
    }
}

function Invoke-GitHubReleaseSetup {
    if ($Action -eq "Pull") {
        throw "-ReleaseSetup ist nur mit -Action Push oder PullPush sinnvoll."
    }

    $script:GitHubCliCommand = Ensure-GitHubCli
    Ensure-GitHubCliAuthentication

    $version = Get-AppVersion
    $tag = "v$version"
    $defaultSetup = Join-Path $repoRoot "artifacts\installer\wStreamAudio-Setup-$version.exe"
    $options = Get-ReleaseOptions -Version $version -DefaultSetupPath $defaultSetup
    $setup = $options.SetupPath

    if (-not (Test-Path -LiteralPath $setup)) {
        throw "Setup-Datei wurde nicht gefunden: $setup`nVorher .\Build.ps1 ausführen."
    }

    if (Test-GitHubReleaseExists -Tag $tag) {
        if (-not (Read-YesNo "GitHub Release $tag existiert bereits. Setup-Asset überschreiben?")) {
            throw "GitHub Release $tag existiert bereits. Version erhöhen oder Überschreiben bestätigen."
        }

        $head = (& git -C $repoRoot rev-parse HEAD).Trim()
        $tagCommit = (& git -C $repoRoot rev-list -n 1 $tag 2>$null | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($tagCommit)) {
            throw "GitHub Release $tag existiert, aber der Tag ist lokal nicht vorhanden. Tags abrufen und erneut prüfen."
        }
        if ($tagCommit -ne $head) {
            throw "GitHub Release $tag gehört zu Commit $tagCommit, HEAD ist $head. Veröffentlichte Versionstags werden nicht verschoben; bitte die Version erhöhen."
        }
        Write-Host "GitHub Release $tag existiert - lade Setup-Asset neu hoch."
        & $script:GitHubCliCommand release upload $tag $setup --clobber
        if ($LASTEXITCODE -ne 0) {
            throw "gh release upload ist fehlgeschlagen."
        }
        return
    }

    $head = (& git -C $repoRoot rev-parse HEAD).Trim()
    $existingTagCommit = (& git -C $repoRoot rev-list -n 1 $tag 2>$null | Out-String).Trim()
    if (-not [string]::IsNullOrWhiteSpace($existingTagCommit) -and $existingTagCommit -ne $head) {
        throw "Tag $tag zeigt bereits auf Commit $existingTagCommit, HEAD ist $head. Version erhöhen statt den Tag zu verschieben."
    }
    if ([string]::IsNullOrWhiteSpace($existingTagCommit)) {
        Invoke-RepoGit @("tag", $tag, $head)
    }
    Invoke-RepoGit @("push", "origin", $tag)

    $args = @("release", "create", $tag, $setup, "--title", $options.Title, "--notes", $options.Notes, "--target", $syncBranch)
    if ($options.Draft) {
        $args += "--draft"
    }
    if ($options.Prerelease) {
        $args += "--prerelease"
    }

    Write-Host ("gh " + ($args -join " "))
    & $script:GitHubCliCommand @args
    if ($LASTEXITCODE -ne 0) {
        throw "gh release create ist fehlgeschlagen."
    }
}

function Clear-StaleIndexLock {
    # Abgebrochene Git-Aufrufe lassen .git/index.lock liegen; jeder spätere Commit
    # scheitert dann. Nur aufräumen, wenn wirklich kein git-Prozess sie halten kann.
    $gitDir = (& git -C $repoRoot rev-parse --git-dir | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($gitDir)) { return }
    if (-not [System.IO.Path]::IsPathRooted($gitDir)) { $gitDir = Join-Path $repoRoot $gitDir }

    $lockFile = Join-Path $gitDir "index.lock"
    if (-not (Test-Path -LiteralPath $lockFile)) { return }

    $lockTime = (Get-Item -LiteralPath $lockFile).LastWriteTime
    $ageMinutes = ((Get-Date) - $lockTime).TotalMinutes

    # Nur ein git-Prozess, der VOR der Sperre gestartet wurde, kann ihr Halter sein.
    # Zwei Sekunden Toleranz für die Zeitstempel-Auflösung.
    $holders = @(
        Get-Process git -ErrorAction SilentlyContinue | Where-Object {
            try { $_.StartTime -le $lockTime.AddSeconds(2) } catch { $false }
        }
    ).Count

    if ($holders -gt 0 -and $ageMinutes -lt 15) {
        throw "Es läuft gerade ein anderer Git-Vorgang in diesem Repository ($holders Prozess(e), Sperre $([math]::Round($ageMinutes)) Minuten alt). Kurz warten und erneut starten."
    }

    Remove-Item -LiteralPath $lockFile -Force
    $grund = if ($holders -gt 0) { "kein gesunder Vorgang hält den Index so lange" } else { "kein passender git-Prozess aktiv" }
    Write-Host ("verwaiste Sperre entfernt (index.lock, {0:N0} Minuten alt, {1})" -f $ageMinutes, $grund) -ForegroundColor Yellow
}

Assert-GitRepo
Clear-StaleIndexLock
Ensure-Origin -Url $OriginUrl
Ensure-TargetBranch
Show-Context

if ($Action -in @("Push", "PullPush")) {
    Invoke-AutoCommitLocalChanges
}

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
