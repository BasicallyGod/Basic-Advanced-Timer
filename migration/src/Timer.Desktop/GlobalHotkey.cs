using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Win32;
using Timer.Core;
using Timer.Desk;

namespace Timer.Desktop;

/// <summary>
/// The shortcut that opens Quick capture from any app, on Windows only (elsewhere it does nothing). Windows ties a shortcut to a window, so it is
/// registered on whichever of the timer's windows has a handle, and the window's messages are watched for the press. It needs no install or rights.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr window, int id);
    private const int WmHotkey = 0x0312, HotkeyId = 0x4D43; private const uint NoRepeat = 0x4000;
    private readonly Action pressed; private IntPtr bound; private HotkeySpec? registered;
    public GlobalHotkey(Action pressed) => this.pressed = pressed;

    /// <summary>The shortcut a key press makes, or null when it holds no Ctrl, Alt or Windows key or is not a key a shortcut may use.</summary>
    public static HotkeySpec? FromKey(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        var name = key switch
        {
            >= Avalonia.Input.Key.A and <= Avalonia.Input.Key.Z => key.ToString(),
            >= Avalonia.Input.Key.D0 and <= Avalonia.Input.Key.D9 => ((int)key - (int)Avalonia.Input.Key.D0).ToString(),
            >= Avalonia.Input.Key.F1 and <= Avalonia.Input.Key.F12 => key.ToString(),
            _ => null
        };
        if (name is null) return null;
        var spec = new HotkeySpec(modifiers.HasFlag(Avalonia.Input.KeyModifiers.Control), modifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt),
            modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift), modifiers.HasFlag(Avalonia.Input.KeyModifiers.Meta), name);
        return spec.IsValid ? spec : null;
    }

    /// <summary>Starts watching a window's messages for the shortcut. Done once per window.</summary>
    public void Watch(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        Win32Properties.AddWndProcHookCallback(window, (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg == WmHotkey && wParam.ToInt32() == HotkeyId) { handled = true; Avalonia.Threading.Dispatcher.UIThread.Post(pressed); }
            return IntPtr.Zero;
        });
    }

    /// <summary>
    /// Registers the shortcut (or clears it for null) on the first window that has a handle. False means Windows refused it, usually because another
    /// app holds it, or no window has a handle yet (it is tried again when one opens).
    /// </summary>
    public bool Apply(HotkeySpec? spec, params Window[] windows)
    {
        if (!OperatingSystem.IsWindows()) return true;
        if (registered == spec && bound != IntPtr.Zero) return true;
        if (bound != IntPtr.Zero) { UnregisterHotKey(bound, HotkeyId); bound = IntPtr.Zero; registered = null; }
        if (spec is null) return true;
        foreach (var window in windows)
        {
            if (window.TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero) continue;
            if (!RegisterHotKey(handle, HotkeyId, (uint)spec.Modifiers | NoRepeat, (uint)HotkeySpec.VirtualKey(spec.Key)!.Value)) return false;
            bound = handle; registered = spec; return true;
        }
        return false;
    }

    public void Dispose() { if (OperatingSystem.IsWindows() && bound != IntPtr.Zero) UnregisterHotKey(bound, HotkeyId); bound = IntPtr.Zero; }
}
