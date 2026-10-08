# Account timer 2
# Same as the account timer, plus a compact pill view and a red 'not tracking' state.
# Shares its accounts, logs, themes and settings with AccountTimer.ps1, so only run one at a time.
#
# Compact view: click the collapse button (top right of the full view) or right-click, Compact view.
# On the pill, click the time to open the full view, the name to open the account list, the button to pause.
#
# Budgets: right-click an account, Set budget, to give it hours per week, month or quarter.
# Notes: after 15 minutes or more on an account, a small box asks what you worked on (optional).
# Weekly view: right-click, Weekly view, for hours by account and day, budgets and notes,
# plus a Time entries tab showing each stretch of time day by day, with consecutive stretches
# on the same account merged into one block (switchable). The entries file follows the same grouping.
# In the Time entries tab, click to select blocks (Ctrl+click for several) and right-click to merge,
# delete, change account, edit the time or note, or add an entry. Undo reverses the last change.
#
# Drawer: click the round button at the top left of the full timer (or right-click, Show drawer)
# to slide out this week's hours by account and day, and budgets, beside the timer.
# The Timeline tab shows one day like a calendar, with hours down the left and a card for each stretch of
# time, Lunch as a hatched gap and a line for now. Scroll with the mouse wheel, zoom with Ctrl and the wheel
# (or the plus and minus buttons), and use Now to jump back. Point at a card to see what it was; Previous and
# Next move between days. Drag the top or bottom edge of a card to change its time (5 minute steps, Shift
# for 1 minute, Escape to cancel), or click a gap to fill it with an account. Undo reverses the last change.
#
# Size: right-click, Appearance, Size makes the whole timer and drawer smaller or larger (remembered).
#
# Hours: right-click, Working hours, Hours and days, gives each working day its own start and finish
# (for shift patterns). From 30 minutes before the end of the day, a Finish day button appears under the
# Lunch row (and the menu has Finish for the day at any time once the day has started). It stops unallocated
# time and overtime counting until you carry on.
# Each week's individual entries are also saved to 'Week commencing <date> entries.csv'.
#
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

# Use Windows' modern text drawing for buttons and labels, as normal Windows apps do
try { [System.Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false) } catch {}

# Its own identity in Windows, so the taskbar shows 'Account timer' rather than grouping it with PowerShell
try {
    Add-Type -Namespace Win -Name Shell -MemberDefinition '[DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SetCurrentProcessExplicitAppUserModelID(string appId);'
    [void][Win.Shell]::SetCurrentProcessExplicitAppUserModelID('AccountTimer.Timesheets')
} catch {}

# Only one account timer at a time, so the same time isn't tracked twice
try {
    $others = @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe' OR Name='pwsh.exe'" |
        Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -match 'AccountTimer2?\.ps1' })
    if ($others.Count -gt 0) {
        [void][System.Windows.Forms.MessageBox]::Show(
            "The account timer is already running. Close it before opening another copy, so time isn't tracked twice.",
            'Account timer')
        exit
    }
} catch {}

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
    $k = (Get-CursorDpi) / 96 * [math]::Max(1.0, $Scale)    # menus follow the size setting when it is larger
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
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace Win {
    public static class Layered {
        // ---- Blur behind the window (experimental, undocumented Windows feature) ----
        [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)] struct CompositionData { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);
        [StructLayout(LayoutKind.Sequential)] struct BlurBehind { public int Flags; public bool Enable; public IntPtr Region; public bool Transition; }
        [DllImport("dwmapi.dll")] static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref BlurBehind bb);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        // Make Windows redo the window's frame and backdrop without moving or resizing it
        public static void RefreshFrame(IntPtr hwnd) {
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
        }
        public static void ClearAllBlur(IntPtr hwnd) {
            try { BlurBehind bb = new BlurBehind(); bb.Flags = 1; bb.Enable = false; DwmEnableBlurBehindWindow(hwnd, ref bb); } catch { }
            SetBlur(hwnd, false);
            RefreshFrame(hwnd);
        }
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
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
        // Cut the window to a rounded rectangle (radius 0 removes the cut). Windows takes ownership of the region.
        // Cut the window to an outline, slightly enlarged so the smooth edges drawn inside aren't clipped
        public static void SetPathRegion(IntPtr hwnd, GraphicsPath path) { SetPathRegion(hwnd, path, 4f); }
        public static void SetPathRegion(IntPtr hwnd, GraphicsPath path, float allowance) {
            Region r = new Region(path);
            using (GraphicsPath edge = (GraphicsPath)path.Clone())
            using (Pen pen = new Pen(Color.Black, allowance)) {
                edge.Widen(pen);
                r.Union(edge);
            }
            using (Graphics g = Graphics.FromHwnd(hwnd)) {
                IntPtr h = r.GetHrgn(g);
                SetWindowRgn(hwnd, h, true);
            }
            r.Dispose();
        }
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);
        // Let Windows move the window, as it does for normal windows (returns once the move ends)
        public static void StartMove(IntPtr hwnd) {
            ReleaseCapture();
            SendMessage(hwnd, 0xA1, (IntPtr)2, IntPtr.Zero);
        }
        public static void SetRoundRegion(IntPtr hwnd, int w, int h, int r) {
            IntPtr rgn = r > 0 ? CreateRoundRectRgn(0, 0, w + 1, h + 1, r * 2, r * 2) : IntPtr.Zero;
            SetWindowRgn(hwnd, rgn, true);
        }
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

# Faster drawing for the full view: a compiled C# component. If it can't be compiled here,
# the timer quietly carries on with its own (slower) PowerShell drawing.
$script:useRenderer = $false
$script:rendererError = $null
try {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Win {
    // One row of the timer, prepared by PowerShell once a second
    public class RowModel {
        public string Name = "";
        public string TodayText = "";
        public string WeekText = "";
        public double Today;
        public double Week;
        public bool Active;
        public bool IsLunch;
        public bool CanRound;
        public double? BudgetFrac;
        public string BudgetText;
    }

    public class DrawerRow {
        public string Name = "";
        public string Style = "normal";     // normal, bold or faint
        public string[] Cells = new string[0];
        public string Total = "";
    }

    public class DrawerBudget {
        public string Name = "";
        public double Frac;
        public string Text = "";
    }

    public class DrawerButton {
        public string Id;
        public RectangleF Rect;
    }

    // One stretch of time on the timeline strip (times are hours since midnight)
    public class TimelineBlock {
        public string Name = "";
        public double StartH, EndH;
        public string Kind = "time";        // time, running or lunch
        public Color Colour = Color.Gray;
        public string Tip = "";
        public bool Editable;               // false for what is running now, which can't be dragged
        public bool Manual;                 // added/edited time has a position but was not recorded live
        public int Lane, Lanes = 1;         // blocks that overlap sit side by side, this one in lane Lane of Lanes
    }

    // A stretch of the day with nothing tracked, which can be filled
    public class TimelineGap {
        public double StartH, EndH;
    }

    // One line under the strip: an account's time for the day, or Lunch, Total or Unallocated
    public class TimelineLegend {
        public string Name = "";
        public string Kind = "account";     // account, lunch, total or unallocated
        public Color Colour = Color.Gray;
        public string Total = "";
    }

    public class TimelineModel {
        public double AxisStartH, AxisEndH, WorkStartH, WorkEndH, NowH;
        public bool HasWork, ShowNow, HasUnplaced;  // legacy merged credits have no reliable clock position
        public string EmptyText = "";
        public List<TimelineBlock> Blocks = new List<TimelineBlock>();
        public List<TimelineGap> Gaps = new List<TimelineGap>();
        public List<TimelineLegend> Legend = new List<TimelineLegend>();
    }

    public class DrawerModel {
        public string Title = "";
        public string Mode = "week";        // week or timeline
        public string[] DayLabels = new string[0];
        public List<DrawerRow> Rows = new List<DrawerRow>();
        public List<DrawerBudget> Budgets = new List<DrawerBudget>();
        public string BudgetsHeading = "BUDGETS";
        public string FormatLabel = "h:mm";
        public bool NextEnabled, UndoEnabled, IsToday;
        public TimelineModel Timeline;
    }

    // Draws the full view (timer, drawer, shadow and rim) into one image with see-through areas
    public class FullRenderer {
        public Font FontDigits, FontLabel, FontRow, FontOvertime, FontButton, FontSmall, FontDrawer, FontDrawerBold;
        public Font FontTimeline, FontTimelineBold, FontTimelineSmall;   // the timeline's own, larger text
        public Color Panel, Circle, CircleHot, Fill;
        public Color Text = Color.White;
        public Color LunchBand = Color.FromArgb(45, 0, 0, 0);
        public Color BudgetWarn = Color.FromArgb(255, 0xff, 0xc1, 0x07);
        public Color BudgetOver = Color.FromArgb(255, 0xff, 0x52, 0x52);

        public float Zoom = 1f, BaseW = 320f, BaseH = 380f, RowTop = 100f, RowH = 30f, LunchGap = 10f, OvertimeH = 32f;
        public float CollapseX = 306f, CollapseY = 9f, CollapseR = 6.5f, DrawerBtnX = 12f, DrawerBtnY = 9f;
        public float RoundR = 8f, RoundDownX = 150f, RoundUpX = 170f;
        public float DrawerW = 520f, DrawerH = 0f, DrawerRowH = 19f;
        // Heights within the drawer, which the PowerShell side uses to work out how tall the drawer must be
        public const float TabsH = 26f;
        public float Margin = 10f, TimerOffX = 0f, TimerOffY = 0f;
        public int ListAlpha = 255;
        public float TextSharpen = 0.8f;
        public bool Shadow = true, Rim = true;

        public string H = "00", M = "00", S = "00", Status = "", Unallocated = "", OT = "";
        public bool Before, OnLunch, Overtime;
        public double Progress;
        public RowModel[] Rows = new RowModel[0];
        public int Hover = -1, GripHover = -1, DraggingRow = -1;
        public bool Reordering;
        public string RoundHover = "";
        public bool CollapseHover, HandleHover, DrawerOpen;
        // The Finish day (or Carry on) button under the Lunch row; its size and place match $DayBtn in PowerShell
        public string DayBtnText = "";
        public bool DayBtnHover;
        public float DayBtnW = 150f, DayBtnH = 22f;
        public DrawerModel Drawer;
        public string DrawerHover = "";
        // Timeline hover and drag state, set by PowerShell each frame. The strip's own position is
        // reported back so PowerShell can tell which time the pointer is over.
        public int TlHoverBlock = -1, TlHoverGap = -1, TlEdgeBlock = -1;
        public string TlEdge = "";          // start, end or join (the shared edge of two blocks that touch)
        public double TlSelStartH = -1, TlSelEndH = -1;
        public string TlDragText = "";
        public double TlDragAtH;
        public float TlX0, TlW, TlY, TlH;   // the timeline's cards column and viewport as last drawn, reported back
        public double TlScrollH;            // the hour at the top of the viewport
        public float TlPx = 64f;            // window units per hour (the zoom)
        public List<DrawerButton> DrawerButtons = new List<DrawerButton>();

        StringFormat sfCentre, sfLeft, sfRight, sfName;
        Graphics layerColour, layerMask;   // used for text over a see-through background

        public FullRenderer() {
            sfCentre = new StringFormat();
            sfCentre.Alignment = StringAlignment.Center; sfCentre.LineAlignment = StringAlignment.Center;
            sfLeft = new StringFormat();
            sfLeft.Alignment = StringAlignment.Near; sfLeft.LineAlignment = StringAlignment.Center;
            sfRight = new StringFormat();
            sfRight.Alignment = StringAlignment.Far; sfRight.LineAlignment = StringAlignment.Center;
            sfName = new StringFormat();
            sfName.Alignment = StringAlignment.Near; sfName.LineAlignment = StringAlignment.Center;
            sfName.Trimming = StringTrimming.EllipsisCharacter; sfName.FormatFlags = StringFormatFlags.NoWrap;
        }

        static Color A(int a, Color c) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B); }

        static Color Mix(Color a, Color b, double t) {
            return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath RoundRect(float x, float y, float w, float h, float r) {
            GraphicsPath p = new GraphicsPath();
            float d = 2 * r;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Outline of everything shown, in window units (including the margin left for the shadow)
        public GraphicsPath GetOutline() {
            if (!DrawerOpen) return RoundRect(Margin, Margin, BaseW, BaseH, 8f);
            // Timer and drawer side by side as one rounded rectangle; if the drawer is taller,
            // the space under the timer is filled with the panel colour, so there's never an empty corner
            return RoundRect(Margin, Margin, DrawerW + BaseW, Math.Max(DrawerH, BaseH), 8f);
        }

        // The earlier L-shaped outline (no longer used)
        public GraphicsPath GetLShapedOutline() {
            // Built for 'drawer on the left, timer on the right', then mirrored if the drawer is on the right
            float jx = DrawerW - 1, W = DrawerW + BaseW - 2, H1 = DrawerH - 2, H2 = BaseH - 2, d = 16f;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(0, 0, d, d, 180, 90);
            p.AddArc(W - d, 0, d, d, 270, 90);
            p.AddArc(W - d, H2 - d, d, d, 0, 90);
            if (H1 - H2 >= d) {
                p.AddArc(jx, H2, d, d, 270, -90);
                p.AddArc(jx - d, H1 - d, d, d, 0, 90);
            }
            p.AddArc(0, H1 - d, d, d, 90, 90);
            p.CloseFigure();
            if (TimerOffX == 0) {
                using (Matrix mx = new Matrix(-1, 0, 0, 1, W, 0)) { p.Transform(mx); }
            }
            using (Matrix mt = new Matrix()) { mt.Translate(1 + Margin, 1 + Margin); p.Transform(mt); }
            return p;
        }

        public Bitmap Render(int w, int h) {
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            Bitmap colourBmp = null, maskBmp = null;
            bool seeThrough = ListAlpha < 255;
            using (Graphics g = Graphics.FromImage(bmp))
            using (GraphicsPath outline = GetOutline()) {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.ScaleTransform(Zoom, Zoom);
                if (Shadow) DrawShadow(g, outline);

                float tx = Margin + TimerOffX, ty = Margin + TimerOffY;
                if (seeThrough) {
                    colourBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    maskBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    layerColour = Graphics.FromImage(colourBmp);
                    layerColour.ScaleTransform(Zoom, Zoom);
                    layerColour.TranslateTransform(tx, ty);
                    layerMask = Graphics.FromImage(maskBmp);
                    layerMask.Clear(Color.Transparent);
                    layerMask.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    layerMask.ScaleTransform(Zoom, Zoom);
                    layerMask.TranslateTransform(tx, ty);
                }
                try {
                    GraphicsState st = g.Save();
                    g.TranslateTransform(tx, ty);
                    using (GraphicsPath clip = (GraphicsPath)outline.Clone()) {
                        using (Matrix m = new Matrix()) { m.Translate(-tx, -ty); clip.Transform(m); }
                        DrawTimer(g, clip);
                    }
                    g.Restore(st);
                    if (DrawerOpen && Drawer != null) DrawDrawer(g, outline, w, h);
                    if (Rim) {
                        using (Pen rim = new Pen(A(50, Text), 1f)) { g.DrawPath(rim, outline); }
                    }
                } finally {
                    if (layerColour != null) { layerColour.Dispose(); layerColour = null; }
                    if (layerMask != null) { layerMask.Dispose(); layerMask = null; }
                }
            }
            if (seeThrough) {
                Combine(bmp, colourBmp, maskBmp, TextSharpen);
                colourBmp.Dispose();
                maskBmp.Dispose();
            }
            return bmp;
        }

        // Soft shadow around the outline: layered strokes, faintest at the outside, kept outside the shape
        void DrawShadow(Graphics g, GraphicsPath outline) {
            using (GraphicsPath p = (GraphicsPath)outline.Clone()) {
                using (Matrix m = new Matrix()) { m.Translate(0, 1.5f); p.Transform(m); }
                g.SetClip(outline, CombineMode.Exclude);
                for (int i = 6; i >= 1; i--) {
                    float sp = i * 1.4f;
                    int a = (int)(16.0 * (6 - i + 1) / 6.0);
                    using (Pen pen = new Pen(Color.FromArgb(a, 0, 0, 0), sp * 2)) {
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, p);
                    }
                }
                g.ResetClip();
            }
        }

        void FillArea(Graphics g, Brush b, float x, float y, float w, float h, GraphicsPath clip) {
            g.SetClip(new RectangleF(x, y, w, h));
            g.FillPath(b, clip);
            g.ResetClip();
        }

        // Text in the account list: drawn normally, or through the see-through text layers
        void ListText(Graphics g, string text, Font f, Color c, RectangleF r, StringFormat sf) {
            if (layerMask != null) {
                using (SolidBrush solid = new SolidBrush(Color.FromArgb(255, c.R, c.G, c.B))) { layerColour.FillRectangle(solid, r); }
                using (SolidBrush shape = new SolidBrush(Color.FromArgb(c.A, 255, 255, 255))) { layerMask.DrawString(text, f, shape, r, sf); }
            } else {
                using (SolidBrush b = new SolidBrush(c)) { g.DrawString(text, f, b, r, sf); }
            }
        }

        void DrawPlay(Graphics g, float cx, float cy) {
            using (SolidBrush b = new SolidBrush(Text)) {
                g.FillPolygon(b, new PointF[] { new PointF(cx - 3, cy - 5), new PointF(cx - 3, cy + 5), new PointF(cx + 5, cy) });
            }
        }

        void DrawPause(Graphics g, float cx, float cy) {
            using (SolidBrush b = new SolidBrush(Text)) {
                g.FillRectangle(b, cx - 4, cy - 5, 2.8f, 10);
                g.FillRectangle(b, cx + 1.2f, cy - 5, 2.8f, 10);
            }
        }

        void DrawLunch(Graphics g, float cx, float cy) {
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.ScaleTransform(0.7f, 0.7f);
            using (Pen pen = new Pen(Text, 1.6f))
            using (SolidBrush b = new SolidBrush(Text)) {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLine(pen, -4f, -1f, -4f, 7f);
                foreach (float tx in new float[] { -6.5f, -4f, -1.5f }) { g.DrawLine(pen, tx, -7f, tx, -3f); }
                g.DrawArc(pen, -6.5f, -5.5f, 5f, 4.5f, 0f, 180f);
                g.DrawLine(pen, 4.5f, 1f, 4.5f, 7f);
                g.FillPolygon(b, new PointF[] { new PointF(3, 1.5f), new PointF(3, -7), new PointF(6.3f, -3), new PointF(6.3f, 1.5f) });
            }
            g.Restore(st);
        }

        void DrawTimer(Graphics g, GraphicsPath clip) {
            Color faint = A(215, Text);
            float split = RowTop - 17;
            using (SolidBrush b = new SolidBrush(Panel)) { FillArea(g, b, 0, 0, BaseW, split, clip); }
            using (SolidBrush b = new SolidBrush(A(ListAlpha, Panel))) { FillArea(g, b, 0, split, BaseW, BaseH - split, clip); }

            using (SolidBrush textB = new SolidBrush(Text))
            using (SolidBrush faintB = new SolidBrush(faint))
            using (SolidBrush digitB = new SolidBrush(Before ? A(140, Text) : Text))
            using (Pen sep = new Pen(A(35, Text), 1f)) {
                // Week drawer button (top left) and compact view button (top right)
                // A more solid fill, a fine ring and bolder lines, so they stand out against the panel
                using (SolidBrush b = new SolidBrush((HandleHover || DrawerOpen) ? CircleHot : Circle))
                using (Pen ring = new Pen(A(70, Text), 1f)) {
                    g.FillEllipse(b, DrawerBtnX - CollapseR, DrawerBtnY - CollapseR, 2 * CollapseR, 2 * CollapseR);
                    g.DrawEllipse(ring, DrawerBtnX - CollapseR, DrawerBtnY - CollapseR, 2 * CollapseR, 2 * CollapseR);
                }
                using (Pen p = new Pen(Text, 1.6f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    foreach (float ly in new float[] { -2.6f, 0f, 2.6f }) { g.DrawLine(p, DrawerBtnX - 3, DrawerBtnY + ly, DrawerBtnX + 3, DrawerBtnY + ly); }
                }
                using (SolidBrush b = new SolidBrush(CollapseHover ? CircleHot : Circle))
                using (Pen ring = new Pen(A(70, Text), 1f)) {
                    g.FillEllipse(b, CollapseX - CollapseR, CollapseY - CollapseR, 2 * CollapseR, 2 * CollapseR);
                    g.DrawEllipse(ring, CollapseX - CollapseR, CollapseY - CollapseR, 2 * CollapseR, 2 * CollapseR);
                }
                using (Pen p = new Pen(Text, 1.6f)) {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawLine(p, CollapseX - 3, CollapseY, CollapseX + 3, CollapseY);
                }

                // Main timer
                string[] values = { H, M, S };
                float[] colX = { 0f, 107f, 213f };
                for (int i = 0; i < 3; i++) {
                    g.DrawString(values[i], FontDigits, digitB, new RectangleF(colX[i], 2, 107, 52), sfCentre);
                }
                foreach (float x in new float[] { 107f, 213f }) {
                    g.FillEllipse(faintB, x - 1.3f, 22, 2.6f, 2.6f);
                    g.FillEllipse(faintB, x - 1.3f, 33, 2.6f, 2.6f);
                }

                // Status and unallocated time
                using (SolidBrush sb = new SolidBrush(OnLunch ? Text : faint)) {
                    g.DrawString(Status, FontLabel, sb, new RectangleF(10, 51, 150, 14), sfLeft);
                }
                g.DrawString(Unallocated, FontLabel, faintB, new RectangleF(160, 51, 150, 14), sfRight);

                // Overtime counter
                if (Overtime) {
                    g.DrawLine(sep, 10, 68, 310, 68);
                    g.DrawString(OT, FontOvertime, textB, new RectangleF(0, 69, BaseW, OvertimeH - 2), sfCentre);
                }

                // Progress through the working day
                using (SolidBrush track = new SolidBrush(A(40, Text)))
                using (SolidBrush fb = new SolidBrush(Fill)) {
                    g.FillRectangle(track, 0, RowTop - 20, BaseW, 3);
                    g.FillRectangle(fb, 0, RowTop - 20, (float)(BaseW * Math.Min(1.0, Math.Max(0.0, Progress))), 3);
                }

                // Column headings
                ListText(g, "TODAY", FontLabel, faint, new RectangleF(180, RowTop - 14, 70, 14), sfRight);
                ListText(g, "WEEK", FontLabel, faint, new RectangleF(250, RowTop - 14, 60, 14), sfRight);

                // Rows
                for (int i = 0; i < Rows.Length; i++) {
                    RowModel r = Rows[i];
                    float y0 = RowTop + i * RowH + (r.IsLunch ? LunchGap : 0);
                    float cy = y0 + RowH / 2;

                    if (r.IsLunch) {
                        using (SolidBrush lb = new SolidBrush(LunchBand)) { FillArea(g, lb, 0, y0, BaseW, BaseH - y0, clip); }
                    }
                    bool dragging = DraggingRow == i;
                    int ha = dragging ? 60 : (r.Active ? 40 : (Hover == i ? 18 : 0));
                    if (ha > 0) {
                        using (SolidBrush hb = new SolidBrush(A(ha, Text))) { FillArea(g, hb, 0, y0, BaseW, RowH, clip); }
                    }
                    if (!r.IsLunch) {
                        g.DrawLine(sep, 10, y0, 310, y0);
                        using (SolidBrush gb = new SolidBrush((dragging || GripHover == i) ? Text : A(110, Text))) {
                            foreach (float gx in new float[] { 6f, 10f }) {
                                foreach (float gy in new float[] { -4f, 0f, 4f }) { g.FillEllipse(gb, gx - 1.1f, cy + gy - 1.1f, 2.2f, 2.2f); }
                            }
                        }
                    }

                    using (SolidBrush cb = new SolidBrush(r.Active ? CircleHot : Circle)) { g.FillEllipse(cb, 18, cy - 10, 20, 20); }
                    if (r.Active) DrawPause(g, 28, cy);
                    else if (r.IsLunch) DrawLunch(g, 28, cy);
                    else DrawPlay(g, 28, cy);

                    // Names use the full width, and only make room while the rounding buttons are showing
                    bool showRound = r.CanRound && Hover == i && !Reordering;
                    float nameW = 88f;
                    if (!showRound) {
                        float tw = g.MeasureString(r.TodayText, FontRow).Width;
                        nameW = Math.Max(88f, 250f - tw - 8f - 46f);
                    }
                    float nameY = r.BudgetFrac.HasValue ? y0 - 3 : y0;
                    ListText(g, r.Name, FontRow, Text, new RectangleF(46, nameY, nameW, RowH), sfName);

                    // Budget progress: a fine line under the name, amber from 80%, red once over
                    if (r.BudgetFrac.HasValue) {
                        double frac = r.BudgetFrac.Value;
                        float barW = 60f, by = y0 + RowH - 7;
                        using (SolidBrush track = new SolidBrush(A(40, Text))) { g.FillRectangle(track, 46, by, barW, 3); }
                        Color bc = frac >= 1 ? BudgetOver : (frac >= 0.8 ? BudgetWarn : Fill);
                        float fillW = (float)(barW * Math.Min(1.0, frac));
                        if (frac > 0 && fillW < 2) fillW = 2;
                        using (SolidBrush bb = new SolidBrush(bc)) { g.FillRectangle(bb, 46, by, fillW, 3); }
                    }
                    ListText(g, r.TodayText, FontRow, Text, new RectangleF(180, y0, 70, RowH), sfRight);
                    ListText(g, r.WeekText, FontLabel, faint, new RectangleF(250, y0, 60, RowH), sfRight);

                    // Round-down and round-up buttons
                    if (showRound) {
                        for (int k = 0; k < 2; k++) {
                            string dir = k == 0 ? "down" : "up";
                            float bx = k == 0 ? RoundDownX : RoundUpX;
                            bool hot = RoundHover == (i.ToString() + ":" + dir);
                            using (SolidBrush rb = new SolidBrush(hot ? CircleHot : Circle)) {
                                g.FillEllipse(rb, bx - RoundR, cy - RoundR, 2 * RoundR, 2 * RoundR);
                            }
                            float tipY = dir == "up" ? -1.8f : 1.8f;
                            using (Pen cp = new Pen(Text, 1.5f)) {
                                cp.StartCap = LineCap.Round; cp.EndCap = LineCap.Round; cp.LineJoin = LineJoin.Round;
                                g.DrawLines(cp, new PointF[] { new PointF(bx - 3.5f, cy - tipY), new PointF(bx, cy + tipY), new PointF(bx + 3.5f, cy - tipY) });
                            }
                        }
                    }
                }

                // Finish day button, centred under the Lunch row
                if (DayBtnText.Length > 0) {
                    RectangleF dbr = new RectangleF((BaseW - DayBtnW) / 2f, BaseH - DayBtnH - 8f, DayBtnW, DayBtnH);
                    using (GraphicsPath dbp = RoundRect(dbr.X, dbr.Y, dbr.Width, dbr.Height, dbr.Height / 2f))
                    using (SolidBrush dbb = new SolidBrush(DayBtnHover ? CircleHot : Circle)) { g.FillPath(dbb, dbp); }
                    ListText(g, DayBtnText, FontLabel, Text, dbr, sfCentre);
                }
            }
        }

        // ---- Windows' own (GDI) text drawing, used for the drawer's solid layer ----
        struct GdiText { public string S; public Font F; public Color C; public RectangleF R; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] static extern int SetTextColor(IntPtr hdc, int color);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DrawTextW(IntPtr hdc, string text, int len, ref RECT r, uint format);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontIndirectW([In] LOGFONT lf);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class LOGFONT {
            public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
            public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
        }
        const byte ANTIALIASED_QUALITY = 4;   // smooth edges in plain shades, no ClearType colour fringes
        const uint DT_LEFT = 0, DT_CENTER = 1, DT_RIGHT = 2, DT_VCENTER = 4, DT_SINGLELINE = 0x20, DT_NOPREFIX = 0x800, DT_END_ELLIPSIS = 0x8000;
        List<GdiText> gdiQueue = new List<GdiText>();
        // Timeline labels use smoother greyscale text on the opaque drawer layer.
        List<GdiText> smoothQueue = new List<GdiText>();
        Dictionary<Font, Font> zoomFonts = new Dictionary<Font, Font>();
        Dictionary<Font, IntPtr> hfonts = new Dictionary<Font, IntPtr>();
        float fontZoom = 0f;

        // Fonts at the screen's scaling (Windows' text drawing works in screen pixels)
        Font ZoomFont(Font f) {
            if (fontZoom != Zoom) {
                foreach (IntPtr hf in hfonts.Values) DeleteObject(hf);
                hfonts.Clear();
                foreach (Font zf in zoomFonts.Values) zf.Dispose();
                zoomFonts.Clear();
                fontZoom = Zoom;
            }
            Font z;
            if (!zoomFonts.TryGetValue(f, out z)) {
                z = new Font(f.FontFamily, f.Size * Zoom, f.Style, GraphicsUnit.Pixel);
                zoomFonts[f] = z;
            }
            return z;
        }

        // Windows' text drawing, placed neatly on the pixel grid, with plain-shade smoothing instead of ClearType
        IntPtr HFont(Font z) {
            IntPtr h;
            if (!hfonts.TryGetValue(z, out h)) {
                LOGFONT lf = new LOGFONT();
                z.ToLogFont(lf);
                lf.lfQuality = ANTIALIASED_QUALITY;
                h = CreateFontIndirectW(lf);
                if (h == IntPtr.Zero) h = z.ToHfont();
                hfonts[z] = h;
            }
            return h;
        }

        void QueueText(string s, Font f, Color c, RectangleF r, uint align) {
            GdiText t = new GdiText();
            t.S = s == null ? "" : s; t.F = f; t.C = c; t.R = r;
            t.Flags = align | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS;
            gdiQueue.Add(t);
        }

        // Put timeline labels on a separate antialiased text path, leaving the rest
        // of the drawer's Windows text drawing unchanged.
        void QueueSmoothText(string s, Font f, Color c, RectangleF r, uint align) {
            GdiText t = new GdiText();
            t.S = s == null ? "" : s; t.F = f; t.C = c; t.R = r; t.Flags = align;
            smoothQueue.Add(t);
        }

        // Draw all queued text, after the shapes on the same solid layer
        void FlushText(Graphics og) {
            IntPtr hdc = og.GetHdc();
            try {
                SetBkMode(hdc, 1);
                IntPtr original = IntPtr.Zero;
                foreach (GdiText t in gdiQueue) {
                    IntPtr prev = SelectObject(hdc, HFont(ZoomFont(t.F)));
                    if (original == IntPtr.Zero) original = prev;
                    SetTextColor(hdc, t.C.R | (t.C.G << 8) | (t.C.B << 16));
                    RECT rc;
                    rc.Left = (int)Math.Round(t.R.X * Zoom); rc.Top = (int)Math.Round(t.R.Y * Zoom);
                    rc.Right = (int)Math.Round(t.R.Right * Zoom); rc.Bottom = (int)Math.Round(t.R.Bottom * Zoom);
                    DrawTextW(hdc, t.S, t.S.Length, ref rc, t.Flags);
                }
                if (original != IntPtr.Zero) SelectObject(hdc, original);
            } finally {
                og.ReleaseHdc(hdc);
                gdiQueue.Clear();
            }
            if (smoothQueue.Count == 0) return;
            TextRenderingHint oldHint = og.TextRenderingHint;
            try {
                og.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (StringFormat sf = new StringFormat()) {
                    sf.FormatFlags = StringFormatFlags.NoWrap;
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    sf.LineAlignment = StringAlignment.Center;
                    foreach (GdiText t in smoothQueue) {
                        sf.Alignment = (t.Flags & DT_CENTER) != 0 ? StringAlignment.Center :
                                       ((t.Flags & DT_RIGHT) != 0 ? StringAlignment.Far : StringAlignment.Near);
                        using (SolidBrush brush = new SolidBrush(t.C)) {
                            og.DrawString(t.S, t.F, brush, t.R, sf);
                        }
                    }
                }
            } finally {
                og.TextRenderingHint = oldHint;
                smoothQueue.Clear();
            }
        }

        void DrawDrawer(Graphics g, GraphicsPath outline, int w, int h) {
            float mg = Margin, dW = DrawerW;
            float dx = mg + (TimerOffX > 0 ? 0 : BaseW);
            float colH = Math.Max(DrawerH, BaseH);

            // Background on the main image, so the rounded corners stay smooth
            using (SolidBrush bg = new SolidBrush(Panel)) {
                g.SetClip(outline);
                g.SetClip(new RectangleF(mg + TimerOffX, mg + TimerOffY, BaseW, BaseH), CombineMode.Exclude);
                g.FillRectangle(bg, 0, 0, 2 * mg + dW + BaseW, 2 * mg + colH);
                g.ResetClip();
            }

            // Contents on a solid layer, where Windows' own text drawing can be used
            using (Bitmap layer = new Bitmap(w, h, PixelFormat.Format24bppRgb)) {
                using (Graphics og = Graphics.FromImage(layer)) {
                    og.Clear(Panel);
                    og.SmoothingMode = SmoothingMode.AntiAlias;
                    og.ScaleTransform(Zoom, Zoom);
                    DrawDrawerContents(og, dx, mg, dW);
                    FlushText(og);
                }
                // Copied in pixel for pixel, kept just inside the drawer's rounded edges
                GraphicsState st = g.Save();
                g.ResetTransform();
                g.SetClip(new RectangleF((dx + 4) * Zoom, (mg + 4) * Zoom, (dW - 8) * Zoom, (colH - 8) * Zoom));
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(layer, new Rectangle(0, 0, w, h));
                g.Restore(st);
            }

            using (Pen sep = new Pen(A(35, Text), 1f)) {
                float seamX = mg + (TimerOffX > 0 ? dW : BaseW);
                g.DrawLine(sep, seamX, mg + 12, seamX, mg + BaseH - 12);
            }
        }

        void DrawDrawerContents(Graphics og, float dx, float mg, float dW) {
            DrawerModel d = Drawer;
            Font fText = FontDrawer ?? FontSmall;
            Font fBold = FontDrawerBold ?? FontButton;
            Color textC = Text;
            Color faintC = Mix(Panel, Text, 215 / 255.0);
            // Lines and bars are drawn as solid colours, as the layer has no see-through areas
            using (Pen sep = new Pen(Mix(Panel, Text, 35 / 255.0), 1f)) {
                float pad = 14f, x0 = dx + pad, xR = dx + dW - pad, y = mg + 10;
                bool timeline = d.Mode == "timeline" && d.Timeline != null;
                QueueText(d.Title, FontRow, textC, new RectangleF(x0, y, timeline ? 200 : 170, 24), DT_LEFT);

                // Buttons, right to left
                DrawerButtons.Clear();
                string[][] defs = timeline
                    ? new string[][] {
                        new string[] { "undo", "Undo" }, new string[] { "format", d.FormatLabel },
                        new string[] { "next", "Next" }, new string[] { "prev", "Previous" } }
                    : new string[][] {
                        new string[] { "weekly", "Weekly view" }, new string[] { "copy", "Copy" },
                        new string[] { "format", d.FormatLabel },
                        new string[] { "next", "Next" }, new string[] { "prev", "Previous" } };
                float bx = xR;
                foreach (string[] def in defs) {
                    float tw = og.MeasureString(def[1], FontButton).Width + 16;
                    bx -= tw;
                    RectangleF rect = new RectangleF(bx, y + 1, tw, 22);
                    bool enabled = !(def[0] == "next" && !d.NextEnabled) && !(def[0] == "undo" && !d.UndoEnabled);
                    Color col = !enabled ? Mix(Panel, Circle, 0.45) : (DrawerHover == def[0] ? CircleHot : Circle);
                    using (GraphicsPath bp = RoundRect(rect.X, rect.Y, rect.Width, rect.Height, 11f))
                    using (SolidBrush bb = new SolidBrush(col)) { og.FillPath(bb, bp); }
                    QueueText(def[1], FontButton, enabled ? textC : Mix(col, Text, 0.5), rect, DT_CENTER);
                    if (enabled) {
                        DrawerButton db = new DrawerButton();
                        db.Id = def[0]; db.Rect = rect;
                        DrawerButtons.Add(db);
                    }
                    bx -= 6;
                }
                y += 34;

                // Tabs for switching between the week's table and the day's timeline
                float tabX = x0;
                string[][] tabs = new string[][] { new string[] { "timeline", "Timeline" }, new string[] { "week", "Week" } };
                foreach (string[] tab in tabs) {
                    float tw = og.MeasureString(tab[1], FontButton).Width + 20;
                    RectangleF rect = new RectangleF(tabX, y, tw, 22);
                    bool current = d.Mode == tab[0];
                    Color col = current ? CircleHot : (DrawerHover == tab[0] ? Circle : Mix(Panel, Circle, 0.45));
                    using (GraphicsPath bp = RoundRect(rect.X, rect.Y, rect.Width, rect.Height, 11f))
                    using (SolidBrush bb = new SolidBrush(col)) { og.FillPath(bb, bp); }
                    QueueText(tab[1], FontButton, textC, rect, DT_CENTER);
                    if (!current) {
                        DrawerButton db = new DrawerButton();
                        db.Id = tab[0]; db.Rect = rect;
                        DrawerButtons.Add(db);
                    }
                    tabX += tw + 6;
                }
                // Zoom and jump-to-now, right-aligned in the same row
                if (timeline) {
                    float zx = xR;
                    string[][] tools = new string[][] { new string[] { "now", "Now" }, new string[] { "zoomin", "+" }, new string[] { "zoomout", "-" } };
                    foreach (string[] tool in tools) {
                        float tw = Math.Max(22f, og.MeasureString(tool[1], FontButton).Width + 16);
                        zx -= tw;
                        RectangleF rect = new RectangleF(zx, y, tw, 22);
                        bool enabled = !(tool[0] == "now" && !d.IsToday);
                        Color col = !enabled ? Mix(Panel, Circle, 0.45) : (DrawerHover == tool[0] ? CircleHot : Circle);
                        using (GraphicsPath bp = RoundRect(rect.X, rect.Y, rect.Width, rect.Height, 11f))
                        using (SolidBrush bb = new SolidBrush(col)) { og.FillPath(bb, bp); }
                        QueueText(tool[1], FontButton, enabled ? textC : Mix(col, Text, 0.5), rect, DT_CENTER);
                        if (enabled) {
                            DrawerButton db = new DrawerButton();
                            db.Id = tool[0]; db.Rect = rect;
                            DrawerButtons.Add(db);
                        }
                        zx -= 6;
                    }
                }
                y += TabsH;

                if (timeline) { DrawTimeline(og, d.Timeline, x0, xR, y, sep, fText, fBold); return; }

                // Hours by account and day
                int nd = d.DayLabels.Length;
                float nameW = 140f, tableW = dW - 2 * pad, colW = (tableW - nameW) / (nd + 1), rowH = DrawerRowH;
                QueueText("ACCOUNT", FontLabel, faintC, new RectangleF(x0, y, nameW, rowH), DT_LEFT);
                for (int j = 0; j < nd; j++) {
                    QueueText(d.DayLabels[j], FontLabel, faintC, new RectangleF(x0 + nameW + j * colW, y, colW - 4, rowH), DT_RIGHT);
                }
                QueueText("TOTAL", FontLabel, faintC, new RectangleF(x0 + nameW + nd * colW, y, colW - 4, rowH), DT_RIGHT);
                y += rowH;
                foreach (DrawerRow tr in d.Rows) {
                    og.DrawLine(sep, x0, y, xR, y);
                    Font f = tr.Style == "bold" ? fBold : fText;
                    Color c = tr.Style == "faint" ? faintC : textC;
                    QueueText(tr.Name, f, c, new RectangleF(x0, y, nameW - 6, rowH), DT_LEFT);
                    for (int j = 0; j < tr.Cells.Length && j < nd; j++) {
                        QueueText(tr.Cells[j], f, c, new RectangleF(x0 + nameW + j * colW, y, colW - 4, rowH), DT_RIGHT);
                    }
                    QueueText(tr.Total, fBold, c, new RectangleF(x0 + nameW + nd * colW, y, colW - 4, rowH), DT_RIGHT);
                    y += rowH;
                }

                // Budgets
                if (d.Budgets.Count > 0) {
                    y += 8;
                    QueueText(d.BudgetsHeading, FontLabel, faintC, new RectangleF(x0, y, 260, 16), DT_LEFT);
                    y += 18;
                    foreach (DrawerBudget bl in d.Budgets) {
                        QueueText(bl.Name, fText, textC, new RectangleF(x0, y, nameW - 6, 20), DT_LEFT);
                        float barX = x0 + nameW, barY = y + 9;
                        using (SolidBrush track = new SolidBrush(Mix(Panel, Text, 40 / 255.0))) { og.FillRectangle(track, barX, barY, 140, 3); }
                        Color bc = bl.Frac >= 1 ? BudgetOver : (bl.Frac >= 0.8 ? BudgetWarn : Fill);
                        float fillW = (float)(140 * Math.Min(1.0, bl.Frac));
                        if (bl.Frac > 0 && fillW < 2) fillW = 2;
                        using (SolidBrush bb = new SolidBrush(bc)) { og.FillRectangle(bb, barX, barY, fillW, 3); }
                        QueueText(bl.Text, fText, faintC, new RectangleF(barX + 150, y, tableW - nameW - 150, 20), DT_RIGHT);
                        y += 20;
                    }
                }
            }
        }

        // The day as a vertical timeline, like a calendar day: hours run down the left, each stretch of time is a
        // card, Lunch is hatched, gaps are left empty, working hours are a little lighter and a line shows where
        // now is. Only the part in the viewport is drawn. PowerShell holds the scroll position (TlScrollH, in
        // hours) and the zoom (TlPx, window units per hour), and works out hover and dragging from the viewport
        // reported back in TlX0, TlW, TlY and TlH.
        const float GutterW = 48f, LegendW = 132f;
        public const float TlRowH = 22f;

        static string Clock(double h) {
            int m = (int)Math.Floor(h * 60 + 0.000001);
            return (m / 60).ToString("00") + ":" + (m % 60).ToString("00");
        }

        static string Dur(double hours) {
            int m = (int)Math.Floor(hours * 60 + 0.000001);
            return (m / 60).ToString() + ":" + (m % 60).ToString("00");
        }

        // Windows draws the queued text afterwards with no clip, so only text that sits wholly inside the viewport is queued
        void QueueIn(RectangleF vp, string s, Font f, Color c, RectangleF r, uint align) {
            if (r.Top < vp.Top - 0.5f || r.Bottom > vp.Bottom + 0.5f) return;
            QueueSmoothText(s, f, c, r, align);
        }

        static double LinearChannel(byte value) {
            double v = value / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color c) {
            return 0.2126 * LinearChannel(c.R) + 0.7152 * LinearChannel(c.G) +
                   0.0722 * LinearChannel(c.B);
        }
        static double Contrast(Color a, Color b) {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        // Prefer the selected theme's text colour when readable, otherwise choose
        // a light or dark foreground with sufficient contrast on the account colour.
        static Color CardTextColour(Color card, Color themeText) {
            if (Contrast(card, themeText) >= 4.5) return themeText;
            Color dark = Color.FromArgb(20, 20, 20);
            Color light = Color.White;
            return Contrast(card, dark) >= Contrast(card, light) ? dark : light;
        }

        void DrawTimeline(Graphics og, TimelineModel t, float x0, float xR, float y, Pen sep, Font fText, Font fBold) {
            Color textC = Text;
            Color faintC = Mix(Panel, Text, 215 / 255.0);
            // The timeline's own, larger fonts (the drawer's regular ones if none were given)
            Font fT = FontTimeline ?? fText, fTB = FontTimelineBold ?? fBold, fTS = FontTimelineSmall ?? fText;
            float vh = Math.Max(DrawerH, BaseH) - (y - Margin) - 12f;
            float bx = x0 + GutterW, bw = (xR - LegendW - 10f) - bx;
            float lx = xR - LegendW;
            RectangleF vp = new RectangleF(x0, y, bx + bw - x0, vh);
            TlX0 = bx; TlW = bw; TlY = y; TlH = vh;
            float px = TlPx;
            double scroll = TlScrollH;
            double viewH = vh / px;
            Func<double, float> yOf = delegate(double h) { return y + (float)((h - scroll) * px); };

            // The strip of the day: empty track, lighter working hours, then hour and half hour lines
            using (SolidBrush trackBrush = new SolidBrush(Mix(Panel, Text, 0.07))) { og.FillRectangle(trackBrush, bx, y, bw, vh); }
            GraphicsState clipState = og.Save();
            og.SetClip(new RectangleF(bx, y, bw, vh));
            if (t.HasWork) {
                float w1 = yOf(t.WorkStartH), w2 = yOf(t.WorkEndH);
                using (SolidBrush workBrush = new SolidBrush(Mix(Panel, Text, 0.14))) { og.FillRectangle(workBrush, bx, w1, bw, w2 - w1); }
            }
            int stepMin = px >= 200f ? 15 : (px >= 100f ? 30 : 60);
            using (Pen hourLine = new Pen(Mix(Panel, Text, 0.22), 1f))
            using (Pen minorLine = new Pen(Mix(Panel, Text, 0.12), 1f)) {
                for (int mm = (int)(Math.Floor(scroll) * 60); mm <= (int)(Math.Ceiling(scroll + viewH) * 60); mm += 15) {
                    if (mm < 0) continue;
                    float ly = yOf(mm / 60.0);
                    if (mm % 60 == 0) og.DrawLine(hourLine, bx, ly, bx + bw, ly);
                    else if (mm % stepMin == 0 || px >= 200f) og.DrawLine(minorLine, bx, ly, bx + bw, ly);
                }
            }

            // A gap under the pointer, or being filled
            for (int gi = 0; gi < t.Gaps.Count; gi++) {
                if (gi != TlHoverGap) continue;
                TimelineGap gp = t.Gaps[gi];
                float g1 = yOf(gp.StartH), g2 = yOf(gp.EndH);
                using (SolidBrush gb = new SolidBrush(Mix(Panel, Text, 0.24))) { og.FillRectangle(gb, bx + 3, g1 + 1, bw - 6, Math.Max(2f, g2 - g1 - 2)); }
                float vis1 = Math.Max(g1, y), vis2 = Math.Min(g2, y + vh);
                if (vis2 - vis1 >= 18f && TlSelStartH < 0) {
                    QueueIn(vp, "+ Allocate time", fT, textC,
                            new RectangleF(bx, (vis1 + vis2) / 2f - 10f, bw, 20f), DT_CENTER);
                }
            }
            if (TlSelStartH >= 0 && TlSelEndH > TlSelStartH) {
                float s1 = yOf(TlSelStartH), s2 = yOf(TlSelEndH);
                using (SolidBrush sb = new SolidBrush(A(150, Fill))) { og.FillRectangle(sb, bx + 3, s1, bw - 6, Math.Max(2f, s2 - s1)); }
                using (Pen sp = new Pen(Fill, 1f)) { og.DrawRectangle(sp, bx + 3, s1, bw - 7, Math.Max(2f, s2 - s1)); }
            }

            // Cards
            for (int i = 0; i < t.Blocks.Count; i++) {
                TimelineBlock b = t.Blocks[i];
                float top = yOf(b.StartH), bot = yOf(b.EndH);
                if (bot < y || top > y + vh) continue;
                // Overlapping stretches share the width, side by side
                int lanes = Math.Max(1, b.Lanes);
                float laneW = (bw - 6f) / lanes;
                RectangleF br = new RectangleF(bx + 3 + b.Lane * laneW, top + 1, laneW - (lanes > 1 ? 2f : 0f), Math.Max(2f, bot - top - 1.5f));
                bool hot = TlHoverBlock == i || TlEdgeBlock == i;
                bool lunch = b.Kind == "lunch";
                Color fillC = hot ? Mix(b.Colour, Color.White, 0.3) : b.Colour;
                if (lunch) {
                    using (HatchBrush hb = new HatchBrush(HatchStyle.WideUpwardDiagonal, Mix(Panel, Text, hot ? 0.45 : 0.33), Mix(Panel, Text, 0.12))) { og.FillRectangle(hb, br); }
                } else {
                    using (SolidBrush bb = new SolidBrush(fillC)) { og.FillRectangle(bb, br); }
                }
                // The label stays at the top of the part of the card that is in view
                float lineTop = Math.Max(br.Top + 1f, y + 1f);
                float room = Math.Min(br.Bottom, y + vh) - lineTop;
                string name = lunch ? "Lunch" : b.Name;
                Color labelC = lunch ? textC : CardTextColour(fillC, textC);
                if (b.Manual && !lunch) {
                    // An entered or edited allocation has a stated time, not a live-observed span.
                    using (Pen manualEdge = new Pen(A(180, labelC), 1.5f)) {
                        manualEdge.DashStyle = DashStyle.Dash;
                        og.DrawRectangle(manualEdge, br.X + 1f, br.Y + 1f,
                                         Math.Max(1f, br.Width - 2f), Math.Max(1f, br.Height - 2f));
                    }
                }
                double len = b.EndH - b.StartH;
                if (br.Height >= 40f && room >= 38f && br.Width >= 90f) {
                    QueueIn(vp, name, fTB, labelC, new RectangleF(br.X + 6, lineTop, br.Width - 12, 18f), DT_LEFT);
                    QueueIn(vp, Clock(b.StartH) + " to " + Clock(b.EndH) + "   " + Dur(len), fT, labelC, new RectangleF(br.X + 6, lineTop + 18f, br.Width - 12, 18f), DT_LEFT);
                } else if (br.Height >= 17f && room >= 17f) {
                    bool wide = br.Width >= 130f;
                    QueueIn(vp, name, fT, labelC, new RectangleF(br.X + 6, lineTop, br.Width - (wide ? 68f : 12f), 17f), DT_LEFT);
                    if (wide) QueueIn(vp, Dur(len), fT, labelC, new RectangleF(br.Right - 60, lineTop, 54, 17f), DT_RIGHT);
                }
                // Grips at both edges of a card that can be dragged, shown while it is pointed at
                if (hot && b.Editable && br.Height > 12f) {
                    using (Pen gp = new Pen(A(200, labelC), 2f)) {
                        float gcx = br.X + br.Width / 2f, gw = Math.Min(10f, br.Width / 3f);
                        og.DrawLine(gp, gcx - gw, br.Top + 3f, gcx + gw, br.Top + 3f);
                        og.DrawLine(gp, gcx - gw, br.Bottom - 3f, gcx + gw, br.Bottom - 3f);
                    }
                }
            }
            // The edge being pointed at or dragged
            if (TlEdgeBlock >= 0 && TlEdgeBlock < t.Blocks.Count) {
                TimelineBlock eb = t.Blocks[TlEdgeBlock];
                double edgeH = TlEdge == "start" ? eb.StartH : eb.EndH;
                float ey = yOf(edgeH);
                int elanes = Math.Max(1, eb.Lanes);
                float elaneW = (bw - 6f) / elanes;
                using (Pen ep = new Pen(textC, 3f)) { og.DrawLine(ep, bx + 3 + eb.Lane * elaneW, ey, bx + 3 + (eb.Lane + 1) * elaneW - (elanes > 1 ? 2f : 0f), ey); }
            }
            // Now
            if (t.ShowNow) {
                float ny = yOf(t.NowH);
                if (ny >= y && ny <= y + vh) {
                    using (Pen np = new Pen(textC, 2f))
                    using (SolidBrush nb = new SolidBrush(textC)) {
                        og.DrawLine(np, bx - 5f, ny, bx + bw, ny);
                        og.FillEllipse(nb, bx - 9f, ny - 3.5f, 7f, 7f);
                    }
                }
            }
            og.Restore(clipState);

            // Hour labels down the left
            int labelStep = px >= 200f ? 15 : (px >= 100f ? 30 : 60);
            for (int mm = (int)(Math.Floor(scroll) * 60); mm <= (int)(Math.Ceiling(scroll + viewH) * 60); mm += labelStep) {
                if (mm < 0 || mm > 24 * 60) continue;
                float ly = yOf(mm / 60.0);
                QueueIn(vp, Clock(mm / 60.0), fTS, mm % 60 == 0 ? textC : faintC, new RectangleF(x0, ly - 8f, GutterW - 6f, 16f), DT_RIGHT);
            }
            if (t.Blocks.Count == 0 && t.EmptyText.Length > 0 && TlHoverGap < 0 && TlSelStartH < 0) {
                QueueIn(vp, t.EmptyText, fT, faintC, new RectangleF(bx, y + vh / 2f - 10f, bw, 20f), DT_CENTER);
            }

            // A thin scroll bar when there is more of the day than fits
            double span = Math.Max(0.5, t.AxisEndH - t.AxisStartH);
            if (span * px > vh + 1f) {
                float thumbH = Math.Max(16f, (float)(vh * vh / (span * px)));
                float thumbY = y + (float)((scroll - t.AxisStartH) / span * vh);
                thumbY = Math.Max(y, Math.Min(y + vh - thumbH, thumbY));
                using (SolidBrush tb = new SolidBrush(Mix(Panel, Text, 0.30))) { og.FillRectangle(tb, bx + bw + 3f, thumbY, 3f, thumbH); }
            }

            // The time being dragged to, in a label across the edge
            if (TlDragText.Length > 0) {
                float dy = yOf(TlDragAtH);
                float lw = Math.Min(bw - 8f, og.MeasureString(TlDragText, fTS).Width + 20f);
                RectangleF lr = new RectangleF(bx + bw - lw - 6f, dy - 9f, lw, 18f);
                if (lr.Top >= y && lr.Bottom <= y + vh) {
                    using (GraphicsPath lp = RoundRect(lr.X, lr.Y, lr.Width, lr.Height, 9f))
                    using (SolidBrush lb = new SolidBrush(CircleHot)) { og.FillPath(lb, lp); }
                    QueueSmoothText(TlDragText, fTS, textC, lr, DT_CENTER);
                }
            }

            // The day's figures in a column on the right
            float gy = y;
            foreach (TimelineLegend lg in t.Legend) {
                og.DrawLine(sep, lx, gy, xR, gy);
                bool isTotal = lg.Kind == "total";
                Font f = isTotal ? fTB : fT;
                Color c = (lg.Kind == "lunch" || lg.Kind == "unallocated") ? faintC : textC;
                float nameX = lx;
                if (lg.Kind == "account") {
                    using (SolidBrush sw = new SolidBrush(lg.Colour)) { og.FillRectangle(sw, lx, gy + 6f, 10f, 10f); }
                    nameX = lx + 16f;
                } else if (lg.Kind == "lunch") {
                    using (HatchBrush hb = new HatchBrush(HatchStyle.WideUpwardDiagonal, Mix(Panel, Text, 0.33), Mix(Panel, Text, 0.12))) { og.FillRectangle(hb, lx, gy + 6f, 10f, 10f); }
                    nameX = lx + 16f;
                }
                QueueSmoothText(lg.Name, f, c, new RectangleF(nameX, gy, LegendW - 46f - (nameX - lx), TlRowH), DT_LEFT);
                QueueSmoothText(lg.Total, f, c, new RectangleF(xR - 46f, gy, 46f, TlRowH), DT_RIGHT);
                gy += TlRowH;
            }
            if (t.HasUnplaced) {
                QueueSmoothText("* Click gap to place", fTS, faintC,
                                new RectangleF(lx, gy + 4f, LegendW, 34f), DT_LEFT);
            }
        }

        // Lays sharpened text onto a see-through background (same method as Win.Layered.Combine)
        static void Combine(Bitmap dest, Bitmap colour, Bitmap mask, double gamma) {
            Rectangle rect = new Rectangle(0, 0, dest.Width, dest.Height);
            BitmapData dd = dest.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            BitmapData cd = colour.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData md = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try {
                int n = rect.Width * rect.Height;
                int[] dp = new int[n]; int[] cp = new int[n]; int[] mp = new int[n];
                Marshal.Copy(dd.Scan0, dp, 0, n);
                Marshal.Copy(cd.Scan0, cp, 0, n);
                Marshal.Copy(md.Scan0, mp, 0, n);
                for (int i = 0; i < n; i++) {
                    int ma = (mp[i] >> 24) & 255;
                    if (ma == 0) continue;
                    double fa = Math.Pow(ma / 255.0, gamma);
                    int cc = cp[i], dc = dp[i];
                    double da = ((dc >> 24) & 255) / 255.0;
                    double fd = da * (1.0 - fa);
                    double oa = fa + fd;
                    if (oa <= 0) continue;
                    int r = (int)((((cc >> 16) & 255) * fa + ((dc >> 16) & 255) * fd) / oa + 0.5);
                    int gg = (int)((((cc >> 8) & 255) * fa + ((dc >> 8) & 255) * fd) / oa + 0.5);
                    int b = (int)(((cc & 255) * fa + (dc & 255) * fd) / oa + 0.5);
                    int a = (int)(oa * 255.0 + 0.5);
                    dp[i] = (a << 24) | (r << 16) | (gg << 8) | b;
                }
                Marshal.Copy(dp, 0, dd.Scan0, n);
            } finally {
                dest.UnlockBits(dd); colour.UnlockBits(cd); mask.UnlockBits(md);
            }
        }
    }
}
'@
    $script:useRenderer = $true
} catch { $script:rendererError = $_.Exception.Message }

# ---- Settings ----
$StartTime       = '08:30'   # usual start and end (also changeable in the app: right-click, Working hours)
$EndTime         = '17:30'
$Scale           = 1.0      # make the timer bigger or smaller, e.g. 0.8 or 1.3 (also changeable in the app: right-click, Appearance, Size)
$SizeFile = Join-Path $PSScriptRoot 'account-timer-size.txt'
if (Test-Path $SizeFile) {
    try {
        $savedSize = [double]::Parse((Get-Content $SizeFile -TotalCount 1).Trim(), [Globalization.CultureInfo]::InvariantCulture)
        if ($savedSize -ge 0.5 -and $savedSize -le 3.0) { $Scale = $savedSize }
    } catch {}
}
$DefaultAccounts = @('ACCOUNT 1', 'ACCOUNT 2', 'ACCOUNT 3', 'ACCOUNT 4', 'ACCOUNT 5')
$SleepGapMinutes = 5        # if the computer sleeps for longer than this, the running slot is paused
$MaxVisibleAccounts = 12    # most accounts shown at once (hide some to make room)
$NoteMinMinutes  = 15       # ask for a note after this long on an account (0 turns the note prompt off)
$EntryMinSeconds = 60       # time entries shorter than this (stray clicks) are left out of the entries view and file
# Pill effects (starting defaults; switch them in the menu under Appearance, which is remembered)
$PillShadow      = $true    # soft shadow and thin rim around the pill, to lift it off other windows
$PillPulse       = $true    # the pill gently glows and breathes while nothing is being tracked
$PillLiveDot     = $true    # a small breathing dot beside the account name while time is being recorded
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
$ViewFile     = Join-Path $PSScriptRoot 'account-timer-view.txt'
$PosFile      = Join-Path $PSScriptRoot 'account-timer-positions.txt'
$BudgetsFile  = Join-Path $PSScriptRoot 'account-timer-budgets.txt'
$NotesFile    = Join-Path $TimesheetDir 'notes.csv'
$SessionsUndo = Join-Path $TimesheetDir 'sessions.undo.csv'
$NotesUndo    = Join-Path $TimesheetDir 'notes.undo.csv'
$GroupsFile   = Join-Path $TimesheetDir 'entry-groups.csv'
$GroupsUndo   = Join-Path $TimesheetDir 'entry-groups.undo.csv'
$PositionsFile = Join-Path $TimesheetDir 'credit-positions.csv'
$PositionsUndo = Join-Path $TimesheetDir 'credit-positions.undo.csv'
$MergeFile    = Join-Path $PSScriptRoot 'account-timer-merge.txt'
$EffectsFile  = Join-Path $PSScriptRoot 'account-timer-effects.txt'
$DrawerFile   = Join-Path $PSScriptRoot 'account-timer-drawer.txt'
$AwayFile     = Join-Path $PSScriptRoot 'account-timer-away.txt'
try { New-Item -ItemType Directory -Path $TimesheetDir -Force | Out-Null } catch {}

$script:accounts       = @()   # every account, including hidden ones
$script:slots          = @()   # visible accounts plus Lunch, in display order
$script:active         = $null
$script:pending        = @()
$script:entryGroups    = @()  # explicit time-entry groups, stored separately from source sessions
$script:creditPositions = @() # timestamps assigned to legacy merged credit, zero additional hours
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
$script:mode           = 'full'   # 'full' or 'pill'
$script:positions      = @{}
$script:pillHover      = $null
$script:pillDown       = $null
$script:collapseHover  = $false
$script:pressedCollapse = $false
$script:regionKey      = ''
$script:pillOpen       = $false  # account list showing below (or above) the pill
$script:pillUp         = $false  # list opened upwards because there was no room below
$script:budgets        = @{}     # account name -> @{ Hours; Period } (period is week, month or quarter)
$script:monthDone      = @{}
$script:quarterDone    = @{}
$script:notes          = @{}     # "date<tab>account<tab>start" -> note text
$script:pendingNote    = $null
$script:noteForm       = $null
$script:noteTimer      = $null
$script:noteClosing    = $false
$script:weekForm       = $null
$script:weekStart      = $null
$script:weekData       = $null
$script:wv             = $null
$script:wk             = 1.0
$script:wvPanel        = $null
$script:weekMode       = 'summary'   # weekly view tab: 'summary' or 'entries'
$script:weekBlocks     = @()   # time entries grouped for the Time entries tab
$script:mergeEntries   = $true
$script:pillMargin     = 0       # room around the pill for its shadow (only with see-through drawing)
$script:animClock      = [System.Diagnostics.Stopwatch]::StartNew()
$script:entryBlocks    = @()
$script:entryHover     = $null
$script:entriesWarned  = $false
$script:entrySelected  = @()     # blocks selected in the Time entries tab
$script:entryCols      = @()
$script:undoLabel      = $null   # description of the change Undo would reverse
$script:drawerOpen     = $false  # week drawer below (or above) the full timer
$script:drawerUp       = $false  # opened upwards because there was no room below
$script:drawerH        = 0
$script:drawerWeek     = $null
$script:drawerData     = $null
$script:drawerBudgets  = @()
$script:drawerButtons  = @()
$script:drawerHover    = $null
$script:drawerPressed  = $null
$script:drawerReopen   = $false
$script:timerOffX      = 0       # where the timer sits within the window when the drawer is open
$script:timerOffY      = 0
$script:handleHover    = $false
$script:unallocHover   = $false
$script:tipShownAt     = $null    # when a tooltip last appeared
$script:pressedHandle  = $false
$script:fullMargin     = 0       # room around the full view for its shadow (with the faster drawing)
$script:renderer       = $null
$script:drawerModel    = $null
$script:drawerVersion  = 0       # goes up whenever the drawer's figures are refreshed
$script:drawerCache    = $null   # the drawer drawn once and reused until something in it changes
$script:drawerCacheKey = ''
$script:drawerCacheBuilds = 0
$script:drawerMode     = 'timeline'   # 'timeline' for one day along a strip (the usual view), 'week' for the table of hours
$script:drawerDay      = $null    # the day shown in the timeline
$script:drawerTimeline = $null    # that day's stretches of time, ready to draw
$script:drawerLegend   = @()      # the lines under the strip
$script:awayIntervals  = @()      # stretches marked as finished for the day: @{ Start; End } (End is $null while still finished)
$script:dayBtnHover    = $false
$script:finishH        = 0       # extra height under the Lunch row while the Finish day button is showing
$script:pressedDayBtn  = $false
$script:tlScrollH      = $null    # the hour at the top of the timeline's view ($null until it is first placed)
$script:tlPx           = 64.0     # window units per hour on the timeline (the zoom)
$script:tlLastPt       = $null    # where the pointer last was over the drawer, to keep a drag following a wheel scroll
$script:tlGeom         = $null    # where the strip was last drawn (window units), reported by the renderer
$script:tlHover        = @{ Block = -1; Gap = -1; EdgeBlock = -1; Edge = '' }
$script:tlDrag         = $null    # an edge or a stretch of gap being dragged
$script:tlPreview      = @()      # edge moves shown while dragging, before they are saved
$script:tlSel          = $null    # part of a gap being selected, in hours
$script:tlDragText     = ''
$script:tlDragAt       = 0.0
$script:tlFill         = $null    # the stretch a gap menu is about to fill

# ======================================================================
# Helpers
# ======================================================================

function Write-Log($msg) {
    try { Add-Content -Path $LogFile -Value ('{0}  {1}' -f (Get-Date -Format 's'), $msg) } catch {}
}

# ---- Working hours and days (right-click, Working hours) ----
$SettingsFile = Join-Path $PSScriptRoot 'account-timer-settings.txt'
$DaysFile     = Join-Path $PSScriptRoot 'account-timer-days.txt'
$DayAbbr = @('Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat')
$script:workDays     = @(1, 2, 3, 4, 5)    # Monday to Friday
$script:weekStartDow = 1                   # weeks start on Monday
$script:hoursFormat  = 'decimal'           # 'decimal' (1.25) or 'hm' (1:15)
$script:dayOverrides = @{}                 # 'yyyy-MM-dd' -> @{ Off; Start; End } for one-off days
$script:dayHours     = @{}                 # weekday number -> @{ Start; End } where that day's hours differ from the usual ones (a shift pattern)

# Reads '9:30' or '09:30' as a time of day, giving '09:30', or $null if it isn't one
function ConvertTo-ClockText($text) {
    $t = "$text".Trim()
    if ($t -notmatch '^(\d{1,2})[:.](\d{2})$') { return $null }
    $h = [int]$Matches[1]; $m = [int]$Matches[2]
    if ($h -gt 23 -or $m -gt 59) { return $null }
    return '{0:00}:{1:00}' -f $h, $m
}

function Load-WorkSettings {
    $script:dayHours = @{}
    if (Test-Path $SettingsFile) {
        foreach ($line in Get-Content $SettingsFile) {
            $kv = $line -split '=', 2
            if ($kv.Count -ne 2) { continue }
            $val = $kv[1].Trim()
            switch ($kv[0].Trim().ToLower()) {
                'start' { $clockText = ConvertTo-ClockText $val; if ($clockText) { $script:StartTime = $clockText } }
                'end'   { $clockText = ConvertTo-ClockText $val; if ($clockText) { $script:EndTime = $clockText } }
                'days'  {
                    $list = @($val -split ',' | ForEach-Object { [array]::IndexOf($DayAbbr, $_.Trim()) } | Where-Object { $_ -ge 0 } | Sort-Object -Unique)
                    if ($list.Count -gt 0) { $script:workDays = $list }
                }
                'weekstart' { $i = [array]::IndexOf($DayAbbr, $val); if ($i -ge 0) { $script:weekStartDow = $i } }
                { $_ -like 'shift.*' } {
                    # shift.mon=09:00,17:00 gives Monday its own hours
                    $dayName = $kv[0].Trim().Substring(6)
                    $di = -1
                    for ($k = 0; $k -lt 7; $k++) { if ($DayAbbr[$k] -eq $dayName) { $di = $k } }
                    $times = @($val -split ',' | ForEach-Object { ConvertTo-ClockText $_ })
                    if ($di -ge 0 -and $times.Count -eq 2 -and $times[0] -and $times[1]) { $script:dayHours[$di] = @{ Start = $times[0]; End = $times[1] } }
                }
                'hours' { if ($val -eq 'decimal' -or $val -eq 'hm') { $script:hoursFormat = $val } }
            }
        }
    }
    $script:dayOverrides = @{}
    if (Test-Path $DaysFile) {
        foreach ($line in Get-Content $DaysFile) {
            $p = $line -split "`t"
            if ($p.Count -ge 2 -and $p[1].Trim() -eq 'off') { $script:dayOverrides[$p[0].Trim()] = @{ Off = $true } }
            elseif ($p.Count -ge 3) {
                $a = ConvertTo-ClockText $p[1]; $b = ConvertTo-ClockText $p[2]
                if ($a -and $b) { $script:dayOverrides[$p[0].Trim()] = @{ Off = $false; Start = $a; End = $b } }
            }
        }
    }
}

function Save-WorkSettings {
    $lines = @(
        "start=$StartTime", "end=$EndTime",
        ('days=' + (($script:workDays | ForEach-Object { $DayAbbr[$_] }) -join ',')),
        ('weekstart=' + $DayAbbr[$script:weekStartDow]),
        "hours=$($script:hoursFormat)")
    foreach ($di in @($script:dayHours.Keys | Sort-Object)) {
        $lines += ('shift.{0}={1},{2}' -f $DayAbbr[$di].ToLower(), $script:dayHours[$di].Start, $script:dayHours[$di].End)
    }
    try { Set-Content -Path $SettingsFile -Value $lines } catch { Write-Log "Could not save settings: $($_.Exception.Message)" }
    $dl = @($script:dayOverrides.Keys | Sort-Object | ForEach-Object {
        $o = $script:dayOverrides[$_]
        if ($o.Off) { "$_`toff" } else { "$_`t$($o.Start)`t$($o.End)" }
    })
    try { Set-Content -Path $DaysFile -Value $dl } catch { Write-Log "Could not save day changes: $($_.Exception.Message)" }
}

# The usual start and end for a weekday: its own hours if the shift pattern gives it some,
# otherwise the same hours as every other day
function Get-UsualHours([int]$dow) {
    $h = $script:dayHours[$dow]
    if ($h) { return @($h.Start, $h.End) }
    return @($StartTime, $EndTime)
}

# What a given day looks like: whether it's a working day, and its start and end
function Get-DayPlan([datetime]$date) {
    $d = $date.Date
    $o = $script:dayOverrides[$d.ToString('yyyy-MM-dd', $Inv)]
    if ($o) {
        if ($o.Off) { return @{ Working = $false; Custom = $true } }
        return @{ Working = $true; Custom = $true; Start = $d.Add([timespan]$o.Start); End = $d.Add([timespan]$o.End) }
    }
    if ($script:workDays -notcontains [int]$d.DayOfWeek) { return @{ Working = $false; Custom = $false } }
    $usual = Get-UsualHours ([int]$d.DayOfWeek)
    return @{ Working = $true; Custom = $false; Start = $d.Add([timespan]$usual[0]); End = $d.Add([timespan]$usual[1]) }
}

# Today's working window (a day off gives an empty window at midnight)
function Get-DayBounds {
    $p = Get-DayPlan ([datetime]::Today)
    if ($p.Working) { return $p.Start, $p.End }
    return [datetime]::Today, [datetime]::Today
}

Load-WorkSettings

# ---- Finishing for the day ----
# Pressing Finish day marks the time from then until the next account is started as away. Away time is
# left out of the working day and of overtime in the same way as lunch, so the unallocated figures
# stop growing, and the red not tracking state stays off until work carries on.

function Save-Away {
    $keep = [datetime]::Today.AddDays(-60)
    $script:awayIntervals = @($script:awayIntervals | Where-Object { $_.Start -ge $keep })
    $lines = @($script:awayIntervals | ForEach-Object {
        $e = if ($null -ne $_.End) { $_.End.ToString('s', $Inv) } else { '' }
        "{0}`t{1}" -f $_.Start.ToString('s', $Inv), $e
    })
    try { Set-Content -Path $AwayFile -Value $lines } catch { Write-Log "Could not save finished days: $($_.Exception.Message)" }
}

function Load-Away {
    $script:awayIntervals = @()
    if (-not (Test-Path $AwayFile)) { return }
    foreach ($line in @(Get-Content $AwayFile)) {
        $p = $line -split "`t"
        if ($p.Count -lt 1 -or -not $p[0].Trim()) { continue }
        try {
            $s = [datetime]::ParseExact($p[0].Trim(), 's', $Inv)
            $e = $null
            if ($p.Count -ge 2 -and $p[1].Trim()) { $e = [datetime]::ParseExact($p[1].Trim(), 's', $Inv) }
            # One left open from an earlier day (the timer was closed while finished) ends with that day
            if ($null -eq $e -and $s.Date -lt [datetime]::Today) { $e = $s.Date.AddDays(1) }
            $script:awayIntervals += ,@{ Start = $s; End = $e }
        } catch { }
    }
}

function Test-DayDone {
    foreach ($iv in @($script:awayIntervals)) {
        if ($null -eq $iv.End -and $iv.Start.Date -eq [datetime]::Today) { return $true }
    }
    return $false
}

# The away stretches that fall on a day, as start and end pairs. One still open runs to now, but never
# past the end of the day it began.
function Get-AwayOn([datetime]$day, [datetime]$now) {
    $day = $day.Date
    $dayEnd = $day.AddDays(1)
    $out = @()
    foreach ($iv in @($script:awayIntervals)) {
        $a = $iv.Start
        $b = if ($null -ne $iv.End) { $iv.End } else { $a.Date.AddDays(1) }
        if ($null -eq $iv.End -and $now -lt $b) { $b = $now }
        if ($a -lt $day) { $a = $day }
        if ($b -gt $dayEnd) { $b = $dayEnd }
        if ($b -gt $a) { $out += ,@($a, $b) }
    }
    return $out
}

function Close-AwayInterval {
    $changed = $false
    foreach ($iv in @($script:awayIntervals)) {
        if ($null -eq $iv.End -and $iv.Start.Date -eq [datetime]::Today) { $iv.End = Get-Date; $changed = $true }
    }
    if ($changed) { Save-Away }
}

Load-Away

function Limit-Time($t, $min, $max) {
    if ($t -lt $min) { return $min }
    if ($t -gt $max) { return $max }
    return $t
}

function Get-WeekStart {
    return Get-WeekStartFor ([datetime]::Today)
}

function Get-WeekFile {
    return Join-Path $TimesheetDir ('Week commencing {0}.csv' -f (Get-WeekStart).ToString('yyyy-MM-dd', $Inv))
}

# Hours and minutes as h:mm, whatever the hours setting is, for the unallocated figures
function Format-Clock($seconds) {
    $mins = [int][math]::Floor([math]::Max(0.0, $seconds) / 60)
    return '{0}:{1:00}' -f [math]::Floor($mins / 60), ($mins % 60)
}

function Format-Dur($seconds) {
    $sec = [math]::Max(0.0, $seconds)
    if ($script:hoursFormat -eq 'decimal') { return ([math]::Round($sec / 3600, 2)).ToString('0.00', $Inv) + 'h' }
    $ts = [timespan]::FromSeconds([math]::Floor($sec))
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
# Budgets
# ======================================================================

function Load-Budgets {
    $script:budgets = @{}
    if (-not (Test-Path $BudgetsFile)) { return }
    foreach ($line in Get-Content $BudgetsFile) {
        $p = $line -split "`t"
        if ($p.Count -lt 3) { continue }
        try {
            $h = [double]::Parse($p[1].Trim(), $Inv)
            $per = $p[2].Trim().ToLower()
            if ($h -gt 0 -and @('week', 'month', 'quarter') -contains $per) {
                $script:budgets[$p[0].Trim()] = @{ Hours = $h; Period = $per }
            }
        } catch {}
    }
}

function Save-Budgets {
    $lines = @($script:budgets.Keys | ForEach-Object {
        "{0}`t{1}`t{2}" -f $_, $script:budgets[$_].Hours.ToString($Inv), $script:budgets[$_].Period
    })
    try { Set-Content -Path $BudgetsFile -Value $lines }
    catch { Write-Log "Could not save budgets: $($_.Exception.Message)" }
}

# ======================================================================
# Notes (kept in Timesheets\notes.csv, matched to sessions by date, account and start time)
# ======================================================================

function Get-NoteKey($dateStr, $account, $startStr) { return ("{0}`t{1}`t{2}" -f $dateStr, $account, $startStr) }

function Load-Notes {
    $script:notes = @{}
    if (-not (Test-Path $NotesFile)) { return }
    try {
        foreach ($r in @(Import-Csv $NotesFile)) {
            if ($r.Note) { $script:notes[(Get-NoteKey $r.Date $r.Account $r.Start)] = $r.Note }
        }
    } catch { Write-Log "Could not read notes: $($_.Exception.Message)" }
}

function Save-Note($dateStr, $account, $startStr, $text) {
    $key = Get-NoteKey $dateStr $account $startStr
    $text = "$text".Trim()
    if ($text) { $script:notes[$key] = $text } else { [void]$script:notes.Remove($key) }
    # Undo restores notes as they were before the last change, so it would lose this edit
    $script:undoLabel = $null
    if (-not (Write-AllNotes)) { return $false }
    try { Write-WeekEntries (Get-WeekStartFor ([datetime]::ParseExact($dateStr, 'yyyy-MM-dd', $Inv))) } catch {}
    return $true
}

function Write-AllNotes {
    $rows = @($script:notes.Keys | Sort-Object | ForEach-Object {
        $p = $_ -split "`t"
        [pscustomobject]@{ Date = $p[0]; Account = $p[1]; Start = $p[2]; Note = $script:notes[$_] }
    })
    try {
        if ($rows.Count -gt 0) { $rows | Export-Csv -Path $NotesFile -NoTypeInformation -Encoding UTF8 }
        else { Set-Content -Path $NotesFile -Value '"Date","Account","Start","Note"' -Encoding UTF8 }
        return $true
    } catch {
        Write-Log "Could not save note (is notes.csv open in Excel?): $($_.Exception.Message)"
        return $false
    }
}

# ======================================================================
# Non-destructive weekly entry grouping. The source CSV stays unchanged.
# Each association identifies a specific existing source row by its five CSV fields.
# If an edited/deleted row no longer matches, its grouping is automatically pruned.
# ======================================================================

function Get-SessionKey($r) {
    return [string]::Join([string][char]31, [string[]]@("$($r.Date)", "$($r.Account)", "$($r.Start)", "$($r.End)", "$($r.Minutes)"))
}

function Get-EntryGroupId($raw) {
    if (-not $raw) { return $null }
    $key = Get-SessionKey $raw
    foreach ($member in @($script:entryGroups)) {
        if ((Get-SessionKey $member) -ceq $key) { return [string]$member.GroupId }
    }
    return $null
}

function Load-EntryGroups {
    $script:entryGroups = @()
    if (-not (Test-Path $GroupsFile)) { return }
    try {
        $script:entryGroups = @(Import-Csv $GroupsFile | Where-Object { $_.GroupId -and $_.Date -and $_.Start })
    } catch { Write-Log "Could not read entry groups: $($_.Exception.Message)" }
}

function Write-EntryGroups {
    $temp = "$GroupsFile.tmp"
    try {
        $members = @($script:entryGroups)
        if ($members.Count -gt 0) {
            $members | Select-Object GroupId, Date, Account, Start, End, Minutes |
                Export-Csv -Path $temp -NoTypeInformation -Encoding UTF8
        } else {
            Set-Content -Path $temp -Value '"GroupId","Date","Account","Start","End","Minutes"' -Encoding UTF8
        }
        Move-Item -Path $temp -Destination $GroupsFile -Force -ErrorAction Stop
        return $true
    } catch {
        Write-Log "Could not save entry groups: $($_.Exception.Message)"
        return $false
    } finally {
        if (Test-Path $temp) { Remove-Item $temp -Force -ErrorAction SilentlyContinue }
    }
}

# Credit positions are additional coordinates for existing legacy merged credit,
# not billable rows. The original sessions.csv is never changed when they are placed.
# A separate projection splits merged credit into positioned and remaining portions
# in the weekly entries view without changing the authoritative daily totals.
function Load-CreditPositions {
    $script:creditPositions = @()
    if (-not (Test-Path $PositionsFile)) { return }
    try {
        $script:creditPositions = @(Import-Csv $PositionsFile | Where-Object { $_.Id -and $_.Date -and $_.Account -and $_.Start -and $_.Minutes })
        foreach ($p in @($script:creditPositions)) {
            if (-not $p.PSObject.Properties['Note']) {
                $p | Add-Member -NotePropertyName Note -NotePropertyValue ''
            }
        }
    } catch { Write-Log "Could not read credit positions: $($_.Exception.Message)" }
}

function Write-CreditPositions {
    $temp = "$PositionsFile.tmp"
    try {
        $rows = @($script:creditPositions)
        if ($rows.Count -gt 0) {
            $rows | Select-Object Id, Date, Account, Start, Minutes, Note |
                Export-Csv -Path $temp -NoTypeInformation -Encoding UTF8
        } else {
            Set-Content -Path $temp -Value '"Id","Date","Account","Start","Minutes","Note"' -Encoding UTF8
        }
        Move-Item -Path $temp -Destination $PositionsFile -Force -ErrorAction Stop
        return $true
    } catch {
        Write-Log "Could not save credit positions: $($_.Exception.Message)"
        return $false
    } finally {
        if (Test-Path $temp) { Remove-Item $temp -Force -ErrorAction SilentlyContinue }
    }
}

function Get-CreditPositionSeconds($row) {
    try { return [double]::Parse([string]$row.Minutes, $Inv) * 60.0 } catch { return 0.0 }
}

function Get-UnplacedCredits([datetime]$day) {
    $totals = @{}
    foreach ($row in @(Read-Sessions)) {
        if ($row.Start.Date -eq $day.Date -and $row.Origin -eq 'merged') {
            $totals[$row.Name] = [double]$totals[$row.Name] + $row.Seconds
        }
    }
    foreach ($p in @($script:creditPositions)) {
        if ($p.Date -eq $day.ToString('yyyy-MM-dd', $Inv)) {
            $totals[$p.Account] = [double]$totals[$p.Account] - (Get-CreditPositionSeconds $p)
        }
    }
    $result = @{}
    foreach ($name in @($totals.Keys)) {
        if ($totals[$name] -gt 0.01) { $result[$name] = [double]$totals[$name] }
    }
    return $result
}

function Get-CreditPositionSessions([datetime]$day) {
    foreach ($p in @($script:creditPositions)) {
        if ($p.Date -ne $day.ToString('yyyy-MM-dd', $Inv)) { continue }
        try {
            $from = [datetime]::ParseExact($p.Date, 'yyyy-MM-dd', $Inv).Add(
                [timespan]::ParseExact($p.Start, 'hh\:mm\:ss', $Inv))
            $secs = Get-CreditPositionSeconds $p
            if ($secs -le 0) { continue }
            [pscustomobject]@{
                Name = $p.Account; Start = $from; End = $from.AddSeconds($secs)
                Seconds = $secs; IsAdjust = $false; Kind = 'time'; Origin = 'positioned'
                Raw = $null; PositionId = [string]$p.Id; Note = [string]$p.Note
            }
        } catch { Write-Log "Invalid credit position $($p.Id): $($_.Exception.Message)" }
    }
}

function Find-CreditPosition($id) {
    foreach ($p in @($script:creditPositions)) {
        if ("$($p.Id)" -ceq "$id") { return $p }
    }
    return $null
}

function Prune-EntryGroups($logRows) {
    $valid = @{}
    foreach ($r in @($logRows)) { $valid[(Get-SessionKey $r)] = $true }
    $kept = @($script:entryGroups | Where-Object { $valid.ContainsKey((Get-SessionKey $_)) })
    $counts = @{}
    foreach ($m in $kept) {
        $id = [string]$m.GroupId
        $counts[$id] = [int]$counts[$id] + 1
    }
    $script:entryGroups = @($kept | Where-Object { $counts[[string]$_.GroupId] -ge 2 })
}

# ======================================================================
# Editing the time log (merge, delete, change account, edit, add) with one level of Undo
# ======================================================================

function Test-SameSession($a, $b) {
    return ("$($a.Date)" -eq "$($b.Date)" -and "$($a.Account)" -ceq "$($b.Account)" -and "$($a.Start)" -eq "$($b.Start)" -and
            "$($a.End)" -eq "$($b.End)" -and "$($a.Minutes)" -eq "$($b.Minutes)")
}

function Find-SessionRow($list, $raw) {
    for ($i = 0; $i -lt $list.Count; $i++) { if (Test-SameSession $list[$i] $raw) { return $i } }
    return -1
}

function New-SessionRow($dateStr, $account, $startStr, $endLabel, [double]$seconds) {
    return [pscustomobject][ordered]@{
        Date = $dateStr; Account = $account; Start = $startStr; End = $endLabel
        Minutes = ([math]::Round($seconds / 60, 2)).ToString($Inv)
    }
}

function Get-PartNoteKey($p) {
    return Get-NoteKey $p.Start.ToString('yyyy-MM-dd', $Inv) $p.Name $p.Start.ToString('HH:mm:ss', $Inv)
}

# Runs a change against the time log. The change works on $logRows (an editable list of the log's rows)
# and on $script:notes, and returns $true to save. Copies are kept first so Undo can put things back.
function Invoke-LogChange($description, [scriptblock]$change) {
    Flush-Pending
    if ($script:pending.Count -gt 0) {
        Show-Message "Couldn't change the time log because sessions.csv is open in another program. Close it and try again."
        return $false
    }
    $notesBefore = $script:notes.Clone()
    $groupsBefore = @($script:entryGroups | ForEach-Object { $_.PSObject.Copy() })
    $positionsBefore = @($script:creditPositions | ForEach-Object { $_.PSObject.Copy() })
    $snapshotsReady = $false
    try {
        $logRows = New-Object System.Collections.ArrayList
        if (Test-Path $SessionsFile) { foreach ($row in @(Import-Csv $SessionsFile)) { [void]$logRows.Add($row) } }
        $proceed = & $change
        if (-not $proceed) { $script:notes = $notesBefore; $script:entryGroups = $groupsBefore; $script:creditPositions = $positionsBefore; return $false }
        Prune-EntryGroups $logRows

        if (Test-Path $SessionsFile) { Copy-Item $SessionsFile $SessionsUndo -Force }
        else { Set-Content -Path $SessionsUndo -Value '"Date","Account","Start","End","Minutes"' -Encoding UTF8 }
        if (Test-Path $NotesFile) { Copy-Item $NotesFile $NotesUndo -Force }
        else { Set-Content -Path $NotesUndo -Value '"Date","Account","Start","Note"' -Encoding UTF8 }
        if (Test-Path $GroupsFile) { Copy-Item $GroupsFile $GroupsUndo -Force }
        else { Set-Content -Path $GroupsUndo -Value '"GroupId","Date","Account","Start","End","Minutes"' -Encoding UTF8 }
        if (Test-Path $PositionsFile) { Copy-Item $PositionsFile $PositionsUndo -Force }
        else { Set-Content -Path $PositionsUndo -Value '"Id","Date","Account","Start","Minutes","Note"' -Encoding UTF8 }
        $snapshotsReady = $true

        if ($logRows.Count -gt 0) {
            $logRows | Select-Object Date, Account, Start, End, Minutes | Export-Csv -Path $SessionsFile -NoTypeInformation -Encoding UTF8
        } else {
            Set-Content -Path $SessionsFile -Value '"Date","Account","Start","End","Minutes"' -Encoding UTF8
        }
        if (-not (Write-AllNotes)) { throw 'Could not save notes' }
        if (-not (Write-EntryGroups)) { throw 'Could not save entry groups' }
        if (-not (Write-CreditPositions)) { throw 'Could not save credit positions' }
        $script:undoLabel = $description
    } catch {
        $script:notes = $notesBefore
        $script:entryGroups = $groupsBefore
        $script:creditPositions = $positionsBefore
        # Restore every component only if this transaction prepared its own snapshots.
        if ($snapshotsReady) {
            try {
                if (Test-Path $SessionsUndo) { Copy-Item $SessionsUndo $SessionsFile -Force }
                if (Test-Path $NotesUndo) { Copy-Item $NotesUndo $NotesFile -Force }
                if (Test-Path $GroupsUndo) { Copy-Item $GroupsUndo $GroupsFile -Force }
                if (Test-Path $PositionsUndo) { Copy-Item $PositionsUndo $PositionsFile -Force }
            } catch { Write-Log "Transaction rollback failed: $($_.Exception.Message)" }
        }
        Write-Log "Time log change failed: $($_.Exception.Message)"
        Show-Message "Couldn't save the change. If sessions.csv or notes.csv is open in Excel, close it and try again."
        return $false
    }
    Complete-LogChange
    return $true
}

# Bring every view and file up to date after the log has changed
function Complete-LogChange {
    $script:entrySelected = @()
    Refresh-Totals
    Update-DrawerContent
    Write-WeekSummary
    if ($script:weekStart -and $script:weekStart -ne (Get-WeekStart)) { Write-WeekEntries $script:weekStart }
    Update-View
    Request-Redraw
    if ($script:weekForm -and -not $script:weekForm.IsDisposed) { Fill-WeeklyView }
}

function Undo-LogChange {
    if (-not $script:undoLabel) { return }
    if (-not (Ask-YesNo "Undo the last change ($($script:undoLabel))?")) { return }
    try {
        Copy-Item $SessionsUndo $SessionsFile -Force
        Copy-Item $NotesUndo $NotesFile -Force
        if (Test-Path $GroupsUndo) { Copy-Item $GroupsUndo $GroupsFile -Force }
        if (Test-Path $PositionsUndo) { Copy-Item $PositionsUndo $PositionsFile -Force }
    } catch {
        Show-Message "Couldn't undo. If sessions.csv or notes.csv is open in Excel, close it and try again."
        return
    }
    Load-Notes
    Load-EntryGroups
    Load-CreditPositions
    $script:undoLabel = $null
    Complete-LogChange
}

function Test-HasRunning($blocks) {
    foreach ($b in @($blocks)) { if ($b.Kind -eq 'running') { return $true } }
    return $false
}

function Remove-EntryBlocks {
    $sel = @($script:entrySelected)
    if ($sel.Count -eq 0) { return }
    if (Test-HasRunning $sel) { Show-Message 'Stop the running account before deleting its time.'; return }
    foreach ($b in $sel) {
        foreach ($part in @($b.Parts)) {
            if ($part.Origin -eq 'merged' -and
                @($script:creditPositions | Where-Object { $_.Date -eq $part.Start.ToString('yyyy-MM-dd', $Inv) -and $_.Account -eq $part.Name }).Count -gt 0) {
                Show-Message 'This merged credit has positioned portions. Unposition those portions first before deleting the source credit.'
                return
            }
        }
    }
    $total = 0.0; foreach ($b in $sel) { $total += $b.Seconds }
    $what = if ($sel.Count -eq 1) { "this block ($($sel[0].Name), $(Format-HM $total))" } else { "these $($sel.Count) blocks ($(Format-HM $total) in total)" }
    if (-not (Ask-YesNo "Delete $what? You can undo this straight afterwards.")) { return }
    [void](Invoke-LogChange 'delete' {
        foreach ($b in $sel) {
            foreach ($part in @($b.Parts)) {
                if ($part.Origin -eq 'positioned') {
                    $script:creditPositions = @($script:creditPositions | Where-Object { $_.Id -cne [string]$part.PositionId })
                } else {
                    $at = Find-SessionRow $logRows $part.Raw
                    if ($at -ge 0) { $logRows.RemoveAt($at) }
                }
                if ($part.Kind -eq 'time' -and $part.Origin -ne 'positioned') {
                    [void]$script:notes.Remove((Get-PartNoteKey $part))
                }
            }
        }
        return $true
    })
}

function Merge-EntryBlocks {
    $sel = @($script:entrySelected)
    if ($sel.Count -lt 2) { return }
    if (Test-HasRunning $sel) { Show-Message 'Stop the running account before grouping its time.'; return }
    $acct = $sel[0].Name; $day = $sel[0].Day
    foreach ($b in $sel) {
        if ($b.Name -ne $acct -or $b.Day -ne $day) {
            Show-Message 'Only entries for the same account and day can be grouped.'
            return
        }
    }
    $allSelectedParts = @($sel | ForEach-Object { $_.Parts })
    if (@($allSelectedParts | Where-Object { $_.Origin -eq 'positioned' }).Count -gt 0) {
        Show-Message 'Positioned credit already belongs to a merged source. It cannot be grouped again.'
        return
    }
    $parts = @($allSelectedParts | Where-Object { $_.Raw })
    if (@($parts | Where-Object { $_.Origin -eq 'merged' }).Count -gt 0) {
        Show-Message 'Legacy merged credit must first be assigned a real start time. It cannot be grouped with positioned sessions.'
        return
    }
    $keys = @($parts | ForEach-Object { Get-SessionKey $_.Raw } | Select-Object -Unique)
    if ($keys.Count -lt 2 -or $keys.Count -ne $parts.Count) {
        Show-Message 'Cannot group duplicate or ambiguous source records. Individual source rows must be identifiable.'
        return
    }
    # Grouping records references in a separate file. No sessions or notes are removed,
    # so every original timestamp and duration remains recoverable on the timeline.
    $id = [guid]::NewGuid().ToString('N')
    [void](Invoke-LogChange 'group entries' {
        $oldIds = @()
        foreach ($member in @($script:entryGroups)) {
            if ($keys -contains (Get-SessionKey $member)) { $oldIds += [string]$member.GroupId }
        }
        $script:entryGroups = @($script:entryGroups | Where-Object {
            $oldIds -notcontains [string]$_.GroupId -and $keys -notcontains (Get-SessionKey $_)
        })
        foreach ($part in $parts) {
            $raw = $part.Raw
            if ((Find-SessionRow $logRows $raw) -lt 0) { return $false }
            $script:entryGroups += [pscustomobject]@{
                GroupId = $id; Date = $raw.Date; Account = $raw.Account; Start = $raw.Start
                End = $raw.End; Minutes = $raw.Minutes
            }
        }
        return $true
    })
}

function Ungroup-EntryBlocks {
    $ids = @()
    foreach ($b in @($script:entrySelected)) {
        if (-not $b.ExplicitGroup) { continue }
        foreach ($part in @($b.Parts)) {
            $id = Get-EntryGroupId $part.Raw
            if ($id -and $ids -notcontains $id) { $ids += $id }
        }
    }
    if ($ids.Count -eq 0) { return }
    [void](Invoke-LogChange 'ungroup entries' {
        $script:entryGroups = @($script:entryGroups | Where-Object { $ids -notcontains [string]$_.GroupId })
        return $true
    })
}

function Set-EntryAccount($newName) {
    $sel = @($script:entrySelected)
    if ($sel.Count -eq 0) { return }
    if (Test-HasRunning $sel) { Show-Message 'Stop the running account before moving its time.'; return }
    foreach ($part in @($sel | ForEach-Object { $_.Parts })) {
        if ($part.Origin -eq 'positioned') {
            Show-Message 'To change the account of positioned credit, remove that position and allocate it from the appropriate account again.'
            return
        }
        if ($part.Origin -eq 'merged' -and @($script:creditPositions | Where-Object {
                    $_.Date -eq $part.Start.ToString('yyyy-MM-dd', $Inv) -and $_.Account -eq $part.Name }).Count -gt 0) {
            Show-Message 'Unposition any linked credit before changing the source merged account.'
            return
        }
    }
    [void](Invoke-LogChange "move to $newName" {
        foreach ($b in $sel) {
            foreach ($part in @($b.Parts)) {
                $at = Find-SessionRow $logRows $part.Raw
                if ($at -lt 0) { continue }
                $logRows[$at].Account = $newName
                if ($part.Kind -eq 'time') {
                    $oldKey = Get-PartNoteKey $part
                    if ($script:notes.ContainsKey($oldKey)) {
                        $txt = $script:notes[$oldKey]
                        [void]$script:notes.Remove($oldKey)
                        $script:notes[(Get-NoteKey $part.Start.ToString('yyyy-MM-dd', $Inv) $newName $part.Start.ToString('HH:mm:ss', $Inv))] = $txt
                    }
                }
            }
        }
        return $true
    })
}

function Edit-EntryTime($b) {
    if ($b.Kind -eq 'running') { Show-Message 'Stop the running account before editing its time.'; return }
    if ($b.ExplicitGroup) {
        Show-Message 'Ungroup these recordings before editing their individual times. This preserves the original intervals.'
        return
    }
    if ($b.Origin -eq 'merged' -and @($script:creditPositions | Where-Object {
                $_.Date -eq $b.Start.ToString('yyyy-MM-dd', $Inv) -and $_.Account -eq $b.Name }).Count -gt 0) {
        Show-Message 'This merged credit has positioned portions. Remove their positions before converting the source to a timed entry.'
        return
    }
    $r = Show-EntryDialog "Edit time for $($b.Name)" @{ Start = $b.Start.ToString('HH:mm', $Inv); Duration = (Format-HM $b.Seconds) }
    if (-not $r) { return }
    $dateStr = $b.Start.ToString('yyyy-MM-dd', $Inv)
    $startStr = $r.Start.ToString('HH:mm:ss', $Inv)
    $keepNote = "$($b.Note)"
    $acct = $b.Name
    if ($b.Origin -eq 'positioned') {
        if (($r.Start - $r.Start.Date).TotalSeconds + $r.Seconds -gt 86400.01) {
            Show-Message 'Positioned credit cannot extend into another day.'
            return
        }
        if (@($b.Parts).Count -ne 1) {
            Show-Message 'Edit this positioned credit individually, with automatic grouping turned off.'
            return
        }
        [void](Invoke-LogChange 'edit credit position' {
            $part = @($b.Parts | Where-Object { $_.Origin -eq 'positioned' })[0]
            $pos = Find-CreditPosition $part.PositionId
            if (-not $pos) { return $false }
            $source = 0.0
            foreach ($row in @($logRows)) {
                if ($row.Date -eq $pos.Date -and $row.Account -eq $pos.Account -and $row.End -eq 'merged') {
                    $source += [double]::Parse($row.Minutes, $Inv) * 60
                }
            }
            $other = 0.0
            foreach ($p in @($script:creditPositions)) {
                if ($p.Id -cne $pos.Id -and $p.Date -eq $pos.Date -and $p.Account -eq $pos.Account) {
                    $other += Get-CreditPositionSeconds $p
                }
            }
            if ($r.Seconds + $other -gt $source + 0.01) {
                Show-Message 'There is not enough merged credit to increase this positioned entry.'
                return $false
            }
            $pos.Start = $startStr
            $pos.Minutes = ([math]::Round($r.Seconds / 60, 2)).ToString('0.00', $Inv)
            return $true
        })
        return
    }
    [void](Invoke-LogChange 'edit time' {
        foreach ($part in @($b.Parts)) {
            $at = Find-SessionRow $logRows $part.Raw
            if ($at -ge 0) { $logRows.RemoveAt($at) }
            if ($part.Kind -eq 'time') { [void]$script:notes.Remove((Get-PartNoteKey $part)) }
        }
        [void]$logRows.Add((New-SessionRow $dateStr $acct $startStr 'edited' $r.Seconds))
        if ($keepNote) { $script:notes[(Get-NoteKey $dateStr $acct $startStr)] = $keepNote }
        return $true
    })
}

function Add-TimeEntry($dayIndex) {
    $ws = $script:weekStart
    $names = @($script:accounts | ForEach-Object { $_.Name })
    if ($names.Count -eq 0) { Show-Message 'Add an account first (right-click the timer, Add account).'; return }
    $r = Show-EntryDialog 'Add a time entry' @{
        Days = @(0..6 | ForEach-Object { $ws.AddDays($_) }); DayIndex = [math]::Max(0, [int]$dayIndex)
        Accounts = $names; Account = $names[0]; Start = '09:00'; Duration = '0:30'; Note = ''
    }
    if (-not $r) { return }
    $dateStr = $r.Date.ToString('yyyy-MM-dd', $Inv)
    $startStr = $r.Start.ToString('HH:mm:ss', $Inv)
    [void](Invoke-LogChange 'add entry' {
        [void]$logRows.Add((New-SessionRow $dateStr $r.Account $startStr 'added' $r.Seconds))
        if ($r.Note) { $script:notes[(Get-NoteKey $dateStr $r.Account $startStr)] = $r.Note }
        return $true
    })
}

# ======================================================================
# Session log
# ======================================================================

function Convert-SessionRow($r) {
    try {
        $day  = [datetime]::ParseExact($r.Date, 'yyyy-MM-dd', $Inv)
        $from = $day.Add([timespan]::ParseExact($r.Start, 'hh\:mm\:ss', $Inv))
        $sec  = [double]::Parse($r.Minutes, $Inv) * 60
        $isAdj = ($r.End -like 'rounded*' -or $r.End -eq 'adjusted')
        # Legacy merged records retain their credited duration but not the positions of their parts.
        $origin = if ($isAdj) { 'adjustment' }
                  elseif ($r.End -eq 'merged') { 'merged' }
                  elseif ($r.End -eq 'edited') { 'edited' }
                  elseif ($r.End -eq 'added') { 'added' }
                  else { 'recorded' }
        return [pscustomobject]@{ Name = $r.Account; Start = $from; End = $from.AddSeconds($sec); Seconds = $sec; IsAdjust = $isAdj; Kind = $(if ($isAdj) { $r.End } else { 'time' }); Origin = $origin; Raw = $r }
    } catch { return $null }
}

function Read-Sessions {
    $rows = @()
    if (Test-Path $SessionsFile) { $rows = @(Import-Csv $SessionsFile) }
    $rows += $script:pending
    foreach ($r in $rows) {
        $session = Convert-SessionRow $r
        if ($session) { $session }
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
        $script:undoLabel = $null
        $script:pending += $new
        Flush-Pending
    }
}

function Add-Rounding($name, $seconds, $label) {
    # Logged with "rounded up" or "rounded down" in the End column so it's easy to spot.
    # Round-downs are logged as negative minutes.
    $now = Get-Date
    $mins = [math]::Ceiling($seconds / 60 * 100) / 100
    $script:undoLabel = $null
    $script:pending += [pscustomobject]@{
        Date    = $now.ToString('yyyy-MM-dd', $Inv)
        Account = $name
        Start   = $now.ToString('HH:mm:ss', $Inv)
        End     = $label
        Minutes = $mins.ToString($Inv)
    }
    Flush-Pending
}

# Hours and minutes, e.g. 0:30 or 1:05 (negative for rounding down)
function Format-HM($seconds) {
    $sign = if ($seconds -lt 0) { '-' } else { '' }
    $ts = [timespan]::FromSeconds([math]::Round([math]::Abs($seconds)))
    return '{0}{1}:{2:00}' -f $sign, [math]::Floor($ts.TotalHours), $ts.Minutes
}

function Get-WeekStartFor([datetime]$date) {
    $back = ([int]$date.DayOfWeek - $script:weekStartDow + 7) % 7
    return $date.Date.AddDays(-$back)
}

# Every stretch of time for a week, one entry each, in order (Lunch left out)
function Get-WeekEntries($ws) {
    $we = $ws.AddDays(7)
    $list = @()
    # Group manually positioned credit by source account/day. Its source merged
    # rows remain untouched. The detailed view gets positioned entries plus any
    # unplaced remainder, never both the entire source and the positioned part.
    $allocated = @{}
    foreach ($p in @($script:creditPositions)) {
        try {
            $day = [datetime]::ParseExact($p.Date, 'yyyy-MM-dd', $Inv)
            if ($day -lt $ws -or $day -ge $we) { continue }
            $key = Get-NoteKey $p.Date $p.Account ''
            $allocated[$key] = [double]$allocated[$key] + (Get-CreditPositionSeconds $p)
        } catch {}
    }
    foreach ($r in @(Read-Sessions)) {
        if ($r.Name -eq 'Lunch' -or $r.Start -lt $ws -or $r.Start -ge $we) { continue }
        if ([math]::Abs($r.Seconds) -le 0) { continue }
        $seconds = [double]$r.Seconds
        if ($r.Origin -eq 'merged') {
            $key = Get-NoteKey $r.Start.ToString('yyyy-MM-dd', $Inv) $r.Name ''
            $take = [math]::Min($seconds, [math]::Max(0.0, [double]$allocated[$key]))
            $allocated[$key] = [math]::Max(0.0, [double]$allocated[$key] - $take)
            $seconds -= $take
            if ($seconds -le 0.01) { continue }
        }
        $note = $null
        if ($r.Kind -eq 'time') {
            $note = $script:notes[(Get-NoteKey $r.Start.ToString('yyyy-MM-dd', $Inv) $r.Name $r.Start.ToString('HH:mm:ss', $Inv))]
        }
        $list += [pscustomobject]@{
            Day = [int][math]::Floor(($r.Start.Date - $ws).TotalDays)
            Name = $r.Name; Start = $r.Start; End = $r.Start.AddSeconds($seconds)
            Seconds = $seconds; Kind = $r.Kind; Origin = $r.Origin; Note = $note
            Raw = $r.Raw; PositionId = $null
        }
    }
    foreach ($p in @($script:creditPositions)) {
        try {
            $day = [datetime]::ParseExact($p.Date, 'yyyy-MM-dd', $Inv)
            if ($day -lt $ws -or $day -ge $we) { continue }
            foreach ($s in @(Get-CreditPositionSessions $day | Where-Object { $_.PositionId -ceq [string]$p.Id })) {
                $note = $s.Note
                $list += [pscustomobject]@{
                    Day = [int][math]::Floor(($day - $ws).TotalDays)
                    Name = $p.Account; Start = $s.Start; End = $s.End
                    Seconds = $s.Seconds; Kind = 'time'; Origin = 'positioned'; Note = $note
                    Raw = $null; PositionId = [string]$p.Id
                }
            }
        } catch { Write-Log "Could not prepare credit position $($p.Id): $($_.Exception.Message)" }
    }
    $now = Get-Date
    if ($script:active -and $script:active.Name -ne 'Lunch' -and $script:active.Start -ge $ws -and $script:active.Start -lt $we) {
        $e = ($now - $script:active.Start).TotalSeconds
        if ($e -gt 0) {
            $list += [pscustomobject]@{
                Day = [int][math]::Floor(($script:active.Start.Date - $ws).TotalDays)
                Name = $script:active.Name; Start = $script:active.Start; End = $now
                Seconds = $e; Kind = 'running'; Origin = 'running'; Note = $null
                Raw = $null; PositionId = $null
            }
        }
    }
    return @($list | Sort-Object Start)
}

# Lunch start times for a week, which break a run of the same account into separate blocks
function Get-WeekLunchStarts($ws) {
    $we = $ws.AddDays(7)
    $starts = @(Read-Sessions | Where-Object { $_.Name -eq 'Lunch' -and -not $_.IsAdjust -and $_.Start -ge $ws -and $_.Start -lt $we } | ForEach-Object { $_.Start })
    if ($script:active -and $script:active.Name -eq 'Lunch' -and $script:active.Start -ge $ws -and $script:active.Start -lt $we) { $starts += $script:active.Start }
    return $starts
}

# One block made from one or more entries: total time, first start, last end, combined notes
function New-EntryBlock($parts) {
    $parts = @($parts)
    $times = @($parts | Where-Object { $_.Kind -eq 'time' -or $_.Kind -eq 'running' })
    $first = if ($times.Count -gt 0) { $times[0] } else { $parts[0] }
    $last  = if ($times.Count -gt 0) { $times[$times.Count - 1] } else { $parts[0] }
    $sum = 0.0
    foreach ($p in $parts) { $sum += $p.Seconds }
    $kind = if (@($parts | Where-Object { $_.Kind -eq 'running' }).Count -gt 0) { 'running' }
            elseif ($times.Count -gt 0) { 'time' }
            else { $parts[0].Kind }
    $notes = @()
    foreach ($p in $times) { if ($p.Note -and -not ($notes -contains $p.Note)) { $notes += $p.Note } }
    # Preserve the classification of legacy merges even when an adjustment
    # is attached to the same block. Their approximate clock span is not real.
    $origins = @($times | ForEach-Object { $_.Origin })
    $origin = if ($origins -contains 'merged') { 'merged' }
              elseif ($origins -contains 'edited') { 'edited' }
              elseif ($origins -contains 'added') { 'added' }
              elseif ($parts.Count -eq 1) { $parts[0].Origin }
              else { 'grouped' }
    return [pscustomobject]@{
        Day = $first.Day; Name = $first.Name; Start = $first.Start; End = $last.End
        Seconds = $sum; Kind = $kind; Origin = $origin; Note = ($notes -join '; '); Parts = $parts
    }
}

# Entries for a week as blocks. With merging on, a run of the same account on the same day becomes
# one block only when the clock intervals are actually adjacent (and not separated
# by Lunch). Legacy merged rows and manual entries stay separate so their dates
# cannot be mistaken for a continuous live recording. Adjustments join the
# account's most recent block on that day.
# Weekly blocks are presentation groups only; no original session is ever removed
# to make one. Explicit groups from entry-groups.csv persist even when automatic
# adjacency grouping is disabled. Rounded/adjusted credits join their nearest
# earlier block unless they have an explicit group assignment.
function Get-EntryBlocks($ws, [switch]$NoRunning) {
    $entries = @(Get-WeekEntries $ws)
    if ($NoRunning) { $entries = @($entries | Where-Object { $_.Kind -ne 'running' }) }
    $lunchStarts = @(Get-WeekLunchStarts $ws)
    $groups = New-Object System.Collections.ArrayList
    $byId = @{}
    $current = $null
    foreach ($e in @($entries | Where-Object { $_.Kind -eq 'time' -or $_.Kind -eq 'running' })) {
        $gid = Get-EntryGroupId $e.Raw
        if ($gid) {
            if (-not $byId.ContainsKey($gid)) {
                $group = New-Object System.Collections.ArrayList
                [void]$groups.Add($group)
                $byId[$gid] = $group
            }
            [void]$byId[$gid].Add($e)
            $current = $null
            continue
        }
        $join = $false
        if ($script:mergeEntries -and $current -and $current.Count -gt 0) {
            $prev = $current[$current.Count - 1]
            if ($prev.Name -eq $e.Name -and $prev.Day -eq $e.Day) {
                $lunchBetween = @($lunchStarts | Where-Object { $_ -ge $prev.Start -and $_ -lt $e.Start }).Count -gt 0
                $gapSeconds = ($e.Start - $prev.End).TotalSeconds
                $observed = ($prev.Origin -eq 'recorded' -or $prev.Kind -eq 'running') -and
                            ($e.Origin -eq 'recorded' -or $e.Kind -eq 'running')
                $join = (-not $lunchBetween) -and $observed -and $gapSeconds -ge -3 -and $gapSeconds -le 5
            }
        }
        if ($join) { [void]$current.Add($e) }
        else {
            $current = New-Object System.Collections.ArrayList
            [void]$current.Add($e)
            [void]$groups.Add($current)
        }
    }
    foreach ($a in @($entries | Where-Object { $_.Kind -ne 'time' -and $_.Kind -ne 'running' })) {
        $gid = Get-EntryGroupId $a.Raw
        $target = $null
        if ($gid -and $byId.ContainsKey($gid)) { $target = $byId[$gid] }
        if (-not $target) {
            foreach ($grp in $groups) {
                $g0 = @($grp | Sort-Object Start)[0]
                if ($g0.Name -eq $a.Name -and $g0.Day -eq $a.Day -and $g0.Start -le $a.Start) {
                    if (-not $target -or $g0.Start -gt @($target | Sort-Object Start)[0].Start) { $target = $grp }
                }
            }
        }
        if ($target) { [void]$target.Add($a) }
        else {
            $target = New-Object System.Collections.ArrayList
            [void]$target.Add($a)
            [void]$groups.Add($target)
        }
    }
    $result = @()
    foreach ($grp in $groups) {
        $parts = @($grp | Sort-Object Start)
        $block = New-EntryBlock $parts
        $gid = Get-EntryGroupId $parts[0].Raw
        $block | Add-Member -NotePropertyName ExplicitGroup -NotePropertyValue ([bool]$gid) -Force
        $result += $block
    }
    return @($result | Sort-Object Start)
}

function Get-EntryTypeLabel($kind) {
    switch ($kind) {
        'rounded up'   { return 'Rounded up' }
        'rounded down' { return 'Rounded down' }
        'adjusted'     { return 'Adjusted' }
        'running'      { return 'Running' }
        'merged'       { return 'Merged credit (unplaced)' }
        'grouped'      { return 'Grouped sessions' }
        'positioned'   { return 'Positioned existing credit' }
        'edited'       { return 'Edited by hand' }
        'added'        { return 'Added by hand' }
        default        { return 'Time' }
    }
}

function Get-WeekEntriesFile($ws) {
    return Join-Path $TimesheetDir ('Week commencing {0} entries.csv' -f $ws.ToString('yyyy-MM-dd', $Inv))
}

# One row per stretch of time, ready to work through when filling in a timesheet
function Write-WeekEntries($ws) {
    try {
        $rows = @(Get-EntryBlocks $ws -NoRunning | ForEach-Object {
            $isTime = $_.Kind -eq 'time'
            $hasAdj = @($_.Parts | Where-Object { $_.Kind -ne 'time' }).Count -gt 0
            [pscustomobject][ordered]@{
                Day      = $_.Start.ToString('ddd dd/MM', $GB)
                Date     = $_.Start.ToString('yyyy-MM-dd', $Inv)
                Account  = $_.Name
                Start    = $(if ($isTime -and $_.Origin -ne 'merged' -and -not $_.ExplicitGroup) { $_.Start.ToString('HH:mm', $Inv) } else { '' })
                End      = $(if ($isTime -and $_.Origin -ne 'merged' -and -not $_.ExplicitGroup) { $_.End.ToString('HH:mm', $Inv) } else { '' })
                Duration = Format-HM $_.Seconds
                Hours    = ([math]::Round($_.Seconds / 3600, 2)).ToString('0.00', $Inv)
                Type     = $(if ($isTime -and $_.ExplicitGroup) { Get-EntryTypeLabel 'grouped' }
                             elseif ($isTime -and $_.Origin -eq 'positioned') { Get-EntryTypeLabel 'positioned' }
                             elseif ($isTime -and $_.Origin -eq 'merged') { Get-EntryTypeLabel 'merged' }
                             elseif ($isTime -and $_.Origin -eq 'edited') { Get-EntryTypeLabel 'edited' }
                             elseif ($isTime -and $_.Origin -eq 'added') { Get-EntryTypeLabel 'added' }
                             elseif ($isTime -and $hasAdj) { 'Time (adjusted)' }
                             else { Get-EntryTypeLabel $_.Kind })
                Parts    = @($_.Parts).Count
                Note     = "$($_.Note)"
            }
        })
        $file = Get-WeekEntriesFile $ws
        if ($rows.Count -gt 0) { $rows | Export-Csv -Path $file -NoTypeInformation -Encoding UTF8 }
        else { Set-Content -Path $file -Value '"Day","Date","Account","Start","End","Duration","Hours","Type","Parts","Note"' -Encoding UTF8 }
        $script:entriesWarned = $false
    } catch {
        if (-not $script:entriesWarned) {
            Write-Log "Could not update the entries file (is it open in Excel?). Will keep retrying. $($_.Exception.Message)"
            $script:entriesWarned = $true
        }
    }
}

function Open-WeekEntriesFile {
    Flush-Pending
    Write-WeekEntries (Get-WeekStart)
    $f = Get-WeekEntriesFile (Get-WeekStart)
    if (Test-Path $f) { Invoke-Item $f }
}

function Refresh-Totals {
    $today = [datetime]::Today
    $ws = Get-WeekStart
    $we = $ws.AddDays(7)
    $script:todayDone      = @{}
    $script:weekDone       = @{}
    $script:monthDone      = @{}
    $script:quarterDone    = @{}
    $script:lunchIntervals = @()
    $script:lunchAdjust    = 0
    $script:todayAccountIntervals = @()
    $ms = New-Object DateTime($today.Year, $today.Month, 1)
    $qs = New-Object DateTime($today.Year, ([int][math]::Floor(($today.Month - 1) / 3) * 3 + 1), 1)
    foreach ($r in @(Read-Sessions)) {
        if ($r.Start -ge $ms) { $script:monthDone[$r.Name] = [double]$script:monthDone[$r.Name] + $r.Seconds }
        if ($r.Start -ge $qs) { $script:quarterDone[$r.Name] = [double]$script:quarterDone[$r.Name] + $r.Seconds }
        if ($r.Start -ge $ws -and $r.Start -lt $we) {
            $script:weekDone[$r.Name] = [double]$script:weekDone[$r.Name] + $r.Seconds
        }
        if ($r.Start.Date -eq $today) {
            $script:todayDone[$r.Name] = [double]$script:todayDone[$r.Name] + $r.Seconds
            if ($r.Name -eq 'Lunch') {
                # Rounding is a straight adjustment; real lunch breaks are time ranges
                if ($r.IsAdjust) { $script:lunchAdjust += $r.Seconds }
                else { $script:lunchIntervals += ,@($r.Start, $r.End) }
            } elseif (-not $r.IsAdjust) {
                $script:todayAccountIntervals += ,@($r.Start, $r.End)
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
        Write-WeekEntries $ws
    } catch {
        if (-not $script:summaryDirty) {
            Write-Log "Could not update the weekly summary (is it open in Excel?). Will keep retrying. $($_.Exception.Message)"
        }
        $script:summaryDirty = $true
    }
}

function Clear-TodayFor($name) {
    $today = (Get-Date).ToString('yyyy-MM-dd', $Inv)
    $done = Invoke-LogChange "clear today's $name" {
        for ($i = $logRows.Count - 1; $i -ge 0; $i--) {
            $r = $logRows[$i]
            if ($r.Date -ne $today -or $r.Account -ne $name) { continue }
            [void]$script:notes.Remove((Get-NoteKey $r.Date $r.Account $r.Start))
            $logRows.RemoveAt($i)
        }
        $script:creditPositions = @($script:creditPositions | Where-Object {
            -not ($_.Date -eq $today -and $_.Account -eq $name)
        })
        return $true
    }
    return $done
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
    # Starting any account carries on from a finished day
    if (Test-DayDone) { Close-AwayInterval }
    $script:active = @{ Name = $script:slots[$i]; Start = Get-Date }
    Save-State
}

# Stopping by hand (switching or pausing) may offer a note, if the stretch was long enough
function Stop-ByUser {
    $a = $script:active
    if (-not $a) { return }
    $end = Get-Date
    Stop-Active $end
    $secs = ($end - $a.Start).TotalSeconds
    if ($a.Name -ne 'Lunch' -and $NoteMinMinutes -gt 0 -and $secs -ge $NoteMinMinutes * 60) {
        $script:pendingNote = @{
            Name     = $a.Name
            DateStr  = $a.Start.ToString('yyyy-MM-dd', $Inv)
            StartStr = $a.Start.ToString('HH:mm:ss', $Inv)
            Seconds  = $secs
        }
        # Shown once the timer has finished redrawing (and the pill has folded away)
        [void]$form.BeginInvoke([System.Windows.Forms.MethodInvoker]{
            if ($script:pendingNote) {
                $n = $script:pendingNote
                $script:pendingNote = $null
                try { Show-NotePrompt $n } catch { Write-Log "Note prompt error: $($_.Exception.Message)" }
            }
        })
    }
}

# Finish the day, or carry on. Finishing stops whatever is running first. It is only offered on a working
# day once the day has started.
function Set-DayDone([bool]$done) {
    if ($done -eq (Test-DayDone)) { return }
    if ($done) {
        $plan = Get-DayPlan ([datetime]::Today)
        if (-not $plan.Working -or (Get-Date) -lt $plan.Start) { return }
        Stop-ByUser
        $script:awayIntervals += ,@{ Start = (Get-Date); End = $null }
        Save-Away
    } else {
        Close-AwayInterval
    }
    Update-View
    Update-DrawerContent
    Request-Redraw
}

function Switch-DayDone { Set-DayDone (-not (Test-DayDone)) }

function Switch-Slot($i) {
    $wasThis = [bool]($script:active -and $script:active.Name -eq $script:slots[$i])
    Stop-ByUser
    if (-not $wasThis) { Start-Slot $i }
}

# ======================================================================
# Numbers for the display
# ======================================================================

function Update-View {
    $now   = Get-Date
    $clock = $now
    $plan  = Get-DayPlan $clock
    $workDay = [bool]$plan.Working
    if ($workDay) { $start = $plan.Start; $end = $plan.End } else { $start = $clock.Date; $end = $clock.Date }
    $cap = Limit-Time $clock $start $end
    $worked = if ($workDay) { ($cap - $start).TotalSeconds } else { 0.0 }

    $onLunch = [bool]($script:active -and $script:active.Name -eq 'Lunch')
    $ints = @($script:lunchIntervals)
    if ($onLunch) { $ints += ,@($script:active.Start, $clock) }
    # Time marked as finished for the day is left out in the same way as lunch
    $dayDone = [bool]($workDay -and (Test-DayDone))
    foreach ($away in @(Get-AwayOn $clock.Date $clock)) { $ints += ,@($away[0], $away[1]) }

    # Billable day, capped at the end time
    $lunchSec = 0
    foreach ($iv in $ints) {
        $a = Limit-Time $iv[0] $start $cap
        $b = Limit-Time $iv[1] $start $cap
        if ($b -gt $a) { $lunchSec += ($b - $a).TotalSeconds }
    }
    $billable = if ($workDay) { [math]::Max(0.0, $worked - $lunchSec - $script:lunchAdjust) } else { 0.0 }

    # Overtime after the end time, less any lunch taken during it (never on a day off)
    $pastEnd = $workDay -and $clock -gt $end
    $otSec = 0
    if ($pastEnd) {
        $otSec = ($clock - $end).TotalSeconds
        foreach ($iv in $ints) {
            $a = Limit-Time $iv[0] $end $clock
            $b = Limit-Time $iv[1] $end $clock
            if ($b -gt $a) { $otSec -= ($b - $a).TotalSeconds }
        }
        $otSec = [math]::Max(0.0, $otSec)
    }
    # A day finished before its end time never turns into overtime
    $overtime = $pastEnd -and -not ($dayDone -and $otSec -lt 1)

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

        # Progress against this account's budget, if it has one
        $bFrac = $null; $bText = $null
        $bud = $script:budgets[$n]
        if ($bud -and $n -ne 'Lunch') {
            $used = if ($bud.Period -eq 'week') { $wk }
                    elseif ($bud.Period -eq 'month') { [double]$script:monthDone[$n] }
                    else { [double]$script:quarterDone[$n] }
            if ($act -and $bud.Period -ne 'week') { $used += ($now - $script:active.Start).TotalSeconds }
            $bFrac = $used / ($bud.Hours * 3600)
            $bText = '{0} of {1}h this {2}' -f (Format-Dur $used), $bud.Hours.ToString('0.##', $Inv), $bud.Period
        }
        if ($script:useRenderer) {
            $rm = New-Object Win.RowModel
            $rm.Name = $n; $rm.Today = $td; $rm.Week = $wk; $rm.Active = $act; $rm.CanRound = $canRound
            $rm.IsLunch = ($n -eq 'Lunch'); $rm.TodayText = Format-Dur $td; $rm.WeekText = Format-Dur $wk
            if ($null -ne $bFrac) { $rm.BudgetFrac = [double]$bFrac }
            $rm.BudgetText = $bText
            $rows += $rm
        } else {
            $rows += @{ Name = $n; Today = $td; Week = $wk; Active = $act; CanRound = $canRound; BudgetFrac = $bFrac; BudgetText = $bText }
        }
    }

    # Account time after the end of the day, so unallocated working time and overtime can be told apart
    $accountOT = 0.0
    if ($overtime) {
        $accIv = @($script:todayAccountIntervals)
        if ($script:active -and $script:active.Name -ne 'Lunch') { $accIv += ,@($script:active.Start, $clock) }
        foreach ($iv in $accIv) {
            $a = Limit-Time $iv[0] $end $clock
            $b = Limit-Time $iv[1] $end $clock
            if ($b -gt $a) { $accountOT += ($b - $a).TotalSeconds }
        }
    }
    $unallocWork = if ($workDay) { [math]::Max(0.0, $billable - ($accountSec - $accountOT)) } else { 0.0 }
    $unallocOT = [math]::Max(0.0, $otSec - $accountOT)

    # Not tracking: the working day has started (or it's overtime) but nothing is running
    $idle = $workDay -and (-not $script:active) -and ($clock -ge $start) -and -not $dayDone
    $before = $workDay -and ($clock -lt $start)
    $status = if ($onLunch) { 'ON LUNCH' }
              elseif ($dayDone) { 'DAY DONE' }
              elseif (-not $workDay) { if ($script:active) { 'DAY OFF, TRACKING' } else { 'DAY OFF' } }
              elseif ($before) { 'STARTS AT ' + $start.ToString('HH:mm', $Inv) }
              elseif ($idle) { 'NOT TRACKING' }
              elseif ($overtime) { 'OVERTIME' }
              else { 'WORKING' }
    # The figure that matters now on the status line; both on hover
    $unallocLine = if ($overtime) { 'UNALLOCATED OT ' + (Format-Clock $unallocOT) }
                   elseif ($workDay) { 'UNALLOCATED ' + (Format-Clock $unallocWork) }
                   else { '' }
    $unallocTip = if (-not $workDay) { $null }
                  elseif ($overtime) { "Working day: $(Format-Clock $unallocWork) unallocated`nOvertime: $(Format-Clock $unallocOT) unallocated" }
                  else { "Working day: $(Format-Clock $unallocWork) unallocated" }
    $activeRow = $rows | Where-Object { $_.Active } | Select-Object -First 1

    # The Finish day button waits until half an hour before the end of the day, and stays once the day is
    # finished so there is a way to carry on
    $showFinish = [bool]($workDay -and ($dayDone -or ($clock -ge $start -and $clock -ge $end.AddMinutes(-$FinishLeadMinutes))))

    $bt = [timespan]::FromSeconds([math]::Floor($billable))
    $ot = [timespan]::FromSeconds([math]::Floor($otSec))
    $pillTime = if ($overtime) { '+{0}:{1:00}' -f [math]::Floor($ot.TotalHours), $ot.Minutes }
                else { '{0}:{1:00}' -f [math]::Floor($bt.TotalHours), $bt.Minutes }
    $script:view = @{
        H           = '{0:00}' -f [math]::Floor($bt.TotalHours)
        M           = '{0:00}' -f $bt.Minutes
        Sec         = '{0:00}' -f $bt.Seconds
        Progress    = $(if ($workDay -and ($end - $start).TotalSeconds -gt 0) { $worked / ($end - $start).TotalSeconds } else { 0.0 })
        Status      = $status
        Unallocated = $unallocLine
        UnallocTip  = $unallocTip
        UnallocText = $(if ($overtime) { (Format-Clock $unallocOT) + ' unallocated OT' } else { (Format-Clock $unallocWork) + ' unallocated' })
        DayOff      = (-not $workDay)
        StartLabel  = $start.ToString('HH:mm', $Inv)
        Idle        = $idle
        PillTime    = $pillTime
        ActiveName  = $(if ($activeRow) { $activeRow.Name } else { $null })
        ActiveToday = $(if ($activeRow) { (Format-Dur $activeRow.Today) + ' today' } else { '' })
        OnLunch     = $onLunch
        Before      = $before
        Overtime    = $overtime
        DayDone     = $dayDone
        DayBtnText  = $(if ($showFinish) { if ($dayDone) { 'Carry on' } else { 'Finish day' } } else { '' })
        OT          = '+{0:00}:{1:00}:{2:00}' -f [math]::Floor($ot.TotalHours), $ot.Minutes, $ot.Seconds
        Rows        = $rows
    }
    $tray.Text = if ($overtime) { 'Account timer: overtime {0}h {1}m' -f [math]::Floor($ot.TotalHours), $ot.Minutes }
                 else { 'Account timer: {0}h {1}m' -f [int]$script:view.H, $script:view.M }

    # Open or close the overtime section
    $newTop = if ($overtime) { $BaseRowTop + $OvertimeH } else { $BaseRowTop }
    $newFinishH = if ($showFinish) { $FinishAreaH } else { 0 }
    if ($newTop -ne $RowTop -or $newFinishH -ne $script:finishH) {
        $script:RowTop = $newTop
        $script:finishH = $newFinishH
        Resize-Form
    }
}

# ======================================================================
# Look and layout (drawn at 320 wide, height depends on the number of rows, then scaled)
# ======================================================================

$g0   = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
$Zoom = $Scale * $g0.DpiX / 96
$script:SystemDpi = $g0.DpiX
$g0.Dispose()

$BaseW  = 320
$BaseRowTop = 88
$OvertimeH  = 32     # height of the overtime section when it appears
$RowTop = $BaseRowTop
$RowH   = 30
$LunchGap = 10   # space separating Lunch from the accounts
$RoundXs  = @{ down = 150; up = 170 }   # positions of the round-down and round-up buttons
$RoundR   = 8
$RoundBlock = 15 * 60   # round up to the next 15 minutes

# Compact pill view
$PillW     = 240
$PillH     = 42
$PillTimeW = 74                    # the time area on the left (click to open the full view)
$PillBtnX  = $PillW - 22           # play/pause button
$PillBtnR  = 14
$PillRowH  = 28                    # rows in the pill's account list
$PillLunchGap = 6
# Collapse button in the full view
$CollapseX = 304
$CollapseY = 15
$CollapseR = 7.5
# Week drawer
$DrawerSideW = 520                  # width of the week drawer beside the timer
$DrawerBtnX = 16                    # week drawer button, top left (mirrors the compact view button)
$DrawerBtnY = 15

# The Finish day button, centred under the Lunch row (the renderer's DayBtnW and DayBtnH match these).
# It shows from FinishLeadMinutes before the end of the day, and the timer grows by FinishAreaH to fit it.
$DayBtn = @{ W = 150; H = 22 }
$FinishLeadMinutes = 30
$FinishAreaH = 30
function Get-DayBtnRect {
    $bx = ($BaseW - $DayBtn.W) / 2
    $by = $BaseH - $DayBtn.H - 8
    return @{ X = $bx; Y = $by; W = $DayBtn.W; H = $DayBtn.H }
}

function Get-PillListH {
    return 4 + $script:slots.Count * $PillRowH + $PillLunchGap + 6
}

function Get-BaseSize {
    if ($script:mode -eq 'pill') {
        $m2 = 2 * $script:pillMargin
        if ($script:pillOpen) { return ($PillW + $m2), ($PillH + (Get-PillListH) + $m2) }
        return ($PillW + $m2), ($PillH + $m2)
    }
    $fm2 = 2 * $script:fullMargin
    if ($script:drawerOpen) { return ($DrawerSideW + $BaseW + $fm2), ($script:drawerH + $fm2) }
    return ($BaseW + $fm2), ($BaseH + $fm2)
}

# Where the pill's header and list sit, depending on whether the list is open and which way
function Get-PillLayout {
    $listH = if ($script:pillOpen) { Get-PillListH } else { 0 }
    $hy = if ($script:pillOpen -and $script:pillUp) { $listH } else { 0 }
    $ly = if ($script:pillUp) { 0 } else { $PillH }
    return @{ HeaderY = $hy; ListY = $ly; ListH = $listH; Height = $PillH + $listH }
}

function Get-PillRowTop($i) {
    $L = Get-PillLayout
    $top = $L.ListY + 4 + $i * $PillRowH
    if ($script:slots[$i] -eq 'Lunch') { $top += $PillLunchGap }
    return $top
}
Load-Accounts
$BaseH  = $RowTop + $script:slots.Count * $RowH + $LunchGap + 6 + $script:finishH

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
; An optional NotTracking line sets the colours for when nothing is being tracked. Without it,
; not tracking uses the Overtime colours and overtime uses a purple version of them.
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

# Colour helpers for deriving the overtime colours: the same lightness and strength, a different hue
function Convert-HslToColour([double]$h, [double]$s, [double]$l) {
    if ($s -le 0) { $v = [int][math]::Round($l * 255); return [System.Drawing.Color]::FromArgb($v, $v, $v) }
    $q = if ($l -lt 0.5) { $l * (1 + $s) } else { $l + $s - $l * $s }
    $p = 2 * $l - $q
    $hk = $h / 360.0
    $rgb = @()
    foreach ($t in @(($hk + 1.0 / 3), $hk, ($hk - 1.0 / 3))) {
        $tc = $t
        if ($tc -lt 0) { $tc += 1 }
        if ($tc -gt 1) { $tc -= 1 }
        $v = if ($tc -lt 1.0 / 6) { $p + ($q - $p) * 6 * $tc }
             elseif ($tc -lt 0.5) { $q }
             elseif ($tc -lt 2.0 / 3) { $p + ($q - $p) * (2.0 / 3 - $tc) * 6 }
             else { $p }
        $rgb += [int][math]::Round([math]::Max(0.0, [math]::Min(1.0, $v)) * 255)
    }
    return [System.Drawing.Color]::FromArgb($rgb[0], $rgb[1], $rgb[2])
}

function Get-ShiftedColour($c, [double]$degrees) {
    $h = ($c.GetHue() + $degrees + 360) % 360
    return Convert-HslToColour $h $c.GetSaturation() $c.GetBrightness()
}

function Get-ShiftedState($state, [double]$degrees) {
    return @{
        Panel     = Get-ShiftedColour $state.Panel $degrees
        Circle    = Get-ShiftedColour $state.Circle $degrees
        CircleHot = Get-ShiftedColour $state.CircleHot $degrees
        Fill      = Get-ShiftedColour $state.Fill $degrees
    }
}

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
        # Not tracking: its own colours if the theme gives them; otherwise the theme's Overtime colours,
        # with overtime itself shifted to a purple version of them so the two can be told apart
        $ntParts = @("$($kv['nottracking'])" -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        if ($ntParts.Count -eq 4) {
            $notTracking = New-State $ntParts[0] $ntParts[1] $ntParts[2] $ntParts[3]
            $overtimeState = $states.overtime
        } else {
            $notTracking = $states.overtime
            $overtimeState = Get-ShiftedState $states.overtime -70
        }
        return @{ Text = $text; Band = $band; running = $states.running; paused = $states.paused; overtime = $overtimeState; nottracking = $notTracking }
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

# A small clock badge in the theme's colours, used for windows, dialogs and the tray
$script:appIcon = $null
function New-AppIcon($theme, $textColour) {
    $size = 64
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $path = New-RoundedPath 2 2 60 60 14
    $bg = New-Object System.Drawing.SolidBrush $theme.Panel
    $g.FillPath($bg, $path)
    $ring = New-Object System.Drawing.Pen($theme.Fill, [float]6)
    $g.DrawEllipse($ring, [float]13, [float]13, [float]38, [float]38)
    $hand = New-Object System.Drawing.Pen($textColour, [float]5)
    $hand.StartCap = 'Round'; $hand.EndCap = 'Round'
    $g.DrawLine($hand, [float]32, [float]32, [float]32, [float]20)
    $g.DrawLine($hand, [float]32, [float]32, [float]41, [float]37)
    foreach ($o in @($bg, $ring, $hand, $path, $g)) { $o.Dispose() }
    $icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
    return $icon
}

function Update-AppIcon {
    try {
        $script:appIcon = New-AppIcon $Themes.running $cWhite
        if ($tray) { $tray.Icon = $script:appIcon }
        if ($script:weekForm -and -not $script:weekForm.IsDisposed) { $script:weekForm.Icon = $script:appIcon }
    } catch { Write-Log "Icon error: $($_.Exception.Message)" }
}

# Windows 11: title bar in the theme's colours (ignored on Windows 10)
function Set-TitleBarColours($win, $theme) {
    try {
        $toRef = { param($col) [int]$col.R -bor ([int]$col.G -shl 8) -bor ([int]$col.B -shl 16) }
        $caption = & $toRef $theme.Panel
        $textCol = & $toRef $cWhite
        $border = & $toRef $theme.Circle
        [void][Win.Dwm]::DwmSetWindowAttribute($win.Handle, 35, [ref]$caption, 4)
        [void][Win.Dwm]::DwmSetWindowAttribute($win.Handle, 36, [ref]$textCol, 4)
        [void][Win.Dwm]::DwmSetWindowAttribute($win.Handle, 34, [ref]$border, 4)
    } catch {}
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

    if ($tray) { Update-AppIcon }
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
$fPillTime = New-Object System.Drawing.Font('Segoe UI Semibold', 19, [System.Drawing.FontStyle]::Regular, $px)
$fPillSub  = New-Object System.Drawing.Font('Segoe UI', 10.5, [System.Drawing.FontStyle]::Regular, $px)
$fDrawerBtn = New-Object System.Drawing.Font('Segoe UI Semibold', 11.5, [System.Drawing.FontStyle]::Regular, $px)
$fDrawerText = New-Object System.Drawing.Font('Segoe UI', 11.5, [System.Drawing.FontStyle]::Regular, $px)
$fDrawerTextBold = New-Object System.Drawing.Font('Segoe UI Semibold', 11.5, [System.Drawing.FontStyle]::Regular, $px)
# The timeline's text is a little larger than the rest of the drawer's, as it has to be read at a glance
$fTimeline = New-Object System.Drawing.Font('Segoe UI', 13, [System.Drawing.FontStyle]::Regular, $px)
$fTimelineBold = New-Object System.Drawing.Font('Segoe UI Semibold', 13, [System.Drawing.FontStyle]::Regular, $px)
$fTimelineSmall = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Regular, $px)

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

if ($script:useRenderer) {
    try {
        $script:renderer = New-Object Win.FullRenderer
        $script:renderer.FontDigits = $fDigits
        $script:renderer.FontLabel = $fLabel
        $script:renderer.FontRow = $fRow
        $script:renderer.FontOvertime = $fOvertime
        $script:renderer.FontButton = $fDrawerBtn
        $script:renderer.FontSmall = $fPillSub
        $script:renderer.CollapseR = [float]$CollapseR
        $script:renderer.CollapseX = [float]$CollapseX
        $script:renderer.CollapseY = [float]$CollapseY
        $script:renderer.DrawerBtnX = [float]$DrawerBtnX
        $script:renderer.DrawerBtnY = [float]$DrawerBtnY
        $script:renderer.FontDrawer = $fDrawerText
        $script:renderer.FontDrawerBold = $fDrawerTextBold
        $script:renderer.FontTimeline = $fTimeline
        $script:renderer.FontTimelineBold = $fTimelineBold
        $script:renderer.FontTimelineSmall = $fTimelineSmall
        $script:renderer.DrawerW = [float]$DrawerSideW
    } catch {
        $script:useRenderer = $false
        $script:rendererError = $_.Exception.Message
    }
}

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
    $script:pillMargin = 10
    if ($script:useRenderer) {
        # Room for the full view's shadow, so the window grows slightly to fit it
        $script:fullMargin = 10
        $bw, $bh = Get-BaseSize
        $form.ClientSize = New-Object System.Drawing.Size([int]($bw * $Zoom), [int]($bh * $Zoom))
    }
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
                $bw, $bh = Get-BaseSize
                $form.ClientSize = New-Object System.Drawing.Size([int]($bw * $z), [int]($bh * $z))
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
$BudgetWarnColour = [System.Drawing.ColorTranslator]::FromHtml('#ffc107')
$BudgetOverColour = [System.Drawing.ColorTranslator]::FromHtml('#ff5252')
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

# Red while overtime or not tracking, orange on lunch, otherwise the working colours
function Get-StateTheme($v) {
    if ($v.OnLunch) { return $Themes.paused }
    if ($v.DayDone) { return $Themes.running }
    if ($v.Idle) { return $(if ($Themes.nottracking) { $Themes.nottracking } else { $Themes.overtime }) }
    if ($v.Overtime) { return $Themes.overtime }
    return $Themes.running
}

function Draw-Full($g, [bool]$layered) {
    try {
        $g.SmoothingMode = 'AntiAlias'
        $g.TextRenderingHint = 'ClearTypeGridFit'
        $g.ScaleTransform($Zoom, $Zoom)
        $v = $script:view
        if (-not $v) { return }

        $theme = Get-StateTheme $v
        $alpha = if ($layered) { $script:listAlpha } else { 255 }
        if ($alpha -lt 255 -and $script:blurOn) { $alpha = [int]($alpha * $BlurTint) }
        if ($script:drawerOpen) {
            # The timer sits within a larger L-shaped window that includes the drawer
            $g.TranslateTransform([float]$script:timerOffX, [float]$script:timerOffY)
            if ($script:ct) {
                foreach ($layerG in @($script:ct.G, $script:ct.M)) { $layerG.TranslateTransform([float]$script:timerOffX, [float]$script:timerOffY) }
            }
            if ($layered) {
                $script:shapePath = New-DrawerShape
                $shift = New-Object System.Drawing.Drawing2D.Matrix
                $shift.Translate([float](-$script:timerOffX), [float](-$script:timerOffY))
                $script:shapePath.Transform($shift)
                $shift.Dispose()
            } else { $script:shapePath = $null }
        } else {
            $script:shapePath = if ($layered) { New-RoundedPath 0 0 $BaseW $BaseH 8 } else { $null }
        }

        # Solid main timer on top, account list below (see-through if chosen)
        $split = $RowTop - 17
        $bg = New-Object System.Drawing.SolidBrush $theme.Panel
        Fill-Area $g $bg 0 0 $BaseW $split
        $bg.Dispose()
        $bg = New-Object System.Drawing.SolidBrush ($C::FromArgb($alpha, $theme.Panel))
        Fill-Area $g $bg 0 $split $BaseW ($BaseH - $split)
        $bg.Dispose()

        # Week drawer button (top left, mirroring the compact view button at top right)
        $dbr = if ($script:handleHover -or $script:drawerOpen) { $bRowOn } else { $bRowHover }
        $g.FillEllipse($dbr, ($DrawerBtnX - $CollapseR), ($DrawerBtnY - $CollapseR), (2 * $CollapseR), (2 * $CollapseR))
        $dpen = New-Object System.Drawing.Pen($cWhite, 1.2)
        $dpen.StartCap = 'Round'; $dpen.EndCap = 'Round'
        foreach ($ly in @(-2.6, 0, 2.6)) { $g.DrawLine($dpen, [float]($DrawerBtnX - 3), [float]($DrawerBtnY + $ly), [float]($DrawerBtnX + 3), [float]($DrawerBtnY + $ly)) }
        $dpen.Dispose()

        # Collapse to the compact view
        $cbr = if ($script:collapseHover) { $bRowOn } else { $bRowHover }
        $g.FillEllipse($cbr, ($CollapseX - $CollapseR), ($CollapseY - $CollapseR), (2 * $CollapseR), (2 * $CollapseR))
        $cpen = New-Object System.Drawing.Pen($cWhite, 1.3)
        $cpen.StartCap = 'Round'; $cpen.EndCap = 'Round'
        $g.DrawLine($cpen, ($CollapseX - 3), $CollapseY, ($CollapseX + 3), $CollapseY)
        $cpen.Dispose()

        # Main timer
        $values = @($v.H, $v.M, $v.Sec)
        $digitBrush = if ($v.Before) { $bDim } else { $bWhite }
        for ($i = 0; $i -lt 3; $i++) {
            $g.DrawString($values[$i], $fDigits, $digitBrush, (New-Object $RF $colX[$i], 2, $colW, 52), $sfCentre)
        }
        foreach ($x in @(107, 213)) {
            $g.FillEllipse($bFaint, ($x - 1.3), 22, 2.6, 2.6)
            $g.FillEllipse($bFaint, ($x - 1.3), 33, 2.6, 2.6)
        }

        # Status and unallocated time
        $statusBrush = if ($v.OnLunch) { $bWhite } else { $bFaint }
        $g.DrawString($v.Status, $fLabel, $statusBrush, (New-Object $RF 10, 51, 150, 14), $sfLeft)
        $g.DrawString($v.Unallocated, $fLabel, $bFaint, (New-Object $RF 160, 51, 150, 14), $sfRight)

        # Overtime counter
        if ($v.Overtime) {
            $g.DrawLine($pSep, 10, 68, 310, 68)
            $g.DrawString($v.OT, $fOvertime, $bWhite, (New-Object $RF 0, 69, $BaseW, ($OvertimeH - 2)), $sfCentre)
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
                $gripBrush = if ($dragging -or $script:gripHover -eq $i) { $bWhite } else { $bGrip }
                foreach ($gx in @(6, 10)) {
                    foreach ($gy in @(-4, 0, 4)) { $g.FillEllipse($gripBrush, ($gx - 1.1), ($cy + $gy - 1.1), 2.2, 2.2) }
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
            $nameY = if ($null -ne $r.BudgetFrac) { $y0 - 3 } else { $y0 }
            Draw-ListText $g $r.Name $fRow $bWhite (New-Object $RF 46, $nameY, $nameW, $RowH) $sfName

            # Budget progress: a fine line under the name, amber from 80%, red once over
            if ($null -ne $r.BudgetFrac) {
                $barW = 60
                $by = [float]($y0 + $RowH - 7)
                $g.FillRectangle($bTrack, [float]46, $by, [float]$barW, [float]3)
                $bc = if ($r.BudgetFrac -ge 1) { $BudgetOverColour } elseif ($r.BudgetFrac -ge 0.8) { $BudgetWarnColour } else { $theme.Fill }
                $bbr = New-Object System.Drawing.SolidBrush $bc
                $fillW = $barW * [math]::Min(1.0, $r.BudgetFrac)
                if ($r.BudgetFrac -gt 0 -and $fillW -lt 2) { $fillW = 2 }
                $g.FillRectangle($bbr, [float]46, $by, [float]$fillW, [float]3)
                $bbr.Dispose()
            }
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

        # Finish day button, centred under the Lunch row
        if ($v.DayBtnText) {
            $dbRect = Get-DayBtnRect
            $dbr = New-Object $RF ([float]$dbRect.X), ([float]$dbRect.Y), ([float]$dbRect.W), ([float]$dbRect.H)
            $dbp = New-RoundedPath $dbr.X $dbr.Y $dbr.Width $dbr.Height ([float]($dbr.Height / 2))
            $dbb = New-Object System.Drawing.SolidBrush ($(if ($script:dayBtnHover) { $theme.CircleHot } else { $theme.Circle }))
            $g.FillPath($dbb, $dbp)
            $dbb.Dispose(); $dbp.Dispose()
            Draw-ListText $g $v.DayBtnText $fLabel $bWhite $dbr $sfCentre
        }

        if ($script:drawerOpen) {
            $g.TranslateTransform([float](-$script:timerOffX), [float](-$script:timerOffY))
            if ($layered) { Draw-DrawerCached $g $theme } else { Draw-Drawer $g $theme }
        }
    } catch {
        Write-Log "Drawing error: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"
    } finally {
        if ($script:shapePath) { $script:shapePath.Dispose(); $script:shapePath = $null }
    }
}

# ---- Week drawer ----

# The outline of timer and drawer together: an L shape with a curved inner corner.
# Built for 'drawer below, timer on the right', then mirrored for the other cases.
function New-DrawerShape {
    # Built for 'drawer on the left, timer on the right', then mirrored if the drawer is on the right
    $jx = [float]($DrawerSideW - 1)                 # where drawer meets timer
    $W  = [float]($DrawerSideW + $BaseW - 2)
    $H1 = [float]($script:drawerH - 2)              # drawer (at least as tall as the timer)
    $H2 = [float]($BaseH - 2)                       # timer
    $d  = [float]16
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc([float]0, [float]0, $d, $d, [float]180, [float]90)
    $p.AddArc([float]($W - $d), [float]0, $d, $d, [float]270, [float]90)
    $p.AddArc([float]($W - $d), [float]($H2 - $d), $d, $d, [float]0, [float]90)
    if (($H1 - $H2) -ge $d) {
        # Drawer is taller: curve in under the timer, then down to the drawer's own corner
        $p.AddArc($jx, $H2, $d, $d, [float]270, [float]-90)
        $p.AddArc([float]($jx - $d), [float]($H1 - $d), $d, $d, [float]0, [float]90)
    }
    $p.AddArc([float]0, [float]($H1 - $d), $d, $d, [float]90, [float]90)
    $p.CloseFigure()
    if ($script:timerOffX -eq 0) {
        $mx = New-Object System.Drawing.Drawing2D.Matrix([float]-1, [float]0, [float]0, [float]1, $W, [float]0)
        $p.Transform($mx); $mx.Dispose()
    }
    $mt = New-Object System.Drawing.Drawing2D.Matrix
    $mt.Translate([float]1, [float]1)
    $p.Transform($mt); $mt.Dispose()
    return $p
}

# Budgets for the current period, for the drawer
function Get-BudgetLines {
    $out = @()
    foreach ($n in @($script:accounts | ForEach-Object { $_.Name })) {
        $b = $script:budgets[$n]
        if (-not $b) { continue }
        $used = if ($b.Period -eq 'week') { [double]$script:weekDone[$n] }
                elseif ($b.Period -eq 'month') { [double]$script:monthDone[$n] }
                else { [double]$script:quarterDone[$n] }
        if ($script:active -and $script:active.Name -eq $n) { $used += ((Get-Date) - $script:active.Start).TotalSeconds }
        $out += @{ Name = $n; Frac = ($used / ($b.Hours * 3600)); Period = $b.Period
                   Short = ('{0} of {1}h' -f (Format-Dur $used), $b.Hours.ToString('0.##', $Inv))
                   Text = ('{0} of {1}h this {2}' -f (Format-Dur $used), $b.Hours.ToString('0.##', $Inv), $b.Period) }
    }
    return $out
}

$DrawerRowH = 19
$TimelineMinH = 420     # the timeline drawer is at least this tall, so there is room to see a few hours at once
$TlMinPx = 24; $TlMaxPx = 240

# The timeline needs the compiled drawing, so without it the drawer stays on the week table
function Get-DrawerMode {
    if ($script:useRenderer -and $script:drawerMode -eq 'timeline') { return 'timeline' }
    return 'week'
}

function Get-DrawerAccountColour($name, $names) {
    $names = @($names)
    $i = [array]::IndexOf($names, $name)
    if ($i -lt 0) { $i = $names.Count + [math]::Abs($name.GetHashCode()) % 5 }
    return $AccountColours[$i % $AccountColours.Count]
}

function Get-DrawerDayTitle([datetime]$day) {
    if ($day.Date -eq [datetime]::Today) { return 'Today, ' + $day.ToString('d MMMM', $GB) }
    if ($day.Date -eq [datetime]::Today.AddDays(-1)) { return 'Yesterday, ' + $day.ToString('d MMMM', $GB) }
    return $day.ToString('ddd d MMMM', $GB)
}

# The stretches of the strip with nothing tracked, a minute or longer, up to now for today
function Get-TimelineGaps($blocks, [double]$axisStartH, [double]$axisEndH, [double]$capH) {
    $minH = 1.0 / 60.0
    $out = @()
    $cursor = $axisStartH
    $limit = [math]::Min($axisEndH, $capH)
    foreach ($b in @($blocks | Sort-Object StartH)) {
        $gapEnd = [math]::Min([double]$b.StartH, $limit)
        if ($gapEnd - $cursor -ge $minH) { $out += [pscustomobject]@{ StartH = $cursor; EndH = $gapEnd } }
        $cursor = [math]::Max($cursor, [double]$b.EndH)
    }
    if ($limit - $cursor -ge $minH) { $out += [pscustomobject]@{ StartH = $cursor; EndH = $limit } }
    return $out
}

# Stretches that overlap (a merged or hand-edited entry can run into its neighbours) are laid out side by
# side in lanes, like a calendar, so none is hidden behind another. Blocks that overlap by only a few seconds,
# which rounding in the log can cause, still count as touching. Each block gets its Lane and the number of
# Lanes across its overlapping group. The blocks must be in start order.
function Set-TimelineLanes($blocks) {
    $bl = @($blocks)
    $slack = 3.0 / 3600.0
    $laneEnds = @()
    $groupEnd = -1.0
    $groupStart = 0
    for ($i = 0; $i -le $bl.Count; $i++) {
        $atEnd = ($i -eq $bl.Count)
        if ($atEnd -or [double]$bl[$i].StartH -ge $groupEnd - $slack) {
            # A new group begins, so the one before is finished and every block in it learns how many lanes it has
            for ($j = $groupStart; $j -lt $i; $j++) { $bl[$j] | Add-Member -NotePropertyName Lanes -NotePropertyValue ([math]::Max(1, $laneEnds.Count)) -Force }
            if ($atEnd) { break }
            $laneEnds = @(); $groupEnd = -1.0; $groupStart = $i
        }
        $lane = -1
        for ($l = 0; $l -lt $laneEnds.Count; $l++) { if ($laneEnds[$l] - $slack -le [double]$bl[$i].StartH) { $lane = $l; break } }
        if ($lane -lt 0) { $lane = $laneEnds.Count; $laneEnds += 0.0 }
        $laneEnds[$lane] = [double]$bl[$i].EndH
        $groupEnd = [math]::Max($groupEnd, [double]$bl[$i].EndH)
        $bl[$i] | Add-Member -NotePropertyName Lane -NotePropertyValue $lane -Force
    }
}

# The next (or previous) block in the same lane, which is the one whose edge limits this block's
function Get-LaneNeighbour($blocks, [int]$i, [int]$dir) {
    $bl = @($blocks)
    $lane = [int]$bl[$i].Lane
    for ($j = $i + $dir; $j -ge 0 -and $j -lt $bl.Count; $j += $dir) {
        if ([int]$bl[$j].Lane -eq $lane) { return $j }
    }
    return -1
}

# Whether a point a fraction of the way across the column is within a block's lane
function Test-InLane($block, $fx) {
    $lanes = [math]::Max(1, [int]$block.Lanes)
    $lane = [int]$block.Lane
    return ($fx -ge ($lane / $lanes) -and $fx -le (($lane + 1) / $lanes))
}

# Which edge of a block is at this time, if any. Two editable blocks that touch (within a minute) share one
# edge, 'join', owned by the left block, so dragging it moves the change-over between them.
function Get-TimelineEdgeAt($blocks, [double]$h, [double]$tolH, $fx = $null) {
    $bl = @($blocks)
    $minH = 1.0 / 60.0
    $best = $null; $bestD = $tolH + 1.0
    for ($i = 0; $i -lt $bl.Count; $i++) {
        if (-not $bl[$i].Editable) { continue }
        # With blocks side by side, only the lane under the pointer counts (fx is how far across the column it is, 0 to 1)
        if ($null -ne $fx -and -not (Test-InLane $bl[$i] $fx)) { continue }
        foreach ($e in @('start', 'end')) {
            $at = if ($e -eq 'start') { [double]$bl[$i].StartH } else { [double]$bl[$i].EndH }
            $d = [math]::Abs($at - $h)
            if ($d -le $tolH -and $d -lt $bestD) { $best = @{ Index = $i; Edge = $e }; $bestD = $d }
        }
    }
    if (-not $best) { return $null }
    $i = $best.Index
    if ($best.Edge -eq 'end') {
        $n = Get-LaneNeighbour $bl $i 1
        if ($n -ge 0 -and $bl[$n].Editable -and ($bl[$n].StartH - $bl[$i].EndH) -le $minH -and ($bl[$n].StartH - $bl[$i].EndH) -ge -$minH) {
            return @{ Index = $i; Edge = 'join' }
        }
    }
    if ($best.Edge -eq 'start') {
        $pv = Get-LaneNeighbour $bl $i -1
        if ($pv -ge 0 -and $bl[$pv].Editable -and ($bl[$i].StartH - $bl[$pv].EndH) -le $minH -and ($bl[$i].StartH - $bl[$pv].EndH) -ge -$minH) {
            return @{ Index = $pv; Edge = 'join' }
        }
    }
    return $best
}

# Where an edge ends up when dragged to a time. The time snaps to the grid (in minutes), then is kept
# between its neighbours, at least a minute from the block's other edge, and no later than the cap
# (now, for today). Returns the new hours for each edge that moves, and whether anything changed.
function Get-TimelineDrag($blocks, [int]$i, [string]$edge, [double]$targetH, [double]$snapMin, [double]$capH) {
    $bl = @($blocks)
    $minH = 1.0 / 60.0
    $snap = $snapMin / 60.0
    $t = [math]::Round($targetH / $snap) * $snap
    $cur = 0.0
    # An edge stops at the neighbour in its own lane; blocks in other lanes overlap it by design
    $nextIdx = Get-LaneNeighbour $bl $i 1
    $prevIdx = Get-LaneNeighbour $bl $i -1
    switch ($edge) {
        'join'  { $lo = $bl[$i].StartH + $minH; $hi = $bl[$nextIdx].EndH - $minH; $cur = $bl[$i].EndH }
        'start' { $lo = $(if ($prevIdx -ge 0) { [double]$bl[$prevIdx].EndH } else { 0.0 }); $hi = $bl[$i].EndH - $minH; $cur = $bl[$i].StartH }
        default { $lo = $bl[$i].StartH + $minH; $hi = $(if ($nextIdx -ge 0) { [double]$bl[$nextIdx].StartH } else { 24.0 }); $cur = $bl[$i].EndH }
    }
    $hi = [math]::Min([double]$hi, $capH)
    if ($hi -lt $lo) { $hi = $lo }
    $t = [math]::Min([math]::Max($t, $lo), $hi)
    $moves = @()
    if ($edge -eq 'join') {
        $moves += @{ Index = $i; Edge = 'end'; H = $t }
        $moves += @{ Index = $nextIdx; Edge = 'start'; H = $t }
    } else {
        $moves += @{ Index = $i; Edge = $edge; H = $t }
    }
    return [pscustomobject]@{ Moves = $moves; H = $t; Changed = ([math]::Abs($t - $cur) -gt 0.0001) }
}

# One day's stretches of time for the timeline strip, in order. Lunch is included because it shows as a
# hatched gap. Rounding and other adjustments are left out, as they were never a stretch of real time.
# Times for the drawing are hours since midnight. The strip covers the working hours plus anything
# tracked outside them, with an hour either side, to whole hours and at least four hours wide.
function Get-DayTimeline([datetime]$day) {
    $day = $day.Date
    $now = Get-Date
    $isToday = ($day -eq $now.Date)
    $blocks = @()
    $unplacedSecs = 0.0
    foreach ($r in @(Read-Sessions)) {
        if ($r.Start.Date -ne $day) { continue }
        # Merged source credit is accounted for in totals. Its exact positions are
        # projections kept separately, so never invent a contiguous span here.
        if ($r.Origin -eq 'merged') { continue }
        if ($r.IsAdjust -or $r.Seconds -le 0) { continue }
        # Include every real interval, even those shorter than a minute. The ledger
        # already counts them, and they must not become fillable gaps on the timeline.
        $isLunch = ($r.Name -eq 'Lunch')
        $note = $null
        if (-not $isLunch) { $note = $script:notes[(Get-NoteKey $r.Start.ToString('yyyy-MM-dd', $Inv) $r.Name $r.Start.ToString('HH:mm:ss', $Inv))] }
        $blocks += [pscustomobject]@{ Name = $r.Name; Start = $r.Start; End = $r.End; Seconds = $r.Seconds
                                      Kind = $(if ($isLunch) { 'lunch' } else { 'time' }); Note = $note; Raw = $r.Raw
                                      Origin = $r.Origin; Label = "$($r.Raw.End)" }
    }
    # Explicit positions for previously merged credit have zero additional billable
    # effect, but are real user-specified clock coordinates and must occupy gaps.
    foreach ($p in @(Get-CreditPositionSessions $day)) {
        $note = $p.Note
        $blocks += [pscustomobject]@{
            Name = $p.Name; Start = $p.Start; End = $p.End; Seconds = $p.Seconds
            Kind = 'time'; Origin = 'positioned'; Note = $note; Raw = $null
            PositionId = $p.PositionId; Label = 'positioned'
        }
    }
    # Whatever is running right now (from midnight if it carried on past it)
    if ($isToday -and $script:active) {
        $from = $script:active.Start
        if ($from -lt $day) { $from = $day }
        $secs = ($now - $from).TotalSeconds
        if ($secs -gt 0) {
            $isLunch = ($script:active.Name -eq 'Lunch')
            $blocks += [pscustomobject]@{ Name = $script:active.Name; Start = $from; End = $now; Seconds = $secs
                                          Kind = $(if ($isLunch) { 'lunch' } else { 'running' }); Origin = 'running'; Note = $null; Raw = $null }
        }
    }
    $blocks = @($blocks | Sort-Object Start, End)
    foreach ($b in $blocks) {
        $label = if ($b.Kind -eq 'lunch') { 'Lunch' } else { $b.Name }
        $span = '{0} to {1}' -f $b.Start.ToString('HH:mm', $Inv), $b.End.ToString('HH:mm', $Inv)
        $text = "$label`n$span ($(Format-Dur $b.Seconds))"
        if ($b.Kind -eq 'running') { $text += "`nRunning now" }
        if ($b.Note) { $text += "`n" + $b.Note }
        # Added and edited rows have an explicit position but are not live recordings.
        if ($b.Origin -eq 'edited') { $text += "`nEdited by hand (dashed outline)" }
        elseif ($b.Origin -eq 'added') { $text += "`nAdded by hand (dashed outline)" }
        elseif ($b.Origin -eq 'positioned') { $text += "`nPositioned from existing merged credit: no additional hours (dashed outline)" }
        $b | Add-Member -NotePropertyName Tip -NotePropertyValue $text -Force
        # Hours since midnight for the drawing and dragging. What is running now has no row in the log
        # yet, so it can't be dragged.
        $b | Add-Member -NotePropertyName StartH -NotePropertyValue (($b.Start - $day).TotalHours) -Force
        # A stretch edited to run past midnight is drawn to the end of the day, so it can never be out of view
        $b | Add-Member -NotePropertyName EndH -NotePropertyValue ([math]::Min(24.0, ($b.End - $day).TotalHours)) -Force
        $editable = ($null -ne $b.Raw -or $b.Origin -eq 'positioned')
        $b | Add-Member -NotePropertyName Editable -NotePropertyValue $editable -Force
    }

    Set-TimelineLanes $blocks

    $unplacedByAccount = Get-UnplacedCredits $day
    $unplacedSecs = 0.0
    foreach ($n in @($unplacedByAccount.Keys)) { $unplacedSecs += [double]$unplacedByAccount[$n] }
    $plan = Get-DayPlan $day
    $lo = $null; $hi = $null
    if ($plan.Working) { $lo = $plan.Start; $hi = $plan.End }
    foreach ($b in $blocks) {
        if ($null -eq $lo -or $b.Start -lt $lo) { $lo = $b.Start }
        if ($null -eq $hi -or $b.End -gt $hi) { $hi = $b.End }
        if ($hi -gt $day.AddDays(1)) { $hi = $day.AddDays(1) }
    }
    if ($isToday -and $null -ne $hi -and $now -gt $hi) { $hi = $now }
    if ($null -eq $lo) { $lo = $day.AddHours(8); $hi = $day.AddHours(18) }
    # An hour either side of the working day and what was tracked, so there is room to add time around it
    $startH = [math]::Max(0.0, [math]::Floor(($lo - $day).TotalHours) - 1)
    $endH = [math]::Min(24.0, [math]::Ceiling(($hi - $day).TotalHours) + 1)
    if ($endH - $startH -lt 4) {
        $endH = [math]::Min(24.0, $startH + 4)
        $startH = $endH - 4
    }
    $nowH = ($now - $day).TotalHours
    $capH = if ($isToday) { $nowH } else { 24.0 }
    $empty = if ($blocks.Count -gt 0) { '' }
             elseif ($unplacedSecs -gt 0) { 'Merged time is credited but has no clock position' }
             elseif (-not $plan.Working) { 'Day off' }
             elseif ($isToday) { 'Nothing tracked yet' }
             else { 'Nothing tracked on this day' }
    return [pscustomobject]@{
        Day = $day; Blocks = $blocks; AxisStartH = [double]$startH; AxisEndH = [double]$endH
        UnplacedSeconds = [double]$unplacedSecs
        Gaps = @(Get-TimelineGaps $blocks ([double]$startH) ([double]$endH) $capH)
        HasWork = [bool]$plan.Working
        WorkStartH = $(if ($plan.Working) { ($plan.Start - $day).TotalHours } else { 0.0 })
        WorkEndH = $(if ($plan.Working) { ($plan.End - $day).TotalHours } else { 0.0 })
        ShowNow = [bool]($isToday -and $nowH -ge $startH -and $nowH -le $endH)
        NowH = [double]$nowH; EmptyText = $empty
    }
}

function Measure-DrawerHeight {
    $d = $script:drawerData
    if ((Get-DrawerMode) -eq 'timeline') {
        # A fixed height, with the day scrolling inside it (or taller if the account totals need more)
        $lines = @($script:drawerLegend).Count
        return [math]::Max($TimelineMinH, 10 + 34 + [Win.FullRenderer]::TabsH + [Win.FullRenderer]::TlRowH * $lines + 12)
    }
    $rows = 1 + @($d.Names).Count + 3
    $h = 10 + 34 + $DrawerRowH * $rows + 10
    if ($script:useRenderer) { $h += [Win.FullRenderer]::TabsH }
    $nb = @($script:drawerBudgets).Count
    if ($nb -gt 0) { $h += 8 + 18 + 20 * $nb }
    return $h
}

function Get-DrawerHeight {
    $content = Measure-DrawerHeight
    if ($content -lt ($BaseH + 16)) { return $BaseH }
    return $content
}

# The lines under the timeline strip: each account used that day, then total, lunch and unallocated.
# The figures come from the week's data, so they include rounding and match the week table.
function Get-DayLegend([datetime]$day, $data, [datetime]$ws, $timeline) {
    $idx = [int][math]::Floor(($day.Date - $ws).TotalDays)
    $out = @()
    $sum = 0.0
    foreach ($n in @($data.Names)) {
        $secs = [double]$data.Accounts[$n][$idx]
        $sum += $secs
        $txt = Format-Hours $secs
        if ($txt) { $out += [pscustomobject]@{ Name = $n; Kind = 'account'; Colour = (Get-DrawerAccountColour $n $data.Names); Total = $txt } }
    }
    $sumText = Format-Hours $sum
    if ($sumText) { $out += [pscustomobject]@{ Name = 'Total'; Kind = 'total'; Colour = $null; Total = $sumText } }
    $lunchText = Format-Hours ([double]$data.Lunch[$idx])
    if ($lunchText) { $out += [pscustomobject]@{ Name = 'Lunch'; Kind = 'lunch'; Colour = $null; Total = $lunchText } }
    $unallocText = Format-Hours ([double]$data.Unalloc[$idx])
    if ($unallocText) { $out += [pscustomobject]@{ Name = 'Unallocated'; Kind = 'unallocated'; Colour = $null; Total = $unallocText } }
    # Already included in Total above. This is a breakdown, not additional time.
    if ($timeline -and $timeline.UnplacedSeconds -gt 0) {
        $out += [pscustomobject]@{ Name = 'Unplaced*'; Kind = 'unplaced'; Colour = $null; Total = (Format-Hours $timeline.UnplacedSeconds) }
    }
    return $out
}

function Refresh-DrawerData {
    if ((Get-DrawerMode) -eq 'timeline') {
        if (-not $script:drawerDay) { $script:drawerDay = [datetime]::Today }
        $script:drawerWeek = Get-WeekStartFor $script:drawerDay
    } elseif (-not $script:drawerWeek) { $script:drawerWeek = Get-WeekStart }
    $script:drawerData = Get-WeekData $script:drawerWeek
    $script:drawerBudgets = @(Get-BudgetLines)
    if ((Get-DrawerMode) -eq 'timeline') {
        $script:drawerTimeline = Get-DayTimeline $script:drawerDay
        $script:drawerLegend = @(Get-DayLegend $script:drawerDay $script:drawerData $script:drawerWeek $script:drawerTimeline)
        # The drawer's height depends on the legend, so work it out before placing the view in it
        $script:drawerH = Get-DrawerHeight
        if ($null -eq $script:tlScrollH) { Reset-TimelineView } else { Set-TimelineScroll $script:tlScrollH }
    } else {
        $script:drawerTimeline = $null
        $script:drawerLegend = @()
    }
    $script:drawerH = Get-DrawerHeight
    $script:drawerVersion++
    if ($script:useRenderer) { Build-DrawerModel }
}

# The timeline half of the drawer's model. While an edge is being dragged, its preview moves are applied.
function Build-TimelineModel {
    $tmData = $script:drawerTimeline
    $d = $script:drawerData
    $tm = New-Object Win.TimelineModel
    $tm.AxisStartH = $tmData.AxisStartH; $tm.AxisEndH = $tmData.AxisEndH
    $tm.HasWork = $tmData.HasWork; $tm.WorkStartH = $tmData.WorkStartH; $tm.WorkEndH = $tmData.WorkEndH
    $tm.ShowNow = $tmData.ShowNow; $tm.NowH = $tmData.NowH
    $tm.EmptyText = [string]$tmData.EmptyText
    $tm.HasUnplaced = ([double]$tmData.UnplacedSeconds -gt 0)
    $i = 0
    foreach ($b in @($tmData.Blocks)) {
        $tb = New-Object Win.TimelineBlock
        $tb.Name = $b.Name; $tb.Kind = $b.Kind; $tb.Tip = $b.Tip; $tb.Editable = [bool]$b.Editable
        $tb.Manual = ($b.Origin -eq 'edited' -or $b.Origin -eq 'added' -or $b.Origin -eq 'positioned')
        $tb.StartH = $b.StartH; $tb.EndH = $b.EndH
        $tb.Lane = [int]$b.Lane; $tb.Lanes = [math]::Max(1, [int]$b.Lanes)
        foreach ($mv in @($script:tlPreview)) {
            if ($mv.Index -ne $i) { continue }
            if ($mv.Edge -eq 'start') { $tb.StartH = $mv.H } else { $tb.EndH = $mv.H }
        }
        if ($b.Kind -ne 'lunch') { $tb.Colour = Get-DrawerAccountColour $b.Name $d.Names }
        [void]$tm.Blocks.Add($tb)
        $i++
    }
    foreach ($gp in @($tmData.Gaps)) {
        $tg = New-Object Win.TimelineGap
        $tg.StartH = $gp.StartH; $tg.EndH = $gp.EndH
        [void]$tm.Gaps.Add($tg)
    }
    foreach ($lg in @($script:drawerLegend)) {
        $tl2 = New-Object Win.TimelineLegend
        $tl2.Name = $lg.Name; $tl2.Kind = $lg.Kind; $tl2.Total = $lg.Total
        if ($lg.Colour) { $tl2.Colour = $lg.Colour }
        [void]$tm.Legend.Add($tl2)
    }
    return $tm
}

function Build-DrawerModel {
    $d = $script:drawerData
    $ws = $script:drawerWeek
    $m = New-Object Win.DrawerModel
    $m.Title = 'Week of ' + $ws.ToString('d MMMM', $GB)
    $m.Mode = Get-DrawerMode
    if ($m.Mode -eq 'timeline') {
        $m.Title = Get-DrawerDayTitle $script:drawerDay
        $m.NextEnabled = $script:drawerDay -lt [datetime]::Today
        $m.UndoEnabled = [bool]$script:undoLabel
        $m.IsToday = ($script:drawerDay -eq [datetime]::Today)
        $m.FormatLabel = if ($script:hoursFormat -eq 'decimal') { 'h:mm' } else { 'Decimal' }
        $m.Timeline = Build-TimelineModel
        $script:drawerModel = $m
        return
    }
    $m.DayLabels = [string[]]@($d.Days | ForEach-Object { $ws.AddDays($_).ToString('ddd dd', $GB).ToUpper() })
    $m.NextEnabled = $ws -lt (Get-WeekStart)
    $m.FormatLabel = if ($script:hoursFormat -eq 'decimal') { 'h:mm' } else { 'Decimal' }
    $dayTotals = New-Object double[] 7
    foreach ($n in $d.Names) { foreach ($dd in $d.Days) { $dayTotals[$dd] += $d.Accounts[$n][$dd] } }
    $specs = @()
    foreach ($n in $d.Names) { $specs += ,@($n, 'normal', $d.Accounts[$n]) }
    $specs += ,@('Total', 'bold', $dayTotals)
    $specs += ,@('Lunch', 'faint', $d.Lunch)
    $specs += ,@('Unallocated', 'faint', $d.Unalloc)
    foreach ($sp in $specs) {
        $vals = $sp[2]
        $row = New-Object Win.DrawerRow
        $row.Name = $sp[0]
        $row.Style = $sp[1]
        $row.Cells = [string[]]@($d.Days | ForEach-Object { Format-Hours $vals[$_] })
        $sum = 0.0
        foreach ($dd in $d.Days) { $sum += $vals[$dd] }
        $row.Total = Format-Hours $sum
        [void]$m.Rows.Add($row)
    }
    $periods = @($script:drawerBudgets | ForEach-Object { $_.Period } | Select-Object -Unique)
    $onePeriod = $periods.Count -eq 1
    if ($onePeriod) { $m.BudgetsHeading = 'BUDGETS THIS ' + $periods[0].ToUpper() }
    foreach ($bl in $script:drawerBudgets) {
        $bm = New-Object Win.DrawerBudget
        $bm.Name = $bl.Name; $bm.Frac = [double]$bl.Frac
        $bm.Text = if ($onePeriod) { $bl.Short } else { '{0} ({1})' -f $bl.Short, $bl.Period }
        [void]$m.Budgets.Add($bm)
    }
    $script:drawerModel = $m
}

# Where the timer itself is on screen (its window may be larger when the drawer is open)
function Get-TimerScreenPos {
    $mg = $script:fullMargin
    return New-Object System.Drawing.Point(($form.Left + [int](($script:timerOffX + $mg) * $Zoom)), ($form.Top + [int](($script:timerOffY + $mg) * $Zoom)))
}

# Put the full view's window so the timer itself lands at this screen position
function Set-TimerScreenPos($pt) {
    $mg = $script:fullMargin
    $form.Location = New-Object System.Drawing.Point(($pt.X - [int](($script:timerOffX + $mg) * $Zoom)), ($pt.Y - [int](($script:timerOffY + $mg) * $Zoom)))
}

# Window coordinates to timer coordinates
function Get-TimerPoint($pt) {
    $mg = $script:fullMargin
    return New-Object System.Drawing.Point(([int]($pt.X - ($script:timerOffX + $mg) * $Zoom)), ([int]($pt.Y - ($script:timerOffY + $mg) * $Zoom)))
}

# Size and place the window so the timer stays put and the drawer opens where there's room
function Update-DrawerGeometry($tl) {
    if (-not $tl) { $tl = Get-TimerScreenPos }
    if ($script:mode -eq 'full') { $script:fullMargin = Get-WantedFullMargin }
    if ($script:drawerOpen) {
        # Out to the left if there's room (the usual case with the timer on the right), otherwise the right
        $area = [System.Windows.Forms.Screen]::FromPoint($tl).WorkingArea
        $leftRoom = ($tl.X - $DrawerSideW * $Zoom) -ge $area.Left
        $script:drawerUp = $false
        $script:timerOffX = if ($leftRoom) { $DrawerSideW } else { 0 }
        $script:timerOffY = 0
    } else {
        $script:timerOffX = 0; $script:timerOffY = 0; $script:drawerUp = $false
    }
    $bw, $bh = Get-BaseSize
    $form.ClientSize = New-Object System.Drawing.Size([int]($bw * $Zoom), [int]($bh * $Zoom))
    Set-TimerScreenPos $tl
}

function Set-DrawerOpen([bool]$open, [switch]$Starting) {
    if ($script:mode -ne 'full') { return }
    $tl = Get-TimerScreenPos
    $script:drawerOpen = $open
    if ($open) {
        if (-not $Starting) { $script:drawerWeek = Get-WeekStart; $script:drawerDay = [datetime]::Today; $script:tlScrollH = $null }
        Refresh-DrawerData
    } elseif ($script:drawerCache) {
        $script:drawerCache.Dispose(); $script:drawerCache = $null; $script:drawerCacheKey = ''
    }
    Update-DrawerGeometry $tl
    Apply-CornerStyle
    $script:regionKey = ''
    Update-Region
    if (-not $Starting) { Save-DrawerState }
    $script:drawerHover = $null
    $tip.Hide($form)
    Apply-BlurState
    Update-View
    Request-Redraw
}

# Whether the drawer is open and which view it shows, kept as two lines for the next start
function Save-DrawerState {
    try { Set-Content -Path $DrawerFile -Value @([string][int]$script:drawerOpen, $script:drawerMode) } catch {}
}

# Switch the drawer between the week table and the day's timeline
function Set-DrawerMode([string]$mode) {
    if ($mode -ne 'week' -and $mode -ne 'timeline') { return }
    if ($script:drawerMode -eq $mode) { return }
    $script:drawerMode = $mode
    Clear-TimelineDrag
    if ($mode -eq 'week') { $script:tlGeom = $null }
    if ($mode -eq 'timeline') {
        $script:tlScrollH = $null
        # Today, unless the week table was showing a past week, then that week's last day so far
        $ws = $script:drawerWeek
        $script:drawerDay = if (-not $ws -or $ws -ge (Get-WeekStart)) { [datetime]::Today } else { $ws.AddDays(6) }
    } elseif ($script:drawerDay) {
        $script:drawerWeek = Get-WeekStartFor $script:drawerDay
    }
    Save-DrawerState
    $script:drawerHover = $null
    $tip.Hide($form)
    Update-DrawerContent
}

# Refresh the figures; resize if the number of rows changed
function Update-DrawerContent {
    if (-not $script:drawerOpen) { return }
    if ($script:tlDrag) { return }      # the regular refresh waits until a drag is finished
    $oldH = $script:drawerH
    Refresh-DrawerData
    if ($script:drawerH -ne $oldH) { Update-DrawerGeometry }
    Request-Redraw
}

function Test-InDrawer($pt) {
    if (-not $script:drawerOpen) { return $false }
    $x = $pt.X / $Zoom - $script:fullMargin
    if ($script:timerOffX -gt 0) { return $x -lt $DrawerSideW }
    return $x -ge $BaseW
}

function Get-DrawerButtonAt($pt) {
    $x = [float]($pt.X / $Zoom); $y = [float]($pt.Y / $Zoom)
    foreach ($b in $script:drawerButtons) { if ($b.Rect.Contains($x, $y) -and -not "$($b.Id)".StartsWith('tip:')) { return $b.Id } }
    return $null
}

# ---- Timeline view: scrolling and zooming ----

# The height of the part of the drawer that shows the day, under the title and tabs
function Get-TimelineViewH {
    return [math]::Max(40.0, [double]$script:drawerH - (10 + 34 + [Win.FullRenderer]::TabsH) - 12)
}

# Keep the view inside the day's range, which runs from the first hour to the last on the strip
function Set-TimelineScroll([double]$h) {
    $tl = $script:drawerTimeline
    if (-not $tl) { return }
    $farthest = [math]::Max([double]$tl.AxisStartH, [double]$tl.AxisEndH - (Get-TimelineViewH) / $script:tlPx)
    $script:tlScrollH = [math]::Min([math]::Max($h, [double]$tl.AxisStartH), $farthest)
}

# Zoom by a factor, keeping the hour under the pointer where it is (or the middle of the view)
function Set-TimelineZoom([double]$factor, $anchorH) {
    $old = $script:tlPx
    $new = [math]::Min([math]::Max($old * $factor, [double]$TlMinPx), [double]$TlMaxPx)
    if ($new -eq $old) { return }
    if ($null -eq $anchorH) { $anchorH = $script:tlScrollH + (Get-TimelineViewH) / $old / 2 }
    $script:tlPx = $new
    Set-TimelineScroll ($anchorH - ($anchorH - $script:tlScrollH) * $old / $new)
}

# Today opens with now about a third of the way down, other days at the start of the working day
function Reset-TimelineView {
    $tl = $script:drawerTimeline
    if (-not $tl) { return }
    $viewHours = (Get-TimelineViewH) / $script:tlPx
    if ($script:drawerDay -eq [datetime]::Today -and $tl.ShowNow) { Set-TimelineScroll ($tl.NowH - 0.35 * $viewHours) }
    elseif ($tl.HasWork) { Set-TimelineScroll ($tl.WorkStartH - 0.25) }
    else { Set-TimelineScroll $tl.AxisStartH }
}

# ---- Timeline editing: dragging edges and filling gaps ----

function New-TimelineHover { return @{ Block = -1; Gap = -1; EdgeBlock = -1; Edge = '' } }

# The time at a window position, in hours since midnight, or $null when the timeline isn't showing.
# It works for positions outside the view too, which a drag past the top or bottom relies on.
function Get-TimelineTimeAt($pt) {
    $g = $script:tlGeom
    $tl = $script:drawerTimeline
    if ((Get-DrawerMode) -ne 'timeline' -or -not $g -or -not $tl -or $g.H -le 0 -or $null -eq $script:tlScrollH) { return $null }
    return $script:tlScrollH + ([double]($pt.Y / $Zoom) - $g.Y) / $script:tlPx
}

function Test-InStrip($pt) {
    $g = $script:tlGeom
    if ((Get-DrawerMode) -ne 'timeline' -or -not $g -or $g.H -le 0) { return $false }
    $x = [double]($pt.X / $Zoom); $y = [double]($pt.Y / $Zoom)
    return ($x -ge $g.X0 -and $x -le ($g.X0 + $g.W) -and $y -ge $g.Y -and $y -le ($g.Y + $g.H))
}

# What is under the pointer on the timeline: an edge, a card, a gap or nothing
function Get-TimelineHit($pt) {
    $hit = New-TimelineHover
    if (-not (Test-InStrip $pt)) { return $hit }
    $tl = $script:drawerTimeline
    $h = Get-TimelineTimeAt $pt
    $hoursPerUnit = 1.0 / $script:tlPx
    $g = $script:tlGeom
    $fx = ([double]($pt.X / $Zoom) - $g.X0) / $g.W
    $edge = Get-TimelineEdgeAt $tl.Blocks $h (5.0 * $hoursPerUnit) $fx
    if ($edge) { $hit.EdgeBlock = $edge.Index; $hit.Edge = $edge.Edge; $hit.Block = $edge.Index; return $hit }
    $blocks = @($tl.Blocks)
    for ($i = 0; $i -lt $blocks.Count; $i++) {
        if (-not (Test-InLane $blocks[$i] $fx)) { continue }
        $pad = [math]::Max(0.0, (6.0 * $hoursPerUnit - ($blocks[$i].EndH - $blocks[$i].StartH)) / 2)
        if ($h -ge ($blocks[$i].StartH - $pad) -and $h -le ($blocks[$i].EndH + $pad)) { $hit.Block = $i; return $hit }
    }
    # Time covered by a block in another lane isn't a gap, so there is nothing to point at here
    foreach ($bk in $blocks) { if ($h -ge $bk.StartH -and $h -le $bk.EndH) { return $hit } }
    $gaps = @($tl.Gaps)
    for ($i = 0; $i -lt $gaps.Count; $i++) {
        if ($h -ge $gaps[$i].StartH -and $h -le $gaps[$i].EndH) { $hit.Gap = $i; return $hit }
    }
    return $hit
}

function Test-SameTimelineHit($a, $b) {
    return ($a.Block -eq $b.Block -and $a.Gap -eq $b.Gap -and $a.EdgeBlock -eq $b.EdgeBlock -and $a.Edge -eq $b.Edge)
}

function Format-ClockHours([double]$h) {
    $mins = [int][math]::Round($h * 60)
    return '{0:00}:{1:00}' -f [math]::Floor($mins / 60), ($mins % 60)
}

# Hover text for the strip, placed just under the part being pointed at
function Show-TimelineTip($hit, $pt) {
    $tl = $script:drawerTimeline
    if (-not $tl) { return }
    $text = $null
    if ($hit.EdgeBlock -ge 0) {
        $text = switch ($hit.Edge) { 'start' { 'Drag to change the start time' } 'end' { 'Drag to change the finish time' } default { 'Drag to move the change-over' } }
    } elseif ($hit.Block -ge 0) {
        $blk = $tl.Blocks[$hit.Block]
        $text = $blk.Tip
        if (-not $blk.Editable) { $text += "`nStop it to change the times" }
    } elseif ($hit.Gap -ge 0) {
        $gp = $tl.Gaps[$hit.Gap]
        $text = "No positioned session from $(Format-ClockHours $gp.StartH) to $(Format-ClockHours $gp.EndH)`nClick to choose existing credited time or add new hours."
    }
    if (-not $text) { return }
    $tip.Show($text, $form, [int]($pt.X + 14 * $Zoom), [int]($pt.Y + 20 * $Zoom), 6000)
}

# The latest the strip may be edited to, which is now for today
function Get-TimelineCapH {
    if ($script:drawerDay -eq [datetime]::Today) { return ((Get-Date) - [datetime]::Today).TotalHours }
    return 24.0
}

function Clear-TimelineDrag {
    $script:tlDrag = $null; $script:tlPreview = @(); $script:tlSel = $null
    $script:tlDragText = ''
    $script:tlHover = New-TimelineHover
}

# Press on the strip: an edge starts a drag, a gap starts a selection, anything else is left to the caller
function Start-TimelineDrag($pt) {
    $hit = Get-TimelineHit $pt
    if ($hit.EdgeBlock -ge 0) {
        $script:tlDrag = @{ Kind = 'edge'; Index = $hit.EdgeBlock; Edge = $hit.Edge; Moved = $false }
        $script:tlHover = $hit
        $tip.Hide($form)
        return $true
    }
    if ($hit.Gap -ge 0) {
        $h = Get-TimelineTimeAt $pt
        $script:tlDrag = @{ Kind = 'gap'; Gap = $hit.Gap; AnchorH = $h; StartY = $pt.Y; Moved = $false }
        $script:tlHover = $hit
        $tip.Hide($form)
        return $true
    }
    return $false
}

function Update-TimelineDrag($pt) {
    $drag = $script:tlDrag
    $tl = $script:drawerTimeline
    $h = Get-TimelineTimeAt $pt
    if ($null -eq $h) { return }
    $snap = if ([System.Windows.Forms.Control]::ModifierKeys -band [System.Windows.Forms.Keys]::Shift) { 1.0 } else { 5.0 }
    if ($drag.Kind -eq 'edge') {
        $res = Get-TimelineDrag $tl.Blocks $drag.Index $drag.Edge $h $snap (Get-TimelineCapH)
        $script:tlPreview = if ($res.Changed) { @($res.Moves) } else { @() }
        $drag.Moved = $true
        $blk = $tl.Blocks[$drag.Index]
        $newStart = $blk.StartH; $newEnd = $blk.EndH
        foreach ($mv in $res.Moves) { if ($mv.Index -eq $drag.Index) { if ($mv.Edge -eq 'start') { $newStart = $mv.H } else { $newEnd = $mv.H } } }
        $script:tlDragAt = $res.H
        $script:tlDragText = if ($drag.Edge -eq 'join') { Format-ClockHours $res.H }
                             else { '{0} ({1})' -f (Format-ClockHours $res.H), (Format-HM (($newEnd - $newStart) * 3600)) }
    } else {
        if (-not $drag.Moved -and [math]::Abs($pt.Y - $drag.StartY) -lt 4) { return }
        $drag.Moved = $true
        $gp = $tl.Gaps[$drag.Gap]
        $a = [math]::Min([math]::Max($drag.AnchorH, $gp.StartH), $gp.EndH)
        $b = [math]::Min([math]::Max($h, $gp.StartH), $gp.EndH)
        $grid = $snap / 60.0
        $lo = [math]::Round([math]::Min($a, $b) / $grid) * $grid
        $hi = [math]::Round([math]::Max($a, $b) / $grid) * $grid
        $lo = [math]::Max($lo, $gp.StartH); $hi = [math]::Min($hi, $gp.EndH)
        if ($hi - $lo -lt (1.0 / 60.0)) { $script:tlSel = $null; $script:tlDragText = '' }
        else {
            $script:tlSel = @{ StartH = $lo; EndH = $hi }
            $script:tlDragAt = ($lo + $hi) / 2
            $script:tlDragText = '{0} to {1} ({2})' -f (Format-ClockHours $lo), (Format-ClockHours $hi), (Format-HM (($hi - $lo) * 3600))
        }
    }
    Build-DrawerModel
    Request-Redraw
}

function Complete-TimelineDrag($pt) {
    $drag = $script:tlDrag
    $moves = @($script:tlPreview)
    $sel = $script:tlSel
    $tl = $script:drawerTimeline
    Clear-TimelineDrag
    if ($drag.Kind -eq 'edge') {
        if ($moves.Count -gt 0) { Save-TimelineEdit $tl $moves }
        else { Build-DrawerModel; Request-Redraw }
        return
    }
    $gp = $tl.Gaps[$drag.Gap]
    $from = $gp.StartH; $to = $gp.EndH
    if ($drag.Moved -and $sel) { $from = $sel.StartH; $to = $sel.EndH }
    elseif ($drag.Moved) { Build-DrawerModel; Request-Redraw; return }
    Show-GapMenu $pt $from $to
}

function Stop-TimelineDrag {
    Clear-TimelineDrag
    Build-DrawerModel
    Request-Redraw
}

# Write edge moves to the time log. Each moved block's row is found again in the file, so a change made
# elsewhere in the meantime is noticed rather than overwritten.
function Save-TimelineEdit($tl, $moves) {
    $day = $tl.Day
    $edits = @{}
    foreach ($mv in $moves) {
        if (-not $edits.ContainsKey($mv.Index)) {
            $edits[$mv.Index] = @{ Start = $tl.Blocks[$mv.Index].Start; End = $tl.Blocks[$mv.Index].End }
        }
        $at = $day.AddSeconds([math]::Round($mv.H * 3600))
        if ($mv.Edge -eq 'start') { $edits[$mv.Index].Start = $at } else { $edits[$mv.Index].End = $at }
    }
    $done = Invoke-LogChange 'change times' {
        # Validate all sources before changing any, including separately positioned credits.
        foreach ($idx in @($edits.Keys)) {
            $blk = $tl.Blocks[$idx]
            if ($blk.Origin -eq 'positioned') {
                if (-not (Find-CreditPosition $blk.PositionId)) {
                    Show-Message 'That positioned credit changed somewhere else. Nothing was saved.'
                    return $false
                }
            } elseif ((Find-SessionRow $logRows $blk.Raw) -lt 0) {
                Show-Message 'That time changed somewhere else. Nothing was saved.'
                return $false
            }
        }
        foreach ($idx in @($edits.Keys)) {
            $blk = $tl.Blocks[$idx]
            $newStart = $edits[$idx].Start; $newEnd = $edits[$idx].End
            if ($newEnd -le $newStart) { return $false }
            if ($blk.Origin -eq 'positioned') {
                $position = Find-CreditPosition $blk.PositionId
                $oldKey = $null
                $newKey = $null
                $position.Start = $newStart.ToString('HH:mm:ss', $Inv)
                $position.Minutes = ([math]::Round(($newEnd - $newStart).TotalSeconds / 60, 2)).ToString('0.00', $Inv)
            } else {
                $row = $logRows[(Find-SessionRow $logRows $blk.Raw)]
                $oldKey = Get-NoteKey $row.Date $row.Account $row.Start
                $row.Start = $newStart.ToString('HH:mm:ss', $Inv)
                $row.Minutes = ([math]::Round(($newEnd - $newStart).TotalSeconds / 60, 2)).ToString('0.00', $Inv)
                if ($row.End -match '^[0-9]{2}:[0-9]{2}:[0-9]{2}$') { $row.End = $newEnd.ToString('HH:mm:ss', $Inv) }
                $newKey = Get-NoteKey $row.Date $row.Account $row.Start
            }
            if ($newKey -and $newKey -ne $oldKey -and $script:notes.ContainsKey($oldKey)) {
                $script:notes[$newKey] = $script:notes[$oldKey]
                [void]$script:notes.Remove($oldKey)
            }
        }
        # The original merge is the sole source of credited hours. A longer
        # positioned card is allowed only when enough merged credit exists.
        foreach ($p in @($script:creditPositions)) {
            $s = [double](Get-CreditPositionSeconds $p)
            $source = 0.0
            foreach ($row in @($logRows)) {
                if ($row.Date -eq $p.Date -and $row.Account -eq $p.Account -and $row.End -eq 'merged') {
                    $source += [double]::Parse($row.Minutes, $Inv) * 60
                }
            }
            $totalPositions = 0.0
            foreach ($other in @($script:creditPositions)) {
                if ($other.Date -eq $p.Date -and $other.Account -eq $p.Account) { $totalPositions += Get-CreditPositionSeconds $other }
            }
            if ($totalPositions -gt $source + 0.01) {
                Show-Message "Not enough existing merged credit remains for $($p.Account). Reduce the positioned duration or add new time instead."
                return $false
            }
        }
        return $true
    }
    if (-not $done) { Update-DrawerContent }
}

# A small menu of accounts for filling a stretch of the strip
$script:gapMenu = New-Object System.Windows.Forms.ContextMenuStrip

function Show-GapMenu($pt, [double]$fromH, [double]$toH) {
    $day = $script:drawerDay.Date
    $script:tlFill = @{ Day = $day; FromH = $fromH; ToH = $toH }
    $m = $script:gapMenu
    $m.Items.Clear()
    $duration = ($toH - $fromH) * 3600.0
    $head = $m.Items.Add(('Fill {0} to {1} ({2})' -f (Format-ClockHours $fromH), (Format-ClockHours $toH), (Format-HM $duration)))
    $head.Enabled = $false

    $existing = New-Object System.Windows.Forms.ToolStripMenuItem 'Position existing credited time (no extra hours)'
    $credits = Get-UnplacedCredits $day
    foreach ($account in @($credits.Keys | Sort-Object)) {
        $available = [double]$credits[$account]
        if ($available -le 0.01) { continue }
        $take = [math]::Min($available, $duration)
        $label = '{0}: position {1} ({2} unplaced)' -f $account, (Format-HM $take), (Format-HM $available)
        $mi = $existing.DropDownItems.Add($label)
        $mi.Tag = @{ Account = $account; Mode = 'credit' }
        $mi.Add_Click({
            try { Complete-GapFill ([string]$this.Tag.Account) ([string]$this.Tag.Mode) }
            catch { Write-Log "Position credit error: $($_.Exception.Message)" }
        })
    }
    $existing.Enabled = ($existing.DropDownItems.Count -gt 0)
    [void]$m.Items.Add($existing)

    $additional = New-Object System.Windows.Forms.ToolStripMenuItem 'Add new credited time (increases total)'
    foreach ($account in @($script:accounts | ForEach-Object { $_.Name })) {
        $mi = $additional.DropDownItems.Add($account)
        $mi.Tag = @{ Account = $account; Mode = 'new' }
        $mi.Add_Click({
            try { Complete-GapFill ([string]$this.Tag.Account) ([string]$this.Tag.Mode) }
            catch { Write-Log "Add gap error: $($_.Exception.Message)" }
        })
    }
    [void]$m.Items.Add($additional)
    Set-MenuScale $m
    $m.Show($form, $pt)
}

# CSV durations have a 0.6-second resolution, so legitimate timeline gaps may
# begin or end between whole seconds. First check the unrounded selection
# against the actual gaps, then snap *inwards* to an integer-second interval.
function Resolve-TimelineGapFill($gaps, [double]$fromH, [double]$toH) {
    if ($toH -le $fromH) { return $null }
    $epsilonH = 0.000001 / 3600.0  # floating-point noise, never overlap tolerance
    foreach ($gap in @($gaps)) {
        $gapStart = [double]$gap.StartH
        $gapEnd = [double]$gap.EndH
        if ($fromH -lt ($gapStart - $epsilonH) -or $toH -gt ($gapEnd + $epsilonH)) { continue }
        $lo = [math]::Max($fromH, $gapStart) * 3600.0
        $hi = [math]::Min($toH, $gapEnd) * 3600.0
        $startSecond = [long][math]::Ceiling($lo - 0.000001)
        $endSecond = [long][math]::Floor($hi + 0.000001)
        if ($endSecond -le $startSecond) { return $null }
        return [pscustomobject]@{ StartSecond = $startSecond; EndSecond = $endSecond }
    }
    return $null
}

function Complete-GapFill([string]$account, [string]$mode) {
    $fill = $script:tlFill
    $script:tlFill = $null
    if (-not $fill -or @('credit', 'new') -notcontains $mode) { return }
    $day = [datetime]$fill.Day
    $dateStr = $day.ToString('yyyy-MM-dd', $Inv)
    # Validate unrounded bounds, then round inwards for CSV-safe whole seconds.
    # Rounding before validation falsely rejected real fractional-second gaps.
    $current = Get-DayTimeline $day
    $safeFill = Resolve-TimelineGapFill $current.Gaps ([double]$fill.FromH) ([double]$fill.ToH)
    if (-not $safeFill) {
        Show-Message 'That gap has changed or is too short to allocate. Please select it again.'
        return
    }
    $from = $day.AddSeconds($safeFill.StartSecond)
    $to = $day.AddSeconds($safeFill.EndSecond)

    if ($mode -eq 'credit') {
        $creditsNow = Get-UnplacedCredits $day
        $available = [double]$creditsNow[$account]
        $take = [math]::Floor(([math]::Min($available, ($to - $from).TotalSeconds) / 60.0) * 100.0 + 0.000001) / 100.0
        if ($take -le 0) { Show-Message 'There is no unplaced credit remaining for that account.'; return }
        $posId = [guid]::NewGuid().ToString('N')
        # No session row is created or changed. One existing merged source still
        # owns the credit; this record only assigns some of it a clock position.
        [void](Invoke-LogChange 'position existing credit' {
            $currentCredits = Get-UnplacedCredits $day
            $still = [double]$currentCredits[$account]
            if ($still + 0.01 -lt ($take * 60.0)) { return $false }
            $script:creditPositions += [pscustomobject]@{
                Id = $posId; Date = $dateStr; Account = $account
                Start = $from.ToString('HH:mm:ss', $Inv); Minutes = $take.ToString('0.00', $Inv)
                Note = ''
            }
            return $true
        })
    } else {
        $secs = ($to - $from).TotalSeconds
        [void](Invoke-LogChange 'add new credited time' {
            [void]$logRows.Add((New-SessionRow $dateStr $account $from.ToString('HH:mm:ss', $Inv) 'added' $secs))
            return $true
        })
    }
}

function Test-HitDayBtn($tp) {
    if (-not $script:view -or -not $script:view.DayBtnText) { return $false }
    $x = $tp.X / $Zoom; $y = $tp.Y / $Zoom
    $dbRect = Get-DayBtnRect
    return ($x -ge $dbRect.X -and $x -le ($dbRect.X + $dbRect.W) -and $y -ge $dbRect.Y -and $y -le ($dbRect.Y + $dbRect.H))
}

function Test-HitUnalloc($tp) {
    $x = $tp.X / $Zoom; $y = $tp.Y / $Zoom
    return ($x -ge 160 -and $x -le 312 -and $y -ge 48 -and $y -le 67)
}

function Test-HitHandle($tp) {
    $x = $tp.X / $Zoom; $y = $tp.Y / $Zoom
    $dx = $x - $DrawerBtnX; $dy = $y - $DrawerBtnY
    $r = $CollapseR + 3
    return (($dx * $dx + $dy * $dy) -le ($r * $r))
}

function Get-DrawerTableLines {
    $d = $script:drawerData
    $ws = $script:drawerWeek
    $lines = @()
    $lines += (@('Account') + @($d.Days | ForEach-Object { $ws.AddDays($_).ToString('ddd dd/MM', $GB) }) + @('Total')) -join "`t"
    $dayTotals = New-Object double[] 7
    $rowsOut = @()
    foreach ($n in $d.Names) {
        $rowsOut += ,@($n, $d.Accounts[$n])
        foreach ($dd in $d.Days) { $dayTotals[$dd] += $d.Accounts[$n][$dd] }
    }
    $rowsOut += ,@('Total (excluding lunch)', $dayTotals)
    $rowsOut += ,@('Lunch', $d.Lunch)
    $rowsOut += ,@('Unallocated', $d.Unalloc)
    foreach ($ro in $rowsOut) {
        $sum = 0.0
        $cells = @($ro[0])
        foreach ($dd in $d.Days) { $cells += (Format-Hours $ro[1][$dd]); $sum += $ro[1][$dd] }
        $cells += (Format-Hours $sum)
        $lines += ($cells -join "`t")
    }
    return $lines
}

function Invoke-DrawerButton($id) {
    switch ($id) {
        'prev' {
            if ((Get-DrawerMode) -eq 'timeline') { $script:drawerDay = $script:drawerDay.AddDays(-1); $script:tlScrollH = $null }
            else { $script:drawerWeek = $script:drawerWeek.AddDays(-7) }
            Update-DrawerContent
        }
        'next' {
            if ((Get-DrawerMode) -eq 'timeline') {
                if ($script:drawerDay -lt [datetime]::Today) { $script:drawerDay = $script:drawerDay.AddDays(1); $script:tlScrollH = $null; Update-DrawerContent }
            }
            elseif ($script:drawerWeek -lt (Get-WeekStart)) { $script:drawerWeek = $script:drawerWeek.AddDays(7); Update-DrawerContent }
        }
        'undo' { Undo-LogChange }
        'zoomin' { Set-TimelineZoom 1.25 $null; $script:tlHover = New-TimelineHover; Request-Redraw }
        'zoomout' { Set-TimelineZoom 0.8 $null; $script:tlHover = New-TimelineHover; Request-Redraw }
        'now' { Reset-TimelineView; Request-Redraw }
        'week' { Set-DrawerMode 'week' }
        'timeline' { Set-DrawerMode 'timeline' }
        'copy' {
            [System.Windows.Forms.Clipboard]::SetText(((Get-DrawerTableLines) -join "`r`n"))
            $b = $script:drawerButtons | Where-Object { $_.Id -eq 'copy' } | Select-Object -First 1
            if ($b) { $tip.Show('Copied, ready to paste', $form, [int]($b.Rect.X * $Zoom), [int](($b.Rect.Bottom + 4) * $Zoom), 1500) }
        }
        'weekly' { try { Show-WeeklyView } catch { Show-WeeklyError $_ } }
        'format' { Switch-HoursFormat }
    }
}

# The drawer only changes when its figures refresh, a button is hovered, or the theme, colour state
# or size changes, so it's drawn once into a stored image and that image is reused every second.
function Draw-DrawerCached($g, $theme) {
    $w = $form.ClientSize.Width
    $h = $form.ClientSize.Height
    $key = '{0}|{1}|{2}|{3}|{4}x{5}|{6}|{7}|{8}|{9}' -f $script:drawerVersion, $script:drawerHover, $script:themeName,
           $theme.Panel.ToArgb(), $w, $h, $Zoom, $script:timerOffX, [bool]$PillShadow, $BaseH
    if ($key -ne $script:drawerCacheKey -or -not $script:drawerCache) {
        if ($script:drawerCache) { $script:drawerCache.Dispose(); $script:drawerCache = $null }
        $img = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $ig = [System.Drawing.Graphics]::FromImage($img)
        try {
            $ig.Clear([System.Drawing.Color]::Transparent)
            $ig.SmoothingMode = 'AntiAlias'
            $ig.TextRenderingHint = 'ClearTypeGridFit'
            $ig.ScaleTransform($Zoom, $Zoom)
            Draw-Drawer $ig $theme
        } finally { $ig.Dispose() }
        $script:drawerCache = $img
        $script:drawerCacheKey = $key
        $script:drawerCacheBuilds++
    }
    # Copy it pixel for pixel, with no scaling or smoothing
    $state = $g.Save()
    $g.ResetTransform()
    $g.InterpolationMode = 'NearestNeighbor'
    $g.PixelOffsetMode = 'Half'
    $g.DrawImage($script:drawerCache, (New-Object System.Drawing.Rectangle(0, 0, $w, $h)))
    $g.Restore($state)
}

function Draw-Drawer($g, $theme) {
    $DW = [float]$DrawerSideW
    $H = [float]$script:drawerH
    $dx = if ($script:timerOffX -gt 0) { [float]0 } else { [float]$BaseW }   # drawer's left edge

    # Background: the drawer and any curved join, leaving the timer as it is
    $shape = New-DrawerShape
    $bg = New-Object System.Drawing.SolidBrush $theme.Panel
    $g.SetClip($shape)
    $g.SetClip((New-Object System.Drawing.RectangleF([float]$script:timerOffX, [float]0, [float]$BaseW, [float]$BaseH)), [System.Drawing.Drawing2D.CombineMode]::Exclude)
    $g.FillRectangle($bg, [float]0, [float]0, [float]($DW + $BaseW), $H)
    $g.ResetClip()
    $bg.Dispose()
    if ($script:layered -and $PillShadow) {
        $rimPen = New-Object System.Drawing.Pen(($C::FromArgb(45, $cWhite)), [float]1)
        $g.DrawPath($rimPen, $shape)
        $rimPen.Dispose()
    }
    $shape.Dispose()
    $seamX = if ($script:timerOffX -gt 0) { $DW } else { [float]$BaseW }
    $g.DrawLine($pSep, $seamX, [float]12, $seamX, [float]($BaseH - 12))

    $d = $script:drawerData
    if (-not $d) { return }
    $pad = [float]14
    $x0 = $dx + $pad
    $xR = $dx + $DW - $pad
    $y = [float]10
    $ws = $script:drawerWeek

    # Title and buttons
    $g.DrawString(('Week of ' + $ws.ToString('d MMMM', $GB)), $fRow, $bWhite, (New-Object $RF $x0, $y, 170, 24), $sfName)
    $script:drawerButtons = @()
    $bx = $xR
    $isCurrent = $ws -ge (Get-WeekStart)
    $fmtLabel = if ($script:hoursFormat -eq 'decimal') { 'h:mm' } else { 'Decimal' }
    foreach ($def in @(@('weekly', 'Weekly view'), @('copy', 'Copy'), @('format', $fmtLabel), @('next', 'Next'), @('prev', 'Previous'))) {
        $txt = $def[1]
        $tw = [float]($g.MeasureString($txt, $fDrawerBtn).Width + 16)
        $bx -= $tw
        $rect = New-Object $RF $bx, ($y + 1), $tw, 22
        $enabled = -not ($def[0] -eq 'next' -and $isCurrent)
        $col = if (-not $enabled) { Mix-Colour $theme.Panel $theme.Circle 0.45 }
               elseif ($script:drawerHover -eq $def[0]) { $theme.CircleHot }
               else { $theme.Circle }
        $bp = New-RoundedPath $rect.X $rect.Y $rect.Width $rect.Height ([float]11)
        $bb = New-Object System.Drawing.SolidBrush $col
        $g.FillPath($bb, $bp)
        $bb.Dispose(); $bp.Dispose()
        $g.DrawString($txt, $fDrawerBtn, $(if ($enabled) { $bWhite } else { $bDim }), $rect, $sfCentre)
        if ($enabled) { $script:drawerButtons += @{ Id = $def[0]; Rect = $rect } }
        $bx -= 6
    }
    $y += 34

    # Hours by account and day
    $days = @($d.Days)
    $nameW = [float]140
    $tableW = $DW - 2 * $pad
    $colW = [float](($tableW - $nameW) / ($days.Count + 1))
    $rowH = [float]$DrawerRowH
    $g.DrawString('ACCOUNT', $fLabel, $bFaint, (New-Object $RF $x0, $y, $nameW, $rowH), $sfName)
    for ($j = 0; $j -lt $days.Count; $j++) {
        $label = $ws.AddDays($days[$j]).ToString('ddd dd', $GB).ToUpper()
        $g.DrawString($label, $fLabel, $bFaint, (New-Object $RF ($x0 + $nameW + $j * $colW), $y, ($colW - 4), $rowH), $sfRight)
    }
    $g.DrawString('TOTAL', $fLabel, $bFaint, (New-Object $RF ($x0 + $nameW + $days.Count * $colW), $y, ($colW - 4), $rowH), $sfRight)
    $y += $rowH

    $dayTotals = New-Object double[] 7
    $tableRows = @()
    foreach ($n in $d.Names) {
        $tableRows += @{ Name = $n; Values = $d.Accounts[$n]; Style = 'normal' }
        foreach ($dd in $days) { $dayTotals[$dd] += $d.Accounts[$n][$dd] }
    }
    $tableRows += @{ Name = 'Total'; Values = $dayTotals; Style = 'bold' }
    $tableRows += @{ Name = 'Lunch'; Values = $d.Lunch; Style = 'faint' }
    $tableRows += @{ Name = 'Unallocated'; Values = $d.Unalloc; Style = 'faint' }
    foreach ($tr in $tableRows) {
        $g.DrawLine($pSep, $x0, $y, $xR, $y)
        $font = if ($tr.Style -eq 'bold') { $fDrawerBtn } else { $fPillSub }
        $brush = if ($tr.Style -eq 'faint') { $bFaint } else { $bWhite }
        $g.DrawString($tr.Name, $font, $brush, (New-Object $RF $x0, $y, ($nameW - 6), $rowH), $sfName)
        $sum = 0.0
        for ($j = 0; $j -lt $days.Count; $j++) {
            $v = $tr.Values[$days[$j]]
            $sum += $v
            $g.DrawString((Format-Hours $v), $font, $brush, (New-Object $RF ($x0 + $nameW + $j * $colW), $y, ($colW - 4), $rowH), $sfRight)
        }
        $g.DrawString((Format-Hours $sum), $fDrawerBtn, $brush, (New-Object $RF ($x0 + $nameW + $days.Count * $colW), $y, ($colW - 4), $rowH), $sfRight)
        $y += $rowH
    }

    # Budgets
    if (@($script:drawerBudgets).Count -gt 0) {
        $y += 8
        $bPeriods = @($script:drawerBudgets | ForEach-Object { $_.Period } | Select-Object -Unique)
        $bHeading = if ($bPeriods.Count -eq 1) { 'BUDGETS THIS ' + $bPeriods[0].ToUpper() } else { 'BUDGETS' }
        $g.DrawString($bHeading, $fLabel, $bFaint, (New-Object $RF $x0, $y, 260, 16), $sfName)
        $y += 18
        foreach ($bl in $script:drawerBudgets) {
            $g.DrawString($bl.Name, $fPillSub, $bWhite, (New-Object $RF $x0, $y, ($nameW - 6), 20), $sfName)
            $barX = $x0 + $nameW
            $barY = [float]($y + 9)
            $g.FillRectangle($bTrack, $barX, $barY, [float]140, [float]3)
            $bc = if ($bl.Frac -ge 1) { $BudgetOverColour } elseif ($bl.Frac -ge 0.8) { $BudgetWarnColour } else { $theme.Fill }
            $fillW = [float](140 * [math]::Min(1.0, $bl.Frac))
            if ($bl.Frac -gt 0 -and $fillW -lt 2) { $fillW = 2 }
            $bbr = New-Object System.Drawing.SolidBrush $bc
            $g.FillRectangle($bbr, $barX, $barY, $fillW, [float]3)
            $bbr.Dispose()
            $bText = if ($bPeriods.Count -eq 1) { $bl.Short } else { '{0} ({1})' -f $bl.Short, $bl.Period }
            $g.DrawString($bText, $fPillSub, $bFaint, (New-Object $RF ($barX + 150), $y, ($tableW - $nameW - 150), 20), $sfRight)
            $y += 20
        }
    }
}

function Draw-Pill($g, [bool]$layered) {
    try {
        $g.SmoothingMode = 'AntiAlias'
        $g.TextRenderingHint = 'ClearTypeGridFit'
        $g.ScaleTransform($Zoom, $Zoom)
        $v = $script:view
        if (-not $v) { return }

        $theme = Get-StateTheme $v
        $L = Get-PillLayout
        $H = $L.Height
        $hy = $L.HeaderY
        $inset = 1.5
        $panelCol = $theme.Panel

        # Slow breathing (0 to 1): about 3 seconds for the not-tracking glow, 2 for the live dot
        $t = $script:animClock.Elapsed.TotalSeconds
        $breath = 0.5 - 0.5 * [math]::Cos(2 * [math]::PI * $t / 3.2)
        $dotBreath = 0.5 - 0.5 * [math]::Cos(2 * [math]::PI * $t / 2.0)
        $nagging = $PillPulse -and $v.Idle -and -not $v.Before

        # Shadow (or, when not tracking, a soft glow in the theme's colour that breathes)
        $mg = $script:pillMargin
        if ($layered -and $mg -gt 0) {
            if ($nagging) { Draw-PillShadow $g $mg $mg $PillW $H $theme.Fill (0.55 + 1.1 * $breath) 0 }
            elseif ($PillShadow) { Draw-PillShadow $g $mg $mg $PillW $H ([System.Drawing.Color]::Black) 1.0 1.5 }
            $g.TranslateTransform([float]$mg, [float]$mg)
        }
        if ($nagging) { $panelCol = Mix-Colour $theme.Panel $theme.CircleHot (0.22 * $breath) }

        # Drawn slightly inside the window's rounded cut, so the cut never clips the smooth edges
        $script:shapePath = if ($layered) { New-RoundedPath $inset $inset ($PillW - 2 * $inset) ($H - 2 * $inset) (($PillH - 2 * $inset) / 2) } else { $null }
        $bg = New-Object System.Drawing.SolidBrush $panelCol
        Fill-Area $g $bg 0 0 $PillW $H
        $bg.Dispose()

        # ---- Header ----
        $listW = $PillBtnX - $PillBtnR - 4 - $PillTimeW
        if ($script:pillHover -eq 'time') { Fill-Area $g $bRowHover 0 $hy $PillTimeW $PillH }
        elseif ($script:pillHover -eq 'list' -or $script:pillOpen) { Fill-Area $g $bRowHover $PillTimeW $hy $listW $PillH }

        # The day so far (or overtime)
        $timeBrush = if ($v.Before) { $bDim } else { $bWhite }
        $g.DrawString($v.PillTime, $fPillTime, $timeBrush, (New-Object $RF 6, $hy, ($PillTimeW - 6), ($PillH - 2)), $sfCentre)
        $g.DrawLine($pSep, $PillTimeW, ($hy + 10), $PillTimeW, ($hy + $PillH - 10))

        # What's being tracked
        if ($v.OnLunch)        { $line1 = 'On lunch';            $line2 = $v.ActiveToday }
        elseif ($v.ActiveName) { $line1 = $v.ActiveName;          $line2 = $v.ActiveToday }
        elseif ($v.Before)     { $line1 = "Starts at $($v.StartLabel)"; $line2 = 'Nothing running' }
        elseif ($v.DayOff)     { $line1 = 'Day off';               $line2 = 'Nothing running' }
        else                   { $line1 = 'Not tracking';         $line2 = $v.UnallocText }
        $tx = $PillTimeW + 9
        if ($PillLiveDot -and $v.ActiveName) {
            # Breathing 'recording' dot
            $da = [int](90 + 165 * $dotBreath)
            $db = New-Object System.Drawing.SolidBrush ($C::FromArgb($da, $cWhite))
            $g.FillEllipse($db, [float]($tx + 0.5), [float]($hy + 10.8), [float]6, [float]6)
            $db.Dispose()
            $tx += 11
        }
        $tw = $PillBtnX - $PillBtnR - 6 - $tx
        $g.DrawString($line1, $fRow, $bWhite, (New-Object $RF $tx, ($hy + 5), $tw, 18), $sfName)
        $g.DrawString($line2, $fPillSub, $bFaint, (New-Object $RF $tx, ($hy + 22), $tw, 15), $sfName)

        # Play / pause
        $cy = $hy + $PillH / 2
        $cc = if ($script:pillHover -eq 'button') { $theme.CircleHot } else { $theme.Circle }
        $cb = New-Object System.Drawing.SolidBrush $cc
        $g.FillEllipse($cb, ($PillBtnX - $PillBtnR), ($cy - $PillBtnR), (2 * $PillBtnR), (2 * $PillBtnR))
        $cb.Dispose()
        if ($v.ActiveName) { Draw-PauseIcon $g $PillBtnX $cy } else { Draw-PlayIcon $g ($PillBtnX + 1) $cy }

        # Progress through the day, as a fine line along the bottom of the header
        $py = $hy + $PillH - 4
        $g.FillRectangle($bTrack, 18, $py, ($PillW - 36), 2)
        $fb = New-Object System.Drawing.SolidBrush $theme.Fill
        $g.FillRectangle($fb, 18, $py, (($PillW - 36) * [math]::Min(1.0, $v.Progress)), 2)
        $fb.Dispose()

        # ---- Account list ----
        if ($script:pillOpen) {
            $sy = if ($script:pillUp) { $hy } else { $hy + $PillH }
            $g.DrawLine($pSep, 14, $sy, ($PillW - 14), $sy)
            for ($i = 0; $i -lt $v.Rows.Count; $i++) {
                $r  = $v.Rows[$i]
                $y0 = Get-PillRowTop $i
                $rc = $y0 + $PillRowH / 2
                $isLunch = $r.Name -eq 'Lunch'
                if ($isLunch) { $g.DrawLine($pSep, 14, ($y0 - $PillLunchGap / 2), ($PillW - 14), ($y0 - $PillLunchGap / 2)) }

                $key = 'row:' + $i
                if ($r.Active) { Fill-Area $g $bRowOn 6 $y0 ($PillW - 12) $PillRowH }
                elseif ($script:pillHover -eq $key) { Fill-Area $g $bRowHover 6 $y0 ($PillW - 12) $PillRowH }

                $ccol = if ($r.Active) { $theme.CircleHot } else { $theme.Circle }
                $cbr = New-Object System.Drawing.SolidBrush $ccol
                $g.FillEllipse($cbr, 15, ($rc - 9), 18, 18)
                $cbr.Dispose()
                if ($r.Active) { Draw-PauseIcon $g 24 $rc }
                elseif ($isLunch) { Draw-LunchIcon $g 24 $rc }
                else { Draw-PlayIcon $g 25 $rc }

                $g.DrawString($r.Name, $fRow, $bWhite, (New-Object $RF 40, $y0, ($PillW - 40 - 72), $PillRowH), $sfName)
                $g.DrawString((Format-Dur $r.Today), $fPillSub, $bFaint, (New-Object $RF ($PillW - 76), $y0, 62, $PillRowH), $sfRight)
            }
        }

        if ($layered -and $PillShadow -and $script:shapePath) {
            $rimPen = New-Object System.Drawing.Pen(($C::FromArgb(60, $cWhite)), [float]1)
            $g.DrawPath($rimPen, $script:shapePath)
            $rimPen.Dispose()
        }
    } catch {
        Write-Log "Drawing error: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"
    } finally {
        if ($script:shapePath) { $script:shapePath.Dispose(); $script:shapePath = $null }
    }
}

# Soft shadow or glow: layered rounded shapes, faintest at the outside
function Draw-PillShadow($g, [float]$x, [float]$y, [float]$w, [float]$h, $colour, [double]$strength, [float]$drop) {
    $steps = 6
    for ($i = $steps; $i -ge 1; $i--) {
        $sp = [float]($i * 1.4)
        $a = [int][math]::Min(255.0, $strength * 16 * (($steps - $i + 1) / $steps))
        if ($a -le 0) { continue }
        $path = New-RoundedPath ($x - $sp) ($y - $sp + $drop) ($w + 2 * $sp) ($h + 2 * $sp) ([float]($PillH / 2 + $sp))
        $br = New-Object System.Drawing.SolidBrush ($C::FromArgb($a, $colour))
        $g.FillPath($br, $path)
        $br.Dispose()
        $path.Dispose()
    }
}

function Draw-Timer($g, [bool]$layered) {
    if ($script:mode -eq 'pill') { Draw-Pill $g $layered } else { Draw-Full $g $layered }
}

# Normal drawing, used if see-through drawing isn't available
$form.Add_Paint({ if (-not $script:layered) { Draw-Timer $_.Graphics $false } })

# ---- Drawing speed check: the most recent 300 redraws for each kind of view ----
$script:perf = @{}
function Get-PerfKey {
    if ($script:mode -eq 'pill') {
        if ($script:pillOpen) { return 'Compact view with account list' }
        return 'Compact view'
    }
    $key = if ($script:drawerOpen) { 'Full view with week drawer' } else { 'Full view' }
    if ($script:listAlpha -lt 255) { $key += ', see-through list' }
    return $key
}

function Add-PerfSample($key, [double]$total, [double]$draw, [double]$text, [double]$push) {
    if (-not $script:perf.ContainsKey($key)) { $script:perf[$key] = New-Object System.Collections.ArrayList }
    $list = $script:perf[$key]
    [void]$list.Add(@($total, $draw, $text, $push))
    if ($list.Count -gt 300) { $list.RemoveAt(0) }
}

function Get-Percentile($values, [double]$pc) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return 0 }
    $i = [int][math]::Floor(($sorted.Count - 1) * $pc)
    return $sorted[$i]
}

function Get-PerfReport {
    $lines = @('Drawing speed, from the most recent redraws of each view on this machine.', '')
    if ($script:perf.Count -eq 0) {
        $lines += 'Nothing measured yet. Use the timer for a minute in each view, then check again.'
    }
    foreach ($key in ($script:perf.Keys | Sort-Object)) {
        $list = $script:perf[$key]
        $totals = @($list | ForEach-Object { $_[0] })
        $avg = { param($i) $sum = 0.0; foreach ($sample in $list) { $sum += $sample[$i] }; $sum / $list.Count }
        $lines += ('{0} ({1} redraws)' -f $key, $list.Count)
        $lines += ('    Typical {0:0} ms, slowest 1 in 20 {1:0} ms, slowest {2:0} ms' -f (Get-Percentile $totals 0.5), (Get-Percentile $totals 0.95), (Get-Percentile $totals 1.0))
        $parts = 'building the image {0:0} ms' -f (& $avg 1)
        $textAvg = & $avg 2
        if ($textAvg -gt 0.05) { $parts += ', see-through text {0:0} ms' -f $textAvg }
        $parts += ', sending to screen {0:0} ms' -f (& $avg 3)
        $lines += ('    On average: ' + $parts)
        $lines += ''
    }
    if ($script:drawerCacheBuilds -gt 0) {
        $lines += ('Week drawer: drawn {0} times since the timer started; every other redraw reused the stored image.' -f $script:drawerCacheBuilds)
        $lines += ''
    }
    $lines += 'As a guide, under 30 ms feels instant and over 50 ms starts to feel sluggish.'
    return $lines
}

function Show-PerfReport {
    $lines = Get-PerfReport
    Write-Log ('Drawing speed report: ' + (($lines | Where-Object { $_ }) -join ' | '))
    Show-Message (($lines -join "`n") + "`n`nThis report has also been added to the error log (Open file, Error log). Press Ctrl+C to copy it.")
}

# The shadow margin is only kept while the shadow can show. Windows' blur fills the whole window
# rectangle, so while the blur is showing the window shrinks back to exactly the timer's size.
function Get-WantedFullMargin {
    if (-not ($script:layered -and $script:useRenderer -and $PillShadow)) { return 0 }
    $blurShowing = $script:blurOn -and $script:listLevel -ne 'Off'
    if ($blurShowing) { return 0 }
    return 10
}

# The full view's own shadow is skipped while the blur is showing (the blur needs the edge to itself)
function Test-FullShadow {
    $blurShowing = $script:blurOn -and $script:listLevel -ne 'Off'
    return [bool]($script:useRenderer -and $PillShadow -and -not $blurShowing -and $script:fullMargin -gt 0)
}

# Size and position details the renderer needs (also used to cut the window to shape)
function Sync-RendererLayout {
    $r = $script:renderer
    $r.Zoom = [float]$Zoom
    $r.BaseH = [float]$BaseH
    $r.RowTop = [float]$RowTop
    $r.DrawerH = [float]$script:drawerH
    $r.Margin = [float]$script:fullMargin
    $r.TimerOffX = [float]$script:timerOffX
    $r.TimerOffY = [float]$script:timerOffY
    $r.DrawerOpen = [bool]$script:drawerOpen
    $r.Shadow = Test-FullShadow
    $r.Rim = [bool]$PillShadow
}

# Everything else for this frame: colours, figures, rows and what's being hovered
function Sync-RendererState {
    $r = $script:renderer
    $v = $script:view
    Sync-RendererLayout
    $theme = Get-StateTheme $v
    $r.Panel = $theme.Panel; $r.Circle = $theme.Circle; $r.CircleHot = $theme.CircleHot; $r.Fill = $theme.Fill
    $r.Text = $cWhite
    $r.LunchBand = $script:bLunchBand.Color
    $alpha = $script:listAlpha
    if ($alpha -lt 255 -and $script:blurOn) { $alpha = [int]($alpha * $BlurTint) }
    $r.ListAlpha = $alpha
    $r.TextSharpen = [float]$TextSharpen
    $r.H = $v.H; $r.M = $v.M; $r.S = $v.Sec
    $r.Status = $v.Status; $r.Unallocated = $v.Unallocated; $r.OT = "$($v.OT)"
    $r.Before = [bool]$v.Before; $r.OnLunch = [bool]$v.OnLunch; $r.Overtime = [bool]$v.Overtime
    $r.Progress = [double]$v.Progress
    $r.Rows = [Win.RowModel[]]@($v.Rows)
    $r.Hover = if ($null -ne $script:hover) { [int]$script:hover } else { -1 }
    $r.GripHover = if ($null -ne $script:gripHover) { [int]$script:gripHover } else { -1 }
    $r.Reordering = [bool]$script:reorder
    $r.DraggingRow = if ($script:reorder) { [array]::IndexOf($script:slots, $script:reorder.Name) } else { -1 }
    $r.RoundHover = "$($script:roundHover)"
    $r.CollapseHover = [bool]$script:collapseHover
    $r.DayBtnText = [string]$v.DayBtnText
    $r.DayBtnHover = [bool]$script:dayBtnHover
    $r.HandleHover = [bool]$script:handleHover
    $r.DrawerHover = "$($script:drawerHover)"
    $r.TlHoverBlock = [int]$script:tlHover.Block; $r.TlHoverGap = [int]$script:tlHover.Gap
    $r.TlEdgeBlock = [int]$script:tlHover.EdgeBlock; $r.TlEdge = [string]$script:tlHover.Edge
    $r.TlSelStartH = if ($script:tlSel) { [double]$script:tlSel.StartH } else { -1.0 }
    $r.TlSelEndH = if ($script:tlSel) { [double]$script:tlSel.EndH } else { -1.0 }
    $r.TlDragText = [string]$script:tlDragText; $r.TlDragAtH = [double]$script:tlDragAt
    $r.TlScrollH = if ($null -ne $script:tlScrollH) { [double]$script:tlScrollH } else { 0.0 }
    $r.TlPx = [float]$script:tlPx
    $r.Drawer = $script:drawerModel
}

# If the faster drawing ever fails, switch back to PowerShell drawing and carry on
function Disable-Renderer($why) {
    Write-Log "Faster drawing switched off, using standard drawing instead: $why"
    $tl = Get-TimerScreenPos
    $script:useRenderer = $false
    $script:fullMargin = 0
    $script:regionKey = ''
    # The timeline needs the compiled drawing, so an open drawer goes back to the week table
    if ($script:drawerOpen) { Refresh-DrawerData }
    Update-DrawerGeometry $tl
    Apply-CornerStyle
}

function Render-FullFrame($w, $h) {
    $perfWatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        Sync-RendererState
        Update-Region
        $bmp = $script:renderer.Render($w, $h)
    } catch {
        Disable-Renderer $_.Exception.Message
        return
    }
    $script:drawerButtons = @($script:renderer.DrawerButtons)
    $script:tlGeom = @{ X0 = [double]$script:renderer.TlX0; Y = [double]$script:renderer.TlY; W = [double]$script:renderer.TlW; H = [double]$script:renderer.TlH }
    $msDraw = $perfWatch.Elapsed.TotalMilliseconds
    try {
        $ok = [Win.Layered]::Push($form.Handle, $bmp, $form.Left, $form.Top)
        $msTotal = $perfWatch.Elapsed.TotalMilliseconds
        Add-PerfSample ((Get-PerfKey) + ' (fast drawing)') $msTotal $msDraw 0 ($msTotal - $msDraw)
        if (-not $ok) {
            Write-Log 'See-through drawing failed, so the timer has switched back to normal drawing.'
            [Win.Layered]::MakeNormal($form.Handle)
            $script:pillMargin = 0
            $script:layered = $false
            Disable-Renderer 'see-through drawing unavailable'
            $form.Invalidate()
        }
    } catch {
        Write-Log "See-through drawing error: $($_.Exception.Message)"
    } finally { $bmp.Dispose() }
}

function Render-Frame {
    $w = $form.ClientSize.Width
    $h = $form.ClientSize.Height
    if ($w -le 0 -or $h -le 0) { return }
    if ($script:mode -eq 'full' -and $script:useRenderer) { Render-FullFrame $w $h; return }
    $perfWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $msDraw = 0.0; $msText = 0.0
    Update-Region
    $fmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    $bmp = New-Object System.Drawing.Bitmap($w, $h, $fmt)
    $g = [System.Drawing.Graphics]::FromImage($bmp)

    # With the account list see-through, prepare the extra text layers
    $ctColour = $null; $ctMask = $null
    if ($script:listAlpha -lt 255 -and $script:mode -eq 'full') {
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
    $msDraw = $perfWatch.Elapsed.TotalMilliseconds
    if ($ctColour) {
        try { [Win.Layered]::Combine($bmp, $ctColour, $ctMask, $TextSharpen) }
        catch { Write-Log "Text drawing error: $($_.Exception.Message)" }
        $ctColour.Dispose(); $ctMask.Dispose()
        $msText = $perfWatch.Elapsed.TotalMilliseconds - $msDraw
    }
    $msBeforePush = $perfWatch.Elapsed.TotalMilliseconds
    try {
        $ok = [Win.Layered]::Push($form.Handle, $bmp, $form.Left, $form.Top)
        $msTotal = $perfWatch.Elapsed.TotalMilliseconds
        Add-PerfSample (Get-PerfKey) $msTotal $msDraw $msText ($msTotal - $msBeforePush)
        if (-not $ok) {
            # Fall back to normal drawing rather than risk an invisible timer
            Write-Log 'See-through drawing failed, so the timer has switched back to normal drawing.'
            [Win.Layered]::MakeNormal($form.Handle)
            $script:pillMargin = 0
            $script:layered = $false
            $form.Invalidate()
        }
    } catch {
        Write-Log "See-through drawing error: $($_.Exception.Message)"
        try { [Win.Layered]::MakeNormal($form.Handle) } catch {}
        $script:pillMargin = 0
        $script:layered = $false
        $form.Invalidate()
    } finally { $bmp.Dispose() }
}

# In the pill view the window is cut to the pill's shape, so no backdrop shows in the corners
function Update-Region {
    if (-not $script:layered) { return }
    $w = $form.ClientSize.Width
    $h = $form.ClientSize.Height
    $key = '{0}:{1}:{2}:{3}:{4}:{5}:{6}:{7}' -f $script:mode, $w, $h, $script:drawerOpen, $script:drawerUp, $script:timerOffX, (Test-FullShadow), $script:fullMargin
    if ($key -eq $script:regionKey) { return }
    $script:regionKey = $key
    try {
        if ($script:mode -eq 'pill') { [Win.Layered]::SetRoundRegion($form.Handle, $w, $h, [int][math]::Floor(($PillH / 2 + $script:pillMargin) * $Zoom)) }
        elseif ($script:useRenderer -and $script:fullMargin -eq 0) {
            [Win.Layered]::SetRoundRegion($form.Handle, $w, $h, 0)
        }
        elseif ($script:useRenderer) {
            # Cut to the outline plus room for the shadow, so nothing else can show around it
            Sync-RendererLayout
            $outline = $script:renderer.GetOutline()
            $toPixels = New-Object System.Drawing.Drawing2D.Matrix
            $toPixels.Scale([float]$Zoom, [float]$Zoom)
            $outline.Transform($toPixels)
            $allowance = if ($script:renderer.Shadow) { [float](2 * $script:fullMargin * $Zoom + 2) } else { [float]4 }
            [Win.Layered]::SetPathRegion($form.Handle, $outline, $allowance)
            $toPixels.Dispose(); $outline.Dispose()
        }
        elseif ($script:drawerOpen) {
            # L-shaped: the empty corner beside the timer is cut away, so nothing can show there
            $outline = New-DrawerShape
            $toPixels = New-Object System.Drawing.Drawing2D.Matrix
            $toPixels.Scale([float]$Zoom, [float]$Zoom)
            $outline.Transform($toPixels)
            [Win.Layered]::SetPathRegion($form.Handle, $outline)
            $toPixels.Dispose(); $outline.Dispose()
        }
        else { [Win.Layered]::SetRoundRegion($form.Handle, $w, $h, 0) }
    } catch { Write-Log "Could not shape the window: $($_.Exception.Message)" }
}

function Request-Redraw {
    if ($script:layered) { Render-Frame } else { $form.Invalidate() }
}

# Blur only shows while the account list is see-through, so it's switched off at 'Off'
function Apply-BlurState {
    if (-not $script:layered) { return }
    if ($script:mode -eq 'full' -and $form.IsHandleCreated) {
        Update-DrawerGeometry
        $script:regionKey = ''
        Apply-CornerStyle
    }
    # With the standard drawing the drawer can make an L shape, which the blur can't follow, so it pauses then
    $want = $script:blurOn -and $script:listLevel -ne 'Off' -and $script:mode -eq 'full' -and ($script:useRenderer -or -not $script:drawerOpen)
    try {
        if ($want) { [void][Win.Layered]::SetBlur($form.Handle, $true) }
        else { [Win.Layered]::ClearAllBlur($form.Handle) }
    } catch { Write-Log "Blur change error: $($_.Exception.Message)" }
}

# Windows 11 rounds window corners itself; the pill draws its own fully round ends, so turn that off there
function Apply-CornerStyle {
    $pref = if ($script:mode -eq 'pill' -or ($script:useRenderer -and $script:fullMargin -gt 0) -or ($script:drawerOpen -and -not $script:useRenderer)) { 1 } else { 2 }   # 1 = don't round, 2 = round
    try { [void][Win.Dwm]::DwmSetWindowAttribute($form.Handle, 33, [ref]$pref, 4) } catch {}
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

function Set-PillEffect($which) {
    if ($which -eq 'shadow') { $script:PillShadow = -not $script:PillShadow }
    elseif ($which -eq 'pulse') { $script:PillPulse = -not $script:PillPulse }
    elseif ($which -eq 'dot') { $script:PillLiveDot = -not $script:PillLiveDot }
    if ($which -eq 'shadow' -and $script:mode -eq 'full') {
        Update-DrawerGeometry
        $script:regionKey = ''
        Apply-CornerStyle
    }
    $lines = @("shadow=$([int][bool]$script:PillShadow)", "pulse=$([int][bool]$script:PillPulse)", "dot=$([int][bool]$script:PillLiveDot)")
    try { Set-Content -Path $EffectsFile -Value $lines } catch {}
    Request-Redraw
}

function Load-PillEffects {
    if (-not (Test-Path $EffectsFile)) { return }
    foreach ($line in Get-Content $EffectsFile) {
        $kv = $line -split '=', 2
        if ($kv.Count -ne 2) { continue }
        $on = $kv[1].Trim() -ne '0'
        switch ($kv[0].Trim()) {
            'shadow' { $script:PillShadow = $on }
            'pulse'  { $script:PillPulse = $on }
            'dot'    { $script:PillLiveDot = $on }
        }
    }
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
function Mix-Colour($a, $b, [double]$t) {
    return $C::FromArgb(255, [int]($a.R + ($b.R - $a.R) * $t), [int]($a.G + ($b.G - $a.G) * $t), [int]($a.B + ($b.B - $a.B) * $t))
}

function New-PxFont($name, [double]$size, $k) {
    return New-Object System.Drawing.Font($name, [float]($size * $k), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
}

# ---- Note prompt: a small box beside the timer. Enter saves, Escape or ignoring it skips. ----
function Show-NotePrompt($info) {
    Complete-NotePrompt $true
    $k = $Zoom / $Scale
    $theme = if ($script:view) { Get-StateTheme $script:view } else { $Themes.running }

    $nf = New-Object System.Windows.Forms.Form
    $nf.FormBorderStyle = 'None'
    $nf.ShowInTaskbar = $false
    $nf.TopMost = $true
    $nf.StartPosition = 'Manual'
    $nf.AutoScaleMode = 'None'
    $nf.BackColor = $theme.Panel
    $w = $form.Width
    $h = [int](62 * $k)
    $nf.ClientSize = New-Object System.Drawing.Size($w, $h)

    # Below the timer if there's room, otherwise above it
    $area = [System.Windows.Forms.Screen]::FromControl($form).WorkingArea
    $gap = [int](6 * $k)
    $y = $form.Bottom + $gap
    if (($y + $h) -gt $area.Bottom) { $y = $form.Top - $h - $gap }
    $nf.Location = New-Object System.Drawing.Point($form.Left, $y)

    $lbl = New-Object System.Windows.Forms.Label
    $lbl.AutoSize = $false
    $lbl.Location = New-Object System.Drawing.Point([int](12 * $k), [int](7 * $k))
    $lbl.Size = New-Object System.Drawing.Size(($w - [int](24 * $k)), [int](18 * $k))
    $lbl.ForeColor = $cWhite
    $lbl.Font = New-PxFont 'Segoe UI' 11.5 $k
    $lbl.AutoEllipsis = $true
    $lbl.Text = 'What did you work on for {0}? ({1})' -f $info.Name, (Format-Dur $info.Seconds)

    $box = New-Object System.Windows.Forms.TextBox
    $box.BorderStyle = 'FixedSingle'
    $box.BackColor = Mix-Colour $theme.Panel $cWhite 0.12
    $box.ForeColor = $cWhite
    $box.Font = New-PxFont 'Segoe UI' 12.5 $k
    $box.Location = New-Object System.Drawing.Point([int](12 * $k), [int](29 * $k))
    $box.Width = $w - [int](24 * $k)

    $nf.Controls.Add($lbl)
    $nf.Controls.Add($box)
    $nf.Tag = $info

    # Disappears after 20 seconds if nothing has been typed
    $script:noteTimer = New-Object System.Windows.Forms.Timer
    $script:noteTimer.Interval = 20000
    $script:noteTimer.Add_Tick({
        $script:noteTimer.Stop()
        $f = $script:noteForm
        if ($f -and -not $f.IsDisposed -and -not $f.Controls[1].Text.Trim()) { Complete-NotePrompt $false }
    })
    $box.Add_TextChanged({ if ($script:noteTimer) { $script:noteTimer.Stop() } })
    $box.Add_KeyDown({
        if ($_.KeyCode -eq 'Enter') { $_.SuppressKeyPress = $true; Complete-NotePrompt $true }
        elseif ($_.KeyCode -eq 'Escape') { $_.SuppressKeyPress = $true; Complete-NotePrompt $false }
    })
    # Clicking away keeps anything typed
    $nf.Add_Deactivate({ Complete-NotePrompt $true })
    $nf.Add_FormClosed({
        if ($script:noteTimer) { $script:noteTimer.Stop(); $script:noteTimer.Dispose(); $script:noteTimer = $null }
        $script:noteForm = $null
    })

    $script:noteForm = $nf
    try { $round = 2; [void][Win.Dwm]::DwmSetWindowAttribute($nf.Handle, 33, [ref]$round, 4) } catch {}
    $nf.Show()
    $nf.Activate()
    [void]$box.Focus()
    $script:noteTimer.Start()
}

# Save (or skip) the note and close the box. Guarded so closing doesn't trigger it twice.
function Complete-NotePrompt([bool]$save) {
    $f = $script:noteForm
    if (-not $f -or $f.IsDisposed -or $script:noteClosing) { return }
    $script:noteClosing = $true
    try {
        if ($save) {
            $i = $f.Tag
            $txt = $f.Controls[1].Text.Trim()
            if ($txt -and -not (Save-Note $i.DateStr $i.Name $i.StartStr $txt)) {
                Write-Log "Note for $($i.Name) wasn't saved."
            }
            if ($txt -and $script:weekForm -and -not $script:weekForm.IsDisposed) { Fill-WeeklyView }
        }
        if ($script:noteTimer) { $script:noteTimer.Stop() }
        $f.Close()
    } finally { $script:noteClosing = $false }
}

# ---- Budget box: hours and a period chosen from a list ----
function Show-BudgetPrompt($name) {
    $k = $Zoom / $Scale
    $cur = $script:budgets[$name]
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = "Budget for $name"
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.MinimizeBox = $false
    $dlg.MaximizeBox = $false
    $dlg.ShowInTaskbar = $false
    $dlg.TopMost = $true
    if ($script:appIcon) { $dlg.Icon = $script:appIcon }
    $dlg.StartPosition = 'CenterScreen'
    $dlg.AutoScaleMode = 'None'
    $dlg.Font = New-PxFont 'Segoe UI' 12 $k
    $dlg.ClientSize = New-Object System.Drawing.Size([int](300 * $k), [int](118 * $k))

    $lh = New-Object System.Windows.Forms.Label
    $lh.Text = 'Hours'
    $lh.AutoSize = $true
    $lh.Location = New-Object System.Drawing.Point([int](12 * $k), [int](14 * $k))
    $box = New-Object System.Windows.Forms.TextBox
    $box.Location = New-Object System.Drawing.Point([int](12 * $k), [int](34 * $k))
    $box.Width = [int](110 * $k)
    $box.Text = if ($cur) { $cur.Hours.ToString('0.##', $Inv) } else { '' }

    $lp = New-Object System.Windows.Forms.Label
    $lp.Text = 'Per'
    $lp.AutoSize = $true
    $lp.Location = New-Object System.Drawing.Point([int](136 * $k), [int](14 * $k))
    $combo = New-Object System.Windows.Forms.ComboBox
    $combo.DropDownStyle = 'DropDownList'
    [void]$combo.Items.AddRange(@('Week', 'Month', 'Quarter'))
    $combo.Location = New-Object System.Drawing.Point([int](136 * $k), [int](34 * $k))
    $combo.Width = [int](152 * $k)
    $combo.SelectedIndex = if ($cur) { @('week', 'month', 'quarter').IndexOf($cur.Period) } else { 1 }

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = 'OK'
    $ok.DialogResult = 'OK'
    $ok.Size = New-Object System.Drawing.Size([int](80 * $k), [int](28 * $k))
    $ok.Location = New-Object System.Drawing.Point([int](122 * $k), [int](80 * $k))
    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = 'Cancel'
    $cancel.DialogResult = 'Cancel'
    $cancel.Size = New-Object System.Drawing.Size([int](80 * $k), [int](28 * $k))
    $cancel.Location = New-Object System.Drawing.Point([int](208 * $k), [int](80 * $k))
    $remove = New-Object System.Windows.Forms.Button
    $remove.Text = 'Remove'
    $remove.DialogResult = 'Abort'
    $remove.Size = New-Object System.Drawing.Size([int](80 * $k), [int](28 * $k))
    $remove.Location = New-Object System.Drawing.Point([int](12 * $k), [int](80 * $k))
    $remove.Visible = [bool]$cur

    $dlg.AcceptButton = $ok
    $dlg.CancelButton = $cancel
    foreach ($ctl in @($lh, $box, $lp, $combo, $ok, $cancel, $remove)) { $dlg.Controls.Add($ctl) }

    $script:dialogOpen = $true
    try { $result = $dlg.ShowDialog((Get-DialogOwner)) } finally { $script:dialogOpen = $false }
    $text = $box.Text.Trim()
    $period = @('week', 'month', 'quarter')[$combo.SelectedIndex]
    $dlg.Dispose()

    if ($result -eq [System.Windows.Forms.DialogResult]::Abort) { return @{ Action = 'remove' } }
    if ($result -ne [System.Windows.Forms.DialogResult]::OK) { return $null }
    $hours = 0.0
    if (-not [double]::TryParse($text.Replace(',', '.'), [Globalization.NumberStyles]::Float, $Inv, [ref]$hours) -or $hours -le 0) {
        Show-Message "Couldn't read '$text' as a number of hours. Try something like 20 or 7.5."
        return $null
    }
    return @{ Action = 'set'; Hours = $hours; Period = $period }
}

function Set-AccountBudget($name) {
    $r = Show-BudgetPrompt $name
    if (-not $r) { return }
    if ($r.Action -eq 'remove') { [void]$script:budgets.Remove($name) }
    else { $script:budgets[$name] = @{ Hours = $r.Hours; Period = $r.Period } }
    Save-Budgets
    Update-View
    Request-Redraw
    if ($script:weekForm -and -not $script:weekForm.IsDisposed) { Fill-WeeklyView }
}

# ---- Weekly view ----
$AccountColours = @('#4fc3f7', '#ffb74d', '#81c784', '#ba68c8', '#f06292', '#4db6ac',
                    '#ffd54f', '#9575cd', '#e57373', '#a1887f', '#90a4ae', '#aed581') |
                  ForEach-Object { [System.Drawing.ColorTranslator]::FromHtml($_) }

function Format-Hours($sec) {
    if ($sec -lt 30) { return '' }
    if ($script:hoursFormat -eq 'hm') {
        $mins = [int][math]::Round($sec / 60)
        return '{0}:{1:00}' -f [math]::Floor($mins / 60), ($mins % 60)
    }
    return ([math]::Round($sec / 3600, 2)).ToString('0.00', $Inv)
}

function Get-WeekData($ws) {
    $we = $ws.AddDays(7)
    $acc = @{}
    $lunch = New-Object double[] 7
    $lunchAdj = New-Object double[] 7
    $lunchIv = @{}
    $sessions = @()
    foreach ($r in @(Read-Sessions)) {
        if ($r.Start -lt $ws -or $r.Start -ge $we) { continue }
        $d = [int][math]::Floor(($r.Start.Date - $ws).TotalDays)
        if ($r.Name -eq 'Lunch') {
            $lunch[$d] += $r.Seconds
            if ($r.IsAdjust) { $lunchAdj[$d] += $r.Seconds }
            else {
                if (-not $lunchIv.ContainsKey($d)) { $lunchIv[$d] = @() }
                $lunchIv[$d] += ,@($r.Start, $r.End)
            }
            continue
        }
        if (-not $acc.ContainsKey($r.Name)) { $acc[$r.Name] = New-Object double[] 7 }
        $acc[$r.Name][$d] += $r.Seconds
        if (-not $r.IsAdjust) { $sessions += $r }
    }

    # Include whatever is running right now
    $now = Get-Date
    if ($script:active -and $script:active.Start -ge $ws -and $script:active.Start -lt $we) {
        $d = [int][math]::Floor(($script:active.Start.Date - $ws).TotalDays)
        $e = ($now - $script:active.Start).TotalSeconds
        if ($script:active.Name -eq 'Lunch') {
            $lunch[$d] += $e
            if (-not $lunchIv.ContainsKey($d)) { $lunchIv[$d] = @() }
            $lunchIv[$d] += ,@($script:active.Start, $now)
        } else {
            if (-not $acc.ContainsKey($script:active.Name)) { $acc[$script:active.Name] = New-Object double[] 7 }
            $acc[$script:active.Name][$d] += $e
        }
    }

    # Accounts in the timer's order, then any others used that week
    $known = @($script:accounts | ForEach-Object { $_.Name })
    $names = @($known | Where-Object { $acc.ContainsKey($_) })
    $names += @($acc.Keys | Where-Object { $known -notcontains $_ } | Sort-Object)

    # Unallocated: working-day time (less lunch) not put against an account. Weekdays up to today only.
    $unalloc = New-Object double[] 7
    for ($d = 0; $d -lt 7; $d++) {
        $day = $ws.AddDays($d)
        $dayPlan = Get-DayPlan $day
        if (-not $dayPlan.Working -or $day -gt $now.Date) { continue }
        $s = $dayPlan.Start
        $e = $dayPlan.End
        $cap = if ($day -eq $now.Date) { Limit-Time $now $s $e } else { $e }
        $worked = ($cap - $s).TotalSeconds
        $l = $lunchAdj[$d]
        if ($lunchIv.ContainsKey($d)) {
            foreach ($iv in $lunchIv[$d]) {
                $a = Limit-Time $iv[0] $s $cap
                $b = Limit-Time $iv[1] $s $cap
                if ($b -gt $a) { $l += ($b - $a).TotalSeconds }
            }
        }
        # Time marked as finished for the day counts like lunch
        foreach ($away in @(Get-AwayOn $day $now)) {
            $a = Limit-Time $away[0] $s $cap
            $b = Limit-Time $away[1] $s $cap
            if ($b -gt $a) { $l += ($b - $a).TotalSeconds }
        }
        $billable = [math]::Max(0.0, $worked - $l)
        $sum = 0.0
        foreach ($n in $names) { $sum += $acc[$n][$d] }
        $unalloc[$d] = [math]::Max(0.0, $billable - $sum)
    }

    # Show working days, plus any other day where something was logged
    $days = @()
    for ($d = 0; $d -lt 7; $d++) {
        $any = (Get-DayPlan $ws.AddDays($d)).Working -or $lunch[$d] -gt 0
        foreach ($n in $names) { if ($acc[$n][$d] -gt 0) { $any = $true } }
        if ($any) { $days += $d }
    }
    if ($days.Count -eq 0) { $days = @(0..6) }

    return @{ Accounts = $acc; Names = $names; Lunch = $lunch; Unalloc = $unalloc; Sessions = $sessions; Days = $days }
}

function New-ThemedGrid($k, $theme) {
    $panel = $theme.Panel
    $gv = New-Object System.Windows.Forms.DataGridView
    $gv.ReadOnly = $true
    $gv.AllowUserToAddRows = $false
    $gv.AllowUserToDeleteRows = $false
    $gv.AllowUserToResizeRows = $false
    $gv.AllowUserToOrderColumns = $false
    $gv.RowHeadersVisible = $false
    $gv.SelectionMode = 'CellSelect'
    $gv.ClipboardCopyMode = 'EnableWithAutoHeaderText'
    $gv.BorderStyle = 'None'
    $gv.CellBorderStyle = 'SingleHorizontal'
    $gv.BackgroundColor = $panel
    $gv.GridColor = Mix-Colour $panel $cWhite 0.15
    $gv.EnableHeadersVisualStyles = $false
    $gv.ColumnHeadersBorderStyle = 'None'
    $gv.ColumnHeadersDefaultCellStyle.BackColor = $theme.Circle
    $gv.ColumnHeadersDefaultCellStyle.ForeColor = $cWhite
    $gv.ColumnHeadersDefaultCellStyle.SelectionBackColor = $theme.Circle
    $gv.ColumnHeadersDefaultCellStyle.Font = New-PxFont 'Segoe UI Semibold' 12 $k
    $gv.ColumnHeadersHeightSizeMode = 'DisableResizing'
    $gv.ColumnHeadersHeight = [int](28 * $k)
    $gv.DefaultCellStyle.BackColor = $panel
    $gv.DefaultCellStyle.ForeColor = $cWhite
    $gv.DefaultCellStyle.SelectionBackColor = $theme.CircleHot
    $gv.DefaultCellStyle.SelectionForeColor = $cWhite
    $gv.DefaultCellStyle.Font = New-PxFont 'Segoe UI' 12 $k
    $gv.AlternatingRowsDefaultCellStyle.BackColor = Mix-Colour $panel $cWhite 0.05
    $gv.RowTemplate.Height = [int](26 * $k)
    $gv.AutoSizeColumnsMode = 'Fill'
    $gv.ScrollBars = 'None'
    $gv.GetType().GetProperty('DoubleBuffered', [Reflection.BindingFlags]'NonPublic,Instance').SetValue($gv, $true, $null)
    $gv.Add_CellPainting({
        if ($_.RowIndex -ne -1) { return }
        try { Draw-SmoothHeader $_ } catch {}
    })
    return $gv
}

# Column headers: smooth anti-aliased text without coloured fringes
function Draw-SmoothHeader($e) {
    $g = $e.Graphics
    $hb = New-Object System.Drawing.SolidBrush $e.CellStyle.BackColor
    $g.FillRectangle($hb, $e.CellBounds)
    $hb.Dispose()
    if ($e.ColumnIndex -ge 0) {
        $g.TextRenderingHint = 'AntiAliasGridFit'
        $sf = New-Object System.Drawing.StringFormat
        $sf.LineAlignment = 'Center'
        $sf.Alignment = if ("$($e.CellStyle.Alignment)" -like '*Right') { 'Far' } else { 'Near' }
        $sf.Trimming = 'EllipsisCharacter'
        $sf.FormatFlags = 'NoWrap'
        $pad = [float](6 * $script:wk)
        $b = $e.CellBounds
        $rect = New-Object System.Drawing.RectangleF(($b.X + $pad), $b.Y, ($b.Width - 2 * $pad), $b.Height)
        $tb = New-Object System.Drawing.SolidBrush $e.CellStyle.ForeColor
        $g.DrawString("$($e.FormattedValue)", $e.CellStyle.Font, $tb, $rect, $sf)
        $tb.Dispose(); $sf.Dispose()
    }
    $e.Handled = $true
}

function New-ThemedButton($text, $k, $theme) {
    $b = New-Object System.Windows.Forms.Button
    $b.Text = $text
    $b.FlatStyle = 'Flat'
    $b.FlatAppearance.BorderSize = 0
    $b.BackColor = $theme.Circle
    $b.ForeColor = $cWhite
    $b.Font = New-PxFont 'Segoe UI Semibold' 12 $k
    $b.Cursor = [System.Windows.Forms.Cursors]::Hand
    $sz = [System.Windows.Forms.TextRenderer]::MeasureText($text, $b.Font)
    $b.Size = New-Object System.Drawing.Size(($sz.Width + [int](24 * $k)), [int](30 * $k))
    $b.Add_Paint({ try { Draw-SmoothButton $_.Graphics $this } catch {} })
    $b.Add_MouseEnter({ $this.Tag = 'hover'; $this.Invalidate() })
    $b.Add_MouseLeave({ $this.Tag = $null; $this.Invalidate() })
    $b.Add_TextChanged({ $this.Invalidate() })
    $b.Add_EnabledChanged({ $this.Invalidate() })
    return $b
}

# Paints over the standard button: rounded shape, smooth anti-aliased text without coloured fringes
function Draw-SmoothButton($g, $b) {
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear($b.Parent.BackColor)
    $base = $b.BackColor
    if ($b.Tag -eq 'hover' -and $b.Enabled) { $base = Mix-Colour $base $cWhite 0.12 }
    $path = New-RoundedPath 0 0 ([float]($b.Width - 1)) ([float]($b.Height - 1)) ([float](5 * $script:wk))
    $br = New-Object System.Drawing.SolidBrush $base
    $g.FillPath($br, $path)
    $br.Dispose(); $path.Dispose()
    $tc = if ($b.Enabled) { $cWhite } else { Mix-Colour $base $cWhite 0.5 }
    $tb = New-Object System.Drawing.SolidBrush $tc
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString($b.Text, $b.Font, $tb, (New-Object System.Drawing.RectangleF(0, 0, $b.Width, $b.Height)), $sf)
    $tb.Dispose(); $sf.Dispose()
}

function New-SectionLabel($text, $k, $size, [bool]$bold) {
    $l = New-Object System.Windows.Forms.Label
    $l.AutoSize = $true
    $l.ForeColor = $cWhite
    # A solid background (not see-through), so text isn't redrawn over itself and thickened when scrolling
    $l.BackColor = if ($script:wvPanel) { $script:wvPanel } else { [System.Drawing.Color]::Transparent }
    $l.Font = New-PxFont ($(if ($bold) { 'Segoe UI Semibold' } else { 'Segoe UI' })) $size $k
    $l.Text = $text
    return $l
}

function Show-WeeklyView {
    if ($script:weekForm -and -not $script:weekForm.IsDisposed) {
        $script:weekForm.Activate()
        return
    }
    $k = (Get-CursorDpi) / 96
    $script:wk = $k
    $theme = $Themes.running
    $script:wvPanel = $theme.Panel
    $script:weekStart = Get-WeekStart

    $wf = New-Object System.Windows.Forms.Form
    $wf.Text = 'Account timer - Weekly view'
    if ($script:appIcon) { $wf.Icon = $script:appIcon }
    $wf.AutoScaleMode = 'None'
    $wf.StartPosition = 'Manual'
    $wf.BackColor = $theme.Panel
    $wf.ForeColor = $cWhite
    $wf.Font = New-PxFont 'Segoe UI' 12 $k
    $wf.AutoScroll = $true
    $wf.TopMost = $true
    $wf.KeyPreview = $true
    $area = [System.Windows.Forms.Screen]::FromPoint([System.Windows.Forms.Cursor]::Position).WorkingArea
    $W = [int](820 * $k)
    $H = [math]::Min([int](760 * $k), $area.Height - [int](40 * $k))
    $wf.ClientSize = New-Object System.Drawing.Size($W, $H)
    $wf.Location = New-Object System.Drawing.Point(($area.Left + [int](($area.Width - $wf.Width) / 2)), ($area.Top + [int](($area.Height - $wf.Height) / 2)))
    $wf.MinimumSize = New-Object System.Drawing.Size([int](600 * $k), [int](400 * $k))
    # Draw off-screen first, so scrolling repaints cleanly
    $wf.GetType().GetProperty('DoubleBuffered', [Reflection.BindingFlags]'NonPublic,Instance').SetValue($wf, $true, $null)

    $wv = @{ Form = $wf; Theme = $theme }
    $wv.Prev  = New-ThemedButton 'Previous week' $k $theme
    $wv.Next  = New-ThemedButton 'Next week' $k $theme
    $wv.Copy  = New-ThemedButton 'Copy hours table' $k $theme
    $wv.Title = New-SectionLabel '' $k 17 $true
    $wv.Chart = New-Object System.Windows.Forms.Panel
    $wv.Chart.BackColor = $theme.Panel
    $wv.Chart.GetType().GetProperty('DoubleBuffered', [Reflection.BindingFlags]'NonPublic,Instance').SetValue($wv.Chart, $true, $null)
    $wv.HoursLabel  = New-SectionLabel 'Hours by account and day' $k 14 $true
    $wv.HoursHint   = New-SectionLabel '' $k 11.5 $false
    $wv.Grid        = New-ThemedGrid $k $theme
    $wv.BudgetLabel = New-SectionLabel 'Budgets' $k 14 $true
    $wv.BudgetHint  = New-SectionLabel '' $k 11.5 $false
    $wv.BudgetGrid  = New-ThemedGrid $k $theme
    $wv.NotesLabel  = New-SectionLabel 'Notes' $k 14 $true
    $wv.NotesHint   = New-SectionLabel '' $k 11.5 $false
    $wv.NotesGrid   = New-ThemedGrid $k $theme
    $wv.NotesGrid.ReadOnly = $false
    $wv.NotesGrid.EditMode = 'EditOnEnter'

    # Tabs: the summary, or every stretch of time individually
    $wv.TabSummary  = New-ThemedButton 'Summary' $k $theme
    $wv.TabEntries  = New-ThemedButton 'Time entries' $k $theme
    $wv.OpenEntries = New-ThemedButton 'Open entries file' $k $theme
    $wv.MergeToggle = New-ThemedButton 'Merge consecutive: Off' $k $theme
    $wv.EntriesHint = New-SectionLabel '' $k 11.5 $false
    $wv.EntriesHint.AutoSize = $true
    $wv.Entries     = New-Object System.Windows.Forms.Panel
    $wv.Entries.BackColor = $theme.Panel
    $wv.Entries.GetType().GetProperty('DoubleBuffered', [Reflection.BindingFlags]'NonPublic,Instance').SetValue($wv.Entries, $true, $null)
    $wv.EntryTip    = New-Object System.Windows.Forms.ToolTip
    Set-ThemedTooltip $wv.EntryTip
    foreach ($ctl in @($wv.TabSummary, $wv.TabEntries, $wv.OpenEntries, $wv.MergeToggle, $wv.EntriesHint, $wv.Entries)) { $wf.Controls.Add($ctl) }
    $wv.MergeToggle.Add_Click({
        $script:mergeEntries = -not $script:mergeEntries
        try { Set-Content -Path $MergeFile -Value ([int]$script:mergeEntries) } catch {}
        try {
            Fill-WeeklyView
            Write-WeekEntries $script:weekStart
            if ($script:weekStart -ne (Get-WeekStart)) { Write-WeekEntries (Get-WeekStart) }
        } catch { Show-WeeklyError $_ }
    })
    $wv.TabSummary.Add_Click({ $script:weekMode = 'summary'; Layout-WeeklyView })
    $wv.TabEntries.Add_Click({ $script:weekMode = 'entries'; Layout-WeeklyView })
    $wv.OpenEntries.Add_Click({
        try {
            Flush-Pending
            Write-WeekEntries $script:weekStart
            $f = Get-WeekEntriesFile $script:weekStart
            if (Test-Path $f) { Invoke-Item $f }
        } catch { Show-WeeklyError $_ }
    })
    $wv.Entries.Add_Paint({ try { Draw-EntriesView $_.Graphics $this } catch { Write-Log "Entries view error: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)" } })
    $wv.Entries.Add_MouseMove({
        $b = Get-EntryBlockAt $_.Location
        if ($b -ne $script:entryHover) {
            $script:entryHover = $b
            $this.Cursor = if ($b) { [System.Windows.Forms.Cursors]::Hand } else { [System.Windows.Forms.Cursors]::Default }
            if ($b) { $script:wv.EntryTip.Show((Get-EntryTipText $b.Entry), $this, ($_.X + 14), ($_.Y + 14), 8000) }
            else { $script:wv.EntryTip.Hide($this) }
            $this.Invalidate()
        }
    })
    $wv.Entries.Add_MouseLeave({
        if ($script:entryHover) { $script:entryHover = $null; $script:wv.EntryTip.Hide($this); $this.Invalidate() }
    })
    # Click to select, Ctrl+click to add to the selection, right-click for options
    $wv.Entries.Add_MouseUp({
        try {
            $b = Get-EntryBlockAt $_.Location
            $blk = if ($b) { $b.Entry } else { $null }
            $ctrl = ([System.Windows.Forms.Control]::ModifierKeys -band [System.Windows.Forms.Keys]::Control) -ne 0
            if ($_.Button -eq 'Left') {
                if (-not $blk) { $script:entrySelected = @() }
                elseif ($ctrl) {
                    if ($script:entrySelected -contains $blk) { $script:entrySelected = @($script:entrySelected | Where-Object { $_ -ne $blk }) }
                    else { $script:entrySelected = @($script:entrySelected) + @($blk) }
                }
                else { $script:entrySelected = @($blk) }
                $this.Invalidate()
                Update-EntryButtons
            } elseif ($_.Button -eq 'Right') {
                if ($blk) {
                    if (-not ($script:entrySelected -contains $blk)) { $script:entrySelected = @($blk); $this.Invalidate() }
                    Show-EntryMenu $this $_.Location $blk.Day
                } else {
                    Show-EntryMenu $this $_.Location (Get-EntryDayAt $_.Location.X)
                }
            }
        } catch { Show-WeeklyError $_ }
    })
    $wv.AddEntry = New-ThemedButton 'Add entry' $k $theme
    $wv.Undo = New-ThemedButton 'Undo last change' $k $theme
    $wf.Controls.Add($wv.AddEntry)
    $wf.Controls.Add($wv.Undo)
    $wv.AddEntry.Add_Click({ try { Add-TimeEntry (Get-DefaultEntryDay) } catch { Show-WeeklyError $_ } })
    $wv.Undo.Add_Click({ try { Undo-LogChange } catch { Show-WeeklyError $_ } })
    $wf.Add_KeyDown({
        if ($script:weekMode -eq 'entries' -and $_.KeyCode -eq 'Delete' -and @($script:entrySelected).Count -gt 0) {
            try { Remove-EntryBlocks } catch { Show-WeeklyError $_ }
        }
    })

    foreach ($ctl in @($wv.Prev, $wv.Next, $wv.Copy, $wv.Title, $wv.Chart, $wv.HoursLabel, $wv.HoursHint, $wv.Grid,
                     $wv.BudgetLabel, $wv.BudgetHint, $wv.BudgetGrid, $wv.NotesLabel, $wv.NotesHint, $wv.NotesGrid)) {
        $wf.Controls.Add($ctl)
    }
    $script:wv = $wv

    $wv.Prev.Add_Click({ $script:weekStart = $script:weekStart.AddDays(-7); try { Fill-WeeklyView } catch { Show-WeeklyError $_ } })
    $wv.Next.Add_Click({ $script:weekStart = $script:weekStart.AddDays(7); try { Fill-WeeklyView } catch { Show-WeeklyError $_ } })
    $wv.Copy.Add_Click({
        $g = $script:wv.Grid
        $lines = @()
        $lines += (@($g.Columns | ForEach-Object { $_.HeaderText }) -join "`t")
        foreach ($row in $g.Rows) { $lines += (@($row.Cells | ForEach-Object { "$($_.Value)" }) -join "`t") }
        [System.Windows.Forms.Clipboard]::SetText(($lines -join "`r`n"))
        $script:wv.Copy.Text = 'Copied'
        $ct = New-Object System.Windows.Forms.Timer
        $ct.Interval = 1500
        $ct.Add_Tick({ $this.Stop(); if ($script:wv) { $script:wv.Copy.Text = 'Copy hours table' } })
        $ct.Start()
    })
    $wv.Chart.Add_Paint({ try { Draw-WeekChart $_.Graphics $this } catch { Write-Log "Chart error: $($_.Exception.Message)" } })
    $wv.Chart.Add_Resize({ $this.Invalidate() })
    $wf.Add_Resize({ if ($script:wv) { Layout-WeeklyView } })
    $wf.Add_KeyDown({ if ($_.KeyCode -eq 'Escape') { $script:weekForm.Close() } })

    # Notes are typed straight into the table and saved when you leave the cell
    $wv.NotesGrid.Add_CellEndEdit({
        $row = $script:wv.NotesGrid.Rows[$_.RowIndex]
        $i = $row.Tag
        $txt = "$($row.Cells['Note'].Value)"
        if (Save-Note $i.DateStr $i.Name $i.StartStr $txt) {
            $row.Cells['Note'].Style.BackColor = if ($txt.Trim()) { [System.Drawing.Color]::Empty } else { $script:wv.Unnoted }
        } else {
            Show-Message "Couldn't save the note. If notes.csv is open in Excel, close it and try again."
        }
    })

    $wf.Add_FormClosed({ $script:weekForm = $null; $script:wv = $null })
    $script:weekForm = $wf
    Set-TitleBarColours $wf $theme
    $wf.Show()
    $wf.Activate()
    try { Fill-WeeklyView }
    catch { Show-WeeklyError $_ }
}

# Show what went wrong, so it can be passed on and fixed
function Show-WeeklyError($err) {
    $msg = "{0} (line {1})" -f $err.Exception.Message, $err.InvocationInfo.ScriptLineNumber
    Write-Log "Weekly view error: $msg"
    $script:dialogOpen = $true
    try {
        $owner = if ($script:weekForm -and -not $script:weekForm.IsDisposed) { $script:weekForm } else { $form }
        [void][System.Windows.Forms.MessageBox]::Show($owner, "The weekly view hit a problem:`n`n$msg", 'Account timer')
    } finally { $script:dialogOpen = $false }
}

function Fill-WeeklyView {
    $wv = $script:wv
    if (-not $wv) { return }
    $k = $script:wk
    $ws = $script:weekStart
    $data = Get-WeekData $ws
    $script:weekData = $data
    $wv.Title.Text = 'Week commencing ' + $ws.ToString('d MMMM yyyy', $GB)
    $wv.HoursHint.Text = $(if ($script:hoursFormat -eq 'hm') { 'Hours and minutes' } else { 'Decimal hours' }) + ', ready for your timesheet. Select cells and press Ctrl+C to copy part of it.'
    $wv.Next.Enabled = ($ws -lt (Get-WeekStart))

    # ---- Hours by account and day ----
    $g = $wv.Grid
    $g.Rows.Clear()
    $g.Columns.Clear()
    $col = $g.Columns.Add('Account', 'Account')
    $g.Columns[$col].FillWeight = 220
    foreach ($d in $data.Days) {
        $ci = $g.Columns.Add("d$d", $ws.AddDays($d).ToString('ddd dd/MM', $GB))
        $g.Columns[$ci].DefaultCellStyle.Alignment = 'MiddleRight'
        $g.Columns[$ci].HeaderCell.Style.Alignment = 'MiddleRight'
    }
    $ci = $g.Columns.Add('Total', 'Total')
    $g.Columns[$ci].DefaultCellStyle.Alignment = 'MiddleRight'
    $g.Columns[$ci].HeaderCell.Style.Alignment = 'MiddleRight'
    foreach ($column in $g.Columns) { $column.SortMode = 'NotSortable' }

    $dayTotals = New-Object double[] 7
    foreach ($n in $data.Names) {
        $cells = @($n)
        $sum = 0.0
        foreach ($d in $data.Days) { $v = $data.Accounts[$n][$d]; $cells += (Format-Hours $v); $sum += $v; $dayTotals[$d] += $v }
        $cells += (Format-Hours $sum)
        [void]$g.Rows.Add([object[]]$cells)
    }
    $bold = New-PxFont 'Segoe UI Semibold' 12 $k
    $faint = Mix-Colour $wv.Theme.Panel $cWhite 0.7
    foreach ($extra in @(
        @{ Name = 'Total (excluding lunch)'; Values = $dayTotals; Bold = $true },
        @{ Name = 'Lunch'; Values = $data.Lunch; Bold = $false },
        @{ Name = 'Unallocated'; Values = $data.Unalloc; Bold = $false })) {
        $cells = @($extra.Name)
        $sum = 0.0
        foreach ($d in $data.Days) { $cells += (Format-Hours $extra.Values[$d]); $sum += $extra.Values[$d] }
        $cells += (Format-Hours $sum)
        $ri = $g.Rows.Add([object[]]$cells)
        if ($extra.Bold) { $g.Rows[$ri].DefaultCellStyle.Font = $bold }
        else { $g.Rows[$ri].DefaultCellStyle.ForeColor = $faint }
    }
    $g.Height = $g.ColumnHeadersHeight + $g.Rows.Count * $g.RowTemplate.Height + 2
    $g.ClearSelection()

    # ---- Budgets (always the current week, month or quarter) ----
    $bg = $wv.BudgetGrid
    $bg.Rows.Clear()
    $bg.Columns.Clear()
    foreach ($h in @('Account', 'Budget', 'Used', 'Remaining', 'Used %')) {
        $ci = $bg.Columns.Add($h, $h)
        if ($h -ne 'Account') { $bg.Columns[$ci].DefaultCellStyle.Alignment = 'MiddleRight'; $bg.Columns[$ci].HeaderCell.Style.Alignment = 'MiddleRight' }
        $bg.Columns[$ci].SortMode = 'NotSortable'
    }
    $bg.Columns[0].FillWeight = 220
    $budgetNames = @($script:accounts | ForEach-Object { $_.Name } | Where-Object { $script:budgets.ContainsKey($_) })
    $weekNow = Get-WeekStart
    foreach ($n in $budgetNames) {
        $b = $script:budgets[$n]
        $used = if ($b.Period -eq 'week') { [double]$script:weekDone[$n] }
                elseif ($b.Period -eq 'month') { [double]$script:monthDone[$n] }
                else { [double]$script:quarterDone[$n] }
        if ($script:active -and $script:active.Name -eq $n) { $used += ((Get-Date) - $script:active.Start).TotalSeconds }
        $limit = $b.Hours * 3600
        $frac = $used / $limit
        $cells = @($n, ('{0}h per {1}' -f $b.Hours.ToString('0.##', $Inv), $b.Period), (Format-Dur $used),
                   (Format-Dur ([math]::Max(0.0, $limit - $used))), ('{0:0}%' -f ($frac * 100)))
        $ri = $bg.Rows.Add([object[]]$cells)
        if ($frac -ge 1) { $bg.Rows[$ri].Cells[4].Style.ForeColor = $BudgetOverColour }
        elseif ($frac -ge 0.8) { $bg.Rows[$ri].Cells[4].Style.ForeColor = $BudgetWarnColour }
    }
    $wv.HasBudgets = $budgetNames.Count -gt 0
    $bg.Visible = $wv.HasBudgets
    $bg.Height = $bg.ColumnHeadersHeight + $bg.Rows.Count * $bg.RowTemplate.Height + 2
    $bg.ClearSelection()
    $wv.BudgetHint.Text = if ($budgetNames.Count -gt 0) { 'For the current week, month or quarter, whichever week you are looking at.' }
                          else { "No budgets set yet. Right-click an account on the timer and choose Set budget." }

    # ---- Notes ----
    $ng = $wv.NotesGrid
    $ng.Rows.Clear()
    $ng.Columns.Clear()
    foreach ($h in @('Day', 'Account', 'Start', 'Length', 'Note')) {
        $ci = $ng.Columns.Add($h, $h)
        $ng.Columns[$ci].SortMode = 'NotSortable'
        $ng.Columns[$ci].ReadOnly = ($h -ne 'Note')
    }
    $ng.Columns['Day'].FillWeight = 70
    $ng.Columns['Account'].FillWeight = 130
    $ng.Columns['Start'].FillWeight = 50
    $ng.Columns['Length'].FillWeight = 60
    $ng.Columns['Note'].FillWeight = 300
    $wv.Unnoted = Mix-Colour $wv.Theme.Panel ([System.Drawing.ColorTranslator]::FromHtml('#ffb74d')) 0.3
    $minSec = [math]::Max(1, $NoteMinMinutes) * 60
    $list = @($data.Sessions | Where-Object { $_.Seconds -ge $minSec } | Sort-Object Start)
    foreach ($sn in $list) {
        $info = @{ Name = $sn.Name; DateStr = $sn.Start.ToString('yyyy-MM-dd', $Inv); StartStr = $sn.Start.ToString('HH:mm:ss', $Inv) }
        $note = $script:notes[(Get-NoteKey $info.DateStr $info.Name $info.StartStr)]
        $ri = $ng.Rows.Add([object[]]@($sn.Start.ToString('ddd dd/MM', $GB), $sn.Name, $sn.Start.ToString('HH:mm', $Inv), (Format-Dur $sn.Seconds), "$note"))
        $ng.Rows[$ri].Tag = $info
        if (-not $note) { $ng.Rows[$ri].Cells['Note'].Style.BackColor = $wv.Unnoted }
    }
    $unnoted = @($list | Where-Object { -not $script:notes[(Get-NoteKey $_.Start.ToString('yyyy-MM-dd', $Inv) $_.Name $_.Start.ToString('HH:mm:ss', $Inv))] }).Count
    $wv.NotesHint.Text = if ($list.Count -eq 0) { "No stretches of $NoteMinMinutes minutes or more logged this week." }
                         elseif ($unnoted -gt 0) { "Stretches of $NoteMinMinutes minutes or more. $unnoted still need a note (highlighted). Click a note to add or edit it." }
                         else { "Stretches of $NoteMinMinutes minutes or more. Click a note to edit it." }
    $wv.HasNotes = $list.Count -gt 0
    $ng.Visible = $wv.HasNotes
    $ng.Height = $ng.ColumnHeadersHeight + $list.Count * $ng.RowTemplate.Height + 2
    $ng.ClearSelection()

    # ---- Time entries ----
    $script:weekBlocks = @(Get-EntryBlocks $ws)
    $script:entryHover = $null
    $script:entrySelected = @()
    Update-EntryButtons
    $wv.MergeToggle.Text = if ($script:mergeEntries) { 'Merge consecutive: On' } else { 'Merge consecutive: Off' }
    $wv.MergeToggle.BackColor = if ($script:mergeEntries) { $wv.Theme.CircleHot } else { $wv.Theme.Circle }
    $wv.EntriesHint.Text = if ($script:weekBlocks.Count -eq 0) { 'No time entries logged this week.' }
        elseif ($script:mergeEntries) { "Adjacent recordings are grouped automatically. Manual groups keep the original sessions and show their details on hover. Select entries to edit or group them." }
        else { "Original recordings are shown individually, except for groups you created manually. Select entries to edit or group them." }

    Layout-WeeklyView
    $wv.Chart.Invalidate()
    $wv.Entries.Invalidate()
}

function Layout-WeeklyView {
    $wv = $script:wv
    if (-not $wv) { return }
    $k = $script:wk
    $m = [int](20 * $k)
    $wf = $wv.Form
    $scroll = $wf.AutoScrollPosition
    $W = $wf.ClientSize.Width - 2 * $m
    $y = [int](14 * $k) + $scroll.Y
    $entriesMode = $script:weekMode -eq 'entries'

    # Top row: week buttons, tabs, and the action for the current tab
    $wv.Prev.Location = New-Object System.Drawing.Point($m, $y)
    $wv.Next.Location = New-Object System.Drawing.Point(($wv.Prev.Right + [int](8 * $k)), $y)
    $wv.TabSummary.Location = New-Object System.Drawing.Point(($wv.Next.Right + [int](24 * $k)), $y)
    $wv.TabEntries.Location = New-Object System.Drawing.Point(($wv.TabSummary.Right + [int](4 * $k)), $y)
    $wv.TabSummary.BackColor = if ($entriesMode) { $wv.Theme.Circle } else { $wv.Theme.CircleHot }
    $wv.TabEntries.BackColor = if ($entriesMode) { $wv.Theme.CircleHot } else { $wv.Theme.Circle }
    $wv.Copy.Location = New-Object System.Drawing.Point(($m + $W - $wv.Copy.Width), $y)
    $wv.OpenEntries.Location = New-Object System.Drawing.Point(($m + $W - $wv.OpenEntries.Width), $y)
    $wv.Copy.Visible = -not $entriesMode
    foreach ($ctl in @($wv.OpenEntries, $wv.MergeToggle, $wv.AddEntry, $wv.Undo)) { $ctl.Visible = $entriesMode }
    $y += $wv.Prev.Height + [int](14 * $k)
    $wv.Title.Location = New-Object System.Drawing.Point($m, $y)
    $y += $wv.Title.Height + [int](8 * $k)

    # Show only the current tab's content
    foreach ($ctl in @($wv.Chart, $wv.HoursLabel, $wv.HoursHint, $wv.Grid, $wv.BudgetLabel, $wv.BudgetHint, $wv.NotesLabel, $wv.NotesHint)) {
        $ctl.Visible = -not $entriesMode
    }
    $wv.BudgetGrid.Visible = (-not $entriesMode) -and [bool]$wv.HasBudgets
    $wv.NotesGrid.Visible = (-not $entriesMode) -and [bool]$wv.HasNotes
    $wv.EntriesHint.Visible = $entriesMode
    $wv.Entries.Visible = $entriesMode

    if ($entriesMode) {
        # Toolbar: add and undo on the left, merging and the file on the right
        $wv.AddEntry.Location = New-Object System.Drawing.Point($m, $y)
        $wv.Undo.Location = New-Object System.Drawing.Point(($wv.AddEntry.Right + [int](8 * $k)), $y)
        $wv.OpenEntries.Location = New-Object System.Drawing.Point(($m + $W - $wv.OpenEntries.Width), $y)
        $wv.MergeToggle.Location = New-Object System.Drawing.Point(($wv.OpenEntries.Left - [int](8 * $k) - $wv.MergeToggle.Width), $y)
        $y += $wv.AddEntry.Height + [int](12 * $k)
        $wv.EntriesHint.MaximumSize = New-Object System.Drawing.Size($W, 0)
        $wv.EntriesHint.Location = New-Object System.Drawing.Point($m, $y)
        $y += $wv.EntriesHint.Height + [int](10 * $k)
        $wv.Entries.Location = New-Object System.Drawing.Point($m, $y)
        $wv.Entries.Width = $W
        Build-EntryLayout
        return
    }

    $wv.Chart.Location = New-Object System.Drawing.Point($m, $y)
    $wv.Chart.Size = New-Object System.Drawing.Size($W, [int](230 * $k))
    $y += $wv.Chart.Height + [int](18 * $k)

    $sections = @(
        @($wv.HoursLabel, $wv.HoursHint, $wv.Grid, $true),
        @($wv.BudgetLabel, $wv.BudgetHint, $wv.BudgetGrid, [bool]$wv.HasBudgets),
        @($wv.NotesLabel, $wv.NotesHint, $wv.NotesGrid, [bool]$wv.HasNotes))
    foreach ($pair in $sections) {
        $pair[0].Location = New-Object System.Drawing.Point($m, $y)
        $y += $pair[0].Height + [int](2 * $k)
        $pair[1].Location = New-Object System.Drawing.Point($m, $y)
        $y += $pair[1].Height + [int](6 * $k)
        if ($pair[3]) {
            $pair[2].Location = New-Object System.Drawing.Point($m, $y)
            $pair[2].Width = $W
            $y += $pair[2].Height
        }
        $y += [int](20 * $k)
    }
}

# ---- Time entries tab: one column per day, one block per stretch of time ----

# Each account keeps the same colour here as in the chart
function Get-EntryColour($name) {
    $names = @($script:weekData.Names)
    $i = [array]::IndexOf($names, $name)
    if ($i -lt 0) { $i = $names.Count + [math]::Abs($name.GetHashCode()) % 5 }
    return $AccountColours[$i % $AccountColours.Count]
}

function Build-EntryLayout {
    $wv = $script:wv
    $d = $script:weekData
    if (-not $wv -or -not $d) { return }
    $k = $script:wk
    $W = [double]$wv.Entries.Width
    $days = @($d.Days)
    $gap = 8 * $k
    $colW = ($W - $gap * ($days.Count - 1)) / [math]::Max(1, $days.Count)
    $headH = 30 * $k
    $blocks = @()
    $maxY = $headH
    for ($j = 0; $j -lt $days.Count; $j++) {
        $x = $j * ($colW + $gap)
        $y = $headH + 6 * $k
        foreach ($e in @($script:weekBlocks | Where-Object { $_.Day -eq $days[$j] })) {
            $h = if ($e.Kind -ne 'time' -and $e.Kind -ne 'running') { 24 * $k }
                 elseif ($e.Note -or @($e.Parts).Count -gt 1) { 46 * $k }
                 else { 30 * $k }
            $blocks += @{ Rect = (New-Object System.Drawing.RectangleF([float]$x, [float]$y, [float]$colW, [float]$h)); Entry = $e }
            $y += $h + 5 * $k
        }
        if ($y -gt $maxY) { $maxY = $y }
    }
    $script:entryBlocks = $blocks
    $script:entryCols = @(for ($j = 0; $j -lt $days.Count; $j++) { @{ X = $j * ($colW + $gap); W = $colW; Day = $days[$j] } })
    $wv.Entries.Height = [int]([math]::Max($maxY + 10 * $k, 140 * $k))
    $wv.Entries.Invalidate()
}

function Get-EntryDayAt($x) {
    foreach ($col in $script:entryCols) { if ($x -ge $col.X -and $x -lt ($col.X + $col.W + 8 * $script:wk)) { return $col.Day } }
    return (Get-DefaultEntryDay)
}

# Today if it's in the week being viewed, otherwise Monday
function Get-DefaultEntryDay {
    $d = [int][math]::Floor(([datetime]::Today - $script:weekStart).TotalDays)
    if ($d -ge 0 -and $d -le 6) { return $d }
    return 0
}

function Update-EntryButtons {
    if (-not $script:wv) { return }
    $script:wv.Undo.Enabled = [bool]$script:undoLabel
}

# Right-click menu for the Time entries tab, built for whatever is selected
$script:entryMenu = New-Object System.Windows.Forms.ContextMenuStrip
function Show-EntryMenu($panel, $pt, $day) {
    $em = $script:entryMenu
    $em.Items.Clear()
    $sel = @($script:entrySelected)
    $n = $sel.Count
    $running = Test-HasRunning $sel
    if ($n -gt 0) {
        $one = $n -eq 1
        $mi = $em.Items.Add('Edit note...')
        $mi.Enabled = $one -and $sel[0].Kind -eq 'time' -and -not $sel[0].ExplicitGroup
        $mi.Add_Click({ try { Edit-EntryNote $script:entrySelected[0] } catch { Show-WeeklyError $_ } })
        $mi = $em.Items.Add('Edit time...')
        $mi.Enabled = $one -and -not $running -and -not $sel[0].ExplicitGroup
        $mi.Add_Click({ try { Edit-EntryTime $script:entrySelected[0] } catch { Show-WeeklyError $_ } })

        $move = New-Object System.Windows.Forms.ToolStripMenuItem 'Change account'
        $move.Enabled = -not $running
        foreach ($an in @($script:accounts | ForEach-Object { $_.Name })) {
            $ai = $move.DropDownItems.Add($an)
            $ai.Tag = $an
            $ai.Checked = (@($sel | Where-Object { $_.Name -ne $an }).Count -eq 0)
            $ai.Add_Click({ try { Set-EntryAccount $this.Tag } catch { Show-WeeklyError $_ } })
        }
        [void]$em.Items.Add($move)

        $sameGroup = (@($sel | Where-Object { $_.Name -ne $sel[0].Name -or $_.Day -ne $sel[0].Day }).Count -eq 0)
        $mi = $em.Items.Add($(if ($n -ge 2) { "Group $n blocks (keep recordings)" } else { 'Group (select two or more)' }))
        $mi.Enabled = ($n -ge 2) -and $sameGroup -and -not $running
        if ($n -ge 2 -and -not $sameGroup) { $mi.Text = 'Merge (same account and day only)' }
        $mi.Add_Click({ try { Merge-EntryBlocks } catch { Show-WeeklyError $_ } })
        $hasGroup = @($sel | Where-Object { $_.ExplicitGroup }).Count -gt 0
        if ($hasGroup) {
            $mi = $em.Items.Add('Ungroup selected (keep recordings)')
            $mi.Add_Click({ try { Ungroup-EntryBlocks } catch { Show-WeeklyError $_ } })
        }

        [void]$em.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
        $mi = $em.Items.Add($(if ($one) { 'Delete' } else { "Delete $n blocks" }))
        $mi.Enabled = -not $running
        $mi.Add_Click({ try { Remove-EntryBlocks } catch { Show-WeeklyError $_ } })
        [void]$em.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
    }
    $dayName = $script:weekStart.AddDays($day).ToString('dddd d', $GB)
    $mi = $em.Items.Add("Add entry on $dayName...")
    $mi.Tag = $day
    $mi.Add_Click({ try { Add-TimeEntry $this.Tag } catch { Show-WeeklyError $_ } })
    if ($script:undoLabel) {
        $mi = $em.Items.Add("Undo last change ($($script:undoLabel))")
        $mi.Add_Click({ try { Undo-LogChange } catch { Show-WeeklyError $_ } })
    }
    Set-MenuScale $em
    $em.Show($panel, $pt)
}

function Get-EntryBlockAt($pt) {
    foreach ($b in $script:entryBlocks) {
        if ($b.Rect.Contains([float]$pt.X, [float]$pt.Y)) { return $b }
    }
    return $null
}

function Get-EntryTipText($e) {
    if ($e.Kind -ne 'time' -and $e.Kind -ne 'running') {
        return '{0}  {1} {2}' -f $e.Name, (Get-EntryTypeLabel $e.Kind).ToLower(), (Format-HM $e.Seconds)
    }
    if ($e.Origin -eq 'merged') {
        return ('{0}  {1} credited' -f $e.Name, (Format-HM $e.Seconds)) +
               "`nOriginal session positions are not available. Edit this entry to give it a specific time."
    }
    if ($e.ExplicitGroup) {
        $t = '{0}  {1} credited, grouped without changing source sessions' -f $e.Name, (Format-HM $e.Seconds)
    } else {
        $t = '{0}  {1} to {2}  ({3})' -f $e.Name, $e.Start.ToString('HH:mm', $Inv), $e.End.ToString('HH:mm', $Inv), (Format-HM $e.Seconds)
    }
    if (@('added', 'edited', 'positioned') -contains $e.Origin) { $t += "`n" + (Get-EntryTypeLabel $e.Origin) }
    $parts = @($e.Parts)
    if ($parts.Count -gt 1) {
        $t += "`n"
        foreach ($p in $parts) {
            if ($p.Kind -eq 'time' -or $p.Kind -eq 'running') {
                $line = '{0} to {1}   {2}' -f $p.Start.ToString('HH:mm', $Inv), $p.End.ToString('HH:mm', $Inv), (Format-HM $p.Seconds)
                if ($p.Kind -eq 'running') { $line += '   still running' }
                elseif ($p.Note) { $line += '   ' + $p.Note }
            } else {
                $line = '{0}   {1}' -f (Get-EntryTypeLabel $p.Kind), (Format-HM $p.Seconds)
            }
            $t += "`n" + $line
        }
        if (-not $e.Note -and $e.Kind -eq 'time') { $t += "`n`nNo note yet. Right-click to add one." }
        return $t
    }
    if ($e.Kind -eq 'running') { $t += "`nStill running" }
    elseif ($e.Note) { $t += "`n" + $e.Note }
    else { $t += "`nNo note yet. Right-click to add one." }
    return $t
}

function Edit-EntryNote($e) {
    if ($e.ExplicitGroup) {
        Show-Message 'Ungroup these recordings to edit their individual notes without replacing the original notes.'
        return
    }
    if ($e.Origin -eq 'positioned') {
        $parts = @($e.Parts | Where-Object { $_.Origin -eq 'positioned' })
        if ($parts.Count -ne 1) { Show-Message 'Select one positioned entry to edit its note.'; return }
        $p = Find-CreditPosition $parts[0].PositionId
        if (-not $p) { return }
        $text = Show-TextPrompt "Note for $($p.Account)" 'What did you work on during this positioned time?' "$($p.Note)"
        if ($null -eq $text) { return }
        [void](Invoke-LogChange 'edit positioned note' {
            $pos = Find-CreditPosition $parts[0].PositionId
            if (-not $pos) { return $false }
            $pos.Note = [string]$text
            return $true
        })
        return
    }
    $times = @($e.Parts | Where-Object { $_.Kind -eq 'time' })
    if ($times.Count -eq 0) { return }
    $prompt = 'What did you work on? ({0} to {1})' -f $e.Start.ToString('HH:mm', $Inv), $e.End.ToString('HH:mm', $Inv)
    $text = Show-TextPrompt "Note for $($e.Name)" $prompt "$($e.Note)"
    if ($null -eq $text) { return }
    $first = $times[0]
    $ok = Save-Note $first.Start.ToString('yyyy-MM-dd', $Inv) $first.Name $first.Start.ToString('HH:mm:ss', $Inv) $text
    for ($i = 1; $i -lt $times.Count -and $ok; $i++) {
        $p = $times[$i]
        if ($p.Note) { $ok = Save-Note $p.Start.ToString('yyyy-MM-dd', $Inv) $p.Name $p.Start.ToString('HH:mm:ss', $Inv) '' }
    }
    if ($ok) { Fill-WeeklyView }
    else { Show-Message "Couldn't save the note. If notes.csv is open in Excel, close it and try again." }
}

# Text drawn the way Windows draws it (matching the tables and labels), aligned to whole pixels.
# Windows text can't be see-through, so faint text uses a colour blended with the background instead.
function Draw-WinText($g, $text, $font, $color, $x, $y, $w, $h, [string]$align = 'left') {
    $flags = switch ($align) {
        'right'  { 'Right, VerticalCenter, SingleLine, NoPrefix, NoPadding' }
        'centre' { 'HorizontalCenter, VerticalCenter, SingleLine, NoPrefix, NoPadding' }
        default  { 'Left, VerticalCenter, SingleLine, NoPrefix, NoPadding, EndEllipsis' }
    }
    $rect = New-Object System.Drawing.Rectangle([int][math]::Round($x), [int][math]::Round($y), [int][math]::Round($w), [int][math]::Round($h))
    [System.Windows.Forms.TextRenderer]::DrawText($g, "$text", $font, $rect, $color, [System.Windows.Forms.TextFormatFlags]$flags)
}

function Measure-WinText($text, $font) {
    $flags = [System.Windows.Forms.TextFormatFlags]'SingleLine, NoPrefix, NoPadding'
    return [System.Windows.Forms.TextRenderer]::MeasureText("$text", $font, (New-Object System.Drawing.Size(10000, 1000)), $flags).Width
}

# A tiny speech-bubble mark for entries that have a note
function Draw-NoteMark($g, $pen, [float]$x, [float]$y, $k) {
    $w = [float](10 * $k); $h = [float](7 * $k)
    $g.DrawRectangle($pen, $x, $y, $w, $h)
    $g.DrawLine($pen, [float]($x + 2 * $k), [float]($y + $h), [float]($x + 2 * $k), [float]($y + $h + 3 * $k))
}

function Draw-EntriesView($g, $panel) {
    $d = $script:weekData
    if (-not $d) { return }
    $k = $script:wk
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $theme = $script:wv.Theme
    $faintCol = Mix-Colour $theme.Panel $cWhite 0.67
    $fHead = New-PxFont 'Segoe UI Semibold' 13 $k
    $fName = New-PxFont 'Segoe UI Semibold' 12 $k
    $fNote = New-PxFont 'Segoe UI' 11.5 $k
    $sfL = New-Object System.Drawing.StringFormat
    $sfL.Alignment = 'Near'; $sfL.LineAlignment = 'Center'; $sfL.Trimming = 'EllipsisCharacter'; $sfL.FormatFlags = 'NoWrap'
    $sfR = New-Object System.Drawing.StringFormat
    $sfR.Alignment = 'Far'; $sfR.LineAlignment = 'Center'; $sfR.FormatFlags = 'NoWrap'
    $sfC = New-Object System.Drawing.StringFormat
    $sfC.Alignment = 'Center'; $sfC.LineAlignment = 'Center'
    $textBrush = New-Object System.Drawing.SolidBrush $cWhite
    $faintBrush = New-Object System.Drawing.SolidBrush ($C::FromArgb(170, $cWhite))
    $badgeBrush = New-Object System.Drawing.SolidBrush (Mix-Colour $theme.Panel $cWhite 0.14)
    $dark = [System.Drawing.ColorTranslator]::FromHtml('#1d1d1f')
    $ws = $script:weekStart
    $days = @($d.Days)
    $W = [double]$panel.ClientSize.Width
    $gap = 8 * $k
    $colW = ($W - $gap * ($days.Count - 1)) / [math]::Max(1, $days.Count)
    $headH = 30 * $k

    # Day headings with the day's total (time entries and adjustments, Lunch excluded)
    for ($j = 0; $j -lt $days.Count; $j++) {
        $x = [float]($j * ($colW + $gap))
        $total = 0.0
        foreach ($e in $script:weekBlocks) { if ($e.Day -eq $days[$j]) { $total += $e.Seconds } }
        Draw-WinText $g ($ws.AddDays($days[$j]).ToString('ddd d', $GB)) $fHead $cWhite $x 0 $colW $headH 'left'
        if ($total -gt 0) {
            $txt = Format-HM $total
            $tw = [float]((Measure-WinText $txt $fName) + 14 * $k)
            $bx = [float]($x + $colW - $tw)
            $path = New-RoundedPath $bx ([float](5 * $k)) $tw ([float]($headH - 10 * $k)) ([float](5 * $k))
            $g.FillPath($badgeBrush, $path)
            $path.Dispose()
            Draw-WinText $g $txt $fName $cWhite $bx 0 $tw $headH 'centre'
        }
    }

    foreach ($b in $script:entryBlocks) {
        $e = $b.Entry
        $r = $b.Rect
        $pad = [float](8 * $k)
        $isAdj = $e.Kind -ne 'time' -and $e.Kind -ne 'running'
        $path = New-RoundedPath $r.X $r.Y $r.Width $r.Height ([float](5 * $k))
        if ($isAdj) {
            # Rounding and manual changes: quiet outlined blocks
            $selected = $script:entrySelected -contains $e
            $pen = New-Object System.Drawing.Pen(($C::FromArgb($(if ($selected) { 255 } else { 110 }), $cWhite)), [float]($(if ($selected) { 2.5 } else { 1 }) * $k))
            if (-not $selected) { $pen.DashStyle = 'Dash' }
            $g.DrawPath($pen, $path)
            $pen.Dispose()
            $label = '{0}  {1}' -f (Get-EntryTypeLabel $e.Kind), $e.Name
            Draw-WinText $g $label $fNote $faintCol ($r.X + $pad) $r.Y ($r.Width - 2 * $pad - 44 * $k) $r.Height 'left'
            Draw-WinText $g (Format-HM $e.Seconds) $fNote $faintCol ($r.X + $pad) $r.Y ($r.Width - 2 * $pad) $r.Height 'right'
            $path.Dispose()
            continue
        }

        $col = Get-EntryColour $e.Name
        $ink = if ($col.GetBrightness() -gt 0.55) { $dark } else { [System.Drawing.Color]::White }
        $fillCol = if ($e.Kind -eq 'running') { Mix-Colour $theme.Panel $col 0.6 } else { $col }
        if ($script:entryHover -and $script:entryHover.Entry -eq $e) { $fillCol = Mix-Colour $col ([System.Drawing.Color]::White) 0.18 }
        $fb = New-Object System.Drawing.SolidBrush $fillCol
        $g.FillPath($fb, $path)
        $fb.Dispose()
        if ($script:entrySelected -contains $e) {
            $selPen = New-Object System.Drawing.Pen($cWhite, [float](2.5 * $k))
            $g.DrawPath($selPen, $path)
            $selPen.Dispose()
        }
        if ($e.Kind -eq 'running') {
            $pen = New-Object System.Drawing.Pen($col, [float](1.5 * $k))
            $g.DrawPath($pen, $path)
            $pen.Dispose()
        }
        $path.Dispose()

        $inkBrush = New-Object System.Drawing.SolidBrush $ink
        $inkPen = New-Object System.Drawing.Pen($ink, [float](1 * $k))
        $lineH = [float](30 * $k)
        $durText = if ($e.Kind -eq 'running') { (Format-HM $e.Seconds) + ' so far' } else { Format-HM $e.Seconds }
        $durW = [float](Measure-WinText $durText $fName)
        $markW = if ($e.Note) { [float](16 * $k) } else { [float]0 }
        Draw-WinText $g $durText $fName $ink ($r.X + $pad) $r.Y ($r.Width - 2 * $pad) $lineH 'right'
        if ($e.Note) { Draw-NoteMark $g $inkPen ([float]($r.Right - $pad - $durW - $markW + 2 * $k)) ([float]($r.Y + $lineH / 2 - 5 * $k)) $k }
        $nameW = [float]([math]::Max(10.0, $r.Width - 2 * $pad - $durW - $markW - 4 * $k))
        Draw-WinText $g $e.Name $fName $ink ($r.X + $pad) $r.Y $nameW $lineH 'left'
        $partCount = @($e.Parts).Count
        $partsW = [float]0
        if ($partCount -gt 1) {
            $partsText = '{0} parts' -f $partCount
            $partsW = [float]((Measure-WinText $partsText $fNote) + 8 * $k)
            Draw-WinText $g $partsText $fNote (Mix-Colour $fillCol $ink 0.75) ($r.X + $pad) ($r.Y + 22 * $k) ($r.Width - 2 * $pad) (20 * $k) 'right'
        }
        if ($e.Note) {
            Draw-WinText $g $e.Note $fNote $ink ($r.X + $pad) ($r.Y + 22 * $k) ($r.Width - 2 * $pad - $partsW) (20 * $k) 'left'
        }
        $inkBrush.Dispose()
        $inkPen.Dispose()
    }
    foreach ($o in @($fHead, $fName, $fNote, $sfL, $sfR, $sfC, $textBrush, $faintBrush, $badgeBrush)) { $o.Dispose() }
}

function Draw-WeekChart($g, $panel) {
    $d = $script:weekData
    if (-not $d) { return }
    $k = $script:wk
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $W = $panel.ClientSize.Width
    $H = $panel.ClientSize.Height
    $faintCol = Mix-Colour $script:wv.Theme.Panel $cWhite 0.67
    $font = New-PxFont 'Segoe UI' 11 $k
    $bold = New-PxFont 'Segoe UI Semibold' 11 $k
    $textBrush = New-Object System.Drawing.SolidBrush $cWhite
    $faintBrush = New-Object System.Drawing.SolidBrush ($C::FromArgb(170, $cWhite))
    $linePen = New-Object System.Drawing.Pen(($C::FromArgb(40, $cWhite)), 1)
    $sfC = New-Object System.Drawing.StringFormat
    $sfC.Alignment = 'Center'; $sfC.LineAlignment = 'Center'
    $sfR = New-Object System.Drawing.StringFormat
    $sfR.Alignment = 'Far'; $sfR.LineAlignment = 'Center'

    # Legend along the bottom
    $legendH = [int](22 * $k)
    $lx = [float](34 * $k)
    $ly = [float]($H - $legendH + 4 * $k)
    for ($i = 0; $i -lt $d.Names.Count; $i++) {
        $n = $d.Names[$i]
        $nw = Measure-WinText $n $font
        if (($lx + 18 * $k + $nw) -gt $W) { break }
        $cb = New-Object System.Drawing.SolidBrush $AccountColours[$i % $AccountColours.Count]
        $g.FillRectangle($cb, $lx, ($ly + 3 * $k), (10 * $k), (10 * $k))
        $cb.Dispose()
        Draw-WinText $g $n $font $faintCol ($lx + 14 * $k) ($ly - 2 * $k) ($nw + 4) (20 * $k) 'left'
        $lx += 14 * $k + $nw + 14 * $k
    }

    # Bars: one per day, stacked by account
    $top = [float](16 * $k)
    $bottom = [float]($H - $legendH - 24 * $k)
    $left = [float](34 * $k)
    $chartH = $bottom - $top
    $max = 8 * 3600.0
    foreach ($day in $d.Days) {
        $t = 0.0
        foreach ($n in $d.Names) { $t += $d.Accounts[$n][$day] }
        if ($t -gt $max) { $max = $t }
    }
    $max = [math]::Ceiling($max / 7200) * 7200
    for ($hr = 0; $hr -le $max; $hr += 7200) {
        $yy = [float]($bottom - $chartH * ($hr / $max))
        $g.DrawLine($linePen, $left, $yy, [float]$W, $yy)
        Draw-WinText $g ('{0}h' -f ($hr / 3600)) $font $faintCol 0 ($yy - 8 * $k) ($left - 6 * $k) (16 * $k) 'right'
    }
    $slot = ($W - $left) / [math]::Max(1, $d.Days.Count)
    $bw = [float]([math]::Min($slot * 0.5, 70 * $k))
    $ws = $script:weekStart
    for ($j = 0; $j -lt $d.Days.Count; $j++) {
        $day = $d.Days[$j]
        $cx = [float]($left + $slot * ($j + 0.5))
        $yy = $bottom
        $t = 0.0
        for ($i = 0; $i -lt $d.Names.Count; $i++) {
            $v = $d.Accounts[$d.Names[$i]][$day]
            if ($v -le 0) { continue }
            $hgt = [float]($chartH * ($v / $max))
            $cb = New-Object System.Drawing.SolidBrush $AccountColours[$i % $AccountColours.Count]
            $g.FillRectangle($cb, ($cx - $bw / 2), ($yy - $hgt), $bw, $hgt)
            $cb.Dispose()
            $yy -= $hgt
            $t += $v
        }
        if ($t -gt 0) {
            Draw-WinText $g ('{0:0.##}h' -f ($t / 3600)) $bold $cWhite ($cx - $slot / 2) ($yy - 18 * $k) $slot (16 * $k) 'centre'
        }
        Draw-WinText $g ($ws.AddDays($day).ToString('ddd dd/MM', $GB)) $font $faintCol ($cx - $slot / 2) ($bottom + 4 * $k) $slot (18 * $k) 'centre'
    }
    foreach ($o in @($font, $bold, $textBrush, $faintBrush, $linePen, $sfC, $sfR)) { $o.Dispose() }
}

# Fields shown depend on what's passed: Days/DayIndex, Accounts/Account, Start, Duration, Note.
# Returns the chosen values, or $null if cancelled.
function Show-EntryDialog($title, $opt) {
    $k = $Zoom / $Scale
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = $title
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.MinimizeBox = $false; $dlg.MaximizeBox = $false
    $dlg.ShowInTaskbar = $false
    $dlg.TopMost = $true
    if ($script:appIcon) { $dlg.Icon = $script:appIcon }
    $dlg.StartPosition = 'CenterScreen'
    $dlg.AutoScaleMode = 'None'
    $dlg.Font = New-PxFont 'Segoe UI' 12 $k
    $labelW = [int](80 * $k); $fieldX = [int](100 * $k); $fieldW = [int](220 * $k); $rowH = [int](34 * $k)
    $y = [int](14 * $k)
    $fields = @{}
    $addRow = {
        param($labelText, $ctl)
        $l = New-Object System.Windows.Forms.Label
        $l.Text = $labelText
        $l.AutoSize = $false
        $l.Size = New-Object System.Drawing.Size($labelW, [int](24 * $k))
        $l.TextAlign = 'MiddleLeft'
        $l.Location = New-Object System.Drawing.Point([int](14 * $k), $y)
        $ctl.Location = New-Object System.Drawing.Point($fieldX, $y)
        $ctl.Width = $fieldW
        $dlg.Controls.Add($l)
        $dlg.Controls.Add($ctl)
    }
    if ($opt.Days) {
        $picker = New-Object System.Windows.Forms.ComboBox
        $picker.DropDownStyle = 'DropDownList'
        foreach ($dd in $opt.Days) { [void]$picker.Items.Add($dd.ToString('dddd d MMMM', $GB)) }
        $picker.SelectedIndex = [math]::Min($opt.DayIndex, $opt.Days.Count - 1)
        . $addRow 'Day' $picker; $fields.Day = $picker; $y += $rowH
    }
    if ($opt.Accounts) {
        $picker = New-Object System.Windows.Forms.ComboBox
        $picker.DropDownStyle = 'DropDownList'
        foreach ($an in $opt.Accounts) { [void]$picker.Items.Add($an) }
        $picker.SelectedIndex = [math]::Max(0, [array]::IndexOf(@($opt.Accounts), $opt.Account))
        . $addRow 'Account' $picker; $fields.Account = $picker; $y += $rowH
    }
    $tbStart = New-Object System.Windows.Forms.TextBox
    $tbStart.Text = $opt.Start
    . $addRow 'Start' $tbStart; $y += $rowH
    $tbDur = New-Object System.Windows.Forms.TextBox
    $tbDur.Text = $opt.Duration
    . $addRow 'Duration' $tbDur; $y += $rowH
    if ($null -ne $opt.Note) {
        $tbNote = New-Object System.Windows.Forms.TextBox
        $tbNote.Text = $opt.Note
        . $addRow 'Note' $tbNote; $fields.Note = $tbNote; $y += $rowH
    }
    $hint = New-Object System.Windows.Forms.Label
    $hint.AutoSize = $true
    $hint.Text = 'Start as 09:30. Duration as 0:45, 45m or 1.25.'
    $hint.ForeColor = [System.Drawing.SystemColors]::GrayText
    $hint.Location = New-Object System.Drawing.Point($fieldX, $y)
    $dlg.Controls.Add($hint)
    $y += [int](30 * $k)

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = 'OK'; $ok.DialogResult = 'OK'
    $ok.Size = New-Object System.Drawing.Size([int](80 * $k), [int](28 * $k))
    $ok.Location = New-Object System.Drawing.Point(($fieldX + $fieldW - [int](166 * $k)), $y)
    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = 'Cancel'; $cancel.DialogResult = 'Cancel'
    $cancel.Size = New-Object System.Drawing.Size([int](80 * $k), [int](28 * $k))
    $cancel.Location = New-Object System.Drawing.Point(($fieldX + $fieldW - [int](80 * $k)), $y)
    $dlg.Controls.Add($ok); $dlg.Controls.Add($cancel)
    $dlg.AcceptButton = $ok; $dlg.CancelButton = $cancel
    $dlg.ClientSize = New-Object System.Drawing.Size(($fieldX + $fieldW + [int](14 * $k)), ($y + [int](42 * $k)))

    while ($true) {
        $script:dialogOpen = $true
        try { $result = $dlg.ShowDialog((Get-DialogOwner)) } finally { $script:dialogOpen = $false }
        if ($result -ne [System.Windows.Forms.DialogResult]::OK) { $dlg.Dispose(); return $null }
        # Check what was typed; if anything's wrong, say so and reopen the box with it intact
        $problem = $null
        $startText = $tbStart.Text.Trim()
        if ($startText -notmatch '^(\d{1,2})[:.](\d{2})$' -or [int]$Matches[1] -gt 23 -or [int]$Matches[2] -gt 59) {
            $problem = "Couldn't read '$startText' as a start time. Try something like 09:30."
        } else {
            $startTs = New-Object TimeSpan([int]$Matches[1], [int]$Matches[2], 0)
        }
        $secs = ConvertFrom-DurationText $tbDur.Text
        if (-not $problem -and (-not $secs -or $secs -lt 60)) { $problem = "Couldn't read '$($tbDur.Text.Trim())' as a duration. Try 0:45, 45m or 1.25." }
        if (-not $problem) { break }
        Show-Message $problem
    }
    $baseDate = if ($fields.Day) { $opt.Days[$fields.Day.SelectedIndex] } else { $null }
    $res = @{
        Start   = $(if ($baseDate) { $baseDate.Date.Add($startTs) } else { [datetime]::Today.Add($startTs) })
        Seconds = [double]$secs
        Account = $(if ($fields.Account) { [string]$fields.Account.SelectedItem } else { $null })
        Note    = $(if ($fields.Note) { $fields.Note.Text.Trim() } else { $null })
        Date    = $baseDate
    }
    $dlg.Dispose()
    return $res
}

# After hours, days or format change: recalculate and redraw everything
function Complete-SettingsChange {
    Refresh-Totals
    Write-WeekSummary
    Update-View
    if ($script:drawerOpen) { $script:drawerWeek = Get-WeekStartFor $script:drawerWeek; Update-DrawerContent }
    Request-Redraw
    if ($script:weekForm -and -not $script:weekForm.IsDisposed) {
        $script:weekStart = Get-WeekStartFor $script:weekStart
        Fill-WeeklyView
    }
}

function Switch-HoursFormat {
    $script:hoursFormat = if ($script:hoursFormat -eq 'decimal') { 'hm' } else { 'decimal' }
    Save-WorkSettings
    Complete-SettingsChange
}

function New-SettingsDialog($title, [int]$width, [int]$height) {
    $k = $Zoom / $Scale
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = $title
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.MinimizeBox = $false; $dlg.MaximizeBox = $false
    $dlg.ShowInTaskbar = $false
    $dlg.TopMost = $true
    $dlg.StartPosition = 'CenterScreen'
    $dlg.AutoScaleMode = 'None'
    $dlg.Font = New-PxFont 'Segoe UI' 12 $k
    if ($script:appIcon) { $dlg.Icon = $script:appIcon }
    $dlg.ClientSize = New-Object System.Drawing.Size([int]($width * $k), [int]($height * $k))
    return $dlg
}

function Add-DialogControl($dlg, $ctl, [double]$x, [double]$y, [double]$w, [double]$h) {
    $k = $Zoom / $Scale
    $ctl.Location = New-Object System.Drawing.Point([int]($x * $k), [int]($y * $k))
    if ($w -gt 0) { $ctl.Width = [int]($w * $k) }
    if ($h -gt 0) { $ctl.Height = [int]($h * $k) }
    $dlg.Controls.Add($ctl)
    return $ctl
}

function New-DialogLabel($text) {
    $l = New-Object System.Windows.Forms.Label
    $l.Text = $text
    $l.AutoSize = $true
    return $l
}

function Add-DialogButtons($dlg, [double]$y, [bool]$withReset, $resetText) {
    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = 'OK'; $ok.DialogResult = 'OK'
    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = 'Cancel'; $cancel.DialogResult = 'Cancel'
    $w = $dlg.ClientSize.Width / ($Zoom / $Scale)
    [void](Add-DialogControl $dlg $ok ($w - 186) $y 80 28)
    [void](Add-DialogControl $dlg $cancel ($w - 96) $y 80 28)
    if ($withReset) {
        $reset = New-Object System.Windows.Forms.Button
        $reset.Text = $resetText; $reset.DialogResult = 'Abort'
        [void](Add-DialogControl $dlg $reset 14 $y 150 28)
    }
    $dlg.AcceptButton = $ok
    $dlg.CancelButton = $cancel
}

# Working days with their start and finish times (so each day can have its own shift), week start and how hours are shown
function Show-WorkSettings {
    $dlg = New-SettingsDialog 'Working hours and days' 380 344
    $order = @(1, 2, 3, 4, 5, 6, 0)
    $rows = @()
    for ($i = 0; $i -lt 7; $i++) {
        $dow = $order[$i]
        $y = 14 + $i * 28
        $cb = New-Object System.Windows.Forms.CheckBox
        $cb.Text = $DayAbbr[$dow]
        $cb.Tag = $dow
        $cb.Checked = $script:workDays -contains $dow
        [void](Add-DialogControl $dlg $cb 14 $y 70 0)
        $usual = Get-UsualHours $dow
        $tbA = Add-DialogControl $dlg (New-Object System.Windows.Forms.TextBox) 90 ($y - 1) 62 0
        $tbA.Text = $usual[0]
        [void](Add-DialogControl $dlg (New-DialogLabel 'to') 160 ($y + 2) 0 0)
        $tbB = Add-DialogControl $dlg (New-Object System.Windows.Forms.TextBox) 182 ($y - 1) 62 0
        $tbB.Text = $usual[1]
        $tbA.Enabled = $cb.Checked; $tbB.Enabled = $cb.Checked
        # Times are only available for days that are ticked
        $cb.Tag = @{ Dow = $dow; Boxes = @($tbA, $tbB) }
        $cb.Add_CheckedChanged({ foreach ($bx in $this.Tag.Boxes) { $bx.Enabled = $this.Checked } })
        $rows += @{ Dow = $dow; Check = $cb; Start = $tbA; End = $tbB }
    }
    $same = New-Object System.Windows.Forms.Button
    $same.Text = 'Same every day'
    $same.Tag = $rows
    $same.Add_Click({
        # Copy the first ticked day's hours to every other day
        $first = $this.Tag | Where-Object { $_.Check.Checked } | Select-Object -First 1
        if (-not $first) { return }
        foreach ($r in $this.Tag) { $r.Start.Text = $first.Start.Text; $r.End.Text = $first.End.Text }
    })
    [void](Add-DialogControl $dlg $same 262 12 104 28)

    [void](Add-DialogControl $dlg (New-DialogLabel 'Week starts on') 14 226 0 0)
    $cbWeek = Add-DialogControl $dlg (New-Object System.Windows.Forms.ComboBox) 130 223 150 0
    $cbWeek.DropDownStyle = 'DropDownList'
    $fullNames = @('Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday')
    foreach ($fn in $fullNames) { [void]$cbWeek.Items.Add($fn) }
    $cbWeek.SelectedIndex = $script:weekStartDow

    [void](Add-DialogControl $dlg (New-DialogLabel 'Show hours as') 14 262 0 0)
    $cbFmt = Add-DialogControl $dlg (New-Object System.Windows.Forms.ComboBox) 130 259 236 0
    $cbFmt.DropDownStyle = 'DropDownList'
    [void]$cbFmt.Items.AddRange(@('Decimal (1.25)', 'Hours and minutes (1:15)'))
    $cbFmt.SelectedIndex = if ($script:hoursFormat -eq 'hm') { 1 } else { 0 }

    Add-DialogButtons $dlg 302 $false ''

    $fullDay = @{ 0 = 'Sunday'; 1 = 'Monday'; 2 = 'Tuesday'; 3 = 'Wednesday'; 4 = 'Thursday'; 5 = 'Friday'; 6 = 'Saturday' }
    while ($true) {
        $script:dialogOpen = $true
        try { $result = $dlg.ShowDialog((Get-DialogOwner)) } finally { $script:dialogOpen = $false }
        if ($result -ne [System.Windows.Forms.DialogResult]::OK) { $dlg.Dispose(); return }
        $problem = $null
        $chosen = @()
        foreach ($r in $rows) {
            if (-not $r.Check.Checked) { continue }
            $a = ConvertTo-ClockText $r.Start.Text
            $b = ConvertTo-ClockText $r.End.Text
            if (-not $a -or -not $b) { $problem = "Enter the hours for $($fullDay[$r.Dow]) as times, such as 08:30 and 17:30."; break }
            if ([timespan]$b -le [timespan]$a) { $problem = "The end time for $($fullDay[$r.Dow]) needs to be after the start time."; break }
            $chosen += @{ Dow = $r.Dow; Start = $a; End = $b }
        }
        if (-not $problem -and $chosen.Count -eq 0) { $problem = 'Choose at least one working day.' }
        if (-not $problem) { break }
        Show-Message $problem
    }
    # The first working day's hours become the usual ones, and any day that differs keeps its own
    $script:StartTime = $chosen[0].Start
    $script:EndTime = $chosen[0].End
    $script:dayHours = @{}
    foreach ($ch in $chosen) {
        if ($ch.Start -ne $script:StartTime -or $ch.End -ne $script:EndTime) { $script:dayHours[[int]$ch.Dow] = @{ Start = $ch.Start; End = $ch.End } }
    }
    $script:workDays = @($chosen | ForEach-Object { [int]$_.Dow } | Sort-Object)
    $script:weekStartDow = $cbWeek.SelectedIndex
    $script:hoursFormat = if ($cbFmt.SelectedIndex -eq 1) { 'hm' } else { 'decimal' }
    $dlg.Dispose()
    Save-WorkSettings
    Complete-SettingsChange
}

# Different hours today, or today off
function Show-TodaySettings {
    $todayKey = [datetime]::Today.ToString('yyyy-MM-dd', $Inv)
    $plan = Get-DayPlan ([datetime]::Today)
    $dlg = New-SettingsDialog ('Today, ' + [datetime]::Today.ToString('dddd d MMMM', $GB)) 380 150
    $cbOff = Add-DialogControl $dlg (New-Object System.Windows.Forms.CheckBox) 14 14 300 0
    $cbOff.Text = 'Not working today (day off)'
    $cbOff.Checked = -not $plan.Working
    [void](Add-DialogControl $dlg (New-DialogLabel 'Hours today') 14 54 0 0)
    $tbStart = Add-DialogControl $dlg (New-Object System.Windows.Forms.TextBox) 130 51 70 0
    $tbEnd = Add-DialogControl $dlg (New-Object System.Windows.Forms.TextBox) 232 51 70 0
    [void](Add-DialogControl $dlg (New-DialogLabel 'to') 208 54 0 0)
    $usualToday = Get-UsualHours ([int][datetime]::Today.DayOfWeek)
    $tbStart.Text = if ($plan.Working) { $plan.Start.ToString('HH:mm', $Inv) } else { $usualToday[0] }
    $tbEnd.Text = if ($plan.Working) { $plan.End.ToString('HH:mm', $Inv) } else { $usualToday[1] }
    $cbOff.Add_CheckedChanged({ $tbStart.Enabled = -not $cbOff.Checked; $tbEnd.Enabled = -not $cbOff.Checked }.GetNewClosure())
    $tbStart.Enabled = -not $cbOff.Checked; $tbEnd.Enabled = -not $cbOff.Checked
    Add-DialogButtons $dlg 108 ([bool]$script:dayOverrides[$todayKey]) 'Back to usual hours'

    while ($true) {
        $script:dialogOpen = $true
        try { $result = $dlg.ShowDialog((Get-DialogOwner)) } finally { $script:dialogOpen = $false }
        if ($result -eq [System.Windows.Forms.DialogResult]::Abort) {
            [void]$script:dayOverrides.Remove($todayKey)
            break
        }
        if ($result -ne [System.Windows.Forms.DialogResult]::OK) { $dlg.Dispose(); return }
        if ($cbOff.Checked) { $script:dayOverrides[$todayKey] = @{ Off = $true }; break }
        $a = ConvertTo-ClockText $tbStart.Text
        $b = ConvertTo-ClockText $tbEnd.Text
        $problem = if (-not $a -or -not $b) { 'Enter the hours as times, such as 08:00 and 16:00.' }
                   elseif ([timespan]$b -le [timespan]$a) { 'The end time needs to be after the start time.' }
                   else { $null }
        if ($problem) { Show-Message $problem; continue }
        # Same as usual on a usual working day: no special arrangement needed
        if ($a -eq $usualToday[0] -and $b -eq $usualToday[1] -and $script:workDays -contains [int][datetime]::Today.DayOfWeek) {
            [void]$script:dayOverrides.Remove($todayKey)
        } else {
            $script:dayOverrides[$todayKey] = @{ Off = $false; Start = $a; End = $b }
        }
        break
    }
    $dlg.Dispose()
    Save-WorkSettings
    Complete-SettingsChange
}

function Show-TextPrompt($title, $prompt, $default) {
    $k = $Zoom / $Scale
    $dlg = New-Object System.Windows.Forms.Form
    $dlg.Text = $title
    $dlg.FormBorderStyle = 'FixedDialog'
    $dlg.MinimizeBox = $false
    $dlg.MaximizeBox = $false
    $dlg.ShowInTaskbar = $false
    $dlg.TopMost = $true
    if ($script:appIcon) { $dlg.Icon = $script:appIcon }
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
    try { $result = $dlg.ShowDialog((Get-DialogOwner)) } finally { $script:dialogOpen = $false }
    $text = $box.Text.Trim()
    $dlg.Dispose()
    if ($result -eq [System.Windows.Forms.DialogResult]::OK -and $text) { return $text }
    return $null
}

function Get-DialogOwner {
    if ($script:weekForm -and -not $script:weekForm.IsDisposed -and [System.Windows.Forms.Form]::ActiveForm -eq $script:weekForm) { return $script:weekForm }
    return $form
}

function Ask-YesNo($question) {
    $script:dialogOpen = $true
    try {
        $answer = [System.Windows.Forms.MessageBox]::Show((Get-DialogOwner), $question, 'Account timer', 'YesNo', 'Question')
    } finally { $script:dialogOpen = $false }
    return ($answer -eq 'Yes')
}

function Show-Message($text) {
    $script:dialogOpen = $true
    try { [void][System.Windows.Forms.MessageBox]::Show((Get-DialogOwner), $text, 'Account timer') }
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

# Tooltips drawn in the theme's colours (panel background, theme text, a fine border)
function Set-ThemedTooltip($tt) {
    $tt.OwnerDraw = $true
    $tt.Add_Popup({
        $script:tipShownAt = Get-Date
        try {
            $theme = if ($script:view) { Get-StateTheme $script:view } else { $Themes.running }
            $this.BackColor = $theme.Panel
            $this.ForeColor = $cWhite
        } catch {}
    })
    $tt.Add_Draw({
        try {
            $e = $_
            $bg = New-Object System.Drawing.SolidBrush $this.BackColor
            $e.Graphics.FillRectangle($bg, $e.Bounds)
            $bg.Dispose()
            $edge = New-Object System.Drawing.Pen((Mix-Colour $this.BackColor $this.ForeColor 0.3), 1)
            $e.Graphics.DrawRectangle($edge, 0, 0, ($e.Bounds.Width - 1), ($e.Bounds.Height - 1))
            $edge.Dispose()
            $e.DrawText([System.Windows.Forms.TextFormatFlags]'HorizontalCenter, VerticalCenter, NoPrefix')
        } catch { $_.DrawBackground(); $_.DrawBorder(); $_.DrawText() }
    })
}

$tip = New-Object System.Windows.Forms.ToolTip
Set-ThemedTooltip $tip

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

# ---- Views ----
function Load-Positions {
    if (-not (Test-Path $PosFile)) { return }
    foreach ($line in Get-Content $PosFile) {
        $p = $line -split ','
        if ($p.Count -eq 3) { $script:positions[$p[0]] = New-Object System.Drawing.Point([int]$p[1], [int]$p[2]) }
    }
}

function Save-Position {
    $script:positions[$script:mode] = if ($script:mode -eq 'full') { Get-TimerScreenPos } else { $form.Location }
    $lines = @($script:positions.Keys | ForEach-Object { '{0},{1},{2}' -f $_, $script:positions[$_].X, $script:positions[$_].Y })
    try { Set-Content -Path $PosFile -Value $lines } catch {}
}

function Set-View($mode, [switch]$Starting) {
    if ($mode -ne 'pill') { $mode = 'full' }
    if ($mode -eq $script:mode) { return }
    if ($script:pillOpen) { Set-PillOpen $false }
    if ($script:mode -eq 'full' -and $script:drawerOpen) { $script:drawerReopen = $true; Set-DrawerOpen $false -Starting }
    if (-not $Starting) { Save-Position }
    $right = $form.Right
    $bottom = $form.Bottom
    $script:mode = $mode
    $bw, $bh = Get-BaseSize
    $form.ClientSize = New-Object System.Drawing.Size([int]($bw * $Zoom), [int]($bh * $Zoom))
    # Each view remembers where it was; the first time, it lines up with the other's bottom-right corner
    if ($script:positions.ContainsKey($mode)) {
        if ($mode -eq 'full') { Set-TimerScreenPos $script:positions[$mode] } else { $form.Location = $script:positions[$mode] }
    }
    else { $form.Location = New-Object System.Drawing.Point(($right - $form.Width), ($bottom - $form.Height)) }
    $script:hover = $null; $script:gripHover = $null; $script:roundHover = $null
    $script:pillHover = $null; $script:collapseHover = $false
    $tip.Hide($form)
    try { Set-Content -Path $ViewFile -Value $mode } catch {}
    Apply-CornerStyle
    $script:regionKey = ''
    Update-Region
    Apply-BlurState
    Keep-Visible
    Update-View
    Request-Redraw
    if ($mode -eq 'full' -and $script:drawerReopen) { $script:drawerReopen = $false; Set-DrawerOpen $true -Starting }
}

function Test-HitCollapse($pt) {
    $x = $pt.X / $Zoom; $y = $pt.Y / $Zoom
    $dx = $x - $CollapseX; $dy = $y - $CollapseY
    $r = $CollapseR + 3
    return (($dx * $dx + $dy * $dy) -le ($r * $r))
}

# Open or close the pill's account list. It opens downwards, or upwards if there's no room below.
function Set-PillOpen([bool]$open) {
    if ($script:mode -ne 'pill' -or $open -eq $script:pillOpen) { return }
    $listPx = [int]((Get-PillListH) * $Zoom)
    if ($open) {
        $area = [System.Windows.Forms.Screen]::FromControl($form).WorkingArea
        $needed = [int]($PillH * $Zoom) + $listPx
        $script:pillUp = (($form.Top + $needed) -gt $area.Bottom) -and (($form.Bottom - $needed) -ge $area.Top)
        $top = $form.Top
        $script:pillOpen = $true
        $bw, $bh = Get-BaseSize
        $form.ClientSize = New-Object System.Drawing.Size([int]($bw * $Zoom), [int]($bh * $Zoom))
        if ($script:pillUp) { $form.Top = $top - $listPx } else { $form.Top = $top }
        $form.Activate()
    } else {
        $headerTop = if ($script:pillUp) { $form.Top + $listPx } else { $form.Top }
        $script:pillOpen = $false
        $script:pillUp = $false
        $bw, $bh = Get-BaseSize
        $form.ClientSize = New-Object System.Drawing.Size([int]($bw * $Zoom), [int]($bh * $Zoom))
        $form.Top = $headerTop
    }
    $script:pillHover = $null
    $tip.Hide($form)
    Request-Redraw
}

function Get-PillZone($pt) {
    $x = $pt.X / $Zoom - $script:pillMargin; $y = $pt.Y / $Zoom - $script:pillMargin
    $L = Get-PillLayout
    if ($y -ge $L.HeaderY -and $y -lt ($L.HeaderY + $PillH)) {
        $dx = $x - $PillBtnX; $dy = $y - ($L.HeaderY + $PillH / 2)
        $r = $PillBtnR + 2
        if (($dx * $dx + $dy * $dy) -le ($r * $r)) { return 'button' }
        if ($x -lt $PillTimeW) { return 'time' }
        return 'list'
    }
    if ($script:pillOpen) {
        for ($i = 0; $i -lt $script:slots.Count; $i++) {
            $top = Get-PillRowTop $i
            if ($y -ge $top -and $y -lt ($top + $PillRowH)) { return ('row:' + $i) }
        }
    }
    return $null
}

function Pill-MouseDown($e) {
    if ($e.Button -ne 'Left') { return }
    $script:pillDown = @{
        Cursor = [System.Windows.Forms.Cursor]::Position
        Form = $form.Location
        Zone = (Get-PillZone $e.Location)
        Dragging = $false
    }
}

function Pill-MouseMove($e) {
    if ($script:pillDown) {
        # A small movement is still a click; beyond that it's a drag
        $p = [System.Windows.Forms.Cursor]::Position
        $dx = $p.X - $script:pillDown.Cursor.X
        $dy = $p.Y - $script:pillDown.Cursor.Y
        if (-not $script:pillOpen -and ($script:pillDown.Dragging -or ([math]::Abs($dx) + [math]::Abs($dy)) -gt 4)) {
            $script:pillDown.Dragging = $true
            $tip.Hide($form)
            $form.Location = New-Object System.Drawing.Point(($script:pillDown.Form.X + $dx), ($script:pillDown.Form.Y + $dy))
        }
        return
    }
    $form.Cursor = [System.Windows.Forms.Cursors]::Hand
    $zone = Get-PillZone $e.Location
    if ($zone -ne $script:pillHover) {
        $script:pillHover = $zone
        if ($script:pillOpen -or -not $zone) {
            $tip.Hide($form)
        } else {
            $text = if ($zone -eq 'time') { 'Open full view' }
                    elseif ($zone -eq 'list') { 'Switch account' }
                    elseif ($script:active) { 'Pause' }
                    else { 'Start an account' }
            $tip.Show($text, $form, [int]$e.Location.X, [int]($form.ClientSize.Height + 4), 2000)
        }
        Request-Redraw
    }
}

function Pill-MouseUp($e) {
    $d = $script:pillDown
    $script:pillDown = $null
    if (-not $d) { return }
    if ($d.Dragging) { Save-Position; return }
    $tip.Hide($form)
    $zone = $d.Zone
    if (-not $zone -or $zone -ne (Get-PillZone $e.Location)) { return }
    if ($zone -eq 'time') {
        Set-View 'full'
    } elseif ($zone -eq 'list') {
        Set-PillOpen (-not $script:pillOpen)
    } elseif ($zone -eq 'button') {
        if ($script:active) { Stop-ByUser; Update-View; Set-PillOpen $false; Request-Redraw }
        else { Set-PillOpen (-not $script:pillOpen) }
    } elseif ($zone -like 'row:*') {
        # Switch to that account (or stop it, if it's the one running) and fold the list away
        Switch-Slot ([int]$zone.Substring(4))
        Update-View
        Set-PillOpen $false
        Request-Redraw
    }
}

$form.KeyPreview = $true
$form.Add_KeyDown({
    if ($_.KeyCode -ne 'Escape') { return }
    if ($script:tlDrag) { Stop-TimelineDrag }      # puts the strip back as it was
    elseif ($script:pillOpen) { Set-PillOpen $false }
})
$form.Add_Deactivate({ if ($script:pillOpen -and -not $script:dialogOpen -and -not $menu.Visible) { Set-PillOpen $false } })

$form.Add_MouseDown({
    if ($script:mode -eq 'pill') { Pill-MouseDown $_; return }
    if ($_.Button -ne 'Left') { return }
    if (Test-InDrawer $_.Location) {
        $btn = Get-DrawerButtonAt $_.Location
        if ($btn) { $script:drawerPressed = $btn }
        elseif ((Get-DrawerMode) -eq 'timeline' -and (Start-TimelineDrag $_.Location)) { }
        else { Start-WindowMove }
        return
    }
    $tp = Get-TimerPoint $_.Location
    if (Test-HitHandle $tp) { $script:pressedHandle = $true; return }
    if (Test-HitCollapse $tp) { $script:pressedCollapse = $true; return }
    if (Test-HitDayBtn $tp) { $script:pressedDayBtn = $true; return }
    $grip = Get-HitGrip $tp
    if ($null -ne $grip) {
        $script:reorder = @{ Name = $script:slots[$grip]; Moved = $false }
        Request-Redraw
        return
    }
    $round = Get-HitRound $tp
    if ($null -ne $round) {
        $script:pressedRound = $round
        return
    }
    $hit = Get-HitRow $tp
    if ($null -ne $hit) {
        $script:pressed = $hit
    } else {
        Start-WindowMove
    }
})
$form.Add_MouseMove({
    if ($script:mode -eq 'pill') { Pill-MouseMove $_; return }
    if ($script:tlDrag) { Update-TimelineDrag $_.Location; return }
    if ($script:dragStart) {
        $p = [System.Windows.Forms.Cursor]::Position
        $form.Location = New-Object System.Drawing.Point(
            ($script:formStart.X + $p.X - $script:dragStart.X),
            ($script:formStart.Y + $p.Y - $script:dragStart.Y))
        return
    }
    $tp = Get-TimerPoint $_.Location
    if ($script:reorder) {
        Move-DraggedRow $tp.Y
        return
    }

    # Over the drawer: just its buttons
    if (Test-InDrawer $_.Location) {
        $btn = Get-DrawerButtonAt $_.Location
        $form.Cursor = if ($btn) { [System.Windows.Forms.Cursors]::Hand } else { [System.Windows.Forms.Cursors]::Default }
        $script:tlLastPt = $_.Location
        # On the timeline, edges, cards and gaps each have their own hover and cursor
        $hit = if ($btn) { New-TimelineHover } else { Get-TimelineHit $_.Location }
        if (-not $btn) {
            if ($hit.EdgeBlock -ge 0) { $form.Cursor = [System.Windows.Forms.Cursors]::SizeNS }
            elseif ($hit.Gap -ge 0) { $form.Cursor = [System.Windows.Forms.Cursors]::Hand }
        }
        if ($btn -ne $script:drawerHover -or -not (Test-SameTimelineHit $hit $script:tlHover) -or $null -ne $script:hover -or $script:handleHover) {
            $script:drawerHover = $btn
            $script:tlHover = $hit
            $script:hover = $null; $script:gripHover = $null; $script:roundHover = $null; $script:handleHover = $false
            $tip.Hide($form)
            if (-not $btn) { Show-TimelineTip $hit $_.Location }
            Request-Redraw
        }
        return
    }
    if ($script:drawerHover -or -not (Test-SameTimelineHit $script:tlHover (New-TimelineHover))) {
        $script:drawerHover = $null; $script:tlHover = New-TimelineHover; Request-Redraw
    }

    # Tooltips are placed in window coordinates, so add the timer's offset
    $ox = $script:timerOffX + $script:fullMargin; $oy = $script:timerOffY + $script:fullMargin
    $handle = Test-HitHandle $tp
    if ($handle -ne $script:handleHover) {
        $script:handleHover = $handle
        if ($handle) {
            $label = if ($script:drawerOpen) { 'Hide drawer' } else { 'Show drawer' }
            $tip.Show($label, $form, [int](($DrawerBtnX - 4 + $ox) * $Zoom), [int](($DrawerBtnY + 12 + $oy) * $Zoom), 2000)
        } else { $tip.Hide($form) }
        Request-Redraw
    }
    $hit   = if ($handle) { $null } else { Get-HitRow $tp }
    $grip  = Get-HitGrip $tp
    $round = Get-HitRound $tp
    $coll  = Test-HitCollapse $tp
    if ($coll -ne $script:collapseHover) {
        $script:collapseHover = $coll
        if ($coll) { $tip.Show('Compact view', $form, [int](($CollapseX - 60 + $ox) * $Zoom), [int](($CollapseY + 12 + $oy) * $Zoom), 2000) }
        else { $tip.Hide($form) }
        Request-Redraw
    }
    $dayHit = Test-HitDayBtn $tp
    if ($dayHit -ne $script:dayBtnHover) {
        $script:dayBtnHover = $dayHit
        if ($dayHit) {
            $dayTip = if ($script:view.DayDone) { 'Carry on working today' } else { "Finish for the day`nUnallocated time stops counting" }
            $dbRect = Get-DayBtnRect
            $tip.Show($dayTip, $form, [int](($dbRect.X + $ox) * $Zoom), [int](($dbRect.Y - 44 + $oy) * $Zoom), 3000)
        } else { $tip.Hide($form) }
        Request-Redraw
    }
    $ua = Test-HitUnalloc $tp
    if ($ua -ne $script:unallocHover) {
        $script:unallocHover = $ua
        if ($ua -and $script:view -and $script:view.UnallocTip) {
            $tip.Show($script:view.UnallocTip, $form, [int]((160 + $ox) * $Zoom), [int]((68 + $oy) * $Zoom), 5000)
        } elseif (-not $coll -and -not $handle) { $tip.Hide($form) }
    }
    $form.Cursor = if ($null -ne $grip) { [System.Windows.Forms.Cursors]::SizeNS }
                   elseif ($null -ne $hit -or $coll -or $handle -or $dayHit) { [System.Windows.Forms.Cursors]::Hand }
                   else { [System.Windows.Forms.Cursors]::Default }
    if ($round -ne $script:roundHover) {
        if ($null -ne $round) {
            $rp = $round -split ':'
            $tipText = if ($rp[1] -eq 'up') { 'Round today up to the next 15 minutes' } else { 'Round today down to the previous 15 minutes' }
            $tip.Show($tipText, $form,
                [int](($RoundXs[$rp[1]] - 70 + $ox) * $Zoom), [int](((Get-RowTop ([int]$rp[0])) + $RowH + $oy) * $Zoom), 2500)
        } else {
            $tip.Hide($form)
        }
    }
    if ($hit -ne $script:hover -or $grip -ne $script:gripHover -or $round -ne $script:roundHover) {
        if ($hit -ne $script:hover -and $null -eq $round) {
            $budgetTip = if ($null -ne $hit -and $script:view -and $hit -lt $script:view.Rows.Count) { $script:view.Rows[$hit].BudgetText } else { $null }
            if ($budgetTip) { $tip.Show($budgetTip, $form, [int]((46 + $ox) * $Zoom), [int](((Get-RowTop $hit) + $RowH + $oy) * $Zoom), 2500) }
            elseif (-not $coll -and -not $handle) { $tip.Hide($form) }
        }
        $script:hover = $hit
        $script:gripHover = $grip
        $script:roundHover = $round
        Request-Redraw
    }
})
# Mouse wheel over the timeline scrolls it, and with Ctrl held it zooms around the pointer
$form.Add_MouseWheel({
    try {
        if ($script:mode -ne 'full' -or -not $script:drawerOpen -or (Get-DrawerMode) -ne 'timeline') { return }
        if (-not (Test-InDrawer $_.Location)) { return }
        if ([System.Windows.Forms.Control]::ModifierKeys -band [System.Windows.Forms.Keys]::Control) {
            Set-TimelineZoom $(if ($_.Delta -gt 0) { 1.25 } else { 0.8 }) (Get-TimelineTimeAt $_.Location)
        } else {
            Set-TimelineScroll ($script:tlScrollH - ($_.Delta / 120.0) * 48.0 / $script:tlPx)
        }
        $tip.Hide($form)
        if ($script:tlDrag) { Update-TimelineDrag $_.Location }
        else {
            $script:tlHover = if (Test-InStrip $_.Location) { Get-TimelineHit $_.Location } else { New-TimelineHover }
            Request-Redraw
        }
    } catch { Write-Log "Timeline scroll error: $($_.Exception.Message)" }
})
$form.Add_MouseUp({
    try {
        if ($script:mode -eq 'pill') { Pill-MouseUp $_; return }
        if ($script:tlDrag) { Complete-TimelineDrag $_.Location; return }
        $tp = Get-TimerPoint $_.Location
        if ($script:drawerPressed) {
            $btn = $script:drawerPressed
            $script:drawerPressed = $null
            if ((Get-DrawerButtonAt $_.Location) -eq $btn) { Invoke-DrawerButton $btn }
            return
        }
        if ($script:pressedHandle) {
            $script:pressedHandle = $false
            if (Test-HitHandle $tp) { $tip.Hide($form); Set-DrawerOpen (-not $script:drawerOpen) }
            return
        }
        if ($script:pressedCollapse) {
            $script:pressedCollapse = $false
            if (Test-HitCollapse $tp) { Set-View 'pill' }
            return
        }
        if ($script:pressedDayBtn) {
            $script:pressedDayBtn = $false
            if (Test-HitDayBtn $tp) { $tip.Hide($form); Switch-DayDone }
            return
        }
        if ($script:dragStart) { $script:dragStart = $null; Save-Position }
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
            if ((Get-HitRound $tp) -eq $pr) {
                $tip.Hide($form)
                Invoke-Round $pr
            }
            return
        }
        if ($null -ne $script:pressed -and (Get-HitRow $tp) -eq $script:pressed) {
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
    if ($script:mode -eq 'pill') {
        if ($script:pillHover) { $script:pillHover = $null; $tip.Hide($form); Request-Redraw }
        return
    }
    if ($script:reorder -or $script:tlDrag) { return }
    if ($script:collapseHover -or $script:handleHover -or $script:dayBtnHover -or $script:drawerHover -or -not (Test-SameTimelineHit $script:tlHover (New-TimelineHover))) {
        $script:collapseHover = $false; $script:handleHover = $false; $script:dayBtnHover = $false; $script:drawerHover = $null
        $script:tlHover = New-TimelineHover
        Request-Redraw
    }
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
# Hand the move to Windows (smooth, like any other window), pausing the timer's redraws meanwhile.
# Falls back to following the mouse if that isn't available.
$script:moving = $false
function Start-WindowMove {
    if ($script:layered) {
        try {
            $script:moving = $true
            [Win.Layered]::StartMove($form.Handle)
        } catch {
            $script:dragStart = [System.Windows.Forms.Cursor]::Position
            $script:formStart = $form.Location
            return
        } finally { $script:moving = $false }
        Save-Position
        Request-Redraw
        return
    }
    $script:dragStart = [System.Windows.Forms.Cursor]::Position
    $script:formStart = $form.Location
}

function Keep-Visible {
    Update-Dpi
    if ($script:dialogOpen -or $menu.Visible -or $trayMenu.Visible) { return }
    $activeForm = [System.Windows.Forms.Form]::ActiveForm
    if ($activeForm -and ($activeForm -eq $script:weekForm -or $activeForm -eq $script:noteForm)) { return }
    $rect = New-Object System.Drawing.Rectangle($form.Location, $form.Size)
    $onScreen = $false
    foreach ($scr in [System.Windows.Forms.Screen]::AllScreens) {
        if ($scr.WorkingArea.IntersectsWith($rect)) { $onScreen = $true }
    }
    if (-not $onScreen) { Move-ToDefaultPosition }
    if (-not $form.Visible) { $form.Show() }
    # Don't jump in front of a tooltip that's showing, or while the mouse is over the timer (when tooltips
    # appear, and when the timer is clearly already in view)
    if ($form.Bounds.Contains([System.Windows.Forms.Control]::MousePosition)) { return }
    if ($script:weekForm -and -not $script:weekForm.IsDisposed -and $script:weekForm.Bounds.Contains([System.Windows.Forms.Control]::MousePosition)) { return }
    if ($script:tipShownAt -and ((Get-Date) - $script:tipShownAt).TotalSeconds -lt 9) { return }
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
    if ($script:mode -eq 'full' -and $script:drawerOpen) {
        # With the drawer open, keep the timer where it is and rebuild around it
        $tl = Get-TimerScreenPos
        $script:BaseH = $RowTop + $script:slots.Count * $RowH + $LunchGap + 6 + $script:finishH
        $script:drawerH = Get-DrawerHeight
        Update-DrawerGeometry $tl
        $script:hover = $null
        Request-Redraw
        return
    }
    # Grow or shrink to fit the rows, keeping the bottom edge where it was
    $script:BaseH = $RowTop + $script:slots.Count * $RowH + $LunchGap + 6 + $script:finishH
    $bottom = $form.Bottom
    $bw, $bh = Get-BaseSize
    $form.ClientSize = New-Object System.Drawing.Size([int]($bw * $Zoom), [int]($bh * $Zoom))
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
    if ($script:budgets.ContainsKey($old)) {
        $script:budgets[$new] = $script:budgets[$old]
        [void]$script:budgets.Remove($old)
        Save-Budgets
    }
    Complete-AccountChange
    if ($wasActive) {
        Start-Slot ([array]::IndexOf($script:slots, $new))
        Update-View
    }
}

# ---- Right-click menu ----
$menu = New-Object System.Windows.Forms.ContextMenuStrip

$miView = $menu.Items.Add('Compact view')
$miView.Add_Click({ if ($script:mode -eq 'pill') { Set-View 'full' } else { Set-View 'pill' } })
$miDrawer = $menu.Items.Add('Show drawer')
$miDrawer.Add_Click({ Set-DrawerOpen (-not $script:drawerOpen) })
$miDone = $menu.Items.Add('Finish for the day')
$miDone.Add_Click({ Switch-DayDone })
$miWork = New-Object System.Windows.Forms.ToolStripMenuItem 'Working hours'
[void]$miWork.DropDownItems.Add('Hours and days...', $null, { try { Show-WorkSettings } catch { Write-Log "Settings error: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)" } })
[void]$miWork.DropDownItems.Add('Today only...', $null, { try { Show-TodaySettings } catch { Write-Log "Settings error: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)" } })
[void]$miWork.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator))
$miFormat = $miWork.DropDownItems.Add('Show hours as h:mm')
$miFormat.Add_Click({ Switch-HoursFormat })
[void]$menu.Items.Add($miWork)
[void]$menu.Items.Add('Weekly view...', $null, {
    try { Show-WeeklyView } catch { Show-WeeklyError $_ }
})
[void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))

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

$miBudget = $menu.Items.Add('Set budget...')
$miBudget.Add_Click({
    try {
        if ($null -ne $script:menuRow) { Set-AccountBudget $script:slots[$script:menuRow] }
    } catch { Write-Log "Budget error: $($_.Exception.Message)" }
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
            if (-not (Clear-TodayFor $n)) { return }
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

# ---- Appearance: theme, transparency and the pill's effects ----
$miAppearance = New-Object System.Windows.Forms.ToolStripMenuItem 'Appearance'
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
[void]$miAppearance.DropDownItems.Add($miTheme)

# ---- Size: the whole timer and drawer, kept for next time ----
$SizeLevels = [ordered]@{ 'Smaller (85%)' = 0.85; 'Normal (100%)' = 1.0; 'Larger (125%)' = 1.25; 'Large (150%)' = 1.5; 'Largest (175%)' = 1.75 }

function Get-WantedZoom {
    $dpi = $script:SystemDpi
    if ($script:perMonitor) { try { $d = [Win.Dpi]::GetDpiForWindow($form.Handle); if ($d -gt 0) { $dpi = $d } } catch {} }
    return $script:Scale * $dpi / 96
}

# Nudge the window back into view if making it bigger pushed it over an edge of the screen
function Keep-OnScreen {
    $area = [System.Windows.Forms.Screen]::FromControl($form).WorkingArea
    $x = $form.Left; $y = $form.Top
    if ($form.Right -gt $area.Right) { $x = $area.Right - $form.Width }
    if ($form.Bottom -gt $area.Bottom) { $y = $area.Bottom - $form.Height }
    if ($x -lt $area.Left) { $x = $area.Left }
    if ($y -lt $area.Top) { $y = $area.Top }
    if ($x -ne $form.Left -or $y -ne $form.Top) { $form.Location = New-Object System.Drawing.Point($x, $y) }
}

function Set-UiScale([double]$newSize) {
    # (Not called $scale, which would hide the script's $Scale from everything this calls)
    $script:Scale = $newSize
    try { Set-Content -Path $SizeFile -Value $newSize.ToString($Inv) } catch {}
    # Keep the timer where it is on screen while the window grows or shrinks around it
    $tl = if ($script:mode -eq 'full') { Get-TimerScreenPos } else { $null }
    $script:Zoom = Get-WantedZoom
    if ($tl) { Update-DrawerGeometry $tl }
    else {
        $bw, $bh = Get-BaseSize
        $form.ClientSize = New-Object System.Drawing.Size([int]($bw * $Zoom), [int]($bh * $Zoom))
    }
    $script:regionKey = ''
    Keep-OnScreen
    $tip.Hide($form)
    Request-Redraw
}

$miSize = New-Object System.Windows.Forms.ToolStripMenuItem 'Size'
foreach ($sl in $SizeLevels.Keys) {
    $si = $miSize.DropDownItems.Add($sl)
    $si.Tag = [double]$SizeLevels[$sl]
    $si.Add_Click({ try { Set-UiScale ([double]$this.Tag) } catch { Write-Log "Size error: $($_.Exception.Message)" } })
}
$miSize.Add_DropDownOpening({
    foreach ($it in $miSize.DropDownItems) { $it.Checked = ([math]::Abs([double]$it.Tag - $script:Scale) -lt 0.01) }
})
[void]$miAppearance.DropDownItems.Add($miSize)

$miList = New-Object System.Windows.Forms.ToolStripMenuItem 'Account list transparency'
foreach ($ll in $ListLevels.Keys) {
    $li = $miList.DropDownItems.Add($ll)
    $li.Tag = $ll
    $li.Add_Click({ Set-ListTransparency $this.Tag })
}
[void]$miList.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator))
$miBlur = $miList.DropDownItems.Add('Blur behind (experimental)')
$miBlur.Add_Click({ Set-Blur (-not $script:blurOn) })
[void]$miAppearance.DropDownItems.Add($miList)
[void]$miAppearance.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator))
$miEffShadow = $miAppearance.DropDownItems.Add('Shadow and rim')
$miEffShadow.Add_Click({ Set-PillEffect 'shadow' })
$miEffPulse = $miAppearance.DropDownItems.Add('Pill glow when not tracking')
$miEffPulse.Add_Click({ Set-PillEffect 'pulse' })
$miEffDot = $miAppearance.DropDownItems.Add('Pill live dot while recording')
$miEffDot.Add_Click({ Set-PillEffect 'dot' })
[void]$miAppearance.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator))
[void]$miAppearance.DropDownItems.Add('Drawing speed...', $null, { try { Show-PerfReport } catch { Write-Log "Speed report error: $($_.Exception.Message)" } })
[void]$menu.Items.Add($miAppearance)

# ---- Open file: everything the timer keeps, in one place ----
function New-FilesMenu {
    $mf = New-Object System.Windows.Forms.ToolStripMenuItem 'Open file'
    [void]$mf.DropDownItems.Add("This week's summary", $null, { Open-WeekFile })
    [void]$mf.DropDownItems.Add("This week's time entries", $null, { Open-WeekEntriesFile })
    [void]$mf.DropDownItems.Add('Timesheets folder', $null, { Invoke-Item $TimesheetDir })
    [void]$mf.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator))
    [void]$mf.DropDownItems.Add('Themes file', $null, {
        if (-not (Test-Path $ThemesFile)) { Load-Themes }
        Invoke-Item $ThemesFile
    })
    [void]$mf.DropDownItems.Add('Error log', $null, {
        if (Test-Path $LogFile) { Invoke-Item $LogFile } else { Show-Message 'Nothing has been logged yet.' }
    })
    return $mf
}
[void]$menu.Items.Add((New-FilesMenu))

[void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
[void]$menu.Items.Add('Reset position', $null, { Move-ToDefaultPosition })
[void]$menu.Items.Add('Close timer', $null, { $form.Close() })

$menu.Add_Opening({
    $miView.Text = if ($script:mode -eq 'pill') { 'Full view' } else { 'Compact view' }
    $miDrawer.Visible = $script:mode -eq 'full'
    $miFormat.Text = if ($script:hoursFormat -eq 'decimal') { 'Show hours as h:mm' } else { 'Show hours as decimal' }
    $miDrawer.Text = if ($script:drawerOpen) { 'Hide drawer' } else { 'Show drawer' }
    $donePlan = Get-DayPlan ([datetime]::Today)
    $miDone.Text = if (Test-DayDone) { 'Carry on working' } else { 'Finish for the day' }
    $miDone.Enabled = [bool]((Test-DayDone) -or ($donePlan.Working -and (Get-Date) -ge $donePlan.Start))
    $pt = $form.PointToClient([System.Windows.Forms.Cursor]::Position)
    if ($script:mode -eq 'pill') {
        # In the pill, only rows in the open account list count
        $zone = Get-PillZone $pt
        $script:menuRow = if ($zone -like 'row:*') { [int]$zone.Substring(4) } else { $null }
    } else {
        $script:menuRow = if (Test-InDrawer $pt) { $null } else { Get-HitRow (Get-TimerPoint $pt) }
    }
    $isRow = $null -ne $script:menuRow
    $isAccount = $isRow -and $script:slots[$script:menuRow] -ne 'Lunch'
    $miRename.Visible = $isAccount
    $miHide.Visible = $isAccount
    $miBudget.Visible = $isAccount
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
    $miEffShadow.Checked = [bool]$script:PillShadow
    $miEffPulse.Checked = [bool]$script:PillPulse
    $miEffDot.Checked = [bool]$script:PillLiveDot
    foreach ($mi in @($miEffShadow, $miEffPulse, $miEffDot)) { $mi.Enabled = $script:layered }
    Set-MenuScale $menu
})
$form.ContextMenuStrip = $menu

# ---- Tray icon ----
$tray = New-Object System.Windows.Forms.NotifyIcon
Update-AppIcon
if (-not $script:appIcon) { $tray.Icon = [System.Drawing.SystemIcons]::Application }
$tray.Text = 'Account timer'
$trayMenu = New-Object System.Windows.Forms.ContextMenuStrip
[void]$trayMenu.Items.Add('Show timer', $null, { Keep-Visible; $form.Activate() })
[void]$trayMenu.Items.Add('Weekly view...', $null, { try { Show-WeeklyView } catch { Show-WeeklyError $_ } })
[void]$trayMenu.Items.Add((New-FilesMenu))
[void]$trayMenu.Items.Add('Reset position', $null, { Move-ToDefaultPosition; Keep-Visible })
[void]$trayMenu.Items.Add('Close timer', $null, { $form.Close() })
$tray.ContextMenuStrip = $trayMenu
$trayMenu.Add_Opening({ Set-MenuScale $trayMenu })
$tray.Add_MouseClick({ if ($_.Button -eq 'Left') { Keep-Visible; $form.Activate() } })
$tray.Visible = $true

$form.Add_FormClosing({
    try { Stop-Active (Get-Date); Flush-Pending } catch {}
    try { Save-Position } catch {}
    $tray.Visible = $false
    $tray.Dispose()
    Write-Log 'Account timer closed normally.'
})

# ---- Refresh every second ----
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 1000
$timer.Add_Tick({
    if ($script:moving) { return }
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
        if ($script:drawerOpen -and $script:tick % 15 -eq 0) { Update-DrawerContent }
        Request-Redraw
        if ($script:tick % 30 -eq 0 -and $script:weekForm -and -not $script:weekForm.IsDisposed -and
            -not $script:wv.NotesGrid.IsCurrentCellInEditMode -and $script:weekStart -eq (Get-WeekStart) -and
            @($script:entrySelected).Count -eq 0 -and -not $script:entryMenu.Visible -and -not $script:dialogOpen) {
            Fill-WeeklyView
        }
    } catch {
        Write-Log "Refresh error: $($_.Exception.Message) $($_.InvocationInfo.PositionMessage)"
    }
})

# ---- Start ----
try { Load-State } catch { Write-Log "Could not load state: $($_.Exception.Message)" }
Load-Budgets
Load-Notes
Load-EntryGroups
Load-CreditPositions
try { Load-PillEffects } catch {}
if (Test-Path $MergeFile) { try { $script:mergeEntries = ((Get-Content $MergeFile -TotalCount 1).Trim() -ne '0') } catch {} }
Refresh-Totals
Write-WeekSummary
Write-Log 'Account timer started.'
if ($script:useRenderer) { Write-Log 'Using fast drawing for the full view.' }
elseif ($script:rendererError) { Write-Log "Fast drawing unavailable, using standard drawing: $($script:rendererError)" }
Update-View
$savedList = 'Off'
if (Test-Path $ListAlphaFile) { try { $savedList = (Get-Content $ListAlphaFile -TotalCount 1).Trim() } catch {} }
$script:listLevel = if ($ListLevels.Contains($savedList)) { $savedList } else { 'Off' }
$script:listAlpha = $ListLevels[$script:listLevel]
$savedBlur = $false
if (Test-Path $BlurFile) { try { $savedBlur = ((Get-Content $BlurFile -TotalCount 1).Trim() -eq '1') } catch {} }
if ($savedBlur) { Set-Blur $true -Quiet }
Load-Positions
$savedView = 'full'
if (Test-Path $ViewFile) { try { $savedView = (Get-Content $ViewFile -TotalCount 1).Trim() } catch {} }
$drawerWasOpen = $false
if (Test-Path $DrawerFile) {
    try {
        $drawerLines = @(Get-Content $DrawerFile -TotalCount 2)
        $drawerWasOpen = ($drawerLines[0].Trim() -eq '1')
        if ($drawerLines.Count -gt 1 -and $drawerLines[1].Trim() -eq 'week') { $script:drawerMode = 'week' }
    } catch {}
}
if ($savedView -eq 'pill') {
    Set-View 'pill' -Starting
    $script:drawerReopen = $drawerWasOpen
} else {
    if ($script:positions.ContainsKey('full')) { Set-TimerScreenPos $script:positions['full'] }
    if ($drawerWasOpen) { Set-DrawerOpen $true -Starting }
    Keep-Visible
}
Request-Redraw
$timer.Start()

# Pill animation (breathing glow and live dot), about 10 frames a second, only when needed
$animTimer = New-Object System.Windows.Forms.Timer
$animTimer.Interval = 100
$animTimer.Add_Tick({
    if ($script:moving) { return }
    try {
        $v = $script:view
        if ($script:mode -eq 'pill' -and $script:layered -and $v -and
            (($PillPulse -and $v.Idle -and -not $v.Before) -or ($PillLiveDot -and $v.ActiveName))) {
            Request-Redraw
        }
    } catch {}
})
$animTimer.Start()
[System.Windows.Forms.Application]::Run($form)
