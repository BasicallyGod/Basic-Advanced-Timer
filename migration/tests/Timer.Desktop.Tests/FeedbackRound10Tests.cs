using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The first feedback from using the Store build all day: the note dot on narrow blocks, the meeting's end following its start, and Tab taking the top account in the card.
public sealed class FeedbackRound10Tests
{
    private static DateTime Day => new(2026, 10, 9);
    private static SessionRow Row(string start, string minutes, string account = "Account one") => new(Day.ToString("yyyy-MM-dd"), account, start, "added", minutes);
    private static TimerModel Model(out TrackingEngine engine)
    {
        engine = new TrackingEngine(clock: () => Day.AddHours(16));
        engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("11:00:00", "60", "Account two") });
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup(); return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window); }
    private static void Type(Window window, string name, string text) { Find<TextBox>(window, name).Text = text; Dispatcher.UIThread.RunJobs(); }
    private static void Key(Control target, Key key) => target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    [AvaloniaFact]
    public void TheNoteDotIsCentredOnANarrowBlockAndStaysAtTheRightEdgeOnAWiderOne()
    {
        var model = Model(out var engine); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            foreach (var index in new[] { 0, 1 })
            {
                model.SelectBlock(new[] { engine.Sessions[index] }, false); Settle(window);
                var box = Find<TextBox>(window, "SelectedNote"); box.Text = "Called the client"; Key(box, Avalonia.Input.Key.Enter);
            }
            model.SelectBlock(Array.Empty<SessionRow>(), false); Settle(window);
            Ellipse DotOf(string block) => Find<Border>(window, block).GetVisualDescendants().OfType<Ellipse>().Single(e => e.Name == "NoteDot");
            Assert.Equal(HorizontalAlignment.Center, DotOf("TimelineEntry0").HorizontalAlignment); // Half an hour is a sliver.
            Assert.Equal(HorizontalAlignment.Right, DotOf("TimelineEntry1").HorizontalAlignment); // An hour has room for it at the edge.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ChangingAMeetingsStartMovesItsEndToKeepItsLengthOnceTheBoxIsLeft()
    {
        var model = Model(out _); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddMeeting");
            var start = Find<TextBox>(window, "MeetingStart"); var end = Find<TextBox>(window, "MeetingEnd");
            Assert.Equal("12:00", start.Text); Assert.Equal("12:30", end.Text);
            start.Text = "16:00"; Dispatcher.UIThread.RunJobs();
            Assert.Equal("12:30", end.Text); // Not while it is still being typed.
            start.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal("16:30", end.Text);
            start.Text = "17:15"; Key(start, Avalonia.Input.Key.Enter); Dispatcher.UIThread.RunJobs();
            Assert.Equal("17:45", end.Text); // Enter does the same, and it is the length that is kept each time.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AMeetingMovedToTheEndOfTheDayStopsItsEndAtMidnight()
    {
        var model = Model(out _); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddMeeting");
            var start = Find<TextBox>(window, "MeetingStart"); start.Text = "23:50"; Key(start, Avalonia.Input.Key.Enter); Dispatcher.UIThread.RunJobs();
            Assert.Equal("23:59", Find<TextBox>(window, "MeetingEnd").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TabInTheNotesCardsBoxTakesTheTopAccountThatMatchesTheHash()
    {
        var model = Model(out _); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddNote");
            Type(window, "NoteField", "Draft the plan #Acc"); Settle(window);
            Assert.Equal(2, window.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("Suggest_") == true));
            Key(Find<TextBox>(window, "NoteField"), Avalonia.Input.Key.Tab); Settle(window);
            Assert.Equal("Draft the plan", Find<TextBox>(window, "NoteField").Text); // The first match went into the account chip and the word is gone.
            Click(window, "SubmitNote");
            Assert.Equal("Account one", model.Notes.AccountOf(model.Notes.All.Single()));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheNotesCardSwitchesBetweenTheDaysNotesAndItsMeetingsAndOpeningAFormFollowsIt()
    {
        var model = Model(out _); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.Notes.AddNote("Tidy the shared drive", null, null); model.Notes.AddMeeting(model.SelectedDate, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Team sync", null, RepeatChoice.None); Settle(window);
            bool Has(string prefix) => window.GetVisualDescendants().OfType<Button>().Any(b => b.Name?.StartsWith(prefix) == true);
            Assert.False(model.NotesCardShowsMeetings);
            Assert.True(Has("NoteDone_")); // Notes first: the note (with its tick), not the meeting.
            Click(window, "CardKindMeetings");
            Assert.True(model.NotesCardShowsMeetings); Assert.True(Has("NoteEdit_")); Assert.False(Has("NoteDone_"));
            Click(window, "CardKindNotes");
            Assert.True(Has("NoteDone_"));
            Click(window, "AddMeeting"); // Adding a meeting turns the switch to meetings, so what is added shows.
            Assert.True(model.NotesCardShowsMeetings);
            Click(window, "AddNote");
            Assert.False(model.NotesCardShowsMeetings);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AnEmptyDayTellsYouWhichKindIsMissing()
    {
        var model = Model(out _); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.Equal("No notes for this day. Add one.", Find<TextBlock>(window, "NotesEmpty").Text);
            Click(window, "CardKindMeetings");
            Assert.Equal("No meetings this day. Add one.", Find<TextBlock>(window, "NotesEmpty").Text);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void TheResetPositionSumsPutThePillAtTheTopRightAndSlideAWindowBackInsideTheWorkArea()
    {
        var area = new ScreenGuard.Box(0, 0, 1920, 1040); // A laptop screen less its taskbar.
        Assert.Equal(new ScreenGuard.Box(1920 - 100 - 24, 24, 100, 300), ScreenGuard.TopRight(area, 100, 300, 24));
        // On a second monitor to the left, the area starts at a negative x, and the pill goes by that area's own edges.
        var left = new ScreenGuard.Box(-1920, 0, 1920, 1080);
        Assert.Equal(new ScreenGuard.Box(-124, 24, 100, 300), ScreenGuard.TopRight(left, 100, 300, 24));
        // A window left on a screen that is gone slides in without changing its size.
        Assert.Equal(new ScreenGuard.Box(820, 320, 1100, 720), ScreenGuard.SlideInside(area, new ScreenGuard.Box(2500, 1800, 1100, 720)));
        Assert.Equal(new ScreenGuard.Box(0, 0, 400, 300), ScreenGuard.SlideInside(area, new ScreenGuard.Box(-3000, -900, 400, 300)));
        // One bigger than the area keeps to its top left corner.
        Assert.Equal(new ScreenGuard.Box(0, 0, 2400, 1200), ScreenGuard.SlideInside(area, new ScreenGuard.Box(500, 500, 2400, 1200)));
        // A window already inside stays where it is.
        Assert.Equal(new ScreenGuard.Box(300, 200, 400, 300), ScreenGuard.SlideInside(area, new ScreenGuard.Box(300, 200, 400, 300)));
    }

    [AvaloniaFact]
    public void ResetPositionAndTheOffScreenRescueLeaveThePillShownAndAreHarmlessWhereThereIsNoMonitorToAskAbout()
    {
        var model = Model(out _);
        var coordinator = new WindowCoordinator(model, () => { });
        coordinator.Pill.Show();
        try
        {
            Assert.True(ScreenGuard.IsOnAnyMonitor(coordinator.Pill)); // Headless there is no handle to ask about, so it counts as reachable.
            coordinator.RescueOffScreen();
            coordinator.Pill.OpenDashboard!();
            coordinator.RescueOffScreen();
            coordinator.ResetPosition();
            Assert.True(coordinator.Pill.IsVisible); Assert.False(coordinator.Dashboard.IsVisible);
            coordinator.Hide(); coordinator.ShowTimer();
            Assert.True(coordinator.IsShown);
        }
        finally { coordinator.Pill.Close(); coordinator.Dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheRecordingBlocksGreenRingIsTheSameWidthAllRoundAndOutsideTheBlockLikeTheSelectionRing()
    {
        var model = Model(out _); model.ToggleAccount("Account one"); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineRecording");
            Assert.Equal(new Thickness(0), card.BorderThickness); // Not a border, which left the left side bare.
            Assert.Equal(1, card.BoxShadow.Count); var ring = card.BoxShadow[0]; Assert.Equal(3, ring.Spread); Assert.Equal(0, ring.Blur);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void BreakTimeIsNeverNegativeWhenTheClockMovesBetweenTheTwoReadingsOfTheRunningBlock()
    {
        var ticks = 0;
        var engine = new TrackingEngine(clock: () => Day.AddHours(10).AddMilliseconds(ticks++ * 3)); // Every reading is a little later than the last.
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        model.ToggleAccount("Account one");
        for (var i = 0; i < 20; i++) { var breaks = model.BreakTimeOn(Day); Assert.True(breaks >= TimeSpan.Zero); Assert.Equal("00:00", model.Format(breaks)); }
    }

    [AvaloniaFact]
    public void ThePrimaryButtonsOnTheNotesFormsHaveTheAccentsInkNotTheGlobalTextColour()
    {
        var model = Model(out _); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var ink = Avalonia.Media.Color.Parse(model.CurrentTheme.Colour("AccentInk")); Assert.Equal(Avalonia.Media.Colors.White, ink); // On the Light accent it is white.
            Avalonia.Media.Color WordsOf(Button b) => ((Avalonia.Media.ISolidColorBrush)b.GetVisualDescendants().OfType<TextBlock>().First().Foreground!).Color;
            Click(window, "AddNote"); Assert.Equal(ink, WordsOf(Find<Button>(window, "SubmitNote")));
            Click(window, "AddMeeting"); Assert.Equal(ink, WordsOf(Find<Button>(window, "SubmitNote")));
            Click(window, "NotesTab"); Assert.Equal(ink, WordsOf(Find<Button>(window, "PageSubmit")));
        }
        finally { window.Close(); }
    }
}
