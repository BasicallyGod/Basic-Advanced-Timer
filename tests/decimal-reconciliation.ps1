# Synthetic decimal-hour reconciliation tests (Windows PowerShell 5.1).
# No client timesheets, user settings or file writes are required.
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'AccountTimer2.ps1'
$tokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
if (@($parseErrors).Count -gt 0) { throw "AccountTimer2.ps1 parse errors: $($parseErrors -join '; ')" }
foreach ($name in @('Distribute-HundredthHours', 'Get-WeekReconciliation', 'Format-HundredthHours', 'Write-WeekSummary', 'Write-WeekEntries')) {
    $fn = @($ast.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    if ($fn.Count -ne 1) { throw "Expected one $name; found $($fn.Count)" }
    Invoke-Expression $fn[0].Extent.Text
}
function Assert([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Block($name, $day, [double]$seconds, $id) {
    $start = ([datetime]'2026-10-05').AddDays($day).AddHours(9)
    if ("$id" -match '^[0-9]+
    [pscustomobject]@{ Name=$name; Day=$day; Seconds=$seconds
                       Start=$start; End=$start.AddSeconds($seconds)
                       Kind='time'; Origin='recorded'; ExplicitGroup=$false
                       Note=''; Parts=@(); Raw=$id }
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

# Exercise the actual CSV writers, not only the pure apportionment functions.
# All files are synthetic and confined to a disposable temporary folder.
function Get-WeekStart { return $ws }
function Get-WeekFile { return (Join-Path $script:outputDir 'summary.csv') }
function Get-WeekEntriesFile($week) { return (Join-Path $script:outputDir 'entries.csv') }
function Get-EntryTypeLabel($kind) { return [string]$kind }
function Format-HM($seconds) {
    $n = [int][math]::Round([math]::Abs($seconds) / 60)
    return ('{0}:{1:00}' -f ([math]::Floor($n/60)),($n % 60))
}
function Write-Log($message) { throw $message }
$script:GB = [System.Globalization.CultureInfo]::GetCultureInfo('en-GB')
$script:accounts = @([pscustomobject]@{ Name='Account A' }, [pscustomobject]@{ Name='Account B' })
$script:outputDir = Join-Path ([System.IO.Path]::GetTempPath()) ('timer-reconciliation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:outputDir -Force | Out-Null
try {
    $script:blocks = @((Block 'Account A' 0 1200 1), (Block 'Account A' 0 1200 2),
                       (Block 'Account A' 0 1200 3), (Block 'Account B' 1 1200 4))
    $script:ledger = @((Row 'Account A' 0 1200),(Row 'Account A' 0 1200),
                       (Row 'Account A' 0 1200),(Row 'Account B' 1 1200),
                       (Row 'Lunch' 0 1200))
    Write-WeekSummary
    $summary = @(Import-Csv (Get-WeekFile))
    $details = @(Import-Csv (Get-WeekEntriesFile $ws))
    Assert ($summary.Count -eq 4) 'CSV summary must have account A/B, Lunch, and Total.'
    Assert ($details.Count -eq 4) 'Detailed CSV must retain all four synthetic account entries.'
    $totalRow = @($summary | Where-Object { $_.Account -eq 'Total (excluding lunch)' })[0]
    $rowA = @($summary | Where-Object { $_.Account -eq 'Account A' })[0]
    $rowB = @($summary | Where-Object { $_.Account -eq 'Account B' })[0]
    $rowLunch = @($summary | Where-Object { $_.Account -eq 'Lunch' })[0]
    Assert ($totalRow.Total -eq '1.33') 'The precise grand total of four 20-minute sessions is 1.33h.'
    Assert ($rowA.Total -eq '1.00' -and $rowB.Total -eq '0.33') 'Account weekly totals must reconcile.'
    Assert ($rowLunch.Total -eq '0.33') 'Lunch is independently reported.'
    [decimal]$sumDetails = 0
    foreach ($item in $details) { $sumDetails += [decimal]::Parse($item.Hours, $script:Inv) }
    Assert ($sumDetails -eq [decimal]::Parse($totalRow.Total, $script:Inv)) 'The written detailed CSV must sum exactly to the written summary grand total.'
    [decimal]$accountADetails = 0
    foreach ($item in @($details | Where-Object { $_.Account -eq 'Account A' })) {
        $accountADetails += [decimal]::Parse($item.Hours, $script:Inv)
    }
    Assert ($accountADetails -eq [decimal]::Parse($rowA.Total, $script:Inv)) 'Detailed account A hours must match summary.'
    Assert ($rowA.'Mon 05/10' -eq '1.00' -and $rowB.'Tue 06/10' -eq '0.33') 'Daily account cells must reconcile.'
} finally {
    Remove-Item -Path $script:outputDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Decimal reconciliation tests passed.'
) { $start = $start.AddMinutes([int]$id) }
    [pscustomobject]@{ Name=$name; Day=$day; Seconds=$seconds
                       Start=$start; End=$start.AddSeconds($seconds)
                       Kind='time'; Origin='recorded'; ExplicitGroup=$false
                       Note=''; Parts=@(); Raw=$id }
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

# Exercise the actual CSV writers, not only the pure apportionment functions.
# All files are synthetic and confined to a disposable temporary folder.
function Get-WeekStart { return $ws }
function Get-WeekFile { return (Join-Path $script:outputDir 'summary.csv') }
function Get-WeekEntriesFile($week) { return (Join-Path $script:outputDir 'entries.csv') }
function Get-EntryTypeLabel($kind) { return [string]$kind }
function Format-HM($seconds) {
    $n = [int][math]::Round([math]::Abs($seconds) / 60)
    return ('{0}:{1:00}' -f ([math]::Floor($n/60)),($n % 60))
}
function Write-Log($message) { throw $message }
$script:GB = [System.Globalization.CultureInfo]::GetCultureInfo('en-GB')
$script:accounts = @([pscustomobject]@{ Name='Account A' }, [pscustomobject]@{ Name='Account B' })
$script:outputDir = Join-Path ([System.IO.Path]::GetTempPath()) ('timer-reconciliation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:outputDir -Force | Out-Null
try {
    $script:blocks = @((Block 'Account A' 0 1200 1), (Block 'Account A' 0 1200 2),
                       (Block 'Account A' 0 1200 3), (Block 'Account B' 1 1200 4))
    $script:ledger = @((Row 'Account A' 0 1200),(Row 'Account A' 0 1200),
                       (Row 'Account A' 0 1200),(Row 'Account B' 1 1200),
                       (Row 'Lunch' 0 1200))
    Write-WeekSummary
    $summary = @(Import-Csv (Get-WeekFile))
    $details = @(Import-Csv (Get-WeekEntriesFile $ws))
    Assert ($summary.Count -eq 4) 'CSV summary must have account A/B, Lunch, and Total.'
    Assert ($details.Count -eq 4) 'Detailed CSV must retain all four synthetic account entries.'
    $totalRow = @($summary | Where-Object { $_.Account -eq 'Total (excluding lunch)' })[0]
    $rowA = @($summary | Where-Object { $_.Account -eq 'Account A' })[0]
    $rowB = @($summary | Where-Object { $_.Account -eq 'Account B' })[0]
    $rowLunch = @($summary | Where-Object { $_.Account -eq 'Lunch' })[0]
    Assert ($totalRow.Total -eq '1.33') 'The precise grand total of four 20-minute sessions is 1.33h.'
    Assert ($rowA.Total -eq '1.00' -and $rowB.Total -eq '0.33') 'Account weekly totals must reconcile.'
    Assert ($rowLunch.Total -eq '0.33') 'Lunch is independently reported.'
    [decimal]$sumDetails = 0
    foreach ($item in $details) { $sumDetails += [decimal]::Parse($item.Hours, $script:Inv) }
    Assert ($sumDetails -eq [decimal]::Parse($totalRow.Total, $script:Inv)) 'The written detailed CSV must sum exactly to the written summary grand total.'
    [decimal]$accountADetails = 0
    foreach ($item in @($details | Where-Object { $_.Account -eq 'Account A' })) {
        $accountADetails += [decimal]::Parse($item.Hours, $script:Inv)
    }
    Assert ($accountADetails -eq [decimal]::Parse($rowA.Total, $script:Inv)) 'Detailed account A hours must match summary.'
    Assert ($rowA.'Mon 05/10' -eq '1.00' -and $rowB.'Tue 06/10' -eq '0.33') 'Daily account cells must reconcile.'
} finally {
    Remove-Item -Path $script:outputDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Decimal reconciliation tests passed.'
