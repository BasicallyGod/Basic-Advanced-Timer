using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Timer.Desktop;

/// <summary>
/// Keeps the timer's windows reachable when the screens change. Unplugging a monitor can leave a window on a screen that is gone, and Windows does not always
/// bring a hidden or tray-only window back, so Reset position could not be trusted to rely on Avalonia's own list of screens or its position setter. These
/// ask Windows directly where the monitors are and place the window with it. The sums are plain functions so they can be tested; the Windows calls are skipped
/// anywhere else, and where a window has no handle (headless tests) it counts as reachable.
/// </summary>
public static class ScreenGuard
{
    /// <summary>A rectangle in device pixels.</summary>
    public readonly record struct Box(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;
    }

    /// <summary>Where a window of this size goes at the top right of the work area, a margin in from its edges, and never past the top left of it.</summary>
    public static Box TopRight(Box area, int width, int height, int margin)
        => new(Math.Max(area.X, area.Right - width - margin), Math.Max(area.Y, area.Y + margin), width, height);

    /// <summary>Slides a window, without changing its size, to lie inside the work area, or against its top left corner when it is bigger than the area.</summary>
    public static Box SlideInside(Box area, Box window)
        => window with
        {
            X = Math.Max(area.X, Math.Min(window.X, area.Right - window.Width)),
            Y = Math.Max(area.Y, Math.Min(window.Y, area.Bottom - window.Height))
        };

    // ---- Windows

    /// <summary>Whether any part of the window is on a monitor that exists now. True where that cannot be told, so nothing is moved without cause.</summary>
    public static bool IsOnAnyMonitor(Window window)
    {
        if (!OperatingSystem.IsWindows() || Handle(window) is not { } hwnd) return true;
        try { return MonitorFromWindow(hwnd, MonitorDefaultToNull) != IntPtr.Zero; }
        catch (Exception) { return true; }
    }

    /// <summary>The work area of the main monitor (the part the taskbar leaves) and its scale, as Windows reports them now.</summary>
    public static bool TryPrimaryWorkArea(out Box area, out double scale)
    {
        area = default; scale = 1;
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var monitor = MonitorFromPoint(new Point32 { X = 0, Y = 0 }, MonitorDefaultToPrimary);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
            area = new Box(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
            try { if (GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0) scale = dpi / 96.0; } catch (Exception) { }
            return area.Width > 0 && area.Height > 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>The window's rectangle now, in device pixels.</summary>
    public static bool TryBoundsOf(Window window, out Box box)
    {
        box = default;
        if (!OperatingSystem.IsWindows() || Handle(window) is not { } hwnd) return false;
        try
        {
            if (!GetWindowRect(hwnd, out var r)) return false;
            box = new Box(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top); return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Moves (and sizes) the window to the box, in device pixels, straight through Windows.</summary>
    public static bool Place(Window window, Box box)
    {
        if (!OperatingSystem.IsWindows() || Handle(window) is not { } hwnd) return false;
        try { return SetWindowPos(hwnd, IntPtr.Zero, box.X, box.Y, box.Width, box.Height, SwpNoZOrder | SwpNoActivate); }
        catch (Exception) { return false; }
    }

    private static IntPtr? Handle(Window window) => window.TryGetPlatformHandle() is { Handle: var h } && h != IntPtr.Zero ? h : null;

    private const uint MonitorDefaultToNull = 0, MonitorDefaultToPrimary = 1, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    [StructLayout(LayoutKind.Sequential)] private struct Point32 { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect32 Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point32 point, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out Rect32 rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
}
