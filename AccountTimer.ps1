# Account timer
# Top: billable time since the start time, less lunch, as in the original timer.
# After the end time it holds at the full day and an overtime counter appears below it
# (the whole timer turns red). Everything resets at midnight.
# Below: five account slots and a lunch slot. Click a row to start or pause it.
# Only one slot runs at a time. Every stretch of time is logged to Timesheets\sessions.csv,
# and a weekly summary (hours per account per day) is kept in
# Timesheets\Week commencing <date>.csv for filling in timesheets.
#
# Drag the dotted handle on the left of a row to move it up or down.
# Hover over an account to show the round-down and round-up buttons, which move today's
# time to the previous or next 15-minute mark (logged as 'rounded down' or 'rounded up').
# Right-click a row to rename or hide it, set today's time for it by hand, or clear it.
# Right-click anywhere to add a new account, show a hidden one, or change the theme.
# Themes are kept in account-timer-themes.txt, which you can edit (Theme, then Edit themes file).
# The account list can be made see-through (right-click, Account list transparency).
# Drag the timer by its top section. Right-click anywhere for more options.

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

# Per-pixel transparency: the timer is drawn as one image with see-through areas
try {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace Win {
    public static class Layered {
        // ---- Blur behind the window (experimental, undocumented Windows feature) ----
        [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)] struct CompositionData { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);
        public static bool SetBlur(IntPtr hwnd, bool on) {
            AccentPolicy accent = new AccentPolicy();
            accent.AccentState = on ? 3 : 0;
            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try {
                Marshal.StructureToPtr(accent, ptr, false);
                CompositionData data = new CompositionData();
                data.Attribute = 19;
                data.Data = ptr;
                data.SizeOfData = size;
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            } finally { Marshal.FreeHGlobal(ptr); }
        }

        // ---- Sharpened text over a see-through background ----
        // colour: the text colour, filled behind where each piece of text goes
        // mask:   the text drawn with standard smoothing, giving its exact shape
        // The text colour is laid onto dest using the mask's shape, with the edges
        // firmed up by a sharpening curve (gamma below 1 makes edges crisper).
        public static void Combine(Bitmap dest, Bitmap colour, Bitmap mask, double gamma) {
            Rectangle rect = new Rectangle(0, 0, dest.Width, dest.Height);
            BitmapData dd = dest.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            BitmapData cd = colour.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData md = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try {
                int n = rect.Width * rect.Height;
                int[] d = new int[n]; int[] c = new int[n]; int[] m = new int[n];
                Marshal.Copy(dd.Scan0, d, 0, n);
                Marshal.Copy(cd.Scan0, c, 0, n);
                Marshal.Copy(md.Scan0, m, 0, n);
                for (int i = 0; i < n; i++) {
                    int ma = (m[i] >> 24) & 255;
                    if (ma == 0) continue;
                    double fa = Math.Pow(ma / 255.0, gamma);
                    int cc = c[i], dc = d[i];
                    double da = ((dc >> 24) & 255) / 255.0;
                    double fd = da * (1.0 - fa);
                    double oa = fa + fd;
                    if (oa <= 0) continue;
                    int r = (int)((((cc >> 16) & 255) * fa + ((dc >> 16) & 255) * fd) / oa + 0.5);
                    int g = (int)((((cc >> 8) & 255) * fa + ((dc >> 8) & 255) * fd) / oa + 0.5);
                    int b = (int)(((cc & 255) * fa + (dc & 255) * fd) / oa + 0.5);
                    int a = (int)(oa * 255.0 + 0.5);
                    d[i] = (a << 24) | (r << 16) | (g << 8) | b;
                }
                Marshal.Copy(d, 0, dd.Scan0, n);
            } finally {
                dest.UnlockBits(dd); colour.UnlockBits(cd); mask.UnlockBits(md);
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx; public int cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op; public byte Flags; public byte Alpha; public byte Format; }
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLEND pblend, int dwFlags);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        public static void MakeLayered(IntPtr hwnd) { SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x80000); }
        public static void MakeNormal(IntPtr hwnd) { SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) & ~0x80000); }
        public static bool Push(IntPtr hwnd, Bitmap bmp, int x, int y) {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr hb = IntPtr.Zero, old = IntPtr.Zero;
            bool ok = false;
            try {
                hb = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(mem, hb);
                SIZE size; size.cx = bmp.Width; size.cy = bmp.Height;
                POINT src; src.x = 0; src.y = 0;
                POINT dst; dst.x = x; dst.y = y;
                BLEND b; b.Op = 0; b.Flags = 0; b.Alpha = 255; b.Format = 1;
                ok = UpdateLayeredWindow(hwnd, screen, ref dst, ref size, mem, ref src, 0, ref b, 2);
            } finally {
                ReleaseDC(IntPtr.Zero, screen);
                if (hb != IntPtr.Zero) { SelectObject(mem, old); DeleteObject(hb); }
                DeleteDC(mem);
            }
            return ok;
        }
    }
}
'@
} catch {}

# ---- Settings ----
$StartTime       = '08:30'
$EndTime         = '17:30'
$Scale           = 1.0      # make the timer bigger or smaller, e.g. 0.8 or 1.3
$DefaultAccounts = @('ACCOUNT 1', 'ACCOUNT 2', 'ACCOUNT 3', 'ACCOUNT 4', 'ACCOUNT 5')
$SleepGapMinutes = 5        # if the computer sleeps for longer than this, the running slot is paused
$MaxVisibleAccounts = 12    # most accounts shown at once (hide some to make room)
# ------------------

$Inv = [Globalization.CultureInfo]::InvariantCulture
$GB  = [Globalization.CultureInfo]::GetCultureInfo('en-GB')

$TimesheetDir = Join-Path $PSScriptRoot 'Timesheets'
$SessionsFile = Join-Path $TimesheetDir 'sessions.csv'
$AccountsFile = Join-Path $PSScriptRoot 'account-timer-accounts.txt'
$StateFile    = Join-Path $PSScriptRoot 'account-timer-state.txt'
$LogFile      = Join-Path $PSScriptRoot 'account-timer-error.log'
$ThemeFile    = Join-Path $PSScriptRoot 'account-timer-theme.txt'
$ThemesFile   = Join-Path $PSScriptRoot 'account-timer-themes.txt'
$ListAlphaFile = Join-Path $PSScriptRoot 'account-timer-transparency.txt'
$BlurFile     = Join-Path $PSScriptRoot 'account-timer-blur.txt'
try { New-Item -ItemType Directory -Path $TimesheetDir -Force | Out-Null } catch {}

$script:accounts       = @()   # every account, including hidden ones
$script:slots          = @()   # visible accounts plus Lunch, in display order
$script:active         = $null
$script:pending        = @()
$script:todayDone      = @{}
$script:weekDone       = @{}
$script:lunchIntervals = @()
$script:lunchAdjust    = 0     # lunch rounding for today, in seconds
$script:summaryDirty   = $false
$script:pendingWarned  = $false
$script:view           = $null
$script:hover          = $null
$script:pressed        = $null
$script:dragStart      = $null
$script:dialogOpen     = $false
$script:menuRow        = $null
$script:reorder        = $null   # account being dragged to a new position
$script:gripHover      = $null
$script:roundHover     = $null
$script:pressedRound   = $null
$script:tick           = 0
$script:lastTick       = Get-Date
$script:layered        = $false  # drawing as one image with see-through areas
$script:listLevel      = 'Off'
$script:listAlpha      = 255
$script:shapePath      = $null
$script:pushWarned     = $false
$script:ct             = $null   # extra layers used for text over a see-through background
$script:blurOn         = $false

# ======================================================================
# Helpers
# ======================================================================

function Write-Log($msg) {
    try { Add-Content -Path $LogFile -Value ('{0}  {1}' -f (Get-Date -Format 's'), $msg) } catch {}
}

function Get-DayBounds {
    $a = [datetime]::Today.Add([timespan]$StartTime)
    $b = [datetime]::Today.Add([timespan]$EndTime)
    return $a, $b
}

function Limit-Time($t, $min, $max) {
    if ($t -lt $min) { return $min }
    if ($t -gt $max) { return $max }
    return $t
}

function Get-WeekStart {
    $today = [datetime]::Today
    return $today.AddDays(-(([int]$today.DayOfWeek + 6) % 7))
}

function Get-WeekFile {
    return Join-Path $TimesheetDir ('Week commencing {0}.csv' -f (Get-WeekStart).ToString('yyyy-MM-dd', $Inv))
}

function Format-Dur($seconds) {
    $ts = [timespan]::FromSeconds([math]::Floor([math]::Max(0.0, $seconds)))
    return '{0}h {1:00}m' -f [math]::Floor($ts.TotalHours), $ts.Minutes
}

# ======================================================================
# Accounts
# ======================================================================

function Rebuild-Slots {
    $script:slots = @($script:accounts | Where-Object { -not $_.Hidden } | ForEach-Object { $_.Name }) + @('Lunch')
}

function Find-Account($name) {
    return $script:accounts | Where-Object { $_.Name -eq $name } | Select-Object -First 1
}

function Get-VisibleCount {
    return @($script:accounts | Where-Object { -not $_.Hidden }).Count
}

function Load-Accounts {
    # One account per line. A tab followed by "hidden" marks a hidden account.
    $list = @()
    if (Test-Path $AccountsFile) {
        foreach ($line in Get-Content $AccountsFile) {
            if (-not $line.Trim()) { continue }
            $parts = $line -split "`t"
            $name = $parts[0].Trim()
            if ($name -eq 'Lunch' -or ($list | Where-Object { $_.Name -eq $name })) { continue }
            $list += @{ Name = $name; Hidden = ($parts.Count -gt 1 -and $parts[1].Trim() -eq 'hidden') }
        }
    }
    if ($list.Count -eq 0) {
        $list = @($DefaultAccounts | ForEach-Object { @{ Name = $_; Hidden = $false } })
    }
    $script:accounts = $list
    Rebuild-Slots
}

function Save-Accounts {
    $lines = @($script:accounts | ForEach-Object {
        if ($_.Hidden) { "$($_.Name)`thidden" } else { $_.Name }
    })
    try { Set-Content -Path $AccountsFile -Value $lines }
    catch { Write-Log "Could not save account names: $($_.Exception.Message)" }
}

# ======================================================================
# Session log
# ======================================================================

function Convert-SessionRow($r) {
    try {
        $day  = [datetime]::ParseExact($r.Date, 'yyyy-MM-dd', $Inv)
        $from = $day.Add([timespan]::ParseExact($r.Start, 'hh\:mm\:ss', $Inv))
        $sec  = [double]::Parse($r.Minutes, $Inv) * 60
        return [pscustomobject]@{ Name = $r.Account; Start = $from; End = $from.AddSeconds($sec); Seconds = $sec; IsAdjust = ($r.End -like 'rounded*' -or $r.End -eq 'adjusted') }
    } catch { return $null }
}

function Read-Sessions {
    $rows = @()
    if (Test-Path $SessionsFile) { $rows = @(Import-Csv $SessionsFile) }
    $rows += $script:pending
    foreach ($r in $rows) {
        $c = Convert-SessionRow $r
        if ($c) { $c }
    }
}

function Flush-Pending {
    if ($script:pending.Count -eq 0) { return }
    try {
        $script:pending | Export-Csv -Path $SessionsFile -Append -NoTypeInformation -Encoding UTF8
        $script:pending = @()
        $script:pendingWarned = $false
    } catch {
        if (-not $script:pendingWarned) {
            Write-Log "Could not write sessions.csv (is it open in Excel?). Will keep retrying. $($_.Exception.Message)"
            $script:pendingWarned = $true
        }
    }
}

function Add-Session($name, [datetime]$from, [datetime]$to) {
    $new = @()
    while ($from -lt $to) {
        # Split anything that runs past midnight so each day is logged separately
        $segEnd   = $to
        $midnight = $from.Date.AddDays(1)
        if ($segEnd -gt $midnight) { $segEnd = $midnight }
        $mins = ($segEnd - $from).TotalMinutes
        if ($mins -ge 0.05) {
            $new += [pscustomobject]@{
                Date    = $from.ToString('yyyy-MM-dd', $Inv)
                Account = $name
                Start   = $from.ToString('HH:mm:ss', $Inv)
                End     = $segEnd.ToString('HH:mm:ss', $Inv)
                Minutes = [math]::Round($mins, 2).ToString($Inv)
            }
        }
        $from = $segEnd
    }
    if ($new.Count -gt 0) {
        $script:pending += $new
        Flush-Pending
    }
}

function Add-Rounding($name, $seconds, $label) {
    # Logged with "rounded up" or "rounded down" in the End column so it's easy to spot.
    # Round-downs are logged as negative minutes.
    $now = Get-Date
    $mins = [math]::Ceiling($seconds / 60 * 100) / 100
    $script:pending += [pscustomobject]@{
        Date    = $now.ToString('yyyy-MM-dd', $Inv)
        Account = $name
        Start   = $now.ToString('HH:mm:ss', $Inv)
        End     = $label
        Minutes = $mins.ToString($Inv)
    }
    Flush-Pending
}

function Refresh-Totals {
    $today = [datetime]::Today
    $ws = Get-WeekStart
    $we = $ws.AddDays(7)
    $script:todayDone      = @{}
    $script:weekDone       = @{}
    $script:lunchIntervals = @()
    $script:lunchAdjust    = 0
    foreach ($r in @(Read-Sessions)) {
        if ($r.Start -ge $ws -and $r.Start -lt $we) {
            $script:weekDone[$r.Name] = [double]$script:weekDone[$r.Name] + $r.Seconds
        }
        if ($r.Start.Date -eq $today) {
            $script:todayDone[$r.Name] = [double]$script:todayDone[$r.Name] + $r.Seconds
            if ($r.Name -eq 'Lunch') {
                # Rounding is a straight adjustment; real lunch breaks are time ranges
                if ($r.IsAdjust) { $script:lunchAdjust += $r.Seconds }
                else { $script:lunchIntervals += ,@($r.Start, $r.End) }
            }
        }
    }
}

function Write-WeekSummary {
    try {
        $ws = Get-WeekStart
        $hours = @{}
        foreach ($r in @(Read-Sessions)) {
            $idx = [int][math]::Floor(($r.Start.Date - $ws).TotalDays)
            if ($idx -lt 0 -or $idx -gt 6) { continue }
            if (-not $hours.ContainsKey($r.Name)) { $hours[$r.Name] = New-Object double[] 7 }
            $hours[$r.Name][$idx] += $r.Seconds / 3600
        }

        # Current accounts first, then any older names used this week, then lunch
        $known  = @($script:accounts | ForEach-Object { $_.Name })
        $order  = @($known | Where-Object { $hours.ContainsKey($_) })
        $order += @($hours.Keys | Where-Object { $known -notcontains $_ -and $_ -ne 'Lunch' } | Sort-Object)
        if ($hours.ContainsKey('Lunch')) { $order += 'Lunch' }

        $dayNames = @(0..6 | ForEach-Object { $ws.AddDays($_).ToString('ddd dd/MM', $GB) })
        $totals = New-Object double[] 7
        $out = @()
        foreach ($n in $order) {
            $o = [ordered]@{ Account = $n }
            $sum = 0
            for ($d = 0; $d -lt 7; $d++) {
                $h = $hours[$n][$d]
                $o[$dayNames[$d]] = [math]::Round($h, 2).ToString($Inv)
                $sum += $h
                if ($n -ne 'Lunch') { $totals[$d] += $h }
            }
            $o['Total'] = [math]::Round($sum, 2).ToString($Inv)
            $out += [pscustomobject]$o
        }

        $t = [ordered]@{ Account = 'Total (excluding lunch)' }
        $grand = 0
        for ($d = 0; $d -lt 7; $d++) {
            $t[$dayNames[$d]] = [math]::Round($totals[$d], 2).ToString($Inv)
            $grand += $totals[$d]
        }
        $t['Total'] = [math]::Round($grand, 2).ToString($Inv)
        $out += [pscustomobject]$t

        $out | Export-Csv -Path (Get-WeekFile) -NoTypeInformation -Encoding UTF8
        $script:summaryDirty = $false
    } catch {
        if (-not $script:summaryDirty) {
            Write-Log "Could not update the weekly summary (is it open in Excel?). Will keep retrying. $($_.Exception.Message)"
        }
        $script:summaryDirty = $true
    }
}

function Clear-TodayFor($name) {
    $today = (Get-Date).ToString('yyyy-MM-dd', $Inv)
    Flush-Pending
    $script:pending = @($script:pending | Where-Object { -not ($_.Date -eq $today -and $_.Account -eq $name) })
    if (Test-Path $SessionsFile) {
        $keep = @(Import-Csv $SessionsFile | Where-Object { -not ($_.Date -eq $today -and $_.Account -eq $name) })
        if ($keep.Count -gt 0) {
            $keep | Export-Csv -Path $SessionsFile -NoTypeInformation -Encoding UTF8
        } else {
            Set-Content -Path $SessionsFile -Value '"Date","Account","Start","End","Minutes"' -Encoding UTF8
        }
    }
}

# ======================================================================
# Running slot
# ======================================================================

function Save-State {
    $lines = @((Get-Date).ToString('o'), '', '', '')
    if ($script:active) {
        $lines[1] = '1'
        $lines[2] = $script:active.Name
        $lines[3] = $script:active.Start.ToString('o')
    }
    try { Set-Content -Path $StateFile -Value $lines }
    catch { Write-Log "Could not save state: $($_.Exception.Message)" }
}

function Load-State {
    # If the timer was closed unexpectedly (crash, power cut) while a slot was running,
    # log that slot up to the last time the timer was known to be running.
    if (-not (Test-Path $StateFile)) { return }
    $lines = @(Get-Content $StateFile)
    if ($lines.Count -ge 4 -and $lines[1]) {
        $styles   = [Globalization.DateTimeStyles]::RoundtripKind
        $lastSeen = [datetime]::Parse($lines[0], $Inv, $styles)
        $from     = [datetime]::Parse($lines[3], $Inv, $styles)
        if ($lastSeen -gt $from) { Add-Session $lines[2] $from $lastSeen }
        Write-Log "Recovered a running '$($lines[2])' session, logged up to $lastSeen."
    }
    $script:active = $null
    Save-State
}

function Stop-Active([datetime]$at) {
    if (-not $script:active) { return }
    $a = $script:active
    $script:active = $null
    if ($at -gt $a.Start) { Add-Session $a.Name $a.Start $at }
    Save-State
    Refresh-Totals
    Write-WeekSummary
}

function Start-Slot($i) {
    $script:active = @{ Name = $script:slots[$i]; Start = Get-Date }
    Save-State
}

function Switch-Slot($i) {
    $wasThis = [bool]($script:active -and $script:active.Name -eq $script:slots[$i])
    Stop-Active (Get-Date)
    if (-not $wasThis) { Start-Slot $i }
}

# ======================================================================
# Numbers for the display
# ======================================================================

function Update-View {
    $now   = Get-Date
    $clock = $now
    $start, $end = Get-DayBounds
    $cap = Limit-Time $clock $start $end
    $worked = ($cap - $start).TotalSeconds

    $onLunch = [bool]($script:active -and $script:active.Name -eq 'Lunch')
    $ints = @($script:lunchIntervals)
    if ($onLunch) { $ints += ,@($script:active.Start, $clock) }

    # Billable day, capped at the end time
    $lunchSec = 0
    foreach ($iv in $ints) {
        $a = Limit-Time $iv[0] $start $cap
        $b = Limit-Time $iv[1] $start $cap
        if ($b -gt $a) { $lunchSec += ($b - $a).TotalSeconds }
    }
    $billable = [math]::Max(0.0, $worked - $lunchSec - $script:lunchAdjust)

    # Overtime after the end time, less any lunch taken during it
    $overtime = $clock -gt $end
    $otSec = 0
    if ($overtime) {
        $otSec = ($clock - $end).TotalSeconds
        foreach ($iv in $ints) {
            $a = Limit-Time $iv[0] $end $clock
            $b = Limit-Time $iv[1] $end $clock
            if ($b -gt $a) { $otSec -= ($b - $a).TotalSeconds }
        }
        $otSec = [math]::Max(0.0, $otSec)
    }

    $rows = @()
    $accountSec = 0
    for ($i = 0; $i -lt $script:slots.Count; $i++) {
        $n   = $script:slots[$i]
        $td  = [double]$script:todayDone[$n]
        $wk  = [double]$script:weekDone[$n]
        $act = [bool]($script:active -and $script:active.Name -eq $script:slots[$i])
        if ($act) {
            $e = ($now - $script:active.Start).TotalSeconds
            $td += $e; $wk += $e
        }
        if ($n -ne 'Lunch') { $accountSec += $td }
        $canRound = ($td -gt 1 -and ($td % $RoundBlock) -gt 1)
        $rows += @{ Name = $n; Today = $td; Week = $wk; Active = $act; CanRound = $canRound }
    }

    $status = if ($onLunch) { 'ON LUNCH' }
              elseif ($clock -lt $start) { "STARTS AT $StartTime" }
              elseif ($overtime) { 'OVERTIME' }
              else { 'WORKING' }

    $bt = [timespan]::FromSeconds([math]::Floor($billable))
    $ot = [timespan]::FromSeconds([math]::Floor($otSec))
    $script:view = @{
        H           = '{0:00}' -f [math]::Floor($bt.TotalHours)
        M           = '{0:00}' -f $bt.Minutes
        Sec         = '{0:00}' -f $bt.Seconds
        Progress    = $worked / ($end - $start).TotalSeconds
        Status      = $status
        Unallocated = 'UNALLOCATED ' + (Format-Dur ([math]::Max(0.0, $billable + $otSec - $accountSec)))
        OnLunch     = $onLunch
        Before      = ($clock -lt $start)
        Overtime    = $overtime
        OT          = '+{0:00}:{1:00}:{2:00}' -f [math]::Floor($ot.TotalHours), $ot.Minutes, $ot.Seconds
        Rows        = $rows
    }
    $tray.Text = if ($overtime) { 'Account timer: overtime {0}h {1}m' -f [math]::Floor($ot.TotalHours), $ot.Minutes }
                 else { 'Account timer: {0}h {1}m' -f [int]$script:view.H, $script:view.M }

    # Open or close the overtime section
    $newTop = if ($overtime) { $BaseRowTop + $OvertimeH } else { $BaseRowTop }
    if ($newTop -ne $RowTop) {
        $script:RowTop = $newTop
        Resize-Form
    }
}

# ======================================================================
# Look and layout (drawn at 320 wide, height depends on the number of rows, then scaled)
# ======================================================================

$g0   = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
$Zoom = $Scale * $g0.DpiX / 96
$g0.Dispose()

$BaseW  = 320
$BaseRowTop = 100
$OvertimeH  = 32     # height of the overtime section when it appears
$RowTop = $BaseRowTop
$RowH   = 30
$LunchGap = 10   # space separating Lunch from the accounts
$RoundXs  = @{ down = 150; up = 170 }   # positions of the round-down and round-up buttons
$RoundR   = 8
$RoundBlock = 15 * 60   # round up to the next 15 minutes
Load-Accounts
$BaseH  = $RowTop + $script:slots.Count * $RowH + $LunchGap + 6

$C = [System.Drawing.Color]
# ---- Themes ----
# Themes live in account-timer-themes.txt next to this script, so they can be edited
# without touching the script and survive script updates. The standard set below is
# only used to create that file if it's missing, or as a fallback if it can't be read.
function New-State($panel, $circle, $hot, $fill) {
    return @{
        Panel     = [System.Drawing.ColorTranslator]::FromHtml($panel)
        Circle    = [System.Drawing.ColorTranslator]::FromHtml($circle)
        CircleHot = [System.Drawing.ColorTranslator]::FromHtml($hot)
        Fill      = [System.Drawing.ColorTranslator]::FromHtml($fill)
    }
}

$DefaultThemesText = @'
; Account timer themes
;
; Edit this file to change the colours or add your own themes, then right-click
; the timer and go to Theme, then Reload themes. Lines starting with ; are notes.
;
; Each theme starts with its name in square brackets, followed by
;   Text      the colour of the numbers, labels and icons
;   Band      how dark the Lunch band is, from 0 (none) to 255 (black)
;   Working   colours while the clock is running
;   Lunch     colours while on lunch
;   Overtime  colours after the end of the working day
;
; Working, Lunch and Overtime each take four colours, separated by commas, in this order
;   background, buttons, button hover, progress bar
;
; Colours are hex codes such as #357a38. Themes appear in the menu in the order
; they're listed here, with a divider before the first light theme (one with dark text).
; To remove a theme, delete its section. If this file is deleted, the timer
; recreates it with the standard themes next time it starts.

[Classic]
Text     = #ffffff
Band     = 45
Working  = #357a38, #4caf50, #6fbf73, #6fbf73
Lunch    = #b26a00, #ff9800, #ffac33, #ffac33
Overtime = #aa2e25, #f44336, #f6685e, #f6685e

[Midnight]
Text     = #ffffff
Band     = 50
Working  = #1c2633, #2e3f55, #3c5270, #5b8def
Lunch    = #33281a, #54422a, #6b5536, #f0b35b
Overtime = #341b20, #562c35, #6e3844, #ef5b6e

[Lavender]
Text     = #ffffff
Band     = 40
Working  = #4f4762, #6a5f82, #7d7196, #b9aedc
Lunch    = #6b4a3a, #8c6450, #a07562, #f2c48d
Overtime = #6b3a4a, #8c4f63, #a15f75, #f28da8

[Graphite]
Text     = #ffffff
Band     = 45
Working  = #2b2d2f, #404346, #4f5357, #9aa0a6
Lunch    = #3a3226, #574a36, #685a43, #d9a95b
Overtime = #3a2626, #573838, #694444, #d96b6b

[Ocean]
Text     = #ffffff
Band     = 45
Working  = #0f4c5c, #1b6f84, #2a8aa3, #5ec8e0
Lunch    = #7a4a12, #a0661f, #b8792e, #ffb74d
Overtime = #7a1f2b, #a12d3c, #b8404f, #ff7083

[Nord]
Text     = #eceff4
Band     = 40
Working  = #2e3440, #3b4252, #4c566a, #88c0d0
Lunch    = #3d3530, #5a4a3f, #6e5a4c, #d08770
Overtime = #3d2c30, #5c3a41, #70474f, #bf616a

[Dracula]
Text     = #f8f8f2
Band     = 45
Working  = #282a36, #44475a, #6272a4, #bd93f9
Lunch    = #3a2f2a, #5a4636, #6e5642, #ffb86c
Overtime = #3a2228, #5c2f38, #723a46, #ff5555

[Solarized Dark]
Text     = #eee8d5
Band     = 45
Working  = #002b36, #073642, #0d4a59, #2aa198
Lunch    = #2b2a10, #4a4214, #5c521a, #b58900
Overtime = #36110f, #5a1d1a, #6e2420, #dc322f

[Sunset]
Text     = #ffffff
Band     = 45
Working  = #3d2352, #5b3577, #6f4391, #f78fb3
Lunch    = #5c3a1e, #80522b, #996436, #ffc36e
Overtime = #5c1e2e, #802b42, #993650, #ff6b81

[Espresso]
Text     = #f5ece4
Band     = 45
Working  = #3b2a22, #57402f, #6b4f3b, #c8a27a
Lunch    = #4a3416, #6e4e1f, #856027, #f2b45c
Overtime = #4a1f1c, #6e2f2a, #853a34, #e86a5c

[High Contrast]
Text     = #ffffff
Band     = 70
Working  = #0a0a0a, #262626, #3a3a3a, #00e676
Lunch    = #2a1a00, #3d2700, #523400, #ffab00
Overtime = #2a0000, #3d0a0a, #521414, #ff1744

[Light]
Text     = #202124
Band     = 14
Working  = #f4f6f4, #dbe7dc, #c8dcca, #4caf50
Lunch    = #fff6e8, #ffe2b8, #ffd38f, #ff9800
Overtime = #fdeeec, #f9cdc9, #f5b2ac, #f44336

[Solarized Light]
Text     = #073642
Band     = 14
Working  = #fdf6e3, #eee8d5, #e4dcc4, #2aa198
Lunch    = #fbefd5, #f2dfae, #ebd393, #b58900
Overtime = #fbe6df, #f2cbbe, #ebb8a7, #dc322f

[Mint]
Text     = #1d2b28
Band     = 14
Working  = #eef8f4, #d3ece2, #bfe2d4, #26a69a
Lunch    = #fff5e6, #ffe0b5, #ffd08c, #fb8c00
Overtime = #fdecee, #f8ccd2, #f4b3bd, #e53935
'@

function New-ThemeFromSettings($name, $kv) {
    try {
        $text = $kv['text']
        if (-not $text) { throw 'Text colour is missing' }
        [void][System.Drawing.ColorTranslator]::FromHtml($text)
        $band = 45
        if ($kv.ContainsKey('band')) { $band = [math]::Max(0, [math]::Min(255, [int]$kv['band'])) }
        $states = @{}
        foreach ($pair in @(@('working', 'running'), @('lunch', 'paused'), @('overtime', 'overtime'))) {
            $parts = @($kv[$pair[0]] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            if ($parts.Count -ne 4) { throw "$($pair[0]) needs four colours" }
            $states[$pair[1]] = New-State $parts[0] $parts[1] $parts[2] $parts[3]
        }
        return @{ Text = $text; Band = $band; running = $states.running; paused = $states.paused; overtime = $states.overtime }
    } catch {
        Write-Log "Theme '$name' skipped: $($_.Exception.Message)"
        return $null
    }
}

function ConvertFrom-ThemeText($text) {
    $sets = [ordered]@{}
    $name = $null
    $kv = $null
    foreach ($raw in ($text -split "`r?`n")) {
        $line = $raw.Trim()
        if (-not $line -or $line.StartsWith(';')) { continue }
        if ($line -match '^\[(.+)\]$') {
            if ($name) { $t = New-ThemeFromSettings $name $kv; if ($t) { $sets[$name] = $t } }
            $name = $Matches[1].Trim()
            $kv = @{}
            continue
        }
        if ($null -ne $kv -and $line -match '^([^=]+)=(.*)$') {
            $kv[$Matches[1].Trim().ToLower()] = $Matches[2].Trim()
        }
    }
    if ($name) { $t = New-ThemeFromSettings $name $kv; if ($t) { $sets[$name] = $t } }
    return $sets
}

function Load-Themes {
    if (-not (Test-Path $ThemesFile)) {
        try { Set-Content -Path $ThemesFile -Value $DefaultThemesText -Encoding UTF8 }
        catch { Write-Log "Could not create the themes file: $($_.Exception.Message)" }
    }
    $sets = [ordered]@{}
    try { $sets = ConvertFrom-ThemeText ((Get-Content $ThemesFile) -join "`n") }
    catch { Write-Log "Could not read the themes file: $($_.Exception.Message)" }
    if ($sets.Count -eq 0) {
        Write-Log 'No usable themes in the themes file, so the standard set is being used.'
        $sets = ConvertFrom-ThemeText $DefaultThemesText
    }
    $script:ThemeSets = $sets
}

function Apply-Theme($name) {
    if (-not $script:ThemeSets.Contains($name)) { $name = @($script:ThemeSets.Keys)[0] }
    $set = $script:ThemeSets[$name]
    $script:themeName = $name
    $script:Themes = $set

    # Text and overlays are all shades of the theme's text colour
    $t = [System.Drawing.ColorTranslator]::FromHtml($set.Text)
    $script:cWhite     = $t
    $script:cFaint     = $C::FromArgb(215, $t)
    $script:bWhite     = New-Object System.Drawing.SolidBrush $t
    $script:bFaint     = New-Object System.Drawing.SolidBrush ($C::FromArgb(215, $t))
    $script:bDim       = New-Object System.Drawing.SolidBrush ($C::FromArgb(140, $t))
    $script:bTrack     = New-Object System.Drawing.SolidBrush ($C::FromArgb(40, $t))
    $script:bRowOn     = New-Object System.Drawing.SolidBrush ($C::FromArgb(40, $t))
    $script:bRowHover  = New-Object System.Drawing.SolidBrush ($C::FromArgb(18, $t))
    $script:bRowDrag   = New-Object System.Drawing.SolidBrush ($C::FromArgb(60, $t))
    $script:bGrip      = New-Object System.Drawing.SolidBrush ($C::FromArgb(110, $t))
    $script:pSep       = New-Object System.Drawing.Pen(($C::FromArgb(35, $t)), 1)
    $script:pFaint     = New-Object System.Drawing.Pen(($C::FromArgb(215, $t)), 0.8)
    $script:bLunchBand = New-Object System.Drawing.SolidBrush ($C::FromArgb($set.Band, 0, 0, 0))

    if ($form) {
        $form.BackColor = $set.running.Panel
        Request-Redraw
    }
}

function Save-Theme {
    try { Set-Content -Path $ThemeFile -Value $script:themeName } catch {}
}

Load-Themes
$savedTheme = 'Classic'
if (Test-Path $ThemeFile) { try { $savedTheme = (Get-Content $ThemeFile -TotalCount 1).Trim() } catch {} }
Apply-Theme $savedTheme

$px = [System.Drawing.GraphicsUnit]::Pixel
$fDigits = New-Object System.Drawing.Font('Segoe UI Semibold', 36, [System.Drawing.FontStyle]::Regular, $px)
$fLabel  = New-Object System.Drawing.Font('Segoe UI', 9.5, [System.Drawing.FontStyle]::Bold, $px)
$fRow    = New-Object System.Drawing.Font('Segoe UI Semibold', 12, [System.Drawing.FontStyle]::Regular, $px)
$fOvertime = New-Object System.Drawing.Font('Segoe UI Semibold', 24, [System.Drawing.FontStyle]::Regular, $px)

$sfCentre = New-Object System.Drawing.StringFormat
$sfCentre.Alignment = 'Center'; $sfCentre.LineAlignment = 'Center'
$sfLeft = New-Object System.Drawing.StringFormat
$sfLeft.Alignment = 'Near'; $sfLeft.LineAlignment = 'Center'
$sfRight = New-Object System.Drawing.StringFormat
$sfRight.Alignment = 'Far'; $sfRight.LineAlignment = 'Center'
$sfName = New-Object System.Drawing.StringFormat
$sfName.Alignment = 'Near'; $sfName.LineAlignment = 'Center'
$sfName.Trimming = 'EllipsisCharacter'; $sfName.FormatFlags = 'NoWrap'

$RF = [System.Drawing.RectangleF]
$PF = [System.Drawing.PointF]
$colX = @(0, 107, 213)
$colW = 107

# ---- Window ----
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.TopMost = $true
$form.ShowInTaskbar = $false
$form.StartPosition = 'Manual'
$form.BackColor = $Themes.running.Panel
$form.ClientSize = New-Object System.Drawing.Size([int]($BaseW * $Zoom), [int]($BaseH * $Zoom))
$form.GetType().GetProperty('DoubleBuffered',
    [Reflection.BindingFlags]'NonPublic,Instance').SetValue($form, $true, $null)

try {
    $corner = 2
    [void][Win.Dwm]::DwmSetWindowAttribute($form.Handle, 33, [ref]$corner, 4)
} catch {}

try {
    [Win.Layered]::MakeLayered($form.Handle)
    $script:layered = $true
} catch {
    Write-Log "See-through drawing isn't available, so the timer is using normal drawing. $($_.Exception.Message)"
}

# Resize and redraw for the scaling of whichever monitor the timer is on
function Update-Dpi {
    if (-not $script:perMonitor) { return }
    try {
        $dpi = [Win.Dpi]::GetDpiForWindow($form.Handle)
        if ($dpi -gt 0) {
            $z = $Scale * $dpi / 96
            if ([math]::Abs($z - $script:Zoom) -gt 0.001) {
                $script:Zoom = $z
                $form.ClientSize = New-Object System.Drawing.Size([int]($BaseW * $z), [int]($BaseH * $z))
                Request-Redraw
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

# ---- Icons ----
function Draw-PlayIcon($g, $cx, $cy) {
    $g.FillPolygon($bWhite, [System.Drawing.PointF[]]@(
        (New-Object $PF ($cx - 3), ($cy - 5)), (New-Object $PF ($cx - 3), ($cy + 5)),
        (New-Object $PF ($cx + 5), $cy)))
}

function Draw-PauseIcon($g, $cx, $cy) {
    $g.FillRectangle($bWhite, ($cx - 4), ($cy - 5), 2.8, 10)
    $g.FillRectangle($bWhite, ($cx + 1.2), ($cy - 5), 2.8, 10)
}

function Draw-LunchIcon($g, $cx, $cy) {
    # Fork and knife, drawn small to fit the circle
    $state = $g.Save()
    $g.TranslateTransform($cx, $cy)
    $g.ScaleTransform(0.7, 0.7)
    $pen = New-Object System.Drawing.Pen($cWhite, 1.6)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLine($pen, -4, -1, -4, 7)
    foreach ($tx in @(-6.5, -4, -1.5)) { $g.DrawLine($pen, $tx, -7, $tx, -3) }
    $g.DrawArc($pen, -6.5, -5.5, 5, 4.5, 0, 180)
    $g.DrawLine($pen, 4.5, 1, 4.5, 7)
    $g.FillPolygon($bWhite, [System.Drawing.PointF[]]@(
        (New-Object $PF 3, 1.5), (New-Object $PF 3, -7),
        (New-Object $PF 6.3, -3), (New-Object $PF 6.3, 1.5)))
    $pen.Dispose()
    $g.Restore($state)
}

# ---- Drawing ----
# How see-through the account list is (255 = solid). The main timer always stays solid.
$ListLevels = [ordered]@{ 'Off' = 255; 'Subtle' = 215; 'Medium' = 170; 'Strong' = 125 }
$TextSharpen = 0.8    # see-through text edge sharpening: 1 = none, lower = crisper and bolder
$BlurTint    = 0.55   # with blur on, the background tint is lightened by this much so the blur shows

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [float](2 * $r)
    $p.AddArc($x, $y, $d, $d, [float]180, [float]90)
    $p.AddArc([float]($x + $w - $d), $y, $d, $d, [float]270, [float]90)
    $p.AddArc([float]($x + $w - $d), [float]($y + $h - $d), $d, $d, [float]0, [float]90)
    $p.AddArc($x, [float]($y + $h - $d), $d, $d, [float]90, [float]90)
    $p.CloseFigure()
    return $p
}

# Fill a band of the timer, keeping the rounded corners when drawing as an image
function Fill-Area($g, $brush, $x, $y, $w, $h) {
    if ($script:shapePath) {
        $g.SetClip((New-Object System.Drawing.RectangleF([float]$x, [float]$y, [float]$w, [float]$h)))
        $g.FillPath($brush, $script:shapePath)
        $g.ResetClip()
    } else {
        $g.FillRectangle($brush, [float]$x, [float]$y, [float]$w, [float]$h)
    }
}

# Text in the account list. With transparency on, ClearType can't be used (its coloured
# edges show up as a halo), so the text's shape is drawn into a mask and then laid onto the
# timer in its own colour with the edges sharpened.
function Draw-ListText($g, $text, $font, $brush, $rect, $sf) {
    if ($script:ct) {
        $col = $brush.Color
        $solid = New-Object System.Drawing.SolidBrush ($C::FromArgb(255, $col))
        $script:ct.G.FillRectangle($solid, $rect)
        $solid.Dispose()
        $shape = New-Object System.Drawing.SolidBrush ($C::FromArgb($col.A, 255, 255, 255))
        $script:ct.M.DrawString($text, $font, $shape, $rect, $sf)
        $shape.Dispose()
    } else {
        $g.DrawString($text, $font, $brush, $rect, $sf)
    }
}

function Draw-Timer($g, [bool]$layered) {
    try {
        $g.SmoothingMode = 'AntiAlias'
        $g.TextRenderingHint = 'ClearTypeGridFit'
        $g.ScaleTransform($Zoom, $Zoom)
        $v = $script:view
        if (-not $v) { return }

        $theme = if ($v.OnLunch) { $Themes.paused } elseif ($v.Overtime) { $Themes.overtime } else { $Themes.running }
        $alpha = if ($layered) { $script:listAlpha } else { 255 }
        if ($alpha -lt 255 -and $script:blurOn) { $alpha = [int]($alpha * $BlurTint) }
        $script:shapePath = if ($layered) { New-RoundedPath 0 0 $BaseW $BaseH 8 } else { $null }

        # Solid main timer on top, account list below (see-through if chosen)
        $split = $RowTop - 17
        $bg = New-Object System.Drawing.SolidBrush $theme.Panel
        Fill-Area $g $bg 0 0 $BaseW $split
        $bg.Dispose()
        $bg = New-Object System.Drawing.SolidBrush ($C::FromArgb($alpha, $theme.Panel))
        Fill-Area $g $bg 0 $split $BaseW ($BaseH - $split)
        $bg.Dispose()

        # Small hourglass
        $g.DrawLines($pFaint, [System.Drawing.PointF[]]@(
            (New-Object $PF 8, 7), (New-Object $PF 14, 7), (New-Object $PF 8, 16),
            (New-Object $PF 14, 16), (New-Object $PF 8, 7)))

        # Main timer
        $labels = @('HOURS', 'MINUTES', 'SECONDS')
        $values = @($v.H, $v.M, $v.Sec)
        $digitBrush = if ($v.Before) { $bDim } else { $bWhite }
        for ($i = 0; $i -lt 3; $i++) {
            $g.DrawString($labels[$i], $fLabel, $bFaint, (New-Object $RF $colX[$i], 4, $colW, 14), $sfCentre)
            $g.DrawString($values[$i], $fDigits, $digitBrush, (New-Object $RF $colX[$i], 14, $colW, 52), $sfCentre)
        }
        foreach ($x in @(107, 213)) {
            $g.FillEllipse($bFaint, ($x - 1.3), 34, 2.6, 2.6)
            $g.FillEllipse($bFaint, ($x - 1.3), 45, 2.6, 2.6)
        }

        # Status and unallocated time
        $statusBrush = if ($v.OnLunch) { $bWhite } else { $bFaint }
        $g.DrawString($v.Status, $fLabel, $statusBrush, (New-Object $RF 10, 63, 150, 14), $sfLeft)
        $g.DrawString($v.Unallocated, $fLabel, $bFaint, (New-Object $RF 160, 63, 150, 14), $sfRight)

        # Overtime counter
        if ($v.Overtime) {
            $g.DrawLine($pSep, 10, 80, 310, 80)
            $g.DrawString($v.OT, $fOvertime, $bWhite, (New-Object $RF 0, 81, $BaseW, ($OvertimeH - 2)), $sfCentre)
        }

        # Progress through the working day
        $fb = New-Object System.Drawing.SolidBrush $theme.Fill
        $g.FillRectangle($bTrack, 0, ($RowTop - 20), $BaseW, 3)
        $g.FillRectangle($fb, 0, ($RowTop - 20), ($BaseW * [math]::Min(1.0, $v.Progress)), 3)
        $fb.Dispose()


        # Column headings
        Draw-ListText $g 'TODAY' $fLabel $bFaint (New-Object $RF 180, ($RowTop - 14), 70, 14) $sfRight
        Draw-ListText $g 'WEEK' $fLabel $bFaint (New-Object $RF 250, ($RowTop - 14), 60, 14) $sfRight

        # Slots
        for ($i = 0; $i -lt $v.Rows.Count; $i++) {
            $r  = $v.Rows[$i]
            $y0 = Get-RowTop $i
            $cy = $y0 + $RowH / 2
            $isLunch = $r.Name -eq 'Lunch'

            # Lunch sits on its own darker band, set apart from the accounts
            if ($isLunch) { Fill-Area $g $bLunchBand 0 $y0 $BaseW ($BaseH - $y0) }

            $dragging = [bool]($script:reorder -and $script:reorder.Name -eq $r.Name)
            if ($dragging) { Fill-Area $g $bRowDrag 0 $y0 $BaseW $RowH }
            elseif ($r.Active) { Fill-Area $g $bRowOn 0 $y0 $BaseW $RowH }
            elseif ($script:hover -eq $i) { Fill-Area $g $bRowHover 0 $y0 $BaseW $RowH }
            if (-not $isLunch) { $g.DrawLine($pSep, 10, $y0, 310, $y0) }

            # Drag handle (accounts only, Lunch stays at the bottom)
            if ($r.Name -ne 'Lunch') {
                $gb = if ($dragging -or $script:gripHover -eq $i) { $bWhite } else { $bGrip }
                foreach ($gx in @(6, 10)) {
                    foreach ($gy in @(-4, 0, 4)) { $g.FillEllipse($gb, ($gx - 1.1), ($cy + $gy - 1.1), 2.2, 2.2) }
                }
            }

            $cb = New-Object System.Drawing.SolidBrush ($(if ($r.Active) { $theme.CircleHot } else { $theme.Circle }))
            $g.FillEllipse($cb, 18, ($cy - 10), 20, 20)
            $cb.Dispose()

            if ($r.Active) { Draw-PauseIcon $g 28 $cy }
            elseif ($r.Name -eq 'Lunch') { Draw-LunchIcon $g 28 $cy }
            else { Draw-PlayIcon $g 28 $cy }

            # Names use the full width, and only make room while the rounding buttons are showing
            $todayText = Format-Dur $r.Today
            $showRound = $r.CanRound -and $script:hover -eq $i -and -not $script:reorder
            if ($showRound) {
                $nameW = 88
            } else {
                $todayW = $g.MeasureString($todayText, $fRow).Width
                $nameW = [math]::Max(88.0, 250 - $todayW - 8 - 46)
            }
            Draw-ListText $g $r.Name $fRow $bWhite (New-Object $RF 46, $y0, $nameW, $RowH) $sfName
            Draw-ListText $g $todayText $fRow $bWhite (New-Object $RF 180, $y0, 70, $RowH) $sfRight
            Draw-ListText $g (Format-Dur $r.Week) $fLabel $bFaint (New-Object $RF 250, $y0, 60, $RowH) $sfRight

            # Round-down and round-up buttons, shown when hovering over a row that isn't on a 15-minute mark
            if ($showRound) {
                foreach ($dir in @('down', 'up')) {
                    $bx = $RoundXs[$dir]
                    $key = '{0}:{1}' -f $i, $dir
                    $rc = if ($script:roundHover -eq $key) { $theme.CircleHot } else { $theme.Circle }
                    $rbr = New-Object System.Drawing.SolidBrush $rc
                    $g.FillEllipse($rbr, ($bx - $RoundR), ($cy - $RoundR), (2 * $RoundR), (2 * $RoundR))
                    $rbr.Dispose()
                    $tipY = if ($dir -eq 'up') { -1.8 } else { 1.8 }
                    $cp = New-Object System.Drawing.Pen($cWhite, 1.5)
                    $cp.StartCap = 'Round'; $cp.EndCap = 'Round'; $cp.LineJoin = 'Round'
                    $g.DrawLines($cp, [System.Drawing.PointF[]]@(
                        (New-Object $PF ($bx - 3.5), ($cy - $tipY)),
                        (New-Object $PF $bx, ($cy + $tipY)),
                        (New-Object $PF ($bx + 3.5), ($cy - $tipY))))
                    $cp.Dispose()
                }
            }
        }
    } catch {
        Write-Log "Drawing error: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"
    } finally {
        if ($script:shapePath) { $script:shapePath.Dispose(); $script:shapePath = $null }
    }
}

# Normal drawing, used if see-through drawing isn't available
$form.Add_Paint({ if (-not $script:layered) { Draw-Timer $_.Graphics $false } })

function Render-Frame {
    $w = $form.ClientSize.Width
    $h = $form.ClientSize.Height
    if ($w -le 0 -or $h -le 0) { return }
    $fmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    $bmp = New-Object System.Drawing.Bitmap($w, $h, $fmt)
    $g = [System.Drawing.Graphics]::FromImage($bmp)

    # With the account list see-through, prepare the extra text layers
    $ctColour = $null; $ctMask = $null
    if ($script:listAlpha -lt 255) {
        $ctColour = New-Object System.Drawing.Bitmap($w, $h, $fmt)
        $ctMask = New-Object System.Drawing.Bitmap($w, $h, $fmt)
        $cg = [System.Drawing.Graphics]::FromImage($ctColour)
        $cg.ScaleTransform($Zoom, $Zoom)
        $mg = [System.Drawing.Graphics]::FromImage($ctMask)
        $mg.Clear([System.Drawing.Color]::Transparent)
        $mg.TextRenderingHint = 'AntiAliasGridFit'
        $mg.ScaleTransform($Zoom, $Zoom)
        $script:ct = @{ G = $cg; M = $mg }
    }
    try {
        $g.Clear([System.Drawing.Color]::Transparent)
        Draw-Timer $g $true
    } finally {
        $g.Dispose()
        if ($script:ct) {
            $script:ct.G.Dispose(); $script:ct.M.Dispose()
            $script:ct = $null
        }
    }
    if ($ctColour) {
        try { [Win.Layered]::Combine($bmp, $ctColour, $ctMask, $TextSharpen) }
        catch { Write-Log "Text drawing error: $($_.Exception.Message)" }
        $ctColour.Dispose(); $ctMask.Dispose()
    }
    try {
        $ok = [Win.Layered]::Push($form.Handle, $bmp, $form.Left, $form.Top)
        if (-not $ok) {
            # Fall back to normal drawing rather than risk an invisible timer
            Write-Log 'See-through drawing failed, so the timer has switched back to normal drawing.'
            [Win.Layered]::MakeNormal($form.Handle)
            $script:layered = $false
            $form.Invalidate()
        }
    } catch {
        Write-Log "See-through drawing error: $($_.Exception.Message)"
        try { [Win.Layered]::MakeNormal($form.Handle) } catch {}
        $script:layered = $false
        $form.Invalidate()
    } finally { $bmp.Dispose() }
}

function Request-Redraw {
    if ($script:layered) { Render-Frame } else { $form.Invalidate() }
}

# Blur only shows while the account list is see-through, so it's switched off at 'Off'
function Apply-BlurState {
    if (-not $script:layered) { return }
    $want = $script:blurOn -and $script:listLevel -ne 'Off'
    try { [void][Win.Layered]::SetBlur($form.Handle, $want) } catch {}
}

function Set-Blur([bool]$on, [switch]$Quiet) {
    if ($on) {
        $ok = $false
        if ($script:layered) {
            try { $ok = [Win.Layered]::SetBlur($form.Handle, $true) } catch {}
        }
        if (-not $ok) {
            Write-Log 'Blur is not available on this version of Windows.'
            if (-not $Quiet) { Show-Message "Blur isn't available on this version of Windows, so it has been left off." }
            $on = $false
        }
    }
    $script:blurOn = $on
    try { Set-Content -Path $BlurFile -Value ([int]$on) } catch {}
    Apply-BlurState
    Request-Redraw
}

function Set-ListTransparency($level) {
    if (-not $ListLevels.Contains($level)) { $level = 'Off' }
    $script:listLevel = $level
    $script:listAlpha = $ListLevels[$level]
    try { Set-Content -Path $ListAlphaFile -Value $level } catch {}
    Apply-BlurState
    Request-Redraw
}

# ---- Small text prompt (for renaming) ----
function Show-TextPrompt($title, $prompt, $default) {
    $k = $Zoom / $Scale
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = $title
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.MinimizeBox = $false
    $dlg.MaximizeBox = $false
    $dlg.ShowInTaskbar = $false
    $dlg.TopMost = $true
    $dlg.StartPosition = 'CenterScreen'
    $dlg.AutoScaleMode = 'None'
    $dlg.Font = New-Object System.Drawing.Font('Segoe UI', [float](12 * $k), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $dlg.ClientSize = New-Object System.Drawing.Size([int](300 * $k), [int](104 * $k))

    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = $prompt
    $lbl.AutoSize = $true
    $lbl.MaximumSize = New-Object System.Drawing.Size([int](276 * $k), 0)
    $lbl.Location = New-Object System.Drawing.Point([int](12 * $k), [int](12 * $k))

    $box = New-Object System.Windows.Forms.TextBox
    $box.Text = $default
    $box.Location = New-Object System.Drawing.Point([int](12 * $k), [int](34 * $k))
    $box.Width = [int](276 * $k)

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = 'OK'
    $ok.DialogResult = 'OK'
    $ok.Location = New-Object System.Drawing.Point([int](132 * $k), [int](68 * $k))
    $ok.Size = New-Object System.Drawing.Size([int](75 * $k), [int](26 * $k))

    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = 'Cancel'
    $cancel.DialogResult = 'Cancel'
    $cancel.Location = New-Object System.Drawing.Point([int](213 * $k), [int](68 * $k))
    $cancel.Size = New-Object System.Drawing.Size([int](75 * $k), [int](26 * $k))

    $dlg.AcceptButton = $ok
    $dlg.CancelButton = $cancel
    $dlg.Controls.Add($lbl)
    $dlg.Controls.Add($box)
    $dlg.Controls.Add($ok)
    $dlg.Controls.Add($cancel)

    $script:dialogOpen = $true
    try { $result = $dlg.ShowDialog($form) } finally { $script:dialogOpen = $false }
    $text = $box.Text.Trim()
    $dlg.Dispose()
    if ($result -eq [System.Windows.Forms.DialogResult]::OK -and $text) { return $text }
    return $null
}

function Ask-YesNo($question) {
    $script:dialogOpen = $true
    try {
        $answer = [System.Windows.Forms.MessageBox]::Show($form, $question, 'Account timer', 'YesNo', 'Question')
    } finally { $script:dialogOpen = $false }
    return ($answer -eq 'Yes')
}

function Show-Message($text) {
    $script:dialogOpen = $true
    try { [void][System.Windows.Forms.MessageBox]::Show($form, $text, 'Account timer') }
    finally { $script:dialogOpen = $false }
}

# ---- Mouse ----
function Get-RowTop($i) {
    $top = $RowTop + $i * $RowH
    if ($script:slots[$i] -eq 'Lunch') { $top += $LunchGap }
    return $top
}

function Get-HitRow($pt) {
    $y = $pt.Y / $Zoom
    for ($i = 0; $i -lt $script:slots.Count; $i++) {
        $top = Get-RowTop $i
        if ($y -ge $top -and $y -lt ($top + $RowH)) { return $i }
    }
    return $null
}

$tip = New-Object System.Windows.Forms.ToolTip

function Get-HitRound($pt) {
    # Returns e.g. "2:up" for the round-up button on row 2, or $null
    $row = Get-HitRow $pt
    if ($null -eq $row -or -not $script:view -or $row -ge $script:view.Rows.Count) { return $null }
    if (-not $script:view.Rows[$row].CanRound) { return $null }
    $x  = $pt.X / $Zoom
    $y  = $pt.Y / $Zoom
    $cy = (Get-RowTop $row) + $RowH / 2
    $r  = $RoundR + 1
    foreach ($dir in @('down', 'up')) {
        $bx = $RoundXs[$dir]
        if ((($x - $bx) * ($x - $bx) + ($y - $cy) * ($y - $cy)) -le ($r * $r)) { return ('{0}:{1}' -f $row, $dir) }
    }
    return $null
}

function Invoke-Round($key) {
    $parts = $key -split ':'
    $i = [int]$parts[0]
    $dir = $parts[1]
    $n = $script:slots[$i]

    # Today's total so far, including any time on the clock right now
    $total = [double]$script:todayDone[$n]
    $running = [bool]($script:active -and $script:active.Name -eq $n)
    if ($running) { $total += ((Get-Date) - $script:active.Start).TotalSeconds }
    if ($dir -eq 'up') { $target = [math]::Ceiling(($total - 1) / $RoundBlock) * $RoundBlock }
    else               { $target = [math]::Floor(($total + 1) / $RoundBlock) * $RoundBlock }
    $change = $target - $total
    if ([math]::Abs($change) -lt 1) { return }

    $mins = [math]::Max(1, [math]::Round([math]::Abs($change) / 60))
    $q = if ($dir -eq 'up') { "Add $mins min to $n so today comes to $(Format-Dur $target)?" }
         else { "Remove $mins min from $n so today comes to $(Format-Dur $target)?" }
    if ($running) { $q += "`n`n$n will be paused." }
    if (-not (Ask-YesNo $q)) { return }

    if ($running) { Stop-Active (Get-Date) }
    $change = $target - [double]$script:todayDone[$n]
    if ([math]::Abs($change) -ge 1) {
        $label = if ($dir -eq 'up') { 'rounded up' } else { 'rounded down' }
        Add-Rounding $n $change $label
    }
    Refresh-Totals
    Write-WeekSummary
    Update-View
    Request-Redraw
}

# Reads times like "1h 15m", "1h", "45m", "1:15" or "1.25" (hours). Returns seconds, or $null.
function ConvertFrom-DurationText($text) {
    $t = ($text -replace '\s+', ' ').Trim().ToLower()
    if (-not $t) { return $null }
    $sec = $null
    if ($t -match '^(\d+):(\d{1,2})$') {
        if ([int]$Matches[2] -lt 60) { $sec = [int]$Matches[1] * 3600 + [int]$Matches[2] * 60 }
    } elseif ($t -match '^\d+(\.\d+)?$') {
        $sec = [double]::Parse($t, $Inv) * 3600
    } elseif ($t -match '^(?:(\d+(?:\.\d+)?)\s*h(?:ours?|rs?)?)?\s*(?:(\d+)\s*m(?:ins?|inutes?)?)?$' -and ($Matches[1] -or $Matches[2])) {
        $h = if ($Matches[1]) { [double]::Parse($Matches[1], $Inv) } else { 0 }
        $m = if ($Matches[2]) { [int]$Matches[2] } else { 0 }
        $sec = $h * 3600 + $m * 60
    }
    if ($null -eq $sec -or $sec -lt 0 -or $sec -gt 24 * 3600) { return $null }
    return [math]::Round($sec)
}

function Set-TodayTime($i) {
    $n = $script:slots[$i]
    $total = [double]$script:todayDone[$n]
    $running = [bool]($script:active -and $script:active.Name -eq $n)
    if ($running) { $total += ((Get-Date) - $script:active.Start).TotalSeconds }

    $entry = Show-TextPrompt "Set today's time for $n" 'Total for today, such as 1h 15m, 1:15 or 1.25' (Format-Dur $total)
    if (-not $entry) { return }
    $target = ConvertFrom-DurationText $entry
    if ($null -eq $target) {
        Show-Message "Couldn't read '$entry'. Try something like 1h 15m, 45m, 1:15 or 1.25."
        return
    }

    if ($running) { Stop-Active (Get-Date) }
    $change = $target - [double]$script:todayDone[$n]
    if ([math]::Abs($change) -ge 1) { Add-Rounding $n $change 'adjusted' }
    Refresh-Totals
    Write-WeekSummary
    Update-View
    Request-Redraw
}

function Get-HitGrip($pt) {
    $row = Get-HitRow $pt
    if ($null -ne $row -and ($pt.X / $Zoom) -lt 15 -and $script:slots[$row] -ne 'Lunch') { return $row }
    return $null
}

function Move-DraggedRow($y) {
    $names = @($script:slots | Where-Object { $_ -ne 'Lunch' })
    if ($names.Count -lt 2) { return }
    $target = [int][math]::Floor(($y / $Zoom - $RowTop) / $RowH)
    $target = [math]::Max(0, [math]::Min($names.Count - 1, $target))
    $current = [array]::IndexOf($names, $script:reorder.Name)
    if ($current -lt 0 -or $target -eq $current) { return }
    $list = New-Object System.Collections.ArrayList
    $list.AddRange($names)
    $list.RemoveAt($current)
    $list.Insert($target, $script:reorder.Name)
    $script:slots = @($list) + @('Lunch')
    $script:reorder.Moved = $true
    Update-View
    Request-Redraw
}

function Save-Order {
    # Visible accounts in their new order, then hidden ones (which reappear at the bottom when shown)
    $ordered = @()
    foreach ($n in $script:slots) {
        if ($n -ne 'Lunch') {
            $acc = Find-Account $n
            if ($acc) { $ordered += $acc }
        }
    }
    $ordered += @($script:accounts | Where-Object { $_.Hidden })
    $script:accounts = $ordered
    Save-Accounts
    Rebuild-Slots
    Write-WeekSummary
}

$form.Add_MouseDown({
    if ($_.Button -ne 'Left') { return }
    $grip = Get-HitGrip $_.Location
    if ($null -ne $grip) {
        $script:reorder = @{ Name = $script:slots[$grip]; Moved = $false }
        Request-Redraw
        return
    }
    $round = Get-HitRound $_.Location
    if ($null -ne $round) {
        $script:pressedRound = $round
        return
    }
    $hit = Get-HitRow $_.Location
    if ($null -ne $hit) {
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
    if ($script:reorder) {
        Move-DraggedRow $_.Location.Y
        return
    }
    $hit   = Get-HitRow $_.Location
    $grip  = Get-HitGrip $_.Location
    $round = Get-HitRound $_.Location
    $form.Cursor = if ($null -ne $grip) { [System.Windows.Forms.Cursors]::SizeNS }
                   elseif ($null -ne $hit) { [System.Windows.Forms.Cursors]::Hand }
                   else { [System.Windows.Forms.Cursors]::Default }
    if ($round -ne $script:roundHover) {
        if ($null -ne $round) {
            $rp = $round -split ':'
            $tipText = if ($rp[1] -eq 'up') { 'Round today up to the next 15 minutes' } else { 'Round today down to the previous 15 minutes' }
            $tip.Show($tipText, $form,
                [int](($RoundXs[$rp[1]] - 70) * $Zoom), [int](((Get-RowTop ([int]$rp[0])) + $RowH) * $Zoom), 2500)
        } else {
            $tip.Hide($form)
        }
    }
    if ($hit -ne $script:hover -or $grip -ne $script:gripHover -or $round -ne $script:roundHover) {
        $script:hover = $hit
        $script:gripHover = $grip
        $script:roundHover = $round
        Request-Redraw
    }
})

$form.Add_MouseUp({
    try {
        $script:dragStart = $null
        if ($script:reorder) {
            if ($script:reorder.Moved) { Save-Order }
            $script:reorder = $null
            Update-View
            Request-Redraw
            return
        }
        if ($null -ne $script:pressedRound) {
            $pr = $script:pressedRound
            $script:pressedRound = $null
            if ((Get-HitRound $_.Location) -eq $pr) {
                $tip.Hide($form)
                Invoke-Round $pr
            }
            return
        }
        if ($null -ne $script:pressed -and (Get-HitRow $_.Location) -eq $script:pressed) {
            Switch-Slot $script:pressed
            Update-View
            Request-Redraw
        }
        $script:pressed = $null
    } catch {
        Write-Log "Click error: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"
    }
})

$form.Add_MouseLeave({
    if ($script:reorder) { return }
    if ($null -ne $script:hover -or $null -ne $script:gripHover -or $null -ne $script:roundHover) {
        $tip.Hide($form)
        $script:hover = $null
        $script:gripHover = $null
        $script:roundHover = $null
        $form.Cursor = [System.Windows.Forms.Cursors]::Default
        Request-Redraw
    }
})

# ---- Keeping it visible ----
function Keep-Visible {
    Update-Dpi
    if ($script:dialogOpen -or $menu.Visible -or $trayMenu.Visible) { return }
    $rect = New-Object System.Drawing.Rectangle($form.Location, $form.Size)
    $onScreen = $false
    foreach ($scr in [System.Windows.Forms.Screen]::AllScreens) {
        if ($scr.WorkingArea.IntersectsWith($rect)) { $onScreen = $true }
    }
    if (-not $onScreen) { Move-ToDefaultPosition }
    if (-not $form.Visible) { $form.Show() }
    $form.TopMost = $false
    $form.TopMost = $true
}

function Open-WeekFile {
    Flush-Pending
    Refresh-Totals
    Write-WeekSummary
    $f = Get-WeekFile
    if (Test-Path $f) { Invoke-Item $f }
}

# ---- Adding, hiding and renaming accounts ----
function Resize-Form {
    # Grow or shrink to fit the rows, keeping the bottom edge where it was
    $script:BaseH = $RowTop + $script:slots.Count * $RowH + $LunchGap + 6
    $bottom = $form.Bottom
    $form.ClientSize = New-Object System.Drawing.Size([int]($BaseW * $Zoom), [int]($script:BaseH * $Zoom))
    $form.Top = $bottom - $form.Height
    $script:hover = $null
    Request-Redraw
}

function Complete-AccountChange {
    Save-Accounts
    Rebuild-Slots
    Refresh-Totals
    Write-WeekSummary
    Resize-Form
    Update-View
}

function Add-Account {
    $name = Show-TextPrompt 'Add account' 'Account code or name:' ''
    if (-not $name) { return }
    if ($name -eq 'Lunch') { Show-Message 'Lunch already has its own row.'; return }
    $existing = Find-Account $name
    if ($existing -and -not $existing.Hidden) { Show-Message "$name is already on the list."; return }
    if ((Get-VisibleCount) -ge $MaxVisibleAccounts) {
        Show-Message "There are already $MaxVisibleAccounts accounts showing. Hide one first to make room."
        return
    }
    if ($existing) { $existing.Hidden = $false }
    else { $script:accounts += @{ Name = $name; Hidden = $false } }
    Complete-AccountChange
}

function Show-Account($name) {
    if ((Get-VisibleCount) -ge $MaxVisibleAccounts) {
        Show-Message "There are already $MaxVisibleAccounts accounts showing. Hide one first to make room."
        return
    }
    $acc = Find-Account $name
    if ($acc) { $acc.Hidden = $false; Complete-AccountChange }
}

function Hide-Account($name) {
    if ($name -eq 'Lunch') { return }
    if ($script:active -and $script:active.Name -eq $name) { Stop-Active (Get-Date) }
    $acc = Find-Account $name
    if ($acc) { $acc.Hidden = $true; Complete-AccountChange }
}

function Rename-Account($old) {
    if ($old -eq 'Lunch') { return }
    $new = Show-TextPrompt 'Rename account' 'Account code or name:' $old
    if (-not $new -or $new -ceq $old) { return }
    if ($new -eq 'Lunch') { Show-Message 'Lunch is reserved for the lunch row.'; return }
    $acc = Find-Account $old
    $clash = Find-Account $new
    if ($clash -and -not [object]::ReferenceEquals($clash, $acc)) {
        Show-Message "There's already an account called $new (it may be hidden)."
        return
    }
    $wasActive = [bool]($script:active -and $script:active.Name -eq $old)
    if ($wasActive) { Stop-Active (Get-Date) }
    $acc.Name = $new
    Complete-AccountChange
    if ($wasActive) {
        Start-Slot ([array]::IndexOf($script:slots, $new))
        Update-View
    }
}

# ---- Right-click menu ----
$menu = New-Object System.Windows.Forms.ContextMenuStrip

$miRename = $menu.Items.Add('Rename this account...')
$miRename.Add_Click({
    try {
        if ($null -ne $script:menuRow) { Rename-Account $script:slots[$script:menuRow] }
    } catch { Write-Log "Rename error: $($_.Exception.Message)" }
})

$miHide = $menu.Items.Add('Hide this account')
$miHide.Add_Click({
    try {
        if ($null -ne $script:menuRow) { Hide-Account $script:slots[$script:menuRow] }
    } catch { Write-Log "Hide error: $($_.Exception.Message)" }
})

$miSet = $menu.Items.Add("Set today's time...")
$miSet.Add_Click({
    try {
        if ($null -ne $script:menuRow) { Set-TodayTime $script:menuRow }
    } catch { Write-Log "Set time error: $($_.Exception.Message)" }
})

$miClear = $menu.Items.Add("Clear today's time")
$miClear.Add_Click({
    try {
        $i = $script:menuRow
        if ($null -eq $i) { return }
        $n = $script:slots[$i]
        if (-not (Ask-YesNo "Clear all of today's time for $n?")) { return }
        try {
            Clear-TodayFor $n
        } catch {
            Show-Message "Couldn't update sessions.csv. If it's open in Excel, close it and try again."
            return
        }
        if ($script:active -and $script:active.Name -eq $n) {
            $script:active.Start = Get-Date
            Save-State
        }
        Refresh-Totals
        Write-WeekSummary
        Update-View
        Request-Redraw
    } catch { Write-Log "Clear error: $($_.Exception.Message)" }
})

$miSep = New-Object System.Windows.Forms.ToolStripSeparator
[void]$menu.Items.Add($miSep)

[void]$menu.Items.Add('Add account...', $null, {
    try { Add-Account } catch { Write-Log "Add error: $($_.Exception.Message)" }
})

$miShow = New-Object System.Windows.Forms.ToolStripMenuItem 'Show hidden account'
[void]$menu.Items.Add($miShow)

[void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
[void]$menu.Items.Add("Open this week's timesheet", $null, { Open-WeekFile })
[void]$menu.Items.Add('Open timesheets folder', $null, { Invoke-Item $TimesheetDir })

$miTheme = New-Object System.Windows.Forms.ToolStripMenuItem 'Theme'

function Build-ThemeMenu {
    $miTheme.DropDownItems.Clear()
    $darkSoFar = $true
    foreach ($tn in $script:ThemeSets.Keys) {
        # Divider before the first light theme (one with dark text)
        $isLight = ([System.Drawing.ColorTranslator]::FromHtml($script:ThemeSets[$tn].Text)).GetBrightness() -lt 0.5
        if ($isLight -and $darkSoFar -and $miTheme.DropDownItems.Count -gt 0) {
            [void]$miTheme.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator))
        }
        if ($isLight) { $darkSoFar = $false }
        $ti = $miTheme.DropDownItems.Add($tn)
        $ti.Tag = $tn
        $ti.Checked = ($tn -eq $script:themeName)
        $ti.Add_Click({
            Apply-Theme $this.Tag
            Save-Theme
        })
    }
    [void]$miTheme.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator))
    [void]$miTheme.DropDownItems.Add('Edit themes file', $null, {
        if (-not (Test-Path $ThemesFile)) { Load-Themes }
        Invoke-Item $ThemesFile
    })
    [void]$miTheme.DropDownItems.Add('Reload themes', $null, {
        Load-Themes
        Apply-Theme $script:themeName
        Save-Theme
    })
}
[void]$menu.Items.Add($miTheme)

$miList = New-Object System.Windows.Forms.ToolStripMenuItem 'Account list transparency'
foreach ($ll in $ListLevels.Keys) {
    $li = $miList.DropDownItems.Add($ll)
    $li.Tag = $ll
    $li.Add_Click({ Set-ListTransparency $this.Tag })
}
[void]$miList.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator))
$miBlur = $miList.DropDownItems.Add('Blur behind (experimental)')
$miBlur.Add_Click({ Set-Blur (-not $script:blurOn) })
[void]$menu.Items.Add($miList)
[void]$menu.Items.Add('Reset position', $null, { Move-ToDefaultPosition })
[void]$menu.Items.Add('Close timer', $null, { $form.Close() })

$menu.Add_Opening({
    $pt = $form.PointToClient([System.Windows.Forms.Cursor]::Position)
    $script:menuRow = Get-HitRow $pt
    $isRow = $null -ne $script:menuRow
    $isAccount = $isRow -and $script:slots[$script:menuRow] -ne 'Lunch'
    $miRename.Visible = $isAccount
    $miHide.Visible = $isAccount
    $miClear.Visible = $isRow
    $miSet.Visible = $isRow
    $miSep.Visible = $isRow
    if ($isRow) { $miClear.Text = "Clear today's time for " + $script:slots[$script:menuRow] }

    # List hidden accounts so they can be brought back
    $miShow.DropDownItems.Clear()
    foreach ($acc in @($script:accounts | Where-Object { $_.Hidden })) {
        $item = $miShow.DropDownItems.Add($acc.Name)
        $item.Tag = $acc.Name
        $item.Add_Click({
            try { Show-Account $this.Tag } catch { Write-Log "Show error: $($_.Exception.Message)" }
        })
    }
    $miShow.Enabled = $miShow.DropDownItems.Count -gt 0

    Build-ThemeMenu
    $miList.Enabled = $script:layered
    foreach ($li in $miList.DropDownItems) {
        if ($li -is [System.Windows.Forms.ToolStripMenuItem] -and $li.Tag) { $li.Checked = ($li.Tag -eq $script:listLevel) }
    }
    $miBlur.Checked = $script:blurOn
    $miBlur.Enabled = ($script:listLevel -ne 'Off')
    Set-MenuScale $menu
})
$form.ContextMenuStrip = $menu

# ---- Tray icon ----
$tray = New-Object System.Windows.Forms.NotifyIcon
$tray.Icon = [System.Drawing.SystemIcons]::Application
$tray.Text = 'Account timer'
$trayMenu = New-Object System.Windows.Forms.ContextMenuStrip
[void]$trayMenu.Items.Add('Show timer', $null, { Keep-Visible; $form.Activate() })
[void]$trayMenu.Items.Add("Open this week's timesheet", $null, { Open-WeekFile })
[void]$trayMenu.Items.Add('Reset position', $null, { Move-ToDefaultPosition; Keep-Visible })
[void]$trayMenu.Items.Add('Close timer', $null, { $form.Close() })
$tray.ContextMenuStrip = $trayMenu
$trayMenu.Add_Opening({ Set-MenuScale $trayMenu })
$tray.Add_MouseClick({ if ($_.Button -eq 'Left') { Keep-Visible; $form.Activate() } })
$tray.Visible = $true

$form.Add_FormClosing({
    try { Stop-Active (Get-Date); Flush-Pending } catch {}
    $tray.Visible = $false
    $tray.Dispose()
    Write-Log 'Account timer closed normally.'
})

# ---- Refresh every second ----
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1000
$timer.Add_Tick({
    try {
        $now = Get-Date
        $gap = ($now - $script:lastTick).TotalMinutes
        if ($script:active -and $gap -gt $SleepGapMinutes) {
            # The computer was asleep, so don't count that time
            Write-Log "Paused '$($script:active.Name)' after a $([int]$gap) minute gap (computer asleep)."
            Stop-Active $script:lastTick
        } elseif ($script:lastTick.Date -ne $now.Date) {
            if ($script:active) { Stop-Active $now.Date } else { Refresh-Totals }
        }
        $script:lastTick = $now

        $script:tick++
        if ($script:tick % 2 -eq 0) { Keep-Visible }
        if ($script:tick % 60 -eq 0) {
            Save-State
            if ($script:pending.Count -gt 0) { Flush-Pending }
            if ($script:summaryDirty) { Write-WeekSummary }
        }

        Update-View
        Request-Redraw
    } catch {
        Write-Log "Refresh error: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"
    }
})

# ---- Start ----
try { Load-State } catch { Write-Log "Could not load state: $($_.Exception.Message)" }
Refresh-Totals
Write-WeekSummary
Write-Log 'Account timer started.'
Update-View
$savedList = 'Off'
if (Test-Path $ListAlphaFile) { try { $savedList = (Get-Content $ListAlphaFile -TotalCount 1).Trim() } catch {} }
$script:listLevel = if ($ListLevels.Contains($savedList)) { $savedList } else { 'Off' }
$script:listAlpha = $ListLevels[$script:listLevel]
$savedBlur = $false
if (Test-Path $BlurFile) { try { $savedBlur = ((Get-Content $BlurFile -TotalCount 1).Trim() -eq '1') } catch {} }
if ($savedBlur) { Set-Blur $true -Quiet }
Request-Redraw
$timer.Start()
[System.Windows.Forms.Application]::Run($form)
