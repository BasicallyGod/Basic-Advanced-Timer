# Synthetic Tidy day tests. No user data or UI is loaded.
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'AccountTimer2.ps1'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if (@($errors).Count) { throw "PowerShell syntax errors: $errors" }
foreach ($name in @('Get-TimelineGaps', 'Resolve-TimelineGapFill', 'Get-TidyDayCandidates', 'Apply-TidyDay', 'New-SessionRow')) {
    $fn = @($ast.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    if ($fn.Count -ne 1) { throw "Missing $name" }
    Invoke-Expression $fn[0].Extent.Text
}
function Assert($ok, $message) { if (-not $ok) { throw $message } }
function Block($name, $start, $end, $kind = 'time', $origin = 'recorded') {
    return [pscustomobject]@{ Name = $name; StartH = $start; EndH = $end; Kind = $kind; Origin = $origin }
}
$script:Inv = [Globalization.CultureInfo]::InvariantCulture
$day = [datetime]::Today.AddDays(-1)
$script:fixture = @{ Day = $day; Blocks = @(
    (Block 'A' 9 10),
    (Block 'B' (10 + 30.0/3600) 11),
    (Block 'C' (11 + 2.0/60) 12),
    (Block 'Lunch' (12 + 1.0/60) 13 'lunch'),
    (Block 'A' (13 + 1.0/60) 14),
    (Block 'B' (14 + 1.0/60) 15 'time' 'running')
) }
$script:away = @()
function Get-DayTimeline($date) { return $script:fixture }
function Get-AwayOn($date, $now) { return $script:away }
function Show-Message($text) { $script:lastMessage = $text }
# Test the editing callback against an in-memory transaction, preserving an Undo snapshot.
$script:ledger = New-Object System.Collections.ArrayList
function Invoke-LogChange($description, $change) {
    $logRows = New-Object System.Collections.ArrayList
    foreach ($row in $script:ledger) { [void]$logRows.Add($row) }
    $ok = & $change
    if ($ok) { $script:undoLedger = @($script:ledger); $script:ledger = $logRows }
    return $ok
}
$candidates = @(Get-TidyDayCandidates $fixture 5 @())
Assert ($candidates.Count -eq 2) 'Include short gaps but exclude lunch and running neighbours.'
Assert ($candidates[0].EndSecond - $candidates[0].StartSecond -eq 30) 'Sub-minute gaps must be offered.'
Assert ($candidates[0].Choices -contains 'A' -and $candidates[0].Choices -contains 'B') 'Both adjacent accounts must be offered.'
Assert (@(Get-TidyDayCandidates $fixture 1 @()).Count -eq 1) 'The maximum gap must filter candidates.'
$script:away = @(); $script:away += ,@($day.AddHours(11), $day.AddHours(11.1))
Assert (@(Get-TidyDayCandidates $fixture 5 $script:away).Count -eq 1) 'Away intervals must not be filled.'
$script:away = @()
$selections = @($candidates | ForEach-Object {
    [pscustomobject]@{ StartSecond = $_.StartSecond; EndSecond = $_.EndSecond; Previous = $_.Previous; Next = $_.Next; Account = $_.Next }
})
Assert (Apply-TidyDay $day $selections 5) 'Batch apply must succeed.'
Assert ($script:ledger.Count -eq 2) 'Only selected gaps should be added.'
$totalMinutes = ($script:ledger | Measure-Object Minutes -Sum).Sum
Assert ([math]::Abs($totalMinutes - 2.5) -lt 0.001) 'Added credited time must equal the selected gaps.'
Assert ($script:undoLedger.Count -eq 0) 'The whole batch must have one undo snapshot.'
Assert ($fixture.Blocks[0].EndH -eq 10 -and $fixture.Blocks[1].StartH -eq (10 + 30.0/3600)) 'Original timestamps must not move.'
$script:ledger.Clear()
$fixture.Blocks[2].StartH += 1.0/60
Assert (-not (Apply-TidyDay $day $selections 5)) 'A changed gap must reject the entire batch.'
Assert ($script:ledger.Count -eq 0) 'A rejected batch must add no earlier valid gap.'
$fixture.Blocks[2].StartH -= 1.0/60
$selections[0].Account = 'Unrelated account'
Assert (-not (Apply-TidyDay $day $selections 5)) 'Only neighbouring accounts may be assigned.'
$selections[0].Account = 'B'
Assert (-not (Apply-TidyDay $day @($selections[0], $selections[0]) 5)) 'Duplicate gap selections must be rejected.'
$future = @{ Day = [datetime]::Today.AddDays(1); Blocks = $fixture.Blocks }
Assert (@(Get-TidyDayCandidates $future 5 @()).Count -eq 0) 'Future time must not be fabricated.'
# Decimal CSV edges should be rounded inward, leaving at most a sub-second residual.
$fractional = @{ Day = $day; Blocks = @((Block 'A' 9 (10 + 0.2/3600)), (Block 'B' (10 + 30.8/3600) 11)) }
$small = @(Get-TidyDayCandidates $fractional 5 @())
Assert ($small[0].StartSecond -eq 36001 -and $small[0].EndSecond -eq 36030) 'Fractional edges must be rounded safely inward.'

$script:fixture = @{ Day = $day; Blocks = @((Block 'A' 9 10), (Block 'B' (10 + 31.0/3600) 11)) }
$script:ledger.Clear()
$gap31 = @(Get-TidyDayCandidates $script:fixture 5 @())[0]
$fill31 = [pscustomobject]@{ StartSecond = $gap31.StartSecond; EndSecond = $gap31.EndSecond; Previous = 'A'; Next = 'B'; Account = 'A' }
Assert (Apply-TidyDay $day @($fill31) 5) 'A 31-second gap must apply.'
Assert (([double]$script:ledger[0].Minutes * 60) -le 31) 'CSV rounding must never extend into the next entry.'
Write-Output 'Tidy day checks passed.'

