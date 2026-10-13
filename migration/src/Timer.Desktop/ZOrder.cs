using System.Runtime.InteropServices;

namespace Timer.Desktop;

/// <summary>
/// Decides when the pill or dashboard may put itself back at the front of the topmost windows. Doing it while another topmost window has the focus, such
/// as the screenshot tool's overlay, puts the timer on top of that window, so then it keeps still.
/// </summary>
public static class ZOrder
{
    /// <summary>Whether a visible window that is not active should be put back in front, given whether the window that is active is itself a topmost one.</summary>
    public static bool ShouldReassert(bool visible, bool active, bool minimised, bool foregroundIsTopmost) => visible && !active && !minimised && !foregroundIsTopmost;

    /// <summary>Whether the window that has the focus now is a topmost one belonging to something else. False where that cannot be told, so snapping still works.</summary>
    public static bool ForegroundIsTopmost()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var foreground = GetForegroundWindow();
            return foreground != IntPtr.Zero && (GetWindowLong(foreground, GwlExStyle) & WsExTopmost) != 0;
        }
        catch (Exception) { return false; }
    }
    private const int GwlExStyle = -20; private const long WsExTopmost = 0x00000008;
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
}
