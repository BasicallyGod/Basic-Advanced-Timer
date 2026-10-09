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
$r.BaseW = 320; $r.BaseH = 370; $r.DrawerW = 720; $r.DrawerH = 480
$r.DrawerOpen = $true; $r.Shadow = $false
foreach ($left in @($true, $false)) {
    $r.TimerOffX = if ($left) { 720 } else { 0 }
    $outline = $r.GetOutline()
    try {
        $timerX = if ($left) { 850 } else { 150 }
        $drawerX = if ($left) { 150 } else { 850 }
        if ($outline.IsVisible([single]$timerX, [single]440)) { throw 'Blank corner under timer remains visible.' }
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
            if ($bitmap.GetPixel(850, 440).A -ne 0) { throw 'Renderer paints the blank corner.' }
        } finally { $bitmap.Dispose() }
        foreach ($button in $r.DrawerButtons) {
            if ($button.Rect.Height -lt 28) { throw 'Drawer button targets are too small.' }
            if ($button.Rect.Left -lt 10 -or $button.Rect.Right -gt 730) { throw 'Drawer button is outside its panel.' }
        }
    }
} finally { $font.Dispose() }
Write-Output 'Renderer layout checks passed.'
