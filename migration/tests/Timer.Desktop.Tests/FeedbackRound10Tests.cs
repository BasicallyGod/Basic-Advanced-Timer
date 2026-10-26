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
}
