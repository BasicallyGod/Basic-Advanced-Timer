using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The dark glass panel the Week instrument is drawn on in the Phosphor look (see <see cref="WeekInstrument"/>).
/// </summary>
public static class VfdWeek
{
    /// <summary>A dark display panel: the trough's glass and what is drawn in it (only the lit fills are meshed).</summary>
    public static Control Panel(Control content, double height, string name)
    {
        var grid = new Grid { Height = double.IsNaN(height) ? double.NaN : height, Children = { content } };
        var panel = new Border { Name = name, CornerRadius = new CornerRadius(14), ClipToBounds = true, Child = grid, Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#0B0E13"), 0), new GradientStop(Color.Parse("#10141B"), 1) } } };
        return new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = panel };
    }
}
