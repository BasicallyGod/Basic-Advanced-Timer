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
            var scroll = Find<ScrollViewer>(window, "TimelineScroll"); var canvas = Assert.IsType<Canvas>(scroll.Content);
            var card = Find<Border>(window, "TimelineEntry0"); var originalTop = Canvas.GetTop(card); var originalHeight = card.Height;
            var label = Find<TextBlock>(window, "TimelineHour_10");
            scroll.Offset = new Vector(0, Canvas.GetTop(label) + canvas.Margin.Top + 12); Settle(window);
            Assert.True(label.TranslatePoint(default, scroll)!.Value.Y >= 0);
            Assert.True(((TranslateTransform)label.RenderTransform!).Y > 0);
            Assert.Equal(originalTop, Canvas.GetTop(card)); Assert.Equal(originalHeight, card.Height);
            Assert.True(card.TranslatePoint(default, scroll)!.Value.Y < 0); // The block is clipped, not shifted down with the label.
            label = Find<TextBlock>(window, "TimelineHour_12");
            scroll.Offset = new Vector(0, Canvas.GetTop(label) + canvas.Margin.Top - scroll.Viewport.Height + 4); Settle(window);
            var bottom = label.TranslatePoint(new Point(0, label.Bounds.Height), scroll)!.Value.Y;
            Assert.True(bottom <= scroll.Viewport.Height);
            Assert.True(((TranslateTransform)label.RenderTransform!).Y < 0);
            Assert.Equal(originalTop, Canvas.GetTop(card)); Assert.Equal(originalHeight, card.Height);
            Assert.True(scroll.ClipToBounds);
            if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is { Length: > 0 } previews)
            {
                Directory.CreateDirectory(previews); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(previews, "timeline-viewport.png"));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void InitialScrollFindsAnEarlyMeetingAndHonoursAnExplicitPosition()
    {
        var model = Model(); model.AddDayNote(Day, "Early review 06:00 - 06:30");
        var id = model.AllDayNotes.Single().Id;
        var window = Open(model);
        try
        {
            var scroll = Find<ScrollViewer>(window, "TimelineScroll"); var meeting = Find<Panel>(window, "TimelineMeeting_" + id);
            var top = meeting.TranslatePoint(default, scroll)!.Value.Y;
            Assert.InRange(top, 0, scroll.Viewport.Height - meeting.Height);
            Assert.Empty(model.Engine.Sessions); // Choosing the view never records the planned meeting.
        }
        finally { window.Close(); }
        model.TimelineOffset = 12 * model.TimelinePixelsPerHour;
        window = Open(model);
        try { Assert.Equal(12 * model.TimelinePixelsPerHour, Find<ScrollViewer>(window, "TimelineScroll").Offset.Y, 1); }
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
            var top = card.TranslatePoint(default, scroll)!.Value.Y;
            Assert.InRange(top, 0, scroll.Viewport.Height - card.Height);
            var now = Find<Border>(window, "NowMarker"); Assert.Equal(2, now.Height);
        }
        finally { window.Close(); }
    }
}
