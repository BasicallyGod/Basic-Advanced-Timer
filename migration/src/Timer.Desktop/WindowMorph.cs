using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Timer.Desktop;

/// <summary>
/// The grow and shrink between the pill and the dashboard. The sidebar is the pill drawn out, so the move reads as one shape growing: the dashboard window is shown at
/// its full size with its content clipped to a rounded rectangle that grows from the pill's own card to the whole window (and back), while a picture of the pill
/// fades out over the first part of the way and the dashboard fades in behind it. No window is resized, so nothing flickers. One eased number, <c>m</c> (0 the
/// pill, 1 the dashboard), drives it all. Under "Show animations" off it jumps to its end.
/// </summary>
public static class WindowMorph
{
    /// <summary>Whether the move is animated. The tests switch it off, since nothing ticks in a headless test.</summary>
    public static bool Enabled { get; set; } = true;
    /// <summary>Opening eases out quickly (most of the growth in the first 120 ms) and closing is a little shorter, since putting away is dismissing.</summary>
    public const int OpenMilliseconds = 340, CloseMilliseconds = 280;
    /// <summary>The pill's card sits this far in from the dashboard's top left when they line up, so it reads as the pill at the sidebar's top.</summary>
    public const double PillInset = 12;
    /// <summary>The dashboard's own corner radius.</summary>
    public const double DashboardRadius = 20;

    /// <summary>Progress along a cubic Bézier ease (the CSS cubic-bezier of the board), for a fraction of the time that has gone.</summary>
    public static double Bezier(double t, double x1, double y1, double x2, double y2)
    {
        t = Math.Clamp(t, 0, 1); double lo = 0, hi = 1;
        for (var i = 0; i < 40; i++)
        {
            var m = (lo + hi) / 2; var x = 3 * (1 - m) * (1 - m) * m * x1 + 3 * (1 - m) * m * m * x2 + m * m * m;
            if (x < t) lo = m; else hi = m;
        }
        var u = (lo + hi) / 2;
        return 3 * (1 - u) * (1 - u) * u * y1 + 3 * (1 - u) * u * u * y2 + u * u * u;
    }
    /// <summary>The eased value of m after <paramref name="elapsed"/> of an opening or closing.</summary>
    public static double Ease(double elapsedMilliseconds, bool closing)
        => closing ? Bezier(elapsedMilliseconds / CloseMilliseconds, .4, 0, .2, 1) : Bezier(elapsedMilliseconds / OpenMilliseconds, .22, .8, .24, 1);

    /// <summary>How much of the pill's picture is left at m: it is gone by about 40 per cent of the way.</summary>
    public static double PillOpacity(double m) => Math.Clamp(1 - m * 2.6, 0, 1);
    /// <summary>How much of the dashboard shows at m: it comes in from 18 to 60 per cent.</summary>
    public static double DashboardOpacity(double m) => Math.Clamp((m - .18) * 2.4, 0, 1);

    /// <summary>A rounded rectangle as a geometry, to clip to.</summary>
    public static Geometry RoundedRect(Rect r, double radius)
    {
        radius = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
        var geometry = new StreamGeometry();
        using (var c = geometry.Open())
        {
            var size = new Size(radius, radius);
            c.BeginFigure(new Point(r.Left + radius, r.Top), true);
            c.LineTo(new Point(r.Right - radius, r.Top)); c.ArcTo(new Point(r.Right, r.Top + radius), size, 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(r.Right, r.Bottom - radius)); c.ArcTo(new Point(r.Right - radius, r.Bottom), size, 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(r.Left + radius, r.Bottom)); c.ArcTo(new Point(r.Left, r.Bottom - radius), size, 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(r.Left, r.Top + radius)); c.ArcTo(new Point(r.Left + radius, r.Top), size, 0, false, SweepDirection.Clockwise);
            c.EndFigure(true);
        }
        return geometry;
    }
    public static Rect Lerp(Rect a, Rect b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Width + (b.Width - a.Width) * t, a.Height + (b.Height - a.Height) * t);

    /// <summary>A picture of a window as it looks now (its content over a transparent ground), for the dashboard to draw the pill from; null if it cannot be taken.</summary>
    public static RenderTargetBitmap? Capture(Window window)
    {
        using var timing = PerformanceTimings.Measure("pill capture");
        try
        {
            var scaling = window.RenderScaling; var width = window.ClientSize.Width; var height = window.ClientSize.Height;
            if (width < 2 || height < 2) return null;
            var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(width * scaling), (int)Math.Ceiling(height * scaling)), new Vector(96 * scaling, 96 * scaling));
            bitmap.Render(window);
            return bitmap;
        }
        catch (Exception) { return null; }
    }
}
