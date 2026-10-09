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
$r.Dashboard = $true; $r.BaseW = 320; $r.DrawerW = 640
$r.BaseH = 640; $r.DrawerH = 640; $r.TimerOffX = 0; $r.DrawerOpen = $true
$r.RowTop = 170; $r.RowH = 34; $r.Margin = 0
$r.DashboardDate = 'Friday, 9 October 2026'; $r.ActiveAccount = 'Account A'
$r.LiveDot = $true; $r.DotPhase = 0.5; $r.DayBtnText = 'Finish day'
$font = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
foreach ($field in $r.GetType().GetFields()) { if ($field.FieldType -eq [System.Drawing.Font]) { $field.SetValue($r, $font) } }
$digitFont = New-Object System.Drawing.Font('Segoe UI', 36, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$r.FontDigits = $digitFont
$rows = @()
for ($i = 0; $i -lt 13; $i++) {
    $row = New-Object Win.RowModel
    $row.Name = if ($i -eq 12) { 'Lunch' } else { 'Client account ' + $i }
    $row.IsLunch = ($i -eq 12); $row.TodayText = '0h 15m'; $row.WeekText = '3h 49m'
    if ($i -lt 12) { $row.BudgetFrac = 0.64 }
    $row.Active = ($i -eq 0); $rows += $row
}
$r.Rows = $rows
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
        if ($mode -eq 'timeline' -and ($r.TlY -lt 124 -or $r.TlY + $r.TlH -gt 640)) { throw 'Timeline viewport does not match the dashboard body.' }
    }
} finally { $font.Dispose(); $digitFont.Dispose() }

foreach ($name in @('Get-BaseSize', 'Get-PillListH', 'Get-PillLayout', 'Get-PillRowTop', 'Get-PillZone', 'Test-HitCollapse', 'Test-HitDayBtn', 'Test-HitDashboardPause')) {
    $fn = @($ast.FindAll({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    Invoke-Expression $fn[0].Extent.Text
}
$script:fullMargin = 0; $script:drawerH = 999
$size = Get-BaseSize
if ($size[0] -ne 960 -or $size[1] -ne 640) { throw 'Content must not resize the dashboard.' }
$script:active = @{ Name = 'Account A' }
if (-not (Test-HitDashboardPause ([Drawing.Point]::new(690, 75)))) { throw 'Pause action must be reachable.' }
if (-not (Test-HitCollapse ([Drawing.Point]::new(880, 25)))) { throw 'Back to pill must be reachable.' }
$script:view.DayBtnText = 'Finish day'
if (-not (Test-HitDayBtn ([Drawing.Point]::new(850, 85)))) { throw 'Day control must be reachable.' }
$script:mode = 'pill'; $script:pillOpen = $true; $script:pillUp = $false; $script:pillMargin = 0
$script:PillW = 320; $script:PillH = 42; $script:PillRowH = 34; $script:PillLunchGap = 6
$script:PillTimeW = 88; $script:PillBtnX = 298; $script:PillBtnR = 14
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
$script:C = [Drawing.Color]; $script:RF = [Drawing.RectangleF]
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
$bitmap = New-Object Drawing.Bitmap(320, ([int]$layout.Height))
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try { Draw-Pill $graphics $false } finally { $graphics.Dispose(); $bitmap.Dispose() }
Write-Output 'Expanded pill ring and footer drawing checks passed.'
