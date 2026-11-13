using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

// The display look for the timeline (the board "The timeline as a display", option A): a setting, lit blocks with segment words, a note dot, the recording ring
// inside its block, segment hours and one honeycomb over the trough.
public sealed class VfdTimelineTests
{
    private static readonly DateTime DemoNow = new(2026, 10, 14, 14, 15, 7);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static TimerModel Demo(string theme = "Dark") { var model = TimerModel.CreateDemo(() => DemoNow); model.CompleteSetup(); model.SetTheme(theme); return model; }

    [Fact]
    public void TheDisplayLookIsASettingThatIsOffUntilChosen()
    {
        var model = Demo(); Assert.False(model.DisplayTimeline);
        model.SetDisplayTimeline(true); Assert.True(model.DisplayTimeline);
        model.SetDisplayTimeline(false); Assert.False(model.DisplayTimeline);
    }

    [AvaloniaFact]
    public void OnTheDisplayTheTimelineIsDrawnInSegmentsUnderOneMesh()
    {
        var model = Demo(); model.SetDisplayTimeline(true); if (!model.IsTracking) model.ToggleAccount(model.Accounts[0].Name);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var canvas = window.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "TimelineCanvas");
            // Only the lit fills go through the honeycomb; the words sit whole on top of them.
            var phosphor = canvas.GetVisualDescendants().OfType<Border>().First(b => b.Name == "BlockPhosphor");
            Assert.Same(Honeycomb.Brush, phosphor.OpacityMask);
            Assert.DoesNotContain(canvas.GetVisualDescendants().OfType<SegmentText>(), t => t.GetVisualAncestors().Any(a => a is Control { OpacityMask: not null }));
            Assert.IsType<SegmentText>(canvas.Children.OfType<Control>().First(c => c.Name == "TimelineHour_9"));
            Assert.Contains(canvas.GetVisualDescendants().OfType<SegmentText>(), s => s.Name == "BlockWords");
            // The block being recorded wears its ring inside its own edge.
            var recording = canvas.Children.OfType<Border>().Single(b => b.Name == "TimelineRecording");
            Assert.True(recording.BoxShadow[0].IsInset);
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { Directory.CreateDirectory(folder); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, "vfd-timeline-dark.png")); }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RemindersAreTagsAboveTheLineAndReadAlongTheRulerWhenPointedAt()
    {
        // Less motion, so the ruler's fade is instant here: headless, nothing would tick it on.
        Motion.Override = true;
        try { RulerReadout(); } finally { Motion.Override = null; }
    }
    private void RulerReadout()
    {
        var model = Demo(); model.SetDisplayTimeline(true);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var canvas = window.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "TimelineCanvas");
            var pins = canvas.Children.OfType<Canvas>().Where(c => c.Name?.StartsWith("TimelinePin_") == true).ToList();
            Assert.NotEmpty(pins);
            Assert.All(pins, p => Assert.Equal(0, Canvas.GetTop(p))); // In the hour row, above the line.
            // Point at the tag holding several reminders: the hours switch off and the first of them reads along the ruler, with its count and track.
            var shared = pins.First(p => p.GetVisualDescendants().OfType<SegmentText>().Any());
            var at = shared.TranslatePoint(new Point(10, 12), window)!.Value;
            window.MouseMove(at); Settle(window);
            var readout = canvas.Children.OfType<StackPanel>().Single(c => c.Name == "TimelineReadout");
            Assert.Equal(1, readout.Opacity);
            // The words never sit on their own tag: they start past it or end before it.
            var tagX = Canvas.GetLeft(shared) + 10; Assert.True(Canvas.GetLeft(readout) >= tagX + 7 || Canvas.GetLeft(readout) + readout.Bounds.Width <= tagX - 7);
            Assert.All(canvas.Children.OfType<Control>().Where(c => c.Name?.StartsWith("TimelineHour_") == true), c => Assert.Equal(0, c.Opacity));
            var count = readout.Children.OfType<SegmentText>().First(); Assert.StartsWith("1/", count.Text);
            var pieces = canvas.Children.OfType<Canvas>().Single(c => c.Name == "TimelineReadoutTrack").Children.Count;
            Assert.InRange(pieces, 2, VfdReadout.TrackPieces);
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { Directory.CreateDirectory(folder); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, "vfd-timeline-readout.png")); }
            // The wheel steps through the tag's reminders and the day does not slide under it; a notch up goes back, wrapping at the ends.
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().Single(c => c.Name == "TimelineScroll"); var offset = scroll.Offset.X;
            string Count() => canvas.Children.OfType<StackPanel>().Single(c => c.Name == "TimelineReadout").Children.OfType<SegmentText>().First().Text;
            var total = Count().Split('/')[1];
            window.MouseWheel(at, new Vector(0, -1)); Settle(window);
            Assert.Equal("2/" + total, Count()); Assert.Equal(offset, scroll.Offset.X);
            window.MouseWheel(at, new Vector(0, 1)); window.MouseWheel(at, new Vector(0, 1)); Settle(window);
            Assert.Equal(total + "/" + total, Count()); Assert.Equal(offset, scroll.Offset.X);
            window.MouseWheel(at, new Vector(0, -1)); Settle(window);
            Assert.Equal("1/" + total, Count());
                        // Pressing a shared tag opens the reminder the ruler is reading, with no list.
            window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Settle(window);
            Assert.DoesNotContain(canvas.Children.OfType<Border>(), b => b.Name == "TimelineReminderList");
            Assert.NotNull(model.SelectedNoteId);
            // Moving off the ruler puts the hours back.
            window.MouseMove(new Point(5, 5)); Settle(window);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OnTheDisplayTheWeekIsDrawnAsPanelsAndADayStillOpensTheTimeline()
    {
        var model = Demo(); model.SetDisplayTimeline(true);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var tab = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WeekOverviewTab");
            tab.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Assert.Contains(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "WeekTotals");
            Assert.Contains(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "WeekVfdChart");
            Assert.Contains(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "WeekVfdAccounts");
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            var today = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "WeekDay_" + model.TodayKey.ToString("yyyyMMdd"));
            var at = today.TranslatePoint(new Point(today.Bounds.Width / 2, today.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at); Settle(window);
            if (folder is not null) { Directory.CreateDirectory(folder); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, "vfd-week-dark.png")); }
            today.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Assert.Equal(model.TodayKey, model.SelectedDate);
            // The table keeps its plain type.
            model.SetWeekAsTable(true); Settle(window);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "WeekVfdChart");
        }
        finally { window.Close(); }
    }
}
