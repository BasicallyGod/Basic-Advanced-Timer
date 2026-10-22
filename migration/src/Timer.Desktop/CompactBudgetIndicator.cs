using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>Three opt-in budget views. Values come from TimerModel's configured budget window.</summary>
public sealed class CompactBudgetIndicator : Control
{
    public string Style { get; }
    public Color AccountColour { get; }
    public double? Ratio { get; private set; }
    public double Pace { get; private set; }
    public bool OverBudget => Ratio > 1;
    public CompactBudgetIndicator(string style, string colour)
    {
        Style = style; AccountColour = Color.Parse(Ui.Visible(colour));
        Width = style == "capsules" ? 30 : style == "heatmap" ? 10 : 9;
        Height = 32; IsHitTestVisible = false;
    }
    public void Set(double? ratio, double pace)
    {
        if (Ratio == ratio && Pace == pace) return;
        Ratio = ratio; Pace = pace; InvalidateVisual();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(ActualThemeVariant)) InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(0.5, 2, Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 4));
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var ink = new SolidColorBrush(Ui.ThemeColour(this, "Ink", Colors.Gray));
        var line = new SolidColorBrush(Ui.ThemeColour(this, "Line", Colors.Gray));
        var colour = new SolidColorBrush(AccountColour);
        var tint = new SolidColorBrush(Color.FromArgb(45, AccountColour.R, AccountColour.G, AccountColour.B));
        var ratio = Math.Clamp(Ratio ?? 0, 0, 1);
        if (Style == "heatmap")
        {
            const int cells = 7;
            var step = rect.Height / cells;
            for (var i = 0; i < cells; i++)
            {
                var cell = new Rect(rect.X, rect.Bottom - (i + 1) * step, rect.Width, step - 1);
                context.DrawRectangle(tint, null, cell, 1, 1);
                var part = Math.Clamp(ratio * cells - i, 0, 1);
                if (part > 0) context.DrawRectangle(colour, null, new Rect(cell.X, cell.Bottom - cell.Height * part, cell.Width, cell.Height * part), 1, 1);
            }
        }
        else
        {
            var radius = Style == "capsules" ? 7 : 3;
            context.DrawRectangle(tint, new Pen(line, .7), rect, radius, radius);
            using (context.PushClip(new RoundedRect(rect, radius)))
            {
                var fill = Style == "capsules" ? new SolidColorBrush(Color.FromArgb(85, AccountColour.R, AccountColour.G, AccountColour.B)) : colour;
                if (ratio > 0) context.DrawRectangle(fill, null, new Rect(rect.X, rect.Bottom - rect.Height * ratio, rect.Width, rect.Height * ratio));
            }
        }
        if (Ratio is null)
        {
            // A dash distinguishes unbudgeted accounts from a genuinely empty budget.
            context.DrawLine(new Pen(ink, 1), new Point(rect.Left, Style == "capsules" ? rect.Bottom - 2 : rect.Center.Y), new Point(rect.Right, Style == "capsules" ? rect.Bottom - 2 : rect.Center.Y));
        }
        else if (Style == "gauges")
        {
            var y = rect.Bottom - Math.Clamp(Pace, 0, 1) * rect.Height;
            context.DrawLine(new Pen(ink, 1), new Point(0, y), new Point(Bounds.Width, y));
        }
        if (OverBudget)
        {
            var warning = new SolidColorBrush(Ui.ThemeColour(this, "Overtime", Colors.OrangeRed));
            context.DrawRectangle(warning, null, new Rect(0, 0, Bounds.Width, 3), 1.5, 1.5);
        }
    }
}
