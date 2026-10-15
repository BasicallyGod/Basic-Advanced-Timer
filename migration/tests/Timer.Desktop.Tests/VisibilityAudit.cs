using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;

namespace Timer.Desktop.Tests;

/// <summary>
/// Looks at what is on screen and checks that it can be read: every piece of text, every text box and every icon is compared with what it sits on
/// (the colours of the layers behind it, blended, over the window's surface), as a person would see them. Text must hold 3 to 1 and icons 3 to 1,
/// which is the floor for large or secondary text; the themes' own tests hold the main text much higher. It looks at the tree as drawn, so it catches a
/// colour that is right in one theme and lost in another, which no test of a single colour can.
/// </summary>
internal static class VisibilityAudit
{
    public sealed record Finding(string Scene, string Kind, string Name, string Text, double Ratio, string Foreground, string Background)
    {
        public override string ToString() => $"{Scene}: {Kind} {Name} \"{Text}\" {Ratio:0.0}:1 ({Foreground} on {Background})";
    }

    /// <summary>
    /// Takes the colour transitions off everything under a window. Headless, nothing ticks, so a card that is changing colour (the pill on starting a break)
    /// would be read at the colour it started from; with no transition it is at the colour it is going to, which is what the person ends up seeing.
    /// </summary>
    public static void NoTransitions(Visual root)
    {
        if (root is Animatable self) self.Transitions = null;
        foreach (var visual in root.GetVisualDescendants()) if (visual is Animatable animatable) animatable.Transitions = null;
    }

    /// <summary>How many things have been looked at, so a test can tell an audit that found nothing wrong from one that looked at nothing.</summary>
    public static int Examined;

    public const double MinimumText = 3.0, MinimumIcon = 3.0;

    private static Color Over(Color top, Color under)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
        return Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
    }
    private static string Hex(Color c) => ThemeColours.Hex(c.R, c.G, c.B);
    private static Color WithOpacity(ISolidColorBrush brush, double opacity) { var c = brush.Color; return Color.FromArgb((byte)Math.Round(c.A * brush.Opacity * opacity), c.R, c.G, c.B); }
    private static double OpacityOf(Visual v) { var o = 1.0; for (Visual? p = v; p is not null; p = p.GetVisualParent()) o *= p.Opacity; return o; }

    /// <summary>What a control sits on, as seen: the backgrounds of its ancestors blended from the nearest opaque one up to it, over the window's surface.</summary>
    private static Color Behind(Visual v, Color surface)
    {
        var layers = new List<Color>();
        for (var p = v.GetVisualParent(); p is not null; p = p.GetVisualParent())
        {
            IBrush? brush = p switch { Border b => b.Background, Panel panel => panel.Background, ContentPresenter c => c.Background, TemplatedControl t => t.Background, _ => null };
            if (brush is ISolidColorBrush solid && solid.Color.A > 0 && solid.Opacity > 0)
            {
                var layer = WithOpacity(solid, OpacityOf(p) / Math.Max(.0001, OpacityOf(p.GetVisualParent() ?? p)));
                layers.Add(layer); if (layer.A >= 250) break;
            }
        }
        var result = surface; for (var i = layers.Count - 1; i >= 0; i--) result = Over(layers[i], result);
        return result;
    }

    private static bool Shown(Control c) => c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.Bounds.Width > 1 && c.Bounds.Height > 1 && OpacityOf(c) > .05;

    public static List<Finding> Check(string scene, Visual root, double minimumText = MinimumText, double minimumIcon = MinimumIcon)
    {
        var found = new List<Finding>();
        var rootControl = root as Control; Color surface = Colors.White;
        if (rootControl is not null && rootControl.TryFindResource("Surface", rootControl.ActualThemeVariant, out var s) && s is ISolidColorBrush sb) surface = sb.Color;
        foreach (var visual in root.GetVisualDescendants())
        {
            if (visual is not Control control || !Shown(control)) continue;
            // Text of any kind: its colour as drawn, blended over whatever is behind it, against that background.
            (string Kind, string Text, ISolidColorBrush? Brush)? text = control switch
            {
                TextBlock t when !string.IsNullOrWhiteSpace(t.Text) => ("text", t.Text!, t.Foreground as ISolidColorBrush),
                TextBox box when !string.IsNullOrEmpty(box.Text) => ("input", box.Text!, box.Foreground as ISolidColorBrush),
                _ => null
            };
            if (text is { Brush: { } brush } found1)
            {
                Examined++;
                var behind = control is TextBox tb && tb.Background is ISolidColorBrush own && own.Color.A > 0 ? Over(WithOpacity(own, 1), Behind(control, surface)) : Behind(control, surface);
                var ink = Over(WithOpacity(brush, OpacityOf(control)), behind);
                var ratio = ThemeColours.Contrast(Hex(ink), Hex(behind));
                if (ratio < minimumText) found.Add(new Finding(scene, found1.Kind, control.Name ?? control.GetType().Name, Trim(found1.Text), ratio, Hex(ink), Hex(behind)));
            }
            else if (control is IconGlyph icon && control.TryFindResource(icon.TintKey ?? "Ink", control.ActualThemeVariant, out var tint) && tint is ISolidColorBrush tinted)
            {
                Examined++;
                var behind = Behind(control, surface); var ink = Over(WithOpacity(tinted, OpacityOf(control)), behind);
                var ratio = ThemeColours.Contrast(Hex(ink), Hex(behind));
                if (ratio < minimumIcon) found.Add(new Finding(scene, "icon", Describe(icon), icon.TintKey ?? "Ink", ratio, Hex(ink), Hex(behind)));
            }
        }
        return found;
    }
    private static string Trim(string text) => text.Length > 30 ? text[..30] + "…" : text;
    private static string Describe(Control c)
    {
        for (Visual? p = c; p is not null; p = p.GetVisualParent()) if (p is Control { Name: { Length: > 0 } name }) return name + "/icon";
        return "icon";
    }
}
