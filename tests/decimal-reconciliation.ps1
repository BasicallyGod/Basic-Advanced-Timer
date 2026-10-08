# Synthetic decimal-hour reconciliation tests (Windows PowerShell 5.1).
# No client timesheets, user settings or file writes are required.
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'AccountTimer2.ps1'
$tokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
if (@($parseErrors).Count -gt 0) { throw "AccountTimer2.ps1 parse errors: $($parseErrors -join '; ')" }
foreach ($name in @('Distribute-HundredthHours', 'Get-WeekReconciliation', 'Format-HundredthHours')) {
    $fn = @($ast.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    if ($fn.Count -ne 1) { throw "Expected one $name; found $($fn.Count)" }
    Invoke-Expression $fn[0].Extent.Text
}
function Assert([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Block($name, $day, [double]$seconds, $id) {
    [pscustomobject]@{ Name=$name; Day=$day; Seconds=$seconds
                       Start=[datetime]'2026-10-05'; Kind='time'; Origin='recorded'
                       Parts=@(); Raw=$id }
}
function Row($name, $day, [double]$seconds) {
    [pscustomobject]@{ Name=$name; Start=([datetime]'2026-10-05').AddDays($day); Seconds=$seconds }
}
function Get-EntryBlocks($ws, [switch]$NoRunning) { return $script:blocks }
function Read-Sessions { return $script:ledger }
$script:Inv = [System.Globalization.CultureInfo]::InvariantCulture
$ws = [datetime]'2026-10-05'

# Three 20-minute sessions: individually rounding would give 0.99h.
$script:blocks = @((Block 'Account A' 0 1200 1), (Block 'Account A' 0 1200 2), (Block 'Account A' 0 1200 3))
$script:ledger = @((Row 'Account A' 0 1200),(Row 'Account A' 0 1200),(Row 'Account A' 0 1200))
$r = Get-WeekReconciliation $ws
Assert ($r.GrandUnits -eq 100) 'Three 20-minute sessions must credit 1.00h.'
Assert ((@($r.Rows | Measure-Object Units -Sum)[0].Sum) -eq 100) 'Detailed decimal rows must total 1.00h.'
Assert ($r.Accounts['Account A'][0] -eq 100) 'Account/day must total 1.00h.'
Assert ((Format-HundredthHours $r.GrandUnits) -eq '1.00') 'Formatting must be invariant and two decimal places.'
$firstRows = @($r.Rows | ForEach-Object { $_.Units })
$rRepeat = Get-WeekReconciliation $ws
Assert ((@($rRepeat.Rows | ForEach-Object { $_.Units }) -join ',') -eq ($firstRows -join ',')) 'Balancing must be deterministic.'

# Two separately rounded half-cent account buckets must reconcile to a one-cent week.
$script:blocks = @((Block 'Account A' 0 18 1),(Block 'Account B' 1 18 2))
$script:ledger = @((Row 'Account A' 0 18),(Row 'Account B' 1 18))
$r = Get-WeekReconciliation $ws
Assert ($r.GrandUnits -eq 1) 'A full 36 seconds is one hundredth of an hour.'
Assert ($r.Accounts['Account A'][0] + $r.Accounts['Account B'][1] -eq 1) 'Account/day decimals must add up to grand total.'

# Negative manual adjustments are included exactly once; preserve net credit.
$script:blocks = @((Block 'Account A' 0 80 1),(Block 'Account A' 0 -40 2))
$script:ledger = @((Row 'Account A' 0 80),(Row 'Account A' 0 -40))
$r = Get-WeekReconciliation $ws
Assert ($r.GrandUnits -eq 1) 'Positive and negative adjustments net to one hundredth.'
Assert ((@($r.Rows | Measure-Object Units -Sum)[0].Sum) -eq 1) 'Negative adjustment detail must reconcile.'
Assert ($r.Rows[1].Units -le 0) 'A negative adjustment must not become positive time.'

# Lunch must be balanced separately from the account total (excluded from billable).
$script:blocks = @((Block 'Account A' 0 3600 1))
$script:ledger = @((Row 'Account A' 0 3600),(Row 'Lunch' 0 1200),(Row 'Lunch' 1 1200),(Row 'Lunch' 2 1200))
$r = Get-WeekReconciliation $ws
Assert ($r.GrandUnits -eq 100) 'Lunch must not be counted in the account grand total.'
Assert ((@($r.LunchUnits | Measure-Object -Sum)[0].Sum) -eq 100) 'Lunch must reconcile across the week.'
Assert ($r.LunchUnits.Count -eq 7) 'All seven days should have lunch columns.'

# If the entry projection omits a recorded credit, error rather than writing mismatched exports.
$script:blocks = @((Block 'Account A' 0 1200 1))
$script:ledger = @((Row 'Account A' 0 1200),(Row 'Account A' 0 60))
$rejected = $false
try { $null = Get-WeekReconciliation $ws } catch { $rejected = $true }
Assert $rejected 'A missing ledger entry must fail reconciliation rather than silently publish.'

# Grouping changes how entries appear, never their exact total or overall decimal credit.
$script:blocks = @((Block 'Account A' 0 2400 'group'),(Block 'Account A' 0 1200 3))
$script:ledger = @((Row 'Account A' 0 1200),(Row 'Account A' 0 1200),(Row 'Account A' 0 1200))
$r = Get-WeekReconciliation $ws
Assert ($r.GrandUnits -eq 100) 'Grouping must not change credited decimal hours.'
Assert ((@($r.Rows | Measure-Object Units -Sum)[0].Sum) -eq 100) 'Grouped rows must reconcile.'
Write-Host 'Decimal reconciliation tests passed.'
