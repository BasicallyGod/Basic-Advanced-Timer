# Billable hours timer
# Shows time worked since the start time, capped at the end time, minus lunch.
# Resets itself each day automatically.
# Drag anywhere to move. Right-hand buttons: lunch (pause/resume) and reset today's lunch time.
# Right-click for options. The tray icon brings the timer back if it gets hidden.

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

# Sharp text on every monitor: handle each screen's scaling separately
try {
    Add-Type -Namespace Win -Name Dpi -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr value);
[DllImport("user32.dll")] public static extern uint GetDpiForWindow(System.IntPtr hwnd);
'@
} catch {}
$script:perMonitor = $false
try { $script:perMonitor = [Win.Dpi]::SetProcessDpiAwarenessContext([IntPtr](-4)) } catch {}
if (-not $script:perMonitor) { try { [void][Win.Dpi]::SetProcessDPIAware() } catch {} }

# Rounded corners on Windows 11 (ignored on Windows 10)
try {
    Add-Type -Namespace Win -Name Dwm -MemberDefinition '[DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(System.IntPtr hwnd, int attr, ref int val, int size);'
} catch {}

# Menus and dialogs: size them for the screen they appear on
try {
    Add-Type -Namespace Win -Name Mon -MemberDefinition @'
public struct POINT { public int X; public int Y; }
[DllImport("user32.dll")] public static extern System.IntPtr MonitorFromPoint(POINT pt, uint flags);
[DllImport("shcore.dll")] public static extern int GetDpiForMonitor(System.IntPtr hmon, int dpiType, out uint dpiX, out uint dpiY);
'@
} catch {}

function Get-CursorDpi {
    try {
        $p = [System.Windows.Forms.Cursor]::Position
        $pt = New-Object 'Win.Mon+POINT'
        $pt.X = $p.X; $pt.Y = $p.Y
        $mon = [Win.Mon]::MonitorFromPoint($pt, 2)
        $dx = [uint32]0; $dy = [uint32]0
        if ([Win.Mon]::GetDpiForMonitor($mon, 0, [ref]$dx, [ref]$dy) -eq 0 -and $dx -gt 0) { return [double]$dx }
    } catch {}
    try { return [double][Win.Dpi]::GetDpiForWindow($form.Handle) } catch {}
    return 96.0
}

function Set-MenuScale($strip) {
    if (-not $script:perMonitor) { return }
    $k = (Get-CursorDpi) / 96
    $font = New-Object System.Drawing.Font('Segoe UI', [float](12 * $k), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $icons = New-Object System.Drawing.Size([int](16 * $k), [int](16 * $k))
    $strip.Font = $font
    $strip.ImageScalingSize = $icons
    foreach ($item in $strip.Items) {
        if ($item -is [System.Windows.Forms.ToolStripMenuItem] -and $item.DropDownItems.Count -gt 0) {
            Set-MenuScale $item.DropDown
        }
    }
}

# ---- Settings ----
$StartTime = '08:30'
$EndTime   = '17:30'
$Scale     = 1.0      # make the timer bigger or smaller, e.g. 0.8 or 1.3
# ------------------

$StateFile = Join-Path $PSScriptRoot 'timer-state.txt'
$LogFile   = Join-Path $PSScriptRoot 'timer-error.log'

$script:stateDate    = [datetime]::Today
$script:breakSeconds = 0
$script:lunchStart   = $null
$script:view         = $null
$script:hover        = $null
$script:pressed      = $null
$script:dragStart    = $null
$script:tick         = 0

function Write-Log($msg) {
    try { Add-Content -Path $LogFile -Value ('{0}  {1}' -f (Get-Date -Format 's'), $msg) } catch {}
}

function Get-DayBounds {
    $s = [datetime]::Today.Add([timespan]$StartTime)
    $e = [datetime]::Today.Add([timespan]$EndTime)
    return $s, $e
}

function Limit-Time($t, $min, $max) {
    if ($t -lt $min) { return $min }
    if ($t -gt $max) { return $max }
    return $t
}

function Load-State {
    if (-not (Test-Path $StateFile)) { return }
    $lines = @(Get-Content $StateFile)
    if ($lines.Count -ge 2 -and $lines[0] -eq (Get-Date -Format 'yyyy-MM-dd')) {
        $script:breakSeconds = [int]$lines[1]
        if ($lines.Count -ge 3 -and $lines[2]) {
            $script:lunchStart = [datetime]::Parse($lines[2],
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind)
        }
    }
}

function Save-State {
    $ls = if ($script:lunchStart) { $script:lunchStart.ToString('o') } else { '' }
    try {
        Set-Content -Path $StateFile -Value @((Get-Date -Format 'yyyy-MM-dd'), [int]$script:breakSeconds, $ls)
    } catch { Write-Log "Could not save state: $($_.Exception.Message)" }
}

function Reset-Day {
    $script:stateDate    = [datetime]::Today
    $script:breakSeconds = 0
    $script:lunchStart   = $null
    Save-State
}

# ---- Look and layout (drawn at 320 x 84, then scaled) ----
$g0 = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
$S  = $Scale * $g0.DpiX / 96
$g0.Dispose()

$BaseW = 320
$BaseH = 84

$C = [System.Drawing.Color]
# Green while the clock is running, orange while paused for lunch
$Themes = @{
    running = @{
        Panel     = $C::FromArgb(0x35, 0x7a, 0x38)   # #357a38
        Circle    = $C::FromArgb(0x4c, 0xaf, 0x50)   # #4caf50
        CircleHot = $C::FromArgb(0x6f, 0xbf, 0x73)   # #6fbf73
        Fill      = $C::FromArgb(0x6f, 0xbf, 0x73)   # #6fbf73
    }
    paused = @{
        Panel     = $C::FromArgb(0xb2, 0x6a, 0x00)   # #b26a00
        Circle    = $C::FromArgb(0xff, 0x98, 0x00)   # #ff9800
        CircleHot = $C::FromArgb(0xff, 0xac, 0x33)   # #ffac33
        Fill      = $C::FromArgb(0xff, 0xac, 0x33)   # #ffac33
    }
}
$cDigits     = $C::FromArgb(255, 255, 255)
$cDigitsDim  = $C::FromArgb(140, 255, 255, 255)
$cFaint      = $C::FromArgb(215, 255, 255, 255)
$cDivider    = $C::FromArgb(50, 255, 255, 255)
$cIcon       = $C::FromArgb(255, 255, 255)
$cTrack      = $C::FromArgb(40, 255, 255, 255)

$px = [System.Drawing.GraphicsUnit]::Pixel
$fDigits = New-Object System.Drawing.Font('Segoe UI Semibold', 36, [System.Drawing.FontStyle]::Regular, $px)
$fLabel  = New-Object System.Drawing.Font('Segoe UI', 9.5, [System.Drawing.FontStyle]::Bold, $px)

$sfCentre = New-Object System.Drawing.StringFormat
$sfCentre.Alignment = 'Center'
$sfCentre.LineAlignment = 'Center'
$sfLeft = New-Object System.Drawing.StringFormat
$sfLeft.Alignment = 'Near'
$sfLeft.LineAlignment = 'Center'
$sfRight = New-Object System.Drawing.StringFormat
$sfRight.Alignment = 'Far'
$sfRight.LineAlignment = 'Center'

$RF = [System.Drawing.RectangleF]
$PF = [System.Drawing.PointF]
$colX = @(0, 88, 176)   # left edge of each digit column
$colW = 88

# Circular buttons: centre x, centre y, radius
$btn = @{
    lunch = @{ X = 292; Y = 23; R = 14 }
    reset = @{ X = 292; Y = 59; R = 14 }
}

# ---- Window ----
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.TopMost = $true
$form.ShowInTaskbar = $false
$form.StartPosition = 'Manual'
$form.BackColor = $Themes.running.Panel
$form.ClientSize = New-Object System.Drawing.Size([int]($BaseW * $S), [int]($BaseH * $S))
$form.GetType().GetProperty('DoubleBuffered',
    [Reflection.BindingFlags]'NonPublic,Instance').SetValue($form, $true, $null)

try {
    $corner = 2
    [void][Win.Dwm]::DwmSetWindowAttribute($form.Handle, 33, [ref]$corner, 4)
} catch {}

# Resize and redraw for the scaling of whichever monitor the timer is on
function Update-Dpi {
    if (-not $script:perMonitor) { return }
    try {
        $dpi = [Win.Dpi]::GetDpiForWindow($form.Handle)
        if ($dpi -gt 0) {
            $z = $Scale * $dpi / 96
            if ([math]::Abs($z - $script:S) -gt 0.001) {
                $script:S = $z
                $form.ClientSize = New-Object System.Drawing.Size([int]($BaseW * $z), [int]($BaseH * $z))
                $form.Invalidate()
            }
        }
    } catch {}
}
Update-Dpi
$form.Add_LocationChanged({ Update-Dpi })

function Move-ToDefaultPosition {
    $area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $form.Location = New-Object System.Drawing.Point(
        ($area.Right - $form.Width - 20), ($area.Bottom - $form.Height - 20))
}
Move-ToDefaultPosition

$tip = New-Object System.Windows.Forms.ToolTip

# ---- Working out the numbers ----
function Update-State {
    if ($script:stateDate -ne [datetime]::Today) { Reset-Day }

    $now = Get-Date
    $start, $end = Get-DayBounds

    $worked = (Limit-Time $now $start $end) - $start
    $paused = [timespan]::FromSeconds($script:breakSeconds)
    if ($script:lunchStart) {
        $paused += (Limit-Time $now $start $end) - (Limit-Time $script:lunchStart $start $end)
    }
    $billable = $worked - $paused
    if ($billable -lt [timespan]::Zero) { $billable = [timespan]::Zero }

    $status = ''
    if ($script:lunchStart)  { $status = 'ON LUNCH' }
    elseif ($now -lt $start) { $status = "STARTS AT $StartTime" }
    elseif ($now -ge $end)   { $status = 'DAY COMPLETE' }
    else                     { $status = 'WORKING' }

    $script:view = @{
        H        = '{0:00}' -f [math]::Floor($billable.TotalHours)
        M        = '{0:00}' -f $billable.Minutes
        Sec      = '{0:00}' -f $billable.Seconds
        Progress = $worked.TotalSeconds / ($end - $start).TotalSeconds
        Status   = $status
        Lunch    = 'LUNCH {0}h {1:00}m' -f [math]::Floor($paused.TotalHours), $paused.Minutes
        OnLunch  = [bool]$script:lunchStart
        Before   = ($now -lt $start)
    }
    $tray.Text = 'Billable timer: {0}h {1}m' -f [int]$script:view.H, $script:view.M
}

# ---- Icons ----
function Draw-LunchIcon($g, $cx, $cy, $colour) {
    # Fork and knife
    $pen = New-Object System.Drawing.Pen($colour, 1.3)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLine($pen, ($cx - 4), ($cy - 1), ($cx - 4), ($cy + 7))
    foreach ($tx in @(-6.5, -4, -1.5)) { $g.DrawLine($pen, ($cx + $tx), ($cy - 7), ($cx + $tx), ($cy - 3)) }
    $g.DrawArc($pen, ($cx - 6.5), ($cy - 5.5), 5, 4.5, 0, 180)
    $g.DrawLine($pen, ($cx + 4.5), ($cy + 1), ($cx + 4.5), ($cy + 7))
    $br = New-Object System.Drawing.SolidBrush $colour
    $g.FillPolygon($br, [System.Drawing.PointF[]]@(
        (New-Object $PF ($cx + 3), ($cy + 1.5)), (New-Object $PF ($cx + 3), ($cy - 7)),
        (New-Object $PF ($cx + 6.3), ($cy - 3)), (New-Object $PF ($cx + 6.3), ($cy + 1.5))))
    $br.Dispose(); $pen.Dispose()
}

function Draw-ResumeIcon($g, $cx, $cy, $colour) {
    # Play triangle
    $br = New-Object System.Drawing.SolidBrush $colour
    $g.FillPolygon($br, [System.Drawing.PointF[]]@(
        (New-Object $PF ($cx - 3), ($cy - 6)), (New-Object $PF ($cx - 3), ($cy + 6)),
        (New-Object $PF ($cx + 6), $cy)))
    $br.Dispose()
}

function Draw-ResetIcon($g, $cx, $cy, $colour) {
    # Circular arrow
    $r = 6
    $pen = New-Object System.Drawing.Pen($colour, 1.5)
    $pen.StartCap = 'Round'
    $g.DrawArc($pen, ($cx - $r), ($cy - $r), (2 * $r), (2 * $r), 300, 290)
    $a  = 230 * [math]::PI / 180              # where the arc ends
    $ex = $cx + $r * [math]::Cos($a); $ey = $cy + $r * [math]::Sin($a)
    $dx = -[math]::Sin($a); $dy = [math]::Cos($a)   # direction of travel
    $nx = -$dy; $ny = $dx
    $br = New-Object System.Drawing.SolidBrush $colour
    $g.FillPolygon($br, [System.Drawing.PointF[]]@(
        (New-Object $PF ($ex + 3.2 * $dx), ($ey + 3.2 * $dy)),
        (New-Object $PF ($ex - 0.8 * $dx + 2.8 * $nx), ($ey - 0.8 * $dy + 2.8 * $ny)),
        (New-Object $PF ($ex - 0.8 * $dx - 2.8 * $nx), ($ey - 0.8 * $dy - 2.8 * $ny))))
    $br.Dispose(); $pen.Dispose()
}

# ---- Drawing ----
$form.Add_Paint({
    try {
        $g = $_.Graphics
        $g.SmoothingMode = 'AntiAlias'
        $g.TextRenderingHint = 'ClearTypeGridFit'
        $g.ScaleTransform($S, $S)
        $v = $script:view
        if (-not $v) { return }

        # Background colour for the current state
        $t = if ($v.OnLunch) { $Themes.paused } else { $Themes.running }
        $bg = New-Object System.Drawing.SolidBrush $t.Panel
        $g.FillRectangle($bg, 0, 0, $BaseW, $BaseH)
        $bg.Dispose()

        $lb = New-Object System.Drawing.SolidBrush $cFaint

        # Small hourglass
        $hp = New-Object System.Drawing.Pen($cFaint, 0.8)
        $g.DrawLines($hp, [System.Drawing.PointF[]]@(
            (New-Object $PF 8, 7), (New-Object $PF 14, 7), (New-Object $PF 8, 16),
            (New-Object $PF 14, 16), (New-Object $PF 8, 7)))
        $hp.Dispose()

        # Column labels and digits
        $labels = @('HOURS', 'MINUTES', 'SECONDS')
        $values = @($v.H, $v.M, $v.Sec)
        $digitColour = if ($v.Before) { $cDigitsDim } else { $cDigits }
        $db = New-Object System.Drawing.SolidBrush $digitColour
        for ($i = 0; $i -lt 3; $i++) {
            $g.DrawString($labels[$i], $fLabel, $lb, (New-Object $RF $colX[$i], 4, $colW, 14), $sfCentre)
            $g.DrawString($values[$i], $fDigits, $db, (New-Object $RF $colX[$i], 14, $colW, 54), $sfCentre)
        }
        $db.Dispose()

        # Separator dots
        foreach ($x in @(88, 176)) {
            $g.FillEllipse($lb, ($x - 1.3), 35, 2.6, 2.6)
            $g.FillEllipse($lb, ($x - 1.3), 46, 2.6, 2.6)
        }

        # Bottom line: status on the left, lunch taken today on the right
        $stb = New-Object System.Drawing.SolidBrush ($(if ($v.OnLunch) { $cDigits } else { $cFaint }))
        if ($v.Status) {
            $g.DrawString($v.Status, $fLabel, $stb, (New-Object $RF 10, 65, 150, 14), $sfLeft)
        }
        $g.DrawString($v.Lunch, $fLabel, $stb, (New-Object $RF 104, 65, 150, 14), $sfRight)
        $stb.Dispose()
        $lb.Dispose()

        # Divider between timer and buttons
        $dp = New-Object System.Drawing.Pen($cDivider, 1)
        $g.DrawLine($dp, 264, 12, 264, 72)
        $dp.Dispose()

        # Circular buttons
        foreach ($name in @('lunch', 'reset')) {
            $b = $btn[$name]
            $fillColour = if ($script:hover -eq $name) { $t.CircleHot } else { $t.Circle }
            $cb = New-Object System.Drawing.SolidBrush $fillColour
            $g.FillEllipse($cb, ($b.X - $b.R), ($b.Y - $b.R), (2 * $b.R), (2 * $b.R))
            $cb.Dispose()
        }
        if ($v.OnLunch) { Draw-ResumeIcon $g $btn.lunch.X $btn.lunch.Y $cIcon }
        else            { Draw-LunchIcon  $g $btn.lunch.X $btn.lunch.Y $cIcon }
        Draw-ResetIcon $g $btn.reset.X $btn.reset.Y $cIcon

        # Progress through the working day
        $tb = New-Object System.Drawing.SolidBrush $cTrack
        $fb = New-Object System.Drawing.SolidBrush $t.Fill
        $g.FillRectangle($tb, 0, ($BaseH - 3), $BaseW, 3)
        $g.FillRectangle($fb, 0, ($BaseH - 3), ($BaseW * [math]::Min(1.0, $v.Progress)), 3)
        $tb.Dispose(); $fb.Dispose()
    } catch {
        Write-Log "Drawing error: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"
    }
})

# ---- Buttons ----
function Get-HitButton($pt) {
    $x = $pt.X / $S; $y = $pt.Y / $S
    foreach ($name in @('lunch', 'reset')) {
        $b = $btn[$name]
        if ((($x - $b.X) * ($x - $b.X) + ($y - $b.Y) * ($y - $b.Y)) -le ($b.R * $b.R)) { return $name }
    }
    return $null
}

function Switch-Lunch {
    if ($script:lunchStart) {
        $start, $end = Get-DayBounds
        $now = Get-Date
        $taken = (Limit-Time $now $start $end) - (Limit-Time $script:lunchStart $start $end)
        $script:breakSeconds += [int]$taken.TotalSeconds
        $script:lunchStart = $null
    } else {
        $script:lunchStart = Get-Date
    }
    Save-State
}

function Confirm-Reset {
    $answer = [System.Windows.Forms.MessageBox]::Show($form,
        "Clear today's lunch time?", 'Billable timer', 'YesNo', 'Question')
    if ($answer -eq 'Yes') { Reset-Day }
}

function Get-TipText($name) {
    if ($name -eq 'lunch') {
        if ($script:lunchStart) { return 'Back from lunch' } else { return 'Start lunch' }
    }
    return "Clear today's lunch time"
}

$form.Add_MouseDown({
    if ($_.Button -ne 'Left') { return }
    $hit = Get-HitButton $_.Location
    if ($hit) {
        $script:pressed = $hit
    } else {
        $script:dragStart = [System.Windows.Forms.Cursor]::Position
        $script:formStart = $form.Location
    }
})

$form.Add_MouseMove({
    if ($script:dragStart) {
        $p = [System.Windows.Forms.Cursor]::Position
        $form.Location = New-Object System.Drawing.Point(
            ($script:formStart.X + $p.X - $script:dragStart.X),
            ($script:formStart.Y + $p.Y - $script:dragStart.Y))
        return
    }
    $hit = Get-HitButton $_.Location
    if ($hit -ne $script:hover) {
        $script:hover = $hit
        if ($hit) {
            $form.Cursor = [System.Windows.Forms.Cursors]::Hand
            $b = $btn[$hit]
            $tip.Show((Get-TipText $hit), $form,
                [int](($b.X - 70) * $S), [int](($b.Y + $b.R + 2) * $S), 2000)
        } else {
            $form.Cursor = [System.Windows.Forms.Cursors]::Default
            $tip.Hide($form)
        }
        $form.Invalidate()
    }
})

$form.Add_MouseUp({
    try {
        $script:dragStart = $null
        if ($script:pressed -and (Get-HitButton $_.Location) -eq $script:pressed) {
            $tip.Hide($form)
            if ($script:pressed -eq 'lunch') { Switch-Lunch } else { Confirm-Reset }
            Update-State
            $form.Invalidate()
        }
        $script:pressed = $null
    } catch {
        Write-Log "Button error: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"
    }
})

$form.Add_MouseLeave({
    if ($script:hover) {
        $script:hover = $null
        $form.Cursor = [System.Windows.Forms.Cursors]::Default
        $tip.Hide($form)
        $form.Invalidate()
    }
})

# ---- Keeping it visible ----
function Keep-Visible {
    Update-Dpi
    $rect = New-Object System.Drawing.Rectangle($form.Location, $form.Size)
    $onScreen = $false
    foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
        if ($s.WorkingArea.IntersectsWith($rect)) { $onScreen = $true }
    }
    if (-not $onScreen) { Move-ToDefaultPosition }
    if (-not $form.Visible) { $form.Show() }
    $form.TopMost = $false
    $form.TopMost = $true
}

# ---- Tray icon and menus ----
$tray = New-Object System.Windows.Forms.NotifyIcon
$tray.Icon = [System.Drawing.SystemIcons]::Application
$tray.Text = 'Billable timer'
$trayMenu = New-Object System.Windows.Forms.ContextMenuStrip
[void]$trayMenu.Items.Add('Show timer', $null, { Keep-Visible; $form.Activate() })
[void]$trayMenu.Items.Add('Reset position', $null, { Move-ToDefaultPosition; Keep-Visible })
[void]$trayMenu.Items.Add('Close timer', $null, { $form.Close() })
$tray.ContextMenuStrip = $trayMenu
$trayMenu.Add_Opening({ Set-MenuScale $trayMenu })
$tray.Add_MouseClick({ if ($_.Button -eq 'Left') { Keep-Visible; $form.Activate() } })
$tray.Visible = $true

$menu = New-Object System.Windows.Forms.ContextMenuStrip
[void]$menu.Items.Add('Reset position', $null, { Move-ToDefaultPosition })
[void]$menu.Items.Add('Close timer', $null, { $form.Close() })
$form.ContextMenuStrip = $menu
$menu.Add_Opening({ Set-MenuScale $menu })

$form.Add_FormClosed({
    $tray.Visible = $false
    $tray.Dispose()
    Write-Log 'Timer closed normally.'
})

# ---- Refresh every second ----
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1000
$timer.Add_Tick({
    try {
        Update-State
        $form.Invalidate()
        $script:tick++
        if ($script:tick % 2 -eq 0) { Keep-Visible }
    } catch {
        Write-Log "Refresh error: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"
    }
})

try { Load-State } catch { Write-Log "Could not load state: $($_.Exception.Message)" }
Write-Log 'Timer started.'
Update-State
$timer.Start()
[System.Windows.Forms.Application]::Run($form)
