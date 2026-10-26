using Avalonia;
using Avalonia.Controls;

namespace Timer.Desktop;

/// <summary>
/// A part of the page that folds away while the account panel takes room from it: its width (and the gap before it) shrinks to nothing as <see cref="Amount"/>
/// goes from 0 to 1, with its contents laid out at their own width and clipped, and it fades as it goes, so the neighbours slide into the room instead of
/// jumping. Fully folded it is not there at all, so it takes no room and no click. The Today button and the Unallocated and Breaks figures use it.
/// </summary>
public sealed class Fold : Decorator
{
    private double amount;
    /// <param name="child">What folds.</param>
    /// <param name="gap">The space before it, which folds with it so the neighbours close up completely.</param>
    /// <param name="trailing">The gap comes after it rather than before, for the first of a row of neighbours.</param>
    public Fold(Control child, double gap = 0, bool trailing = false) { Child = child; Gap = gap; this.trailing = trailing; ClipToBounds = true; }
    public double Gap { get; }
    private readonly bool trailing;
    /// <summary>0 shown in full, 1 folded away.</summary>
    public double Amount
    {
        get => amount;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (value == amount) return;
            amount = value; Opacity = 1 - value; IsHitTestVisible = value < .5; IsVisible = value < 1; InvalidateMeasure();
        }
    }
    protected override Size MeasureOverride(Size available)
    {
        if (Child is null) return default;
        Child.Measure(new Size(double.PositiveInfinity, available.Height));
        return new Size((Child.DesiredSize.Width + Gap) * (1 - amount), Child.DesiredSize.Height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        Child?.Arrange(new Rect(trailing ? 0 : Gap, 0, Child.DesiredSize.Width, final.Height));
        return final;
    }
}

/// <summary>
/// Holds something that is laid out at its full width however much room it is given, and clips it to that room, so a card that is being narrowed to nothing
/// (or a column that is folding away) keeps its shape and its text does not wrap and reflow on every frame.
/// </summary>
public sealed class Reserve : Decorator
{
    public Reserve(Control child, double fullWidth) { Child = child; FullWidth = fullWidth; ClipToBounds = true; }
    public double FullWidth { get; }
    protected override Size MeasureOverride(Size available)
    {
        if (Child is null) return default;
        var width = Math.Max(FullWidth, double.IsInfinity(available.Width) ? FullWidth : available.Width);
        Child.Measure(new Size(width, available.Height));
        return new Size(double.IsInfinity(available.Width) ? FullWidth : available.Width, Child.DesiredSize.Height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        Child?.Arrange(new Rect(0, 0, Math.Max(FullWidth, final.Width), final.Height));
        return final;
    }
}
