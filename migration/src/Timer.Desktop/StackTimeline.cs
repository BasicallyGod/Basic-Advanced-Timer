using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Timer.Desktop;

/// <summary>
/// A stack of blocks or meetings drawn across the Details card, from the first start to the last end, one bar to a row at about three times the timeline's scale. Pressing a
/// bar picks it; the picked one carries a ring and a handle at each end that can be dragged (to the minute for a block, to five minutes for a meeting). The block that is
/// recording now has its ring in green, no handles, and cannot be picked, since nothing under a running clock can be edited.
/// </summary>
public sealed class StackTimeline : Panel
{
    /// <summary>One bar: what it says, when it runs, how it is coloured, and whether it is the picked one, the one recording, and whether its ends can be dragged.</summary>
    public sealed record Bar(string Key, string Label, string Length, string Tip, DateTime Start, DateTime End, Color Fill, Color Ink, bool Picked, bool Live, bool Editable, Control? Mark);

    public const double AxisHeight = 18, RowHeight = 19, RowGap = 3, HandleWidth = 6, MinimumBar = 8;
    private readonly TimerModel model; private readonly IReadOnlyList<Bar> bars; private readonly int stepMinutes; private readonly DateTime windowStart, windowEnd;
    private readonly List<(Border Box, Bar Bar, Control Length, Control Times)> boxes = new(); private readonly List<(Border Handle, Bar Bar, bool Start)> handles = new();
    private readonly List<TextBlock> labels = new(); private readonly List<(Border Line, DateTime At)> guides = new();
    private (Bar Bar, DateTime Start, DateTime End)? preview;
    public event Action<Bar>? PickedBar; public event Action<Bar, DateTime, DateTime>? Resized;

    public StackTimeline(TimerModel model, IReadOnlyList<Bar> bars, int stepMinutes)
    {
        this.model = model; this.bars = bars; this.stepMinutes = stepMinutes; Name = "StackTimeline";
        var first = bars.Min(b => b.Start); var last = bars.Max(b => b.End);
        windowStart = first.Date.AddMinutes(Math.Floor((first - first.Date).TotalMinutes / 30) * 30); windowEnd = first.Date.AddMinutes(Math.Ceiling((last - first.Date).TotalMinutes / 30) * 30);
        if (windowEnd - windowStart < TimeSpan.FromHours(1)) windowEnd = windowStart.AddHours(1);
        var span = (windowEnd - windowStart).TotalMinutes; var every = span <= 150 ? 30 : span <= 360 ? 60 : 120;
        for (var at = 0.0; at <= span + .01; at += every)
        {
            var line = new Border { Width = 1, Opacity = .45, IsHitTestVisible = false, Tag = "StackGuide" }; line.Classes.Add("rule"); Children.Add(line); guides.Add((line, windowStart.AddMinutes(at)));
            var label = Ui.Text(windowStart.AddMinutes(at).ToString("HH:mm"), 10.5, "muted"); label.Classes.Add("figures"); label.IsHitTestVisible = false; label.Name = "StackHour_" + windowStart.AddMinutes(at).ToString("HHmm"); Children.Add(label); labels.Add(label);
        }
        foreach (var bar in bars) AddBar(bar);
    }

    private void AddBar(Bar bar)
    {
        var length = new TextBlock { Text = bar.Length, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(bar.Ink), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Name = "StackLength" };
        var times = new TextBlock { Text = $"{bar.Start:HH:mm}–{(bar.Live ? "now" : bar.End.ToString("HH:mm"))}", FontSize = 10.5, Opacity = .75, Foreground = new SolidColorBrush(bar.Ink), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var line = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Margin = new Thickness(6, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        if (bar.Mark is not null) line.Children.Add(bar.Mark);
        var label = new TextBlock { Text = bar.Label, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(bar.Ink), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Name = "StackLabel" };
        if (bar.Label.Length > 0) line.Children.Add(label);
        line.Children.Add(length); line.Children.Add(times);
        if (bar.Live) line.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brush.Parse(model.CurrentTheme.Colour("Live")), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Name = "StackRecordingDot" });
        var box = new Border { Name = "Stack_" + bar.Key, Background = new SolidColorBrush(bar.Fill), CornerRadius = new CornerRadius(8), Child = new Border { ClipToBounds = true, CornerRadius = new CornerRadius(8), Child = line }, Focusable = !bar.Live, Cursor = new Cursor(bar.Live ? StandardCursorType.Arrow : StandardCursorType.Hand), Tag = bar };
        if (bar.Picked) { box.Classes.Add("mergeSelected"); box.BoxShadow = new BoxShadows(new BoxShadow { Spread = 2, Color = Color.Parse(model.CurrentTheme.Colour("Accent")) }); box.ZIndex = 3; }
        else if (bar.Live) box.BoxShadow = new BoxShadows(new BoxShadow { Spread = 2, Color = Color.Parse(model.CurrentTheme.Colour("Live")) });
        ToolTip.SetTip(box, bar.Tip); Avalonia.Automation.AutomationProperties.SetName(box, bar.Tip + (bar.Live ? ", recording" : bar.Picked ? ", picked" : ""));
        Point? press = null;
        box.PointerPressed += (_, e) => { if (e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) { press = e.GetPosition(this); e.Handled = true; } };
        box.PointerReleased += (_, e) =>
        {
            if (press is { } p && e.InitialPressMouseButton == MouseButton.Left && Math.Abs(e.GetPosition(this).X - p.X) <= 4 && Math.Abs(e.GetPosition(this).Y - p.Y) <= 4)
            { if (bar.Live) model.ExplainRunning(); else PickedBar?.Invoke(bar); }
            press = null; e.Handled = true;
        };
        box.KeyDown += (_, e) => { if (!bar.Live && e.Key is Key.Enter or Key.Space) { PickedBar?.Invoke(bar); e.Handled = true; } };
        Children.Add(box); boxes.Add((box, bar, length, times));
        if (bar.Picked && bar.Editable) { AddHandle(bar, true); AddHandle(bar, false); }
    }

    // A handle at one end of the picked bar. It is dragged to a new time, shown as it goes, and applied when it is let go.
    private void AddHandle(Bar bar, bool atStart)
    {
        var handle = new Border { Name = atStart ? "StackHandleStart" : "StackHandleEnd", Width = HandleWidth, Height = RowHeight - 3, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), ZIndex = 5, Cursor = new Cursor(StandardCursorType.SizeWestEast), Background = Brush.Parse(model.CurrentTheme.Colour("Accent")), BorderBrush = Brush.Parse(model.CurrentTheme.Colour("Panel")) }; // The accent with a card-coloured edge: seen on both themes, where the text colour was near black on Light.
        ToolTip.SetTip(handle, atStart ? "Drag the start" : "Drag the end");
        var dragging = false;
        handle.PointerPressed += (_, e) => { if (e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) { dragging = true; e.Pointer.Capture(handle); preview = (bar, bar.Start, bar.End); e.Handled = true; } };
        handle.PointerMoved += (_, e) =>
        {
            if (!dragging || preview is not { } now) return;
            var at = AtX(e.GetPosition(this).X); var minimum = TimeSpan.FromMinutes(stepMinutes);
            var day = bar.Start.Date; var latest = stepMinutes >= 5 ? day.AddMinutes(1439) : day.AddDays(1);
            if (atStart) now = now with { Start = at < now.End - minimum ? (at < day ? day : at) : now.End - minimum };
            else now = now with { End = at > now.Start + minimum ? (at > latest ? latest : at) : now.Start + minimum };
            preview = now; InvalidateArrange(); e.Handled = true;
        };
        handle.PointerReleased += (_, e) =>
        {
            if (!dragging) return;
            dragging = false; e.Pointer.Capture(null); var result = preview; preview = null; InvalidateArrange(); e.Handled = true;
            if (result is { } r && (r.Start != bar.Start || r.End != bar.End)) Resized?.Invoke(bar, r.Start, r.End);
        };
        handle.PointerCaptureLost += (_, _) => { if (dragging) { dragging = false; preview = null; InvalidateArrange(); } };
        Children.Add(handle); handles.Add((handle, bar, atStart));
    }

    // The time under a pointer position, to the minute (a block) or five minutes (a meeting).
    private DateTime AtX(double x)
    {
        var minutes = Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1) * (windowEnd - windowStart).TotalMinutes + (windowStart - windowStart.Date).TotalMinutes;
        return windowStart.Date.AddMinutes(Math.Round(minutes / stepMinutes) * stepMinutes);
    }

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 440 : available.Width;
        foreach (var child in Children) child.Measure(new Size(width, double.PositiveInfinity));
        return new Size(width, AxisHeight + bars.Count * (RowHeight + RowGap) - RowGap);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var width = final.Width; var span = (windowEnd - windowStart).TotalMinutes;
        double X(DateTime at) => (at - windowStart).TotalMinutes / span * width;
        foreach (var (line, at) in guides) line.Arrange(new Rect(Math.Min(X(at), width - 1), AxisHeight - 2, 1, Math.Max(0, final.Height - AxisHeight + 2)));
        for (var i = 0; i < labels.Count; i++)
        {
            var size = labels[i].DesiredSize; var centre = X(guides[i].At);
            var left = i == 0 ? 0 : i == labels.Count - 1 ? width - size.Width : centre - size.Width / 2;
            labels[i].Arrange(new Rect(left, 0, size.Width, size.Height));
        }
        for (var i = 0; i < boxes.Count; i++)
        {
            var (box, bar, length, times) = boxes[i];
            var (start, end) = preview is { } p && p.Bar == bar ? (p.Start, p.End) : (bar.Start, bar.End);
            var left = X(start); var w = Math.Max(MinimumBar, X(end) - left); if (left + w > width) { left = Math.Max(0, width - w); }
            box.Arrange(new Rect(left, AxisHeight + i * (RowHeight + RowGap), w, RowHeight));
            // What a bar says depends on its width: the label always, the length when there is room, the times when there is plenty.
            length.IsVisible = w >= 70 && bar.Length.Length > 0; times.IsVisible = w >= 170;
            if (preview is { } q && q.Bar == bar && times is TextBlock t) { t.Text = $"{start:HH:mm}–{end:HH:mm}"; t.IsVisible = w >= 110; }
        }
        foreach (var (handle, bar, atStart) in handles)
        {
            var index = bars.ToList().IndexOf(bar); var (start, end) = preview is { } p && p.Bar == bar ? (p.Start, p.End) : (bar.Start, bar.End);
            var x = atStart ? X(start) : X(end);
            handle.Arrange(new Rect(x - HandleWidth / 2, AxisHeight + index * (RowHeight + RowGap) + 1.5, HandleWidth, RowHeight - 3));
        }
        return final;
    }
}
