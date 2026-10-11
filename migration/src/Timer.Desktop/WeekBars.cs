using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Timer.Desktop;

public sealed record BarSegment(string Account, Color Colour, double Hours);
public sealed record DayBar(string Day, string Tooltip, string TotalText, double Hours, List<BarSegment> Segments);

// A bar for each day of the week, split by account. It is drawn here
// rather than built from controls so a week with many accounts stays light, and so the bars share one scale.
public sealed class WeekBars : Control
{
    public List<DayBar> Bars { get; init; } = new();
    public WeekBars() { Height = 170; HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch; Name = "WeekBars"; }

    private IBrush Resource(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;
    private FormattedText Text(string text, double size, IBrush brush, bool bold = false)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(Avalonia.Controls.Documents.TextElement.GetFontFamily(this), FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, brush);
    private (double Left, double Slot, double Top, double Bottom, double Max) Layout()
    {
        var max = Math.Max(Bars.Count == 0 ? 0 : Bars.Max(b => b.Hours), 1.0);
        max = Math.Ceiling(max); // A whole number of hours at the top, so the scale reads easily.
        return (6, Bounds.Width > 12 && Bars.Count > 0 ? (Bounds.Width - 12) / Bars.Count : 1, 24, Bounds.Height - 26, max);
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bars.Count == 0 || Bounds.Width < 40) return;
        var surface = Resource("Surface", Brushes.White); var ink = Resource("Ink", Brushes.Black); var muted = Resource("Muted", Brushes.Gray); var line = Resource("Line", Brushes.LightGray);
        var (left, slot, top, bottom, max) = Layout();
        var height = bottom - top;
        context.DrawLine(new Pen(line, 1), new Point(left, bottom), new Point(left + slot * Bars.Count, bottom));
        for (var i = 0; i < Bars.Count; i++)
        {
            var bar = Bars[i]; var width = Math.Min(54, slot * .62); var x = left + slot * i + (slot - width) / 2;
            var y = bottom;
            foreach (var segment in bar.Segments.Where(s => s.Hours > 0))
            {
                var h = Math.Max(1.5, segment.Hours / max * height);
                // A hairline of the page colour between slices keeps neighbours apart even when two accounts have similar colours.
                context.DrawRectangle(new SolidColorBrush(segment.Colour), new Pen(surface, 1), new Rect(x, y - h, width, h), 3, 3);
                y -= h;
            }
            var total = Text(bar.TotalText, 12, bar.Hours > 0 ? ink : muted, bar.Hours > 0);
            var at = new Point(left + slot * i + (slot - total.Width) / 2, Math.Min(y, bottom) - total.Height - 3);
            // A patch of the page colour behind the figure, so the dashed working-day line never runs through it.
            context.DrawRectangle(surface, null, new Rect(at.X - 3, at.Y - 1, total.Width + 6, total.Height + 2), 3, 3);
            context.DrawText(total, at);
            var day = Text(bar.Day, 12, muted);
            context.DrawText(day, new Point(left + slot * i + (slot - day.Width) / 2, bottom + 5));
        }
    }
    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Bars.Count == 0) return;
        var (left, slot, _, _, _) = Layout();
        var index = (int)Math.Floor((e.GetPosition(this).X - left) / slot);
        ToolTip.SetTip(this, index >= 0 && index < Bars.Count ? Bars[index].Tooltip : null);
    }
}
