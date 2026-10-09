# Compile and exercise the real renderer without launching the timer or loading user data.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'AccountTimer2.ps1'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if (@($errors).Count) { throw "PowerShell syntax errors: $errors" }
$blocks = @($ast.FindAll({ param($node)
    $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
    $node.Value.Contains('public class FullRenderer')
}, $true))
if ($blocks.Count -ne 1) { throw 'Expected one embedded renderer.' }
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition $blocks[0].Value
$r = New-Object Win.FullRenderer
$r.BaseW = 320; $r.BaseH = 480; $r.DrawerW = 720; $r.DrawerH = 480
$r.DrawerOpen = $true; $r.Shadow = $false
foreach ($left in @($true, $false)) {
    $r.TimerOffX = if ($left) { 720 } else { 0 }
    $outline = $r.GetOutline()
    try {
        $timerX = if ($left) { 850 } else { 150 }
        $drawerX = if ($left) { 150 } else { 850 }
        if (-not $outline.IsVisible([single]$timerX, [single]440)) { throw 'Timer backdrop does not reach the drawer height.' }
        if (-not $outline.IsVisible([single]$drawerX, [single]440)) { throw 'Drawer content area was cut away.' }
        if (-not $outline.IsVisible([single]$timerX, [single]150)) { throw 'Timer controls were cut away.' }
    } finally { $outline.Dispose() }
}
$r.TimerOffX = 720
$font = New-Object System.Drawing.Font('Segoe UI', 15, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
foreach ($field in $r.GetType().GetFields()) {
    if ($field.FieldType -eq [System.Drawing.Font]) { $field.SetValue($r, $font) }
}
$r.Drawer = New-Object Win.DrawerModel
$r.Drawer.Title = 'Today, 9 October'
$r.Drawer.FormatLabel = 'Decimal'
$r.Drawer.NextEnabled = $true
$r.Drawer.UndoEnabled = $true
$r.Drawer.Mode = 'timeline'
$r.Drawer.Timeline = New-Object Win.TimelineModel
$r.Drawer.Timeline.AxisStartH = 7; $r.Drawer.Timeline.AxisEndH = 19
$r.TlScrollH = 7
try {
    foreach ($mode in @('timeline', 'week')) {
        $r.Drawer.Mode = $mode
        $bitmap = $r.Render(1060, 500)
        try {
            if ($bitmap.GetPixel(850, 440).A -eq 0) { throw 'Renderer leaves a hole in the timer backdrop.' }
        } finally { $bitmap.Dispose() }
        foreach ($button in $r.DrawerButtons) {
            if ($button.Rect.Height -lt 28) { throw 'Drawer button targets are too small.' }
            if ($button.Rect.Left -lt 10 -or $button.Rect.Right -gt 730) { throw 'Drawer button is outside its panel.' }
        }
    }
} finally { $font.Dispose() }
Write-Output 'Renderer layout checks passed.'

# Load the same fonts, layout constants and renderer factory used by app startup.
$script:useRenderer = $true; $script:Zoom = 1; $script:fullMargin = 0
$startupNames = @('$px', '$fDigits', '$fDashboardHeader', '$fDashboardClock', '$fLabel', '$fRow',
    '$fOvertime', '$fPillTime', '$fPillSub', '$fDrawerBtn', '$fDrawerText', '$fDrawerTextBold',
    '$fTimeline', '$fTimelineBold', '$fTimelineSmall', '$BaseRowTop', '$RowH', '$LunchGap',
    '$DashboardW', '$DashboardH', '$BaseW', '$DrawerSideW', '$CollapseR', '$CollapseX', '$CollapseY', '$DrawerBtnX', '$DrawerBtnY')
foreach ($assignment in @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true))) {
    if ($startupNames -contains $assignment.Left.Extent.Text) { Invoke-Expression $assignment.Extent.Text }
}
foreach ($name in @('New-DashboardRenderer', 'Get-RowTop', 'Get-HitRow', 'Get-TimerPoint', 'Test-HitCollapse', 'Test-HitDashboardPause', 'Test-HitDayBtn')) {
    $fn = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true))
    Invoke-Expression $fn[0].Extent.Text
}
$drawerFixture = $r.Drawer
$r = New-DashboardRenderer
$r.Drawer = $drawerFixture
if ($r.RowH -ne 48 -or $r.RowTop -ne 164) { throw 'Production startup must use the dashboard row geometry.' }

foreach ($name in @('Sync-RendererLayout', 'Get-DayBtnRect')) {
    $fn = @($ast.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    Invoke-Expression $fn[0].Extent.Text
}
function Test-FullShadow { return $false }
$script:renderer = $r; $script:Zoom = 1; $script:BaseH = 370; $script:BaseW = 320
$script:DashboardW = 960; $script:DashboardH = 640; $script:mode = 'full'
$script:GB = [Globalization.CultureInfo]::GetCultureInfo('en-GB')
$script:animClock = [Diagnostics.Stopwatch]::StartNew()
$script:view = @{ ActiveName = 'Account A' }
$script:drawerH = 480; $script:drawerOpen = $true; $script:useRenderer = $true
$script:DayBtn = @{ W = 150; H = 22 }; $script:RowTop = 100
Sync-RendererLayout
$rect = Get-DayBtnRect
if ($r.BaseH -ne 640 -or $rect.Y -ne 72 -or $rect.X -ne 790) { throw 'Dashboard day button hit target is misaligned.' }
$script:drawerOpen = $false
Sync-RendererLayout
if ($r.BaseH -ne 640) { throw 'Dashboard height must remain fixed.' }

# Exercise the new dashboard at maximum account capacity and on both tabs.
$script:drawerOpen = $true
Sync-RendererLayout
$r.DashboardDate = 'Friday, 9 October 2026'; $r.ActiveAccount = 'Account A'
$r.LiveDot = $true; $r.DotPhase = 0.5; $r.DayBtnText = 'Finish day'
# Fonts remain those from production startup; the fixture does not replace renderer metrics.
$rows = @()
for ($i = 0; $i -lt 13; $i++) {
    $row = New-Object Win.RowModel
    $row.Name = if ($i -eq 12) { 'Lunch' } else { 'Client account ' + $i }
    $row.IsLunch = ($i -eq 12); $row.TodayText = '0h 15m'; $row.WeekText = '3h 49m'
    if ($i -lt 12) { $row.BudgetFrac = 0.64 }
    $row.BudgetText = if ($i -lt 12) { '3h 49m of 6h this week' } else { '' }
    $row.Active = ($i -eq 0); $rows += $row
}
$r.Panel = [Drawing.ColorTranslator]::FromHtml('#f2f7f2'); $r.Circle = [Drawing.ColorTranslator]::FromHtml('#e4eee5')
$r.CircleHot = [Drawing.ColorTranslator]::FromHtml('#d4e5d6'); $r.Text = [Drawing.ColorTranslator]::FromHtml('#183523'); $r.Fill = [Drawing.ColorTranslator]::FromHtml('#3d984b')
$r.H = '00'; $r.M = '29'; $r.S = '59'; $r.TodayAllocated = '0h 34m'; $r.Unallocated = 'UNALLOCATED 0:00'
$r.Drawer.Timeline.HasWork = $true; $r.Drawer.Timeline.WorkStartH = 8.5; $r.Drawer.Timeline.WorkEndH = 17.5
for ($i = 0; $i -lt 3; $i++) {
    $block = New-Object Win.TimelineBlock
    $block.Name = @('Hawthorn non-billable', 'ION Analytics', 'DLA Piper')[$i]
    $block.StartH = 8.5 + $i * 0.5; $block.EndH = $block.StartH + 0.5
    $block.Colour = @([Drawing.Color]::CornflowerBlue, [Drawing.Color]::Orange, [Drawing.Color]::MediumSeaGreen)[$i]
    $r.Drawer.Timeline.Blocks.Add($block)
}
$r.Rows = $rows
$r.Drawer.DayLabels = @('MON 05', 'TUE 06', 'WED 07', 'THU 08', 'FRI 09')
for ($i = 0; $i -lt 16; $i++) {
    $weekRow = New-Object Win.DrawerRow
    $weekRow.Name = 'Client account ' + $i; $weekRow.Cells = @('1:00', '1:00', '1:00', '1:00', '1:00'); $weekRow.Total = '5:00'
    $r.Drawer.Rows.Add($weekRow)
}
try {
    foreach ($mode in @('timeline', 'week')) {
        $r.Drawer.Mode = $mode
        $bitmap = $r.Render(960, 640)
        try {
            if ($bitmap.GetPixel(150, 630).A -eq 0 -or $bitmap.GetPixel(850, 630).A -eq 0) { throw 'Dashboard backdrop must fill the fixed window.' }
            $previewRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { $env:TEMP }
            $outputDir = Join-Path $previewRoot 'timer-dashboard-previews'
            [void](New-Item -ItemType Directory -Path $outputDir -Force)
            $bitmap.Save((Join-Path $outputDir ($mode + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
        foreach ($button in $r.DrawerButtons) {
            if ($button.Rect.Left -lt 320 -or $button.Rect.Right -gt 960 -or $button.Rect.Bottom -gt 640) { throw 'Dashboard action is outside its workspace.' }
        }
        if ($mode -eq 'week') {
            if ($r.WeekContentH -le $r.WeekViewportH) { throw 'Maximum account fixture must exercise Week scrolling.' }
            $r.WeekScrollY = $r.WeekContentH - $r.WeekViewportH
            $scrolled = $r.Render(960, 640)
            $scrolled.Dispose()
        }
        if ($mode -eq 'timeline' -and ($r.TlY -ne 216 -or $r.TlW -lt 500 -or $r.TlY + $r.TlH -gt 604)) { throw 'Timeline viewport does not match the dashboard body.' }
    }
    $r.AccountScrollY = $r.Rows.Length * $r.RowH + $r.LunchGap - ([Win.FullRenderer]::AccountBottom - $r.RowTop)
    $scrolled = $r.Render(960, 640)
    try { $scrolled.Save((Join-Path $outputDir 'accounts-scrolled.png'), [Drawing.Imaging.ImageFormat]::Png) } finally { $scrolled.Dispose() }
    # Test the app's coordinate conversion and actual shared row bounds at Windows DPI scales.
    $script:slots = @((0..11 | ForEach-Object { 'Client account ' + $_ })) + @('Lunch')
    $script:mode = 'full'; $script:active = @{ Name = 'Client account 0' }; $script:view.DayBtnText = 'Finish day'
    $script:timerOffX = 0; $script:timerOffY = 0
    foreach ($testScale in @(0.8, 1.0, 1.25, 1.5, 2.0)) {
        $script:Zoom = $testScale; $script:fullMargin = 10
        Sync-RendererLayout
        foreach ($scroll in @(0, [Win.DashboardLayout]::MaxAccountScroll($script:slots.Count))) {
            $r.AccountScrollY = $scroll
            for ($rowIndex = 0; $rowIndex -lt $script:slots.Count; $rowIndex++) {
                $bounds = [Win.DashboardLayout]::AccountRect($rowIndex, ($script:slots[$rowIndex] -eq 'Lunch'), $script:slots.Count, $scroll)
                if (-not [Win.DashboardLayout]::AccountVisible($bounds)) { continue }
                foreach ($within in @(2, 24, 46)) {
                    $client = [Drawing.Point]::new([int](($r.Margin + 28) * $testScale), [int](($r.Margin + $bounds.Top + $within) * $testScale))
                    $hit = Get-HitRow (Get-TimerPoint $client)
                    if ($hit -ne $rowIndex) { throw "Pointer selects row $hit instead of $rowIndex at scale $testScale, scroll $scroll." }
                }
            }
            if ($null -ne (Get-HitRow (Get-TimerPoint ([Drawing.Point]::new([int](38 * $testScale), [int](630 * $testScale)))))) { throw 'Summary selected an account.' }
            foreach ($testMode in @('timeline', 'week')) {
                $r.Drawer.Mode = $testMode
                $scaled = $r.Render([int](980 * $testScale), [int](660 * $testScale))
                try {
                    if ($scaled.GetPixel([int](100 * $testScale), [int](620 * $testScale)).A -ne 255) { throw 'Dashboard must remain opaque at each scale.' }
                    if ($scroll -eq 0) { $scaled.Save((Join-Path $outputDir ("$testMode-scale-$testScale.png")), [Drawing.Imaging.ImageFormat]::Png) }
                } finally { $scaled.Dispose() }
            }
        }
    }
    $script:Zoom = 1; $script:fullMargin = 0; Sync-RendererLayout
    $r.AccountScrollY = [Win.DashboardLayout]::MaxAccountScroll($script:slots.Count)
} finally {
    foreach ($font in @($fDigits, $fDashboardHeader, $fDashboardClock, $fLabel, $fRow, $fOvertime, $fPillTime, $fPillSub, $fDrawerBtn, $fDrawerText, $fDrawerTextBold, $fTimeline, $fTimelineBold, $fTimelineSmall)) { $font.Dispose() }
}

foreach ($name in @('Get-BaseSize', 'Get-PillListH', 'Get-PillLayout', 'Get-PillRowTop', 'Get-PillZone', 'Test-HitCollapse', 'Get-DayBtnRect', 'Test-HitDayBtn', 'Test-HitDashboardPause', 'Get-RowTop', 'Get-HitRow')) {
    $fn = @($ast.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    Invoke-Expression $fn[0].Extent.Text
}
$script:mode = 'full'
$script:fullMargin = 0; $script:drawerH = 999
$size = Get-BaseSize
if ($size[0] -ne 960 -or $size[1] -ne 640) { throw 'Content must not resize the dashboard.' }
$script:active = @{ Name = 'Account A' }
if (-not (Test-HitDashboardPause ([Drawing.Point]::new(590, 85)))) { throw 'Pause action must be reachable.' }
if (-not (Test-HitCollapse ([Drawing.Point]::new(880, 25)))) { throw 'Back to pill must be reachable.' }
$script:view.DayBtnText = 'Finish day'
if (-not (Test-HitDayBtn ([Drawing.Point]::new(850, 85)))) { throw 'Day control must be reachable.' }
$script:RowTop = 164; $script:RowH = 48; $script:LunchGap = 10; $script:BaseW = 320
$script:slots = @((0..11 | ForEach-Object { 'Client account ' + $_ })) + @('Lunch')
if ((Get-HitRow ([Drawing.Point]::new(28, 536))) -ne 12) { throw 'Scrolled Lunch hit target must match its row.' }
if ($null -ne (Get-HitRow ([Drawing.Point]::new(28, 620)))) { throw 'Sidebar summary must not select a hidden account.' }
$script:mode = 'pill'; $script:pillOpen = $true; $script:pillUp = $false; $script:pillMargin = 0
$script:PillW = 350; $script:PillH = 52; $script:PillRowH = 44; $script:PillLunchGap = 6
$script:PillTimeW = 116; $script:PillBtnX = 328; $script:PillBtnR = 14
$script:slots = @('Account A', 'Lunch')
$layout = Get-PillLayout
$footerY = $layout.ListY + $layout.ListH - 30
if ((Get-PillZone ([Drawing.Point]::new(70, $footerY))) -ne 'dashboard') { throw 'Pill Dashboard action must be reachable.' }
if ((Get-PillZone ([Drawing.Point]::new(210, $footerY))) -ne 'day') { throw 'Pill day control must be reachable.' }
$script:pillUp = $true
$layout = Get-PillLayout
$footerY = $layout.ListY + $layout.ListH - 30
if ((Get-PillZone ([Drawing.Point]::new(210, $footerY))) -ne 'day') { throw 'Upward expanded pill day control must be reachable.' }
Write-Output 'Dashboard rendering, fixed size and navigation checks passed.'

# Render the expanded pill through its production PowerShell drawing path.
foreach ($name in @('Draw-Pill', 'Fill-Area', 'New-RoundedPath', 'Draw-PlayIcon', 'Draw-PauseIcon', 'Draw-LunchIcon')) {
    $fn = @($ast.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    if ($fn.Count -ne 1) { throw "Missing pill drawing function $name" }
    Invoke-Expression $fn[0].Extent.Text
}
function Get-StateTheme($view) { return @{ Panel = [Drawing.Color]::DarkGreen; Fill = [Drawing.Color]::LightGreen; Circle = [Drawing.Color]::ForestGreen; CircleHot = [Drawing.Color]::Green } }
function Mix-Colour($left, $right, $amount) { return $left }
function Format-Dur($seconds) { return '0h 15m' }
function Write-Log($message) { throw $message }
$script:C = [Drawing.Color]; $script:RF = [Drawing.RectangleF]; $script:PF = [Drawing.PointF]
$script:cWhite = [Drawing.Color]::White
$script:BudgetWarnColour = [Drawing.Color]::Gold; $script:BudgetOverColour = [Drawing.Color]::Red
$script:PillShadow = $false; $script:PillPulse = $false; $script:PillLiveDot = $true
$script:pillUp = $false; $script:pillHover = $null; $script:shapePath = $null
$script:view = @{ PillTime = '0:29'; ActiveName = 'Client account'; ActiveToday = '0h 15m today'; Progress = 0.2; Rows = @($rows[0], $rows[12]); DayBtnText = 'Finish day' }
$script:fRow = New-Object Drawing.Font('Segoe UI', 14)
$script:fPillSub = New-Object Drawing.Font('Segoe UI', 12)
$script:fPillTime = New-Object Drawing.Font('Segoe UI', 19)
$script:fDrawerBtn = $script:fRow
foreach ($name in @('bWhite', 'bFaint', 'bDim', 'bTrack', 'bRowHover', 'bRowOn')) {
    Set-Variable -Scope Script -Name $name -Value (New-Object Drawing.SolidBrush([Drawing.Color]::White))
}
$script:pSep = New-Object Drawing.Pen([Drawing.Color]::LightGreen)
$script:sfCentre = New-Object Drawing.StringFormat
$script:sfCentre.Alignment = 'Center'; $script:sfCentre.LineAlignment = 'Center'
$script:sfName = New-Object Drawing.StringFormat
$script:sfName.Trimming = 'EllipsisCharacter'; $script:sfName.FormatFlags = 'NoWrap'
$script:sfRight = New-Object Drawing.StringFormat; $script:sfRight.Alignment = 'Far'
$layout = Get-PillLayout
$bitmap = New-Object Drawing.Bitmap(350, ([int]$layout.Height))
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try { Draw-Pill $graphics $false } finally { $graphics.Dispose(); $bitmap.Dispose() }
Write-Output 'Expanded pill ring and footer drawing checks passed.'
