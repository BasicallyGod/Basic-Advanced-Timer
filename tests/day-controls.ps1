# Synthetic day-control checks; never loads the application's UI or personal data.
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'AccountTimer2.ps1'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if (@($errors).Count) { throw "PowerShell syntax errors: $errors" }
foreach ($name in @('Get-UsualHours', 'Get-ScheduledDayPlan', 'Get-DayPlan', 'Start-Day',
    'Get-DayButtonText', 'Test-DayDone', 'Close-AwayInterval', 'Get-AwayOn',
    'Set-DayDone', 'Switch-DayDone', 'Limit-Time', 'Update-View')) {
    $fn = @($ast.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    if ($fn.Count -ne 1) { throw "Missing function $name" }
    Invoke-Expression $fn[0].Extent.Text
}
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Get-Date { return $script:clock }
function Write-SettingSection($key, $lines) { $script:savedStarts = @($lines) }
function Save-Away { }
function Stop-ByUser { $script:active = $null }
function Update-DrawerContent { }
function Request-Redraw { }
function Resize-Form { }
function Format-Clock($seconds) { return [string][math]::Floor($seconds) }
function Format-Dur($seconds) { return [string]$seconds }

$script:Inv = [Globalization.CultureInfo]::InvariantCulture
$script:clock = [datetime]::Today.AddHours(8).AddSeconds(17)
$script:StartTime = '09:00'; $script:EndTime = '17:00'
$script:workDays = @(0..6); $script:dayHours = @{}; $script:dayOverrides = @{}
$script:dayStarts = @{}; $script:awayIntervals = @()
$script:active = $null; $script:slots = @(); $script:lunchIntervals = @()
$script:lunchAdjust = 0; $script:todayAccountIntervals = @()
$script:BaseRowTop = 100; $script:RowTop = 100; $script:OvertimeH = 30
$script:FinishAreaH = 30; $script:finishH = 0; $script:tray = @{}
$script:AppName = 'Test timer'
Assert ((Get-DayButtonText $script:clock) -eq 'Start day') 'Early arrival needs Start day.'
Switch-DayDone
Assert ((Get-DayPlan $script:clock).Start -eq $script:clock) 'Start must use the exact press time.'
Assert ($script:view.DayBtnText -eq 'Finish day') 'The button must immediately offer Finish day.'
$script:clock = $script:clock.AddMinutes(20)
Update-View
Assert ($script:view.Unallocated -eq 'UNALLOCATED 1200') 'Early work must count towards the day.'
Switch-DayDone
Assert (Test-DayDone) 'Finishing early must work.'
$script:clock = $script:clock.AddMinutes(10)
Update-View
Assert ($script:view.Unallocated -eq 'UNALLOCATED 1200') 'Finished time must not increase unallocated time.'
Assert ($script:view.DayBtnText -eq 'Start day') 'Finished days must offer Start day.'
Switch-DayDone
$script:clock = $script:clock.AddMinutes(10)
Update-View
Assert ($script:view.Unallocated -eq 'UNALLOCATED 1800') 'Resume must preserve earlier work and exclude away time.'

# Persistence uses the round-trip datetime representation, preserving seconds.
$script:dayStarts = @{}
foreach ($line in $script:savedStarts) {
    $parts = $line -split "`t", 2
    $script:dayStarts[$parts[0]] = [datetime]::ParseExact($parts[1], 'o', $Inv)
}
Assert ((Get-DayPlan $script:clock).Start.Second -eq 17) 'A restart must preserve exact start seconds.'
Assert ((Get-DayPlan $script:clock.AddDays(1)).Start.Hour -eq 9) 'Tomorrow must retain usual hours.'

$script:clock = [datetime]::Today.AddHours(18)
Update-View
Assert ($script:view.DayBtnText -eq 'Finish day') 'The control must remain available after the scheduled end.'
Switch-DayDone
$script:clock = $script:clock.AddMinutes(15)
Update-View
Assert ($script:view.OT -eq '+01:00:00') 'Finishing must freeze overtime.'
Switch-DayDone
$script:clock = $script:clock.AddMinutes(10)
Update-View
Assert ($script:view.OT -eq '+01:10:00') 'Resuming after the scheduled end must exclude the finished interval.'

# An explicit day off can be started, including a first start after the usual end.
$script:dayStarts = @{}; $script:awayIntervals = @()
$script:dayOverrides[$script:clock.ToString('yyyy-MM-dd', $Inv)] = @{ Off = $true }
Assert ((Get-DayButtonText $script:clock) -eq 'Start day') 'Days off need an explicit start.'
Switch-DayDone
Assert ($script:view.OT -eq '+00:00:00') 'Late first starts must not invent earlier overtime.'
$script:clock = $script:clock.AddMinutes(5)
Update-View
Assert ($script:view.OT -eq '+00:05:00') 'Late work must count from the press.'
Write-Output 'Day-control checks passed.'
