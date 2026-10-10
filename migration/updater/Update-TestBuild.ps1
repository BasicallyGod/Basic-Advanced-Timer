#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Update', 'Launch', 'Rollback')][string]$Mode = 'Update',
    # Which branch to take the newest successful build from. The picker window passes this; double-clicking the cmd files uses main.
    [string]$Branch = 'main'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repository = 'BasicallyGod/Basic-Advanced-Timer'
$Workflow = 'avalonia-preview.yml'
$ArtifactName = 'BasicsAdvancedTimer-Windows-preview'
$Executable = 'BasicsAdvancedTimer.Preview.exe'
$UpdaterDirectory = $PSScriptRoot
# Fixed private installation alongside this standalone script. Never accept a production data path.
$Root = Join-Path $PSScriptRoot 'test-install'
$Versions = Join-Path $Root 'versions'
$Data = Join-Path $Root 'data'
$StatePath = Join-Path $Root 'state.json'

function Assert-BranchName([string]$Name) {
    # Branch names go into a web address and a record on disk, so only ordinary characters are accepted.
    if ($Name -notmatch '^[A-Za-z0-9][A-Za-z0-9._/-]{0,99}$' -or $Name -match '\.\.|//|/$|\.lock$') { throw "Not a usable branch name: $Name" }
}

function Invoke-GhJson([string]$Endpoint) {
    $output = & gh api --hostname github.com $Endpoint
    if ($LASTEXITCODE -ne 0) { throw "GitHub request failed: $Endpoint. Check gh auth status and repository access." }
    return ($output -join "`n" | ConvertFrom-Json)
}

function Assert-Build([string]$Folder) {
    foreach ($name in @($Executable, 'BasicsAdvancedTimer.Preview.dll', 'BasicsAdvancedTimer.Preview.deps.json',
            'BasicsAdvancedTimer.Preview.runtimeconfig.json', 'Timer.Core.dll', 'Timer.Storage.dll', 'coreclr.dll', 'test-build-support.json')) {
        $file = Join-Path $Folder $name
        if (!(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
            throw "Invalid portable build: missing or empty $name. Current build is unchanged."
        }
    }
    $marker = Get-Content -LiteralPath (Join-Path $Folder 'test-build-support.json') -Raw | ConvertFrom-Json
    if ($marker.schemaVersion -ne 1 -or $marker.testBuildArgument -ne '--test-build') {
        throw 'This artifact does not support isolated test mode. Wait for a build containing this updater change.'
    }
    $stream = [IO.File]::OpenRead((Join-Path $Folder $Executable))
    try { if ($stream.ReadByte() -ne 77 -or $stream.ReadByte() -ne 90) { throw 'Executable is not a Windows PE file.' } }
    finally { $stream.Dispose() }
    if (@(Get-ChildItem -LiteralPath $Folder -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
        throw 'Artifact contains a link/reparse point.'
    }
}

function Get-BuildFolder($Build) {
    if ($null -eq $Build -or $Build.folder -notmatch '^run-[0-9]+-attempt-[0-9]+-artifact-[0-9]+$') { throw 'Invalid installed-build record.' }
    return Join-Path $Versions $Build.folder
}

function Read-State {
    if (!(Test-Path -LiteralPath $StatePath)) { return [pscustomobject]@{ current = $null; previous = $null } }
    $state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
    if ($state.schemaVersion -ne 1) { throw 'Unsupported updater state. No files have been reset.' }
    foreach ($build in @($state.current, $state.previous)) {
        if ($null -ne $build) { $null = Get-BuildFolder $build }
    }
    return $state
}

function Write-State($Current, $Previous) {
    $temporary = Join-Path $Root ('state-' + [guid]::NewGuid().ToString('N') + '.tmp')
    $json = [ordered]@{ schemaVersion = 1; current = $Current; previous = $Previous } | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($temporary, $json, [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $StatePath) { [IO.File]::Replace($temporary, $StatePath, (Join-Path $Root 'state.json.bak')) }
    else { [IO.File]::Move($temporary, $StatePath) }
}

function Wait-TestClosed {
    while ($true) {
        $running = @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($Executable)) -ErrorAction SilentlyContinue | Where-Object {
            try { $_.Path.StartsWith($Versions + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) }
            catch { $true } # Fail closed when process access is denied.
        })
        $locked = $false
        try {
            $handle = [IO.File]::Open((Join-Path $Data '.migration.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $handle.Dispose()
        } catch { $locked = $true }
        if ($running.Count -eq 0 -and !$locked) { return }
        $answer = Read-Host 'The test timer is running. Exit it from the tray menu, then press Enter to retry (Q cancels)'
        if ($answer -eq 'Q') { throw 'Cancelled. No running application was terminated.' }
    }
}

function Backup-TestData {
    $backup = Join-Path $Root ('data-backups/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $backup
    Get-ChildItem -LiteralPath $Data -Force | Where-Object Name -ne '.migration.lock' | Copy-Item -Destination $backup -Recurse
    Write-Host "Test data snapshot: $backup"
}

function Start-Build($Build) {
    $folder = Get-BuildFolder $Build
    Assert-Build $folder
    Wait-TestClosed
    Write-Host "Branch: $($Build.branch)"
    Write-Host "Commit: $($Build.commitSha)"
    Write-Host "Run: $($Build.runId), attempt $($Build.runAttempt)"
    Write-Host "Build date (artifact upload, local): $(([datetimeoffset]$Build.buildDate).ToLocalTime())"
    Write-Host "Test data: $Data"
    Start-Process -FilePath (Join-Path $folder $Executable) -WorkingDirectory $Data -ArgumentList @('--test-build', '--data-dir', ('"' + $Data + '"'))
}

function Get-LatestBuild {
    # Only this workflow on this branch; PR merge builds and unrelated successful checks are excluded.
    for ($page = 1; ; $page++) {
        $response = Invoke-GhJson "repos/$Repository/actions/workflows/$Workflow/runs?branch=$([uri]::EscapeDataString($Branch))&status=success&per_page=100&page=$page"
        $runs = @($response.workflow_runs)
        foreach ($run in $runs) {
            if ($run.head_branch -ne $Branch -or $run.event -notin @('push', 'workflow_dispatch') -or
                $run.status -ne 'completed' -or $run.conclusion -ne 'success') { continue }
            $artifacts = @()
            for ($artifactPage = 1; ; $artifactPage++) {
                $result = Invoke-GhJson "repos/$Repository/actions/runs/$($run.id)/artifacts?per_page=100&page=$artifactPage"
                $artifacts += @($result.artifacts | Where-Object { $_.name -eq $ArtifactName -and !$_.expired })
                if (@($result.artifacts).Count -lt 100) { break }
            }
            if ($artifacts.Count -eq 0) { continue }
            if ($artifacts.Count -ne 1) { throw 'Ambiguous portable artifacts for one run; refusing to choose.' }
            return [pscustomobject]@{ run = $run; artifact = $artifacts[0] }
        }
        if ($runs.Count -lt 100) { throw 'No successful portable build for this branch with with an unexpired artifact is available.' }
    }
}

function Invoke-TestUpdater {
    Assert-BranchName $Branch
    # Reject junctions into production, including any parent of the standalone updater.
    $item = Get-Item -LiteralPath $UpdaterDirectory
    while ($null -ne $item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Updater installation must not be under a junction or symbolic link.' }
        $item = $item.Parent
    }
    if ((Test-Path -LiteralPath $Root) -and ((Get-Item -LiteralPath $Root).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Updater installation must not be a junction or symbolic link.'
    }
    if ((Test-Path -LiteralPath $Root) -and @(Get-ChildItem -LiteralPath $Root -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
        throw 'Updater installation contains a junction or symbolic link.'
    }
    foreach ($path in @($Root, $Versions, $Data)) { $null = New-Item -ItemType Directory -Path $path -Force }
    $lock = [IO.File]::Open((Join-Path $Root 'updater.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $state = Read-State
        if ($Mode -eq 'Launch') { Start-Build $state.current; return }
        if ($Mode -eq 'Rollback') {
            if ($null -eq $state.previous) { throw 'No previous build is available.' }
            Assert-Build (Get-BuildFolder $state.previous)
            Wait-TestClosed
            Backup-TestData
            Write-State $state.previous $state.current
            Start-Build $state.previous
            return
        }
        if (!(Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Install GitHub CLI (https://cli.github.com), then run gh auth login once. Launch Current works offline.' }
        $latest = Get-LatestBuild
        $run = $latest.run
        $artifact = $latest.artifact
        Write-Host "Latest successful $Branch build: run $($run.id), commit $($run.head_sha), uploaded $($artifact.created_at)"
        $build = [pscustomobject]@{
            branch = $Branch; runId = $run.id; runAttempt = $run.run_attempt; commitSha = $run.head_sha
            artifactId = $artifact.id; buildDate = $artifact.created_at
            folder = "run-$($run.id)-attempt-$($run.run_attempt)-artifact-$($artifact.id)"
        }
        if ($null -ne $state.current -and $state.current.runId -eq $build.runId -and
            $state.current.commitSha -eq $build.commitSha -and $state.current.artifactId -eq $build.artifactId) {
            Write-Host 'Latest successful build is already installed; skipping download.'
            Start-Build $state.current
            return
        }
        $staging = Join-Path $Root ('staging-' + [guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $staging
        # gh extracts only the named artifact directly into this fresh staging directory.
        & gh run download "$($run.id)" --repo $Repository --name $ArtifactName --dir $staging
        if ($LASTEXITCODE -ne 0) { throw "Download failed. Current build is unchanged. Staging retained at $staging" }
        Assert-Build $staging
        Wait-TestClosed
        Backup-TestData
        $destination = Get-BuildFolder $build
        if (Test-Path -LiteralPath $destination) {
            # A previous interrupted promotion may have left this complete version behind.
            Assert-Build $destination
        } else { [IO.Directory]::Move($staging, $destination) }
        # Immutable version directories + atomic pointer replacement keep the old build usable on failure.
        Write-State $build $state.current
        Start-Build $build
    } finally { $lock.Dispose() }
}

try { Invoke-TestUpdater }
catch { Write-Host "Updater stopped: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
