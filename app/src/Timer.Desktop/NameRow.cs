using Avalonia;
using Avalonia.Controls;

namespace Timer.Desktop;

/// <summary>An optional dot, a name and a chevron in a line, where only the name gives way: it takes what room is left and the chevron stays beside it.</summary>
public sealed class NameRow : Panel
{
    public double Spacing { get; set; } = 9;
    // The name is the middle child, or the first when there is no dot before it. The chevron is always last.
    private IEnumerable<Control> Parts => Children.Count >= 3 ? new[] { Children[0], Children[1], Children[^1] } : Children.Take(2);
    private Control? Flexible => Children.Count >= 3 ? Children[1] : Children.Count == 2 ? Children[0] : null;
    protected override Size MeasureOverride(Size available)
    {
        if (Flexible is not { } middle) return default;
        var fixedWidth = 0.0; var height = 0.0; var gaps = 0;
        foreach (var part in Parts)
        {
            gaps++;
            if (part == middle) continue;
            part.Measure(new Size(double.PositiveInfinity, available.Height)); fixedWidth += part.DesiredSize.Width; height = Math.Max(height, part.DesiredSize.Height);
        }
        middle.Measure(new Size(Math.Max(0, available.Width - fixedWidth - (gaps - 1) * Spacing), available.Height));
        height = Math.Max(height, middle.DesiredSize.Height);
        return new Size(fixedWidth + middle.DesiredSize.Width + (gaps - 1) * Spacing, height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        var x = 0.0;
        foreach (var child in Parts)
        {
            var size = child.DesiredSize;
            child.Arrange(new Rect(x, (final.Height - size.Height) / 2, size.Width, size.Height));
            x += size.Width + Spacing;
        }
        return final;
    }
}
