# Run in Windows PowerShell 5.1: powershell -NoProfile -File tests\timeline-projection.ps1
# Static, non-visual tests. No account settings, private CSV files or windows are loaded.
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'AccountTimer2.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
if (@($parseErrors).Count -gt 0) { throw "The application has PowerShell parse errors: $($parseErrors -join '; ')" }
$names = @('Convert-SessionRow', 'Get-TimelineGaps', 'Set-TimelineLanes', 'Get-DayTimeline',
           'Get-WeekEntries', 'Get-WeekLunchStarts', 'New-EntryBlock', 'Get-EntryBlocks',
           'Get-DayLegend', 'Get-EntryTypeLabel')
foreach ($name in $names) {
    $fn = @($ast.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    if ($fn.Count -ne 1) { throw "Expected one function named $name, got $($fn.Count)" }
    Invoke-Expression $fn[0].Extent.Text
}
function Assert($condition, $message) { if (-not $condition) { throw $message } }
$script:Inv = [System.Globalization.CultureInfo]::InvariantCulture
$script:GB = [System.Globalization.CultureInfo]::GetCultureInfo('en-GB')
$script:notes = @{}
$script:active = $null
$script:mergeEntries = $true
$script:EntryMinSeconds = 60
$script:weekStartDow = [int][DayOfWeek]::Monday
$script:fixtureRows = @()
function Read-Sessions { return $script:fixtureRows }
function Get-DayPlan([datetime]$day) {
    return @{ Working = $true; Start = $day.Date.AddHours(9); End = $day.Date.AddHours(17) }
}
function Get-NoteKey($date, $account, $start) { return "$date/$account/$start" }
function Format-Dur($seconds) { return '{0:0}m' -f [math]::Floor($seconds / 60) }
function Format-Hours($seconds) { return ([math]::Round($seconds / 3600, 2)).ToString('0.00', $script:Inv) }

function Row($account, $start, $end, $minutes) {
    return [pscustomobject]@{ Date='2026-10-07'; Account=$account; Start=$start; End=$end; Minutes=$minutes.ToString($script:Inv) }
}
$raw = @(
    (Row 'ION Analytics' '09:00:00' '09:20:00' 20),
    (Row 'ION Analytics' '09:23:00' '09:23:20' 0.33),
    (Row 'ION Analytics' '09:00:00' 'merged' 60),
    (Row 'ION Analytics' '10:00:00' 'added' 30),
    (Row 'ION Analytics' '10:40:00' 'edited' 15),
    (Row 'ION Analytics' '11:00:00' 'rounded up' 5),
    (Row 'Lunch' '12:00:00' '12:50:00' 50)
)
$script:fixtureRows = @($raw | ForEach-Object { Convert-SessionRow $_ })
$day = [datetime]'2026-10-07'
$timeline = Get-DayTimeline $day
Assert ($timeline.Blocks.Count -eq 5) 'Timeline should show two recorded stretches, two positioned manual stretches and lunch.'
Assert ($timeline.UnplacedSeconds -eq 3600) 'Merged hour must remain credited but unplaced.'
Assert (@($timeline.Blocks | Where-Object { $_.Origin -eq 'merged' }).Count -eq 0) 'Legacy merge must not become an invented interval.'
Assert (@($timeline.Blocks | Where-Object { $_.Seconds -lt 60 }).Count -eq 1) 'Short recorded session must remain on the timeline.'
Assert (@($timeline.Blocks | Where-Object { $_.Origin -eq 'edited' -or $_.Origin -eq 'added' }).Count -eq 2) 'Explicit manual positions must remain visible.'

$ws = [datetime]'2026-10-05'
$entries = @(Get-WeekEntries $ws)
$blocks = @(Get-EntryBlocks $ws)
Assert ($entries.Count -eq 6) 'Every non-lunch record, including a short one and an adjustment, must appear in weekly entries.'
Assert ($blocks.Count -eq 5) 'Separated same-account intervals must not be grouped as if continuous.'
$sourceSum = ($entries | Measure-Object -Property Seconds -Sum).Sum
$blockSum = ($blocks | Measure-Object -Property Seconds -Sum).Sum
Assert ([math]::Abs($sourceSum - $blockSum) -lt 0.01) 'Weekly blocks must reconcile to source credits.'
$mergeBlock = @($blocks | Where-Object { $_.Origin -eq 'merged' })
Assert ($mergeBlock.Count -eq 1) 'Merged credit should have its own weekly entry.'
Assert ((Get-EntryTypeLabel $mergeBlock[0].Origin) -eq 'Merged credit (unplaced)') 'Legacy merge must be labelled unplaced.'

$accountSecs = New-Object 'double[]' 7
$lunchSecs = New-Object 'double[]' 7
$unallocSecs = New-Object 'double[]' 7
$accountSecs[2] = $sourceSum; $lunchSecs[2] = 3000
$data = [pscustomobject]@{ Names=@('ION Analytics'); Accounts=@{ 'ION Analytics'=$accountSecs };
    Lunch=$lunchSecs; Unalloc=$unallocSecs }
$legend = @(Get-DayLegend $day $data $ws $timeline)
Assert (@($legend | Where-Object { $_.Kind -eq 'unplaced' }).Count -eq 1) 'Sidebar must expose unplaced credited time.'
Write-Host 'Timeline projection and weekly reconciliation tests passed.'
