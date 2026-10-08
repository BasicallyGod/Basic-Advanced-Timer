# Run on Windows: powershell -NoProfile -File tests\timeline-projection.ps1
# Synthetic fixtures only; no user sessions or configuration files are loaded.
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'AccountTimer2.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
if (@($parseErrors).Count -gt 0) { throw "PowerShell syntax errors: $($parseErrors -join '; ')" }
$names = @(
    'Convert-SessionRow', 'Get-SessionKey', 'Get-EntryGroupId',
    'Get-CreditPositionSeconds', 'Get-UnplacedCredits', 'Get-CreditPositionSessions',
    'Get-TimelineGaps', 'Resolve-TimelineGapFill', 'Set-TimelineLanes', 'Get-DayTimeline',
    'Get-WeekEntries', 'Get-WeekLunchStarts', 'New-EntryBlock', 'Get-EntryBlocks',
    'Get-DayLegend', 'Get-EntryTypeLabel'
)
foreach ($name in $names) {
    $fn = @($ast.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    if ($fn.Count -ne 1) { throw "Expected one $name function, found $($fn.Count)" }
    Invoke-Expression $fn[0].Extent.Text
}
function Assert([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function CloseTo([double]$left, [double]$right, [string]$message) {
    if ([math]::Abs($left - $right) -gt 0.01) { throw "$message ($left versus $right)" }
}
function Row($account, $start, $end, $minutes) {
    [pscustomobject]@{
        Date = '2026-10-07'; Account = $account; Start = $start; End = $end
        Minutes = $minutes.ToString('0.00', $script:Inv)
    }
}
$script:Inv = [System.Globalization.CultureInfo]::InvariantCulture
$script:GB = [System.Globalization.CultureInfo]::GetCultureInfo('en-GB')
$script:notes = @{}
$script:active = $null
$script:mergeEntries = $true
$script:weekStartDow = [int][DayOfWeek]::Monday
$script:entryGroups = @()
$script:creditPositions = @()
$raw = @(
    (Row 'Account A' '09:00:00' '09:20:00' 20),
    (Row 'Account A' '09:23:00' '09:23:20' 0.33),
    (Row 'Account A' '09:00:00' 'merged' 60),
    (Row 'Account A' '10:00:00' 'added' 30),
    (Row 'Account A' '10:40:00' 'edited' 15),
    (Row 'Account A' '11:00:00' 'rounded up' 5),
    (Row 'Lunch' '12:00:00' '12:50:00' 50)
)
$script:fixtureRows = @($raw | ForEach-Object { Convert-SessionRow $_ })
function Read-Sessions { return $script:fixtureRows }
function Get-DayPlan([datetime]$day) {
    return @{ Working = $true; Start = $day.Date.AddHours(9); End = $day.Date.AddHours(17) }
}
function Get-NoteKey($date, $account, $start) { return "$date/$account/$start" }
function Format-Dur($seconds) { return '{0:0}m' -f [math]::Floor($seconds / 60) }
function Format-Hours($seconds) { return ([math]::Round($seconds / 3600, 2)).ToString('0.00', $script:Inv) }

$day = [datetime]'2026-10-07'
$ws = [datetime]'2026-10-05'
$before = Get-DayTimeline $day
$entriesBefore = @(Get-WeekEntries $ws)
$beforeSeconds = [double]($entriesBefore | Measure-Object -Property Seconds -Sum).Sum
Assert ($before.Blocks.Count -eq 5) 'Only recorded/manual/lunch intervals should be positioned.'
CloseTo $before.UnplacedSeconds 3600 'The merged hour must be unplaced initially.'
Assert (@($before.Blocks | Where-Object { $_.Seconds -lt 60 }).Count -eq 1) 'A short recording must occupy real timeline time.'
Assert ($entriesBefore.Count -eq 6) 'Detailed weekly entries must include all six non-lunch credits.'

# Position twenty minutes of the existing hour, without editing the source ledger.
$script:creditPositions = @(
    [pscustomobject]@{ Id='sample-1'; Date='2026-10-07'; Account='Account A'; Start='11:20:00'; Minutes='20.00'; Note='Synthetic activity note' }
)
$after = Get-DayTimeline $day
$entriesAfter = @(Get-WeekEntries $ws)
CloseTo $after.UnplacedSeconds 2400 'Unplaced credit must decrease by exactly the positioned amount.'
CloseTo [double]($entriesAfter | Measure-Object -Property Seconds -Sum).Sum $beforeSeconds 'Positioning cannot create new credited time.'
Assert ($after.Blocks.Count -eq 6) 'Positioned credit must show as an additional timeline interval.'
Assert (@($after.Blocks | Where-Object { $_.Origin -eq 'positioned' }).Count -eq 1) 'Position should have its own projection.'
Assert (@($after.Blocks | Where-Object { $_.Origin -eq 'positioned' -and $_.Note -eq 'Synthetic activity note' }).Count -eq 1) 'Position notes must remain attached to their own metadata.'
Assert (@($entriesAfter | Where-Object { $_.Origin -eq 'positioned' }).Count -eq 1) 'Weekly detail should show the positioned portion.'
CloseTo [double](@($entriesAfter | Where-Object { $_.Origin -eq 'merged' })[0].Seconds) 2400 'Merged residual is only the unplaced balance.'
Assert (@($script:fixtureRows | Where-Object { $_.Origin -eq 'merged' }).Count -eq 1) 'Original merged source remains untouched.'
CloseTo [double](@($script:fixtureRows | Where-Object { $_.Origin -eq 'merged' })[0].Seconds) 3600 'Merged source retains full original credit.'
Assert (@($after.Gaps | Where-Object { $_.StartH -le (11 + 20/60.0) -and $_.EndH -ge (11 + 40/60.0) }).Count -eq 0) 'A positioned interval is not available for gap filling.'

# Removing a position restores the unplaced amount, without changing totals.
$script:creditPositions = @()
$restored = Get-DayTimeline $day
CloseTo $restored.UnplacedSeconds 3600 'Removing a position restores unplaced credit.'
CloseTo [double]((Get-WeekEntries $ws | Measure-Object Seconds -Sum).Sum) $beforeSeconds 'Removing a position must not change account totals.'

# Explicit grouping references the source rows without erasing their timestamps.
$groupId = 'test-group'
$script:entryGroups = @(
    [pscustomobject]@{ GroupId=$groupId; Date=$raw[0].Date; Account=$raw[0].Account; Start=$raw[0].Start; End=$raw[0].End; Minutes=$raw[0].Minutes },
    [pscustomobject]@{ GroupId=$groupId; Date=$raw[1].Date; Account=$raw[1].Account; Start=$raw[1].Start; End=$raw[1].End; Minutes=$raw[1].Minutes }
)
$blocks = @(Get-EntryBlocks $ws)
$explicit = @($blocks | Where-Object { $_.ExplicitGroup })
Assert ($explicit.Count -eq 1) 'An explicit group must be displayed as one weekly block.'
Assert (@($explicit[0].Parts).Count -eq 2) 'The group must still contain both original source sessions.'
CloseTo [double]($blocks | Measure-Object Seconds -Sum).Sum $beforeSeconds 'Grouping must preserve credited totals.'
$script:mergeEntries = $false
Assert (@((Get-EntryBlocks $ws) | Where-Object { $_.ExplicitGroup }).Count -eq 1) 'Explicit groups persist when automatic grouping is disabled.'
CloseTo [double](@($script:fixtureRows | Where-Object { $_.Origin -eq 'recorded' }).Count) 2 'Grouping must not delete source rows.'
# Synthetic reproduction: end of preceding session derived from 16.62 CSV minutes
# falls at 10:03:14.2; next session starts at 10:29:59.
$gapStartH = (10 * 3600 + 3 * 60 + 14.2) / 3600.0
$gapEndH = (10 * 3600 + 29 * 60 + 59) / 3600.0
$gapRange = @([pscustomobject]@{ StartH = $gapStartH; EndH = $gapEndH })
$safe = Resolve-TimelineGapFill $gapRange $gapStartH $gapEndH
Assert ($null -ne $safe) 'A fractional-second gap must be fillable.'
Assert ($safe.StartSecond -eq (10 * 3600 + 3 * 60 + 15)) 'The start must snap inside the gap, not before the preceding session.'
Assert ($safe.EndSecond -eq (10 * 3600 + 29 * 60 + 59)) 'The end must not overlap the following session.'
$snapped = Resolve-TimelineGapFill $gapRange ((10 * 60 + 5) / 60.0) ((10 * 60 + 15) / 60.0)
Assert ($null -ne $snapped) 'A snapped drag inside the gap remains valid.'
Assert ($snapped.StartSecond -eq (10 * 3600 + 5 * 60) -and $snapped.EndSecond -eq (10 * 3600 + 15 * 60)) 'Minute-aligned bounds are retained.'
$changedGap = @([pscustomobject]@{ StartH = ((10 * 3600 + 10 * 60) / 3600.0); EndH = $gapEndH })
Assert ($null -eq (Resolve-TimelineGapFill $changedGap $gapStartH $gapEndH)) 'Genuinely occupied portions must still be rejected.'
$subsecond = @([pscustomobject]@{ StartH = ((10 * 3600 + 3 * 60 + 14.2) / 3600.0); EndH = ((10 * 3600 + 3 * 60 + 15.1) / 3600.0) })
Assert ($null -eq (Resolve-TimelineGapFill $subsecond $subsecond[0].StartH $subsecond[0].EndH)) 'A sub-second gap cannot be saved at whole-second precision.'

Write-Host 'Synthetic timeline, credit positioning, and grouping checks passed.'
