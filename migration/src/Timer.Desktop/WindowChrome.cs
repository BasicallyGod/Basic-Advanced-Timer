using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>The app's own icon, drawn once from the bundled asset and shared by every window and the taskbar.</summary>
public static class AppIcon
{
    private static WindowIcon? icon;
    public static WindowIcon? Get()
    {
        if (icon is not null) return icon;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://" + typeof(AppIcon).Assembly.GetName().Name + "/Assets/AppIcon.png"));
            return icon = new WindowIcon(stream);
        }
        catch (Exception) { return null; } // A missing icon should never stop the timer opening.
    }
}

/// <summary>
/// Makes a window's title bar wear the selected theme and the app icon, instead of the system's grey bar. On Windows 11 the title bar, its text and
/// the window border take the theme's colours and follow a theme change while the window is open. Older Windows and other systems keep their normal
/// title bar, with the icon, and nothing else changes.
/// </summary>
public static class WindowChrome
{
    private const int DwmBorderColour = 34, DwmCaptionColour = 35, DwmTextColour = 36, DwmImmersiveDark = 20;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>The colours a window's chrome should use, as hex, taken from its own theme so a previewed theme shows at once.</summary>
    public static (string Caption, string Text, string Border, bool Dark)? Colours(Window window)
    {
        string? Hex(string key) => window.TryFindResource(key, window.ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? ThemeColours.Hex(brush.Color.R, brush.Color.G, brush.Color.B) : null;
        var surface = Hex("Surface"); var ink = Hex("Ink"); var line = Hex("Line");
        if (surface is null || ink is null || line is null) return null;
        return (surface, ink, line, ThemeColours.Luminance(surface) < .4);
    }
    /// <summary>Gives the window the app icon and keeps its title bar in step with the theme for as long as it lives.</summary>
    public static void Attach(Window window)
    {
        if (AppIcon.Get() is { } icon) window.Icon = icon;
        void Refresh() => Apply(window);
        window.Opened += (_, _) => Refresh();
        window.ActualThemeVariantChanged += (_, _) => Refresh();
        ThemeEngine.Applied += Refresh;
        window.Closed += (_, _) => ThemeEngine.Applied -= Refresh;
    }
    public static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows() || Colours(window) is not { } colours) return;
        if (window.TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero) return;
        int Ref(string hex) { var (r, g, b) = ThemeColours.Rgb(hex); return r | (g << 8) | (b << 16); }
        // These return an error on Windows 10, which has no coloured title bars. That is fine, so the results are ignored.
        var dark = colours.Dark ? 1 : 0; var caption = Ref(colours.Caption); var text = Ref(colours.Text); var border = Ref(colours.Border);
        DwmSetWindowAttribute(handle, DwmImmersiveDark, ref dark, sizeof(int));
        DwmSetWindowAttribute(handle, DwmCaptionColour, ref caption, sizeof(int));
        DwmSetWindowAttribute(handle, DwmTextColour, ref text, sizeof(int));
        DwmSetWindowAttribute(handle, DwmBorderColour, ref border, sizeof(int));
    }
}
