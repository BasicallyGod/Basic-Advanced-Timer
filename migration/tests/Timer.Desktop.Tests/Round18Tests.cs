using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 18: a pin on the timeline's ruler for each timed note, and a dashed guide line that follows the pointer along the day.
public sealed class Round18Tests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TimerModel Model(bool withNotes = true)
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)), new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        if (!withNotes) return model;
        model.Notes.AddNote("Send the revised budget", "Account one", Day.AddHours(8));          // Late.
        model.Notes.AddNote("Follow up on the new brief", "Account two", Day.AddHours(15.5));    // Coming.
        model.Notes.AddNote("Tidy the shared drive", null, null);                                // No time, so no pin.
        model.Notes.AddNote("Already done", null, Day.AddHours(9)); model.Notes.Toggle(model.Notes.All.Single(n => model.Notes.Text(n) == "Already done").Id);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(11), TimeSpan.FromHours(12), "Client call", null, RepeatChoice.None); // A meeting has no pin either.
        return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static bool Has(Control root, string name) => root.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name);
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static long IdOf(TimerModel model, string text) => model.Notes.All.Single(n => model.Notes.Text(n) == text).Id;
    private static Color Brush(Avalonia.Controls.Shapes.Shape shape) => ((ISolidColorBrush)shape.Fill!).Color;
    private static Color Theme(Control c, string key) => ((ISolidColorBrush)(c.TryFindResource(key, c.ActualThemeVariant, out var r) ? r! : throw new InvalidOperationException(key))).Color;

    [Fact]
    public void OnlyOpenNotesWithAReminderOnTheDayGetAPin()
    {
        var model = Model(); var pinned = model.Notes.PinnedOn(Day);
        Assert.Equal(new[] { "Send the revised budget", "Follow up on the new brief" }, pinned.Select(model.Notes.Text)); // In time order: not the undated, the ticked off or the meeting.
        Assert.Empty(model.Notes.PinnedOn(Day.AddDays(1)));
    }

    [AvaloniaFact]
    public void PinsHangFromTheRulerAtTheirTimesRedWhenLateAndAccentWhileComingAndTheBandMakesRoom()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var late = Find<Canvas>(window, "TimelinePin_" + IdOf(model, "Send the revised budget")); var coming = Find<Canvas>(window, "TimelinePin_" + IdOf(model, "Follow up on the new brief"));
            Assert.False(Has(window, "TimelinePin_" + IdOf(model, "Tidy the shared drive"))); Assert.False(Has(window, "TimelinePin_" + IdOf(model, "Already done")));
            var pph = model.TimelinePixelsPerHour;
            Assert.Equal(ReviewViews.TimelineGutter + 8 * pph, Canvas.GetLeft(late) + 10, .5); Assert.Equal(ReviewViews.TimelineGutter + 15.5 * pph, Canvas.GetLeft(coming) + 10, .5);
            var lateDot = late.Children.OfType<Avalonia.Controls.Shapes.Ellipse>().Last(); var comingDot = coming.Children.OfType<Avalonia.Controls.Shapes.Ellipse>().Last();
            Assert.Equal(Theme(lateDot, "Overtime"), Brush(lateDot)); Assert.Equal(Theme(comingDot, "Accent"), Brush(comingDot));
            // The meeting row sits lower to make room for the pins, as the quiet day below has it.
            var lane = Find<Border>(window, "MeetingLane");
            Assert.Equal(28 + 14 + TimelineGuide.PinRoom, Canvas.GetTop(lane), .5);
            Assert.Contains("late", Avalonia.Automation.AutomationProperties.GetName(late)); Assert.DoesNotContain("late", Avalonia.Automation.AutomationProperties.GetName(coming));
        }
        finally { window.Close(); }
        var quiet = Model(false); quiet.Notes.AddMeeting(Day, TimeSpan.FromHours(11), TimeSpan.FromHours(12), "Client call", null, RepeatChoice.None);
        var other = new DashboardWindow(quiet); other.Show(); Settle(other);
        try { Assert.Equal(28 + 14, Canvas.GetTop(Find<Border>(other, "MeetingLane")), .5); } finally { other.Close(); }
    }

    // A point on the timeline's canvas, in the window's coordinates, with the timeline scrolled so that point is in view.
    private static Point At(DashboardWindow window, TimerModel model, double hour, double y = 120)
    {
        var scroll = Find<ScrollViewer>(window, "TimelineScroll"); var canvas = Find<Canvas>(window, "TimelineCanvas");
        var x = ReviewViews.TimelineGutter + hour * model.TimelinePixelsPerHour;
        scroll.Offset = new Vector(Math.Max(0, x - scroll.Viewport.Width / 2), 0); Settle(window);
        return canvas.TranslatePoint(new Point(x, y), window)!.Value;
    }

    [AvaloniaFact]
    public void TheGuideFollowsThePointerWithATimeChipRingsWhatLiesUnderItAndStepsAsideForAPressOrALeave()
    {
        var model = Model(); Assert.Null(model.Notes.AddNote("x", null, null)); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var line = Find<Avalonia.Controls.Shapes.Line>(window, "TimelineGuideLine"); var chip = Find<Border>(window, "TimelineGuideChip"); var dot = Find<Avalonia.Controls.Shapes.Ellipse>(window, "TimelineGuideDot");
            Assert.False(line.IsVisible); // Nothing until the pointer is on the timeline.
            window.MouseMove(At(window, model, 12.67)); Settle(window);
            Assert.True(line.IsVisible && chip.IsVisible && dot.IsVisible);
            Assert.Equal("12:40", ((TextBlock)chip.Child!).Text);
            Assert.NotEmpty(line.StrokeDashArray!); // Dashed, where the now line is solid.
            Assert.True(Has(window, "NowChip")); // The solid now chip is still its own thing.
            // The line is where the pointer is.
            var canvas = Find<Canvas>(window, "TimelineCanvas");
            Assert.Equal(ReviewViews.TimelineGutter + 12.6667 * model.TimelinePixelsPerHour, Canvas.GetLeft(line) + 1, 1.5);
            // Over a block, a ring goes round it.
            var rings = canvas.Children.OfType<Border>().Where(b => Equals(b.Tag, "GuideRing")).ToList(); Assert.All(rings, r => Assert.False(r.IsVisible && r.Width <= 0));
            window.MouseMove(new Point(2, 2)); Settle(window); // Off the timeline: it goes.
            Assert.False(line.IsVisible); Assert.False(chip.IsVisible);
            // A press (dragging a block) has the pointer's attention, so the guide steps aside.
            var there = At(window, model, 12.67); window.MouseMove(there); Settle(window); Assert.True(line.IsVisible);
            window.MouseDown(there, MouseButton.Left); window.MouseMove(there + new Vector(10, 0), RawInputModifiers.LeftMouseButton); Assert.False(line.IsVisible);
            window.MouseUp(there + new Vector(10, 0), MouseButton.Left);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HoveringBlocksRingsThemWithoutDisturbingTheCanvasChildrenItIsWalking()
    {
        // The rings are added to the canvas the first time they are needed, so the walk over its children must not be the live list.
        var model = Model(false);
        Assert.True(model.AddEntry("Account one", Day.AddHours(7), Day.AddHours(8), "")); model.Notes.AddMeeting(Day, TimeSpan.FromHours(7.5), TimeSpan.FromHours(8.5), "Stand-up", null, RepeatChoice.None);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var canvas = Find<Canvas>(window, "TimelineCanvas");
            window.MouseMove(At(window, model, 7.75)); Settle(window); // Under the block and the meeting.
            var rings = canvas.Children.OfType<Border>().Where(b => Equals(b.Tag, "GuideRing") && b.IsVisible).ToList();
            Assert.Equal(2, rings.Count);
            window.MouseMove(At(window, model, 7.25)); Settle(window); // Under the block alone: the meeting's ring goes.
            Assert.Single(canvas.Children.OfType<Border>().Where(b => Equals(b.Tag, "GuideRing") && b.IsVisible));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NearAPinTheGuideSnapsToItTheNoteAppearsBesideTheLineAndPressingItOpensTheNote()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var lateId = IdOf(model, "Send the revised budget"); var late = Find<Canvas>(window, "TimelinePin_" + lateId);
            var line = Find<Avalonia.Controls.Shapes.Line>(window, "TimelineGuideLine"); var chip = Find<Border>(window, "TimelineGuideChip"); var card = Find<Border>(window, "TimelineGuideNote");
            // A few pixels off the pin: the guide snaps to it.
            window.MouseMove(At(window, model, 8.0) + new Vector(4, 0)); Settle(window);
            Assert.Equal("08:00", ((TextBlock)chip.Child!).Text); Assert.Equal(Canvas.GetLeft(late) + 10, Canvas.GetLeft(line) + 1, .5);
            Assert.True(card.IsVisible); var words = string.Join(" ", card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains("Send the revised budget", words); Assert.Contains("Today, 08:00", words); Assert.Contains("2h 00m late", words);
            Assert.Equal(TimelineGuide.ActiveDot, late.Children.OfType<Avalonia.Controls.Shapes.Ellipse>().Last().Width); // The pin swells while the guide is on it.
            // Further away it lets go of the pin.
            window.MouseMove(At(window, model, 8.0) + new Vector(24, 0)); Settle(window);
            Assert.False(card.IsVisible); Assert.Equal(TimelineGuide.RestDot, late.Children.OfType<Avalonia.Controls.Shapes.Ellipse>().Last().Width);
            // With tooltips switched off the words stay in the Notes card, not beside the line.
            model.SetTooltips(false); Settle(window); // The timeline is drawn again, so its parts are found again.
            window.MouseMove(At(window, model, 8.0) + new Vector(2, 0)); Settle(window); Assert.False(Find<Border>(window, "TimelineGuideNote").IsVisible); Assert.True(Find<Border>(window, "TimelineGuideChip").IsVisible);
            model.SetTooltips(true); Settle(window); late = Find<Canvas>(window, "TimelinePin_" + lateId); At(window, model, 8.0);
            // Pressing the pin opens the note in the Notes page.
            var onPin = late.TranslatePoint(new Point(10, 14), window)!.Value; window.MouseDown(onPin, MouseButton.Left); window.MouseUp(onPin, MouseButton.Left); Settle(window);
            Assert.Equal("Send the revised budget", Find<TextBox>(window, "PageNoteField").Text);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Editing a note");
        }
        finally { window.Close(); }
    }

    // Pictures for looking at the layout: TIMER_PREVIEW_DIR names the folder.
    [AvaloniaFact]
    public void WriteRound18Pictures()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = Model(); model.SetTheme(theme); var window = new DashboardWindow(model); window.Show(); Settle(window);
            try
            {
                using (var rest = window.CaptureRenderedFrame()) rest?.Save(Path.Combine(folder, $"r18-pins-{theme}.png"));
                window.MouseMove(At(window, model, 12.67)); Settle(window);
                using (var free = window.CaptureRenderedFrame()) free?.Save(Path.Combine(folder, $"r18-guide-free-{theme}.png"));
                window.MouseMove(At(window, model, 8.0) + new Vector(3, 0)); Settle(window);
                using (var snapped = window.CaptureRenderedFrame()) snapped?.Save(Path.Combine(folder, $"r18-guide-pin-{theme}.png"));
            }
            finally { window.Close(); }
        }
    }
}
