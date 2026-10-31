using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class TimelineViewportTests
{
    private static readonly DateTime Day = new(2026, 10, 21);
    private static TimerModel Model()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(14))); model.CompleteSetup(); return model;
    }
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static Window Open(TimerModel model)
    {
        var window = new Window { Content = ReviewViews.Timeline(model), Width = 600, Height = 400 }; window.Show(); Settle(window); return window;
    }
    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
    }

    [AvaloniaFact]
    public void PartlyVisibleHourLabelsRemainReadableWithoutMovingBlocksOrTicks()
    {
        var model = Model();
        model.Engine.Sessions.Add(new("2026-10-21", "Account one", "10:00:00", "recorded", "120"));
        var window = Open(model);
        try
        {
            var scroll = Find<ScrollViewer>(window, "TimelineScroll");
            var card = Find<Border>(window, "TimelineEntry0"); var originalLeft = Canvas.GetLeft(card); var originalWidth = card.Width;
            var label = Find<TextBlock>(window, "TimelineHour_10");
            // Scroll until the 10:00 label is cut by the left edge: its centre, where the block starts, is just outside.
            scroll.Offset = new Vector(Canvas.GetLeft(label) + label.Bounds.Width * .75, 0); Settle(window);
            Assert.True(label.TranslatePoint(default, scroll)!.Value.X >= 0);
            Assert.True(((TranslateTransform)label.RenderTransform!).X > 0);
            Assert.Equal(originalLeft, Canvas.GetLeft(card)); Assert.Equal(originalWidth, card.Width);
            Assert.True(card.TranslatePoint(default, scroll)!.Value.X < 0); // The block is clipped, not shifted along with the label.
            label = Find<TextBlock>(window, "TimelineHour_12");
            scroll.Offset = new Vector(Canvas.GetLeft(label) - scroll.Viewport.Width + label.Bounds.Width / 4, 0); Settle(window);
            var right = label.TranslatePoint(new Point(label.Bounds.Width, 0), scroll)!.Value.X;
            Assert.True(right <= scroll.Viewport.Width);
            Assert.True(((TranslateTransform)label.RenderTransform!).X < 0);
            Assert.Equal(originalLeft, Canvas.GetLeft(card)); Assert.Equal(originalWidth, card.Width);
            Assert.True(scroll.ClipToBounds);
            if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is { Length: > 0 } previews)
            {
                Directory.CreateDirectory(previews); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(previews, "timeline-viewport.png"));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AnExplicitScrollPositionIsHonouredWhenOpeningTheDay()
    {
        var model = Model();
        model.TimelineOffset = 12 * model.TimelinePixelsPerHour;
        var window = Open(model);
        try { Assert.Equal(12 * model.TimelinePixelsPerHour, Find<ScrollViewer>(window, "TimelineScroll").Offset.X, 1); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LateRecordedTimeIsVisibleWhenOpeningTheDay()
    {
        var model = Model(); model.Engine.Sessions.Add(new("2026-10-21", "Account one", "22:30:00", "recorded", "30"));
        var window = Open(model);
        try
        {
            var scroll = Find<ScrollViewer>(window, "TimelineScroll"); var card = Find<Border>(window, "TimelineEntry0");
            var left = card.TranslatePoint(default, scroll)!.Value.X;
            Assert.InRange(left, 0, scroll.Viewport.Width - card.Width);
            var now = Find<Border>(window, "NowMarker"); Assert.Equal(2, now.Width);
        }
        finally { window.Close(); }
    }
}
