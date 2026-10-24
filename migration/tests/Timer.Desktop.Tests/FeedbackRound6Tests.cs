using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The second round of feedback on the experimental build: the timeline's spacing, the now line, the breaks on the day bar, the bell and the reminder light.
public sealed class FeedbackRound6Tests
{
    private static DateTime Day => new(2026, 10, 9);
    private static SessionRow Row(string start, string minutes, string account = "Account one") => new(Day.ToString("yyyy-MM-dd"), account, start, "added", minutes);
    private static TimerModel Model(bool meeting)
    {
        var engine = new TrackingEngine(clock: () => Day.AddHours(16));
        engine.Sessions.AddRange(new[] { Row("09:00:00", "60"), Row("10:00:00", "60", "Account two"), Row("12:00:00", "30", "Break") });
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        if (meeting) model.Notes.AddMeeting(Day, TimeSpan.FromHours(14), TimeSpan.FromHours(15), "Review", null, RepeatChoice.None);
        return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window); }
    private static double Top(Control c) => Canvas.GetTop(c);

    [AvaloniaFact]
    public void TheMeetingRowSitsWellBelowTheAxisAndTheBlocksBelowTheRowAndTheNowLineRunsUpToTheAxis()
    {
        var model = Model(true); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var axis = Find<Border>(window, "TimelineAxis"); var meeting = Find<Grid>(window, "TimelineMeeting" + model.Notes.All.Single().Id); var block = Find<Border>(window, "TimelineEntry0");
            Assert.True(Top(meeting) - Top(axis) >= 12); // Room between the hours and the meetings,
            Assert.True(Top(block) - (Top(meeting) + meeting.Height) >= 8); // and between the meetings and the blocks.
            Assert.Equal(Top(axis), Top(Find<Border>(window, "NowMarker"))); // The now line starts at the axis.
            var lane = Find<Border>(window, "MeetingLane"); Assert.Null(lane.Child); // And the row holds no hint text.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WithNoMeetingsTheBlocksTakeTheWholeHeightUnderTheAxis()
    {
        var withMeeting = Model(true); var without = Model(false);
        var a = new DashboardWindow(withMeeting); a.Show(); Settle(a); var tall = 0.0; var shortHeight = Find<Border>(a, "TimelineEntry0").Height; a.Close();
        var b = new DashboardWindow(without); b.Show(); Settle(b);
        try
        {
            var block = Find<Border>(b, "TimelineEntry0"); tall = block.Height;
            Assert.True(tall > shortHeight + 40); Assert.True(Top(block) - Top(Find<Border>(b, "TimelineAxis")) >= 12);
            Assert.Equal(Top(Find<Border>(a.GetType() == null ? b : b, "TimelineEntry0")) + tall, 176, 1); // They end where they always did, above the now chip.
        }
        finally { b.Close(); }
    }

    [AvaloniaFact]
    public void NeighbouringBlocksAreFarEnoughApartForTheSelectionRing()
    {
        var model = Model(false); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var one = Find<Border>(window, "TimelineEntry0"); var two = Find<Border>(window, "TimelineEntry1");
            Assert.True(Canvas.GetLeft(two) - (Canvas.GetLeft(one) + one.Width) >= 6); // A 3 pixel ring on each side does not meet.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheDayBarHasABreaksSegmentInTheBreakColour()
    {
        var model = Model(false); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var bar = Find<Border>(window, "DayBar"); var breaks = bar.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DayBarBreaks");
            Assert.Contains("barBreaks", breaks.Classes); Assert.True(breaks.Bounds.Width > 0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheReminderPopupsPresetTimesAreATwoByTwoGrid()
    {
        var model = Model(false); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddNote"); Click(window, "FormBell");
            var a = Find<Button>(window, "ReminderTime9"); var b = Find<Button>(window, "ReminderTime12"); var c = Find<Button>(window, "ReminderTime15"); var d = Find<Button>(window, "ReminderTime17");
            Point At(Control x) => x.TranslatePoint(default, window)!.Value;
            Assert.Equal(At(a).Y, At(b).Y, 1); Assert.Equal(At(c).Y, At(d).Y, 1); Assert.True(At(c).Y > At(a).Y); Assert.Equal(At(a).X, At(c).X, 1); Assert.True(At(b).X > At(a).X);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheBellSitsNextToTheGearAndAnotherPressOnItClosesTheCard()
    {
        var model = Model(false); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var bell = Find<Button>(window, "Notifications"); var gear = Find<Button>(window, "Settings"); var today = Find<Button>(window, "GoToToday");
            double X(Control c) => c.TranslatePoint(default, window)!.Value.X;
            Assert.True(X(today) < X(bell) && X(bell) < X(gear)); // Today, the bell, then the gear.
            var popup = Find<Avalonia.Controls.Primitives.Popup>(window, "NotificationsPopup");
            Click(window, "Notifications"); Assert.True(popup.IsOpen); Click(window, "Notifications"); Assert.False(popup.IsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePillsReminderLightIsAlwaysThereGreyWhenNothingIsWaitingAndBlueWhenAReminderIsDue()
    {
        var model = Model(false); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var light = Find<Border>(pill, "PillReminderLight"); Assert.True(light.IsVisible);
            var grey = ((Avalonia.Media.ISolidColorBrush)light.Background!).Color;
            model.Notes.AddNote("Send the budget", null, Day.AddHours(8)); Dispatcher.UIThread.RunJobs();
            var blue = ((Avalonia.Media.ISolidColorBrush)Find<Border>(pill, "PillReminderLight").Background!).Color;
            Assert.NotEqual(grey, blue); Assert.True(blue.B > blue.R); // Blue, where the grey was.
        }
        finally { pill.Close(); }
    }
}
