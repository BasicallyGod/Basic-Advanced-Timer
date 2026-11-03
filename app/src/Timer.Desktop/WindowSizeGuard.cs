using Avalonia;
using Avalonia.Controls;

namespace Timer.Desktop;

/// <summary>
/// Keeps a borderless window's intended client size separate from native resize notifications.
/// Reassigning Width/Height is insufficient when those properties already contain the requested
/// values but the native window retained an earlier size while hidden or moving between screens.
/// </summary>
public sealed class WindowSizeGuard
{
    private readonly Window window;
    private readonly Func<Window, ScreenGuard.Box?> read;
    private readonly Func<Window, ScreenGuard.Box, bool> place;
    private Size requested;

    public WindowSizeGuard(Window window, Size requested,
        Func<Window, ScreenGuard.Box?>? read = null, Func<Window, ScreenGuard.Box, bool>? place = null)
    {
        this.window = window;
        this.read = read ?? (w => ScreenGuard.TryBoundsOf(w, out var box) ? box : null);
        this.place = place ?? ScreenGuard.Place;
        Request(requested);
    }

    public void Request(Size size)
    {
        requested = size;
        window.Width = size.Width; window.Height = size.Height;
    }

    /// <summary>Checks only at show/expansion boundaries, never on each animation frame.</summary>
    public string Ensure()
    {
        if (!window.IsVisible || window.WindowState != WindowState.Normal) return "size check skipped";
        window.Width = requested.Width; window.Height = requested.Height;
        var width = (int)Math.Round(requested.Width * window.RenderScaling);
        var height = (int)Math.Round(requested.Height * window.RenderScaling);
        if (read(window) is not { } actual)
            return $"expected {width}x{height} px, native unavailable";
        var result = $"expected {width}x{height} px, native {actual.Width}x{actual.Height} px";
        if (Math.Abs(actual.Width - width) <= 1 && Math.Abs(actual.Height - height) <= 1) return result;
        bool applied;
        using (PerformanceTimings.Measure("dashboard bounds correction"))
            applied = place(window, actual with { Width = width, Height = height });
        var after = read(window);
        return result + $", resize {(applied ? "accepted" : "failed")}, now "
            + (after is { } box ? $"{box.Width}x{box.Height} px" : "unavailable");
    }
}
