# Offline regression tests: mock GitHub/download/process boundaries, exercise real filesystem promotion.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$source = Join-Path $PSScriptRoot 'Update-TestBuild.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($definition in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    Invoke-Expression $definition.Extent.Text
}
# The picker window cannot be opened in a test, but it must at least parse.
$windowTokens = $null; $windowErrors = $null
$null = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Timer-Updater-Window.ps1'), [ref]$windowTokens, [ref]$windowErrors)
if ($windowErrors.Count) { throw ($windowErrors | Out-String) }
$Repository = 'BasicallyGod/Basic-Advanced-Timer'; $Branch = 'migration'; $Workflow = 'avalonia-preview.yml'
$UpdaterDirectory = $PSScriptRoot
$UpdaterScript = Join-Path $PSScriptRoot 'Update-TestBuild.ps1'
$ArtifactName = 'BasicsAdvancedTimer-Windows-preview'; $Executable = 'BasicsAdvancedTimer.Preview.exe'
$Root = Join-Path ([IO.Path]::GetTempPath()) ('timer-updater-tests-' + [guid]::NewGuid().ToString('N'))
$Versions = Join-Path $Root 'versions'; $Data = Join-Path $Root 'data'; $StatePath = Join-Path $Root 'state.json'
$Mode = 'Update'; $script:Downloads = 0; $script:Launches = 0; $script:FailDownload = $false; $script:BadMarker = $false
$script:Offline = $false; $script:RunId = 10; $script:ArtifactId = 100
$null = New-Item -ItemType Directory -Path $Root
function Assert($condition, $message) { if (!$condition) { throw $message } }
function Expect-Failure([scriptblock]$action) {
    $failed = $false
    try { & $action } catch { $failed = $true }
    Assert $failed 'Expected an error.'
}
function Invoke-GhJson($Endpoint) {
    if ($script:Offline) { throw 'Offline' }
    if ($Endpoint -match '/artifacts\?') {
        return [pscustomobject]@{ artifacts = @([pscustomobject]@{ id = $script:ArtifactId; name = $ArtifactName; expired = $false; created_at = '2026-10-10T12:32:16Z' }) }
    }
    Assert ($Endpoint -match ('workflows/avalonia-preview.yml/runs\?branch=' + [regex]::Escape([uri]::EscapeDataString($Branch)) + '&status=success')) 'Wrong workflow/branch filter.'
    return [pscustomobject]@{ workflow_runs = @([pscustomobject]@{
        id = $script:RunId; run_attempt = 1; head_branch = $Branch; head_sha = ('a' * 40)
        event = 'push'; status = 'completed'; conclusion = 'success'
    }) }
}
function gh {
    $script:Downloads++
    $destination = $args[[Array]::IndexOf($args, '--dir') + 1]
    [IO.File]::WriteAllBytes((Join-Path $destination $Executable), [byte[]]@(77, 90, 1))
    if ($script:FailDownload) { $global:LASTEXITCODE = 1; return }
    foreach ($name in @('BasicsAdvancedTimer.Preview.dll', 'BasicsAdvancedTimer.Preview.deps.json', 'BasicsAdvancedTimer.Preview.runtimeconfig.json', 'Timer.Core.dll', 'Timer.Storage.dll', 'coreclr.dll')) {
        [IO.File]::WriteAllText((Join-Path $destination $name), 'fixture')
    }
    if (!$script:BadMarker) { [IO.File]::WriteAllText((Join-Path $destination 'test-build-support.json'), '{"schemaVersion":1,"testBuildArgument":"--test-build"}') }
    $global:LASTEXITCODE = 0
}
function Get-Process { param($Name, $ErrorAction) return @() }
function Start-Process {
    param($FilePath, $WorkingDirectory, $ArgumentList)
    Assert ($WorkingDirectory -eq $Data) 'Wrong working directory.'
    Assert ($ArgumentList[0] -eq '--test-build' -and $ArgumentList[1] -eq '--data-dir') 'Missing isolation arguments.'
    Assert ($ArgumentList[2] -eq ('"' + $Data + '"')) 'Data path was not quoted.'
    Assert ($ArgumentList[3] -eq '--updater-script' -and $ArgumentList[4] -match 'Update-TestBuild\.ps1"$') 'Missing updater script path.'
    $script:Launches++
}
try {
    Invoke-TestUpdater
    Assert ($script:Downloads -eq 1 -and $script:Launches -eq 1) 'First install failed.'
    $sentinel = Join-Path $Data 'basics-advanced-timer.settings.json'
    [IO.File]::WriteAllText($sentinel, 'persistent settings')
    Invoke-TestUpdater
    Assert ($script:Downloads -eq 1 -and $script:Launches -eq 2) 'Same build downloaded again.'
    $script:RunId = 11; $script:ArtifactId = 101; $script:FailDownload = $true
    Expect-Failure { Invoke-TestUpdater }
    Assert ((Read-State).current.runId -eq 10) 'Partial download changed current build.'
    $script:FailDownload = $false; $script:BadMarker = $true
    Expect-Failure { Invoke-TestUpdater }
    Assert ((Read-State).current.runId -eq 10) 'Unisolated artifact replaced current build.'
    $script:BadMarker = $false
    Invoke-TestUpdater
    $state = Read-State
    Assert ($state.current.runId -eq 11 -and $state.previous.runId -eq 10) 'Previous build was not preserved.'
    Assert ((Get-Content -LiteralPath $sentinel -Raw) -eq 'persistent settings') 'Update changed persistent data.'
    Assert (Test-Path -LiteralPath (Join-Path (Get-BuildFolder $state.previous) $Executable)) 'Previous executable is missing.'
    $Mode = 'Rollback'
    Invoke-TestUpdater
    Assert ((Read-State).current.runId -eq 10 -and (Read-State).previous.runId -eq 11) 'Rollback did not swap versions.'
    Assert ((Get-Content -LiteralPath $sentinel -Raw) -eq 'persistent settings') 'Rollback changed data.'
    $Mode = 'Launch'; $script:Offline = $true
    Invoke-TestUpdater
    $Mode = 'Update'
    Expect-Failure { Invoke-TestUpdater }
    Assert ((Read-State).current.runId -eq 10) 'Offline error changed installed version.'
    $handle = [IO.File]::Open((Join-Path $Root 'updater.lock'), 'Open', 'ReadWrite', 'None')
    try { Expect-Failure { Invoke-TestUpdater } } finally { $handle.Dispose() }
    # A busy timer must prompt, and cancelling must leave the build untouched.
    function Read-Host { param($Prompt) return 'Q' }
    $handle = [IO.File]::Open((Join-Path $Data '.migration.lock'), 'Open', 'ReadWrite', 'None')
    try { Expect-Failure { Wait-TestClosed } } finally { $handle.Dispose() }
    Expect-Failure { Get-BuildFolder ([pscustomobject]@{ folder = '../production' }) }
    foreach ($bad in @('', '..', 'a/../b', 'x y', 'main;calc', '-flag', 'a//b', 'a/', 'x.lock')) { Expect-Failure { Assert-BranchName $bad } }
    foreach ($good in @('main', 'migration', 'feature/new-theme', 'release-1.2')) { Assert-BranchName $good }
    # Choosing a different branch asks GitHub for that branch and records it with the build.
    $Mode = 'Update'; $script:Offline = $false; $script:RunId = 20; $script:ArtifactId = 200; $Branch = 'feature/new-theme'
    Invoke-TestUpdater
    Assert ((Read-State).current.runId -eq 20 -and (Read-State).current.branch -eq 'feature/new-theme') 'Other branch was not recorded.'
    Write-Host 'PASS: install, no-download launch, partial download, validation, data preservation, rollback, offline launch, locks and path rejection.'
} finally {
    # Only delete the exact temporary directory created by this test.
    $resolved = [IO.Path]::GetFullPath($Root)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved).StartsWith('timer-updater-tests-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
