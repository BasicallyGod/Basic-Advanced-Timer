# Exercise the actual Windows PowerShell 5.1 launcher and form, without GitHub or a timer build.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('timer-window-tests-' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $temporaryRoot "Timer's folder & spaces"
$null = New-Item -ItemType Directory -Path $fixture -Force
function Assert($condition, $message) { if (!$condition) { throw $message } }
function Wait-Check($process) {
    $null = $process.Handle
    if (!$process.WaitForExit(20000)) { $process.Kill(); throw 'Updater window test timed out.' }
    $process.WaitForExit()
    Assert ($process.ExitCode -eq 0) "Updater window child process failed (exit $($process.ExitCode))."
}
try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Timer Updater.cmd'), (Join-Path $PSScriptRoot 'Timer-Updater-Window.ps1') -Destination $fixture
    # A missing CLI must not prevent the picker opening. Preserve Windows runtime paths only.
    $savedPath = $env:PATH
    try {
        $env:PATH = "$env:SystemRoot\System32;$env:SystemRoot\System32\WindowsPowerShell\v1.0"
        $launcher = Join-Path $fixture 'Timer Updater.cmd'
        $process = Start-Process -FilePath $env:ComSpec -WindowStyle Hidden -ArgumentList ('/d /c ""' + $launcher + '" -SmokeTest"') -PassThru -RedirectStandardOutput (Join-Path $temporaryRoot 'stdout.txt') -RedirectStandardError (Join-Path $temporaryRoot 'stderr.txt')
        Wait-Check $process
        Assert ((Get-Content (Join-Path $temporaryRoot 'stdout.txt') -Raw) -match 'PASS: Windows Forms picker shown') 'Launcher did not show the form.'
    } finally { $env:PATH = $savedPath }

    # Load only the worker dispatch function; replace the external worker with a parameter-recording fixture.
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $fixture 'Timer-Updater-Window.ps1'), [ref]$tokens, [ref]$errors)
    Assert ($errors.Count -eq 0) 'Window script failed to parse.'
    $definition = $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-Worker' }, $false)[0]
    Invoke-Expression $definition.Extent.Text
    $Worker = Join-Path $fixture 'Update-TestBuild.ps1'
    [IO.File]::WriteAllText($Worker, 'param($Mode, $Branch); [IO.File]::WriteAllText((Join-Path $PSScriptRoot "received.txt"), "$Mode|$Branch"); exit 0')
    $Root = Join-Path $fixture 'test-install'; $ChoicePath = Join-Path $Root 'last-branch.txt'
    $combo = [pscustomobject]@{ Text = 'alpha' }; $status = [pscustomobject]@{ Text = '' }
    function Show-UpdaterError($Record) { throw $Record }
    function Start-Process {
        param($FilePath, $WindowStyle, $ArgumentList)
        Assert ($WindowStyle -eq 'Normal') 'Worker console must be visible.'
        Assert ($ArgumentList[-2] -eq '-EncodedCommand') 'Worker dispatch must use encoded command.'
        $process = Microsoft.PowerShell.Management\Start-Process -FilePath $FilePath -WindowStyle Hidden -ArgumentList $ArgumentList -PassThru
        Wait-Check $process
    }
    foreach ($branch in @('alpha', 'experimental')) {
        $combo.Text = $branch
        foreach ($mode in @('Update', 'Launch', 'Rollback')) {
            Invoke-Worker $mode
            Assert ((Get-Content (Join-Path $fixture 'received.txt') -Raw) -eq "$mode|$branch") 'Worker arguments changed in a path with spaces, apostrophe or ampersand.'
            Assert ((Get-Content $ChoicePath -Raw) -eq $branch) 'Channel preference was not saved.'
        }
    }
    # Force a local startup error and verify it is reported, with a failing exit code and a diagnostic log.
    Remove-Item -LiteralPath $ChoicePath
    $null = New-Item -ItemType Directory -Path $ChoicePath
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments = '-NoProfile -ExecutionPolicy Bypass -STA -File "' + (Join-Path $fixture 'Timer-Updater-Window.ps1') + '" -SmokeTest'
    $process = Microsoft.PowerShell.Management\Start-Process -FilePath $powershell -WindowStyle Hidden -ArgumentList $arguments -PassThru -RedirectStandardOutput (Join-Path $temporaryRoot 'failure.txt')
    $null = $process.Handle
    if (!$process.WaitForExit(20000)) { $process.Kill(); throw 'Startup error check timed out.' }
    $process.WaitForExit()
    Assert ($process.ExitCode -eq 1) 'Startup error must return exit code 1.'
    Assert (Test-Path -LiteralPath (Join-Path $fixture 'updater-startup-error.log')) 'Startup diagnostic log is missing.'
    Assert ((Get-Item (Join-Path $fixture 'updater-startup-error.log')).Length -gt 0) 'Startup diagnostic log is empty.'
    Write-Host 'PASS: actual CMD startup without gh; visible form; encoded dispatch for both channels and all modes; startup error logging.'
} catch {
    foreach ($log in @('stdout.txt', 'stderr.txt')) {
        $path = Join-Path $temporaryRoot $log
        if (Test-Path -LiteralPath $path) { Write-Host (Get-Content -LiteralPath $path -Raw) }
    }
    throw
} finally {
    $resolved = [IO.Path]::GetFullPath($temporaryRoot)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved).StartsWith('timer-window-tests-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
