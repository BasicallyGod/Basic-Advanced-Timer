using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Timer.Desktop;

/// <summary>
/// Holds a one-line text and, when it is too long for the room it is given, slides it to the end and back instead of cutting it off.
/// It rests at the start for a moment each way, so the beginning of the text is easy to read.
/// </summary>
public sealed class AutoScrollText : Decorator
{
    public const double PixelsPerSecond = 38;
    public static readonly TimeSpan Rest = TimeSpan.FromSeconds(1.6);
    private readonly TranslateTransform shift = new();
    private DispatcherTimer? timer;
    private double offset, travel;
    private int direction = 1;
    private DateTime restingUntil;
    public AutoScrollText(TextBlock text)
    {
        text.TextTrimming = TextTrimming.None; text.TextWrapping = TextWrapping.NoWrap; text.RenderTransform = shift;
        Child = text; ClipToBounds = true; HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        text.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) Restart(); };
    }
    /// <summary>How far the text overhangs the room it has, which is how far it has to travel. Zero when it fits.</summary>
    public double Overflow => travel;
    public bool Scrolling => timer is { IsEnabled: true };
    protected override Size MeasureOverride(Size available)
    {
        if (Child is null) return default;
        Child.Measure(new Size(double.PositiveInfinity, available.Height));
        var natural = Child.DesiredSize;
        return new Size(Math.Min(natural.Width, available.Width), natural.Height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        if (Child is null) return final;
        var natural = Child.DesiredSize.Width;
        Child.Arrange(new Rect(0, 0, Math.Max(natural, final.Width), final.Height));
        var needed = Math.Max(0, natural - final.Width);
        if (Math.Abs(needed - travel) > .5) { travel = needed; Restart(); }
        return final;
    }
    private void Restart()
    {
        offset = 0; direction = 1; shift.X = 0; restingUntil = DateTime.UtcNow + Rest;
        if (travel > 0 && IsAttachedToVisualTree()) Start(); else Stop();
    }
    private bool IsAttachedToVisualTree() => this.GetVisualRoot() is not null;
    private void Start()
    {
        if (timer is null) { timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; timer.Tick += (_, _) => Step(); }
        timer.Start();
    }
    private void Stop() { timer?.Stop(); shift.X = 0; }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Restart(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); Stop(); }
    private void Step()
    {
        if (travel <= 0) { Stop(); return; }
        var now = DateTime.UtcNow;
        if (now < restingUntil) return;
        offset += direction * PixelsPerSecond * timer!.Interval.TotalSeconds;
        if (offset >= travel) { offset = travel; direction = -1; restingUntil = now + Rest; }
        else if (offset <= 0) { offset = 0; direction = 1; restingUntil = now + Rest; }
        shift.X = -offset;
    }
}

/// <summary>A dot, a name and a chevron in a line, where only the name gives way: it takes what room is left and the chevron stays beside it.</summary>
public sealed class NameRow : Panel
{
    public double Spacing { get; set; } = 9;
    protected override Size MeasureOverride(Size available)
    {
        if (Children.Count < 3) return default;
        var first = Children[0]; var middle = Children[1]; var last = Children[^1];
        first.Measure(new Size(double.PositiveInfinity, available.Height)); last.Measure(new Size(double.PositiveInfinity, available.Height));
        var room = Math.Max(0, available.Width - first.DesiredSize.Width - last.DesiredSize.Width - 2 * Spacing);
        middle.Measure(new Size(room, available.Height));
        var width = first.DesiredSize.Width + middle.DesiredSize.Width + last.DesiredSize.Width + 2 * Spacing;
        return new Size(width, Math.Max(first.DesiredSize.Height, Math.Max(middle.DesiredSize.Height, last.DesiredSize.Height)));
    }
    protected override Size ArrangeOverride(Size final)
    {
        if (Children.Count < 3) return final;
        var x = 0.0;
        foreach (var child in new[] { Children[0], Children[1], Children[^1] })
        {
            var size = child.DesiredSize;
            child.Arrange(new Rect(x, (final.Height - size.Height) / 2, size.Width, size.Height));
            x += size.Width + Spacing;
        }
        return final;
    }
}
