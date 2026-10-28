using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Notes and meetings: the model on the shared notes storage, the Notes card with its forms and the reminder pop-up, and the meetings on the timeline.
public sealed class NotesTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TimerModel Model(Func<DateTime>? clock = null)
    {
        var engine = new TrackingEngine(clock: clock ?? (() => Day.AddHours(10)));
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup(); return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    // The notes card no longer counts its notes in its title, so the rows it lists are counted.
    private static int Listed(Window window) => window.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("noteRow"));
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window); }
    private static void Type(Window window, string name, string text) { Find<TextBox>(window, name).Text = text; Dispatcher.UIThread.RunJobs(); }

    // ---- The model ----

    [Fact]
    public void ANoteWithNoReminderIsUndatedAndShowsOnEveryDayWhileOneWithAReminderShowsOnItsDay()
    {
        var model = Model(); var notes = model.Notes;
        Assert.Null(notes.AddNote("Tidy the shared drive", null, null));
        Assert.Null(notes.AddNote("Send the budget", "Account two", Day.AddDays(1).AddHours(14)));
        Assert.Single(notes.ForDay(Day)); Assert.Equal(2, notes.ForDay(Day.AddDays(1)).Count);
        var dated = notes.All.Single(n => n.HasDate); Assert.Equal("Account two", notes.AccountOf(dated)); Assert.Equal("Send the budget", notes.Text(dated));
        Assert.Equal(Day.AddDays(1).AddHours(14), NotesModel.ReminderOf(dated));
        Assert.False(notes.All.Single(n => !n.HasDate).HasDate); Assert.Null(NotesModel.ReminderOf(notes.All.Single(n => !n.HasDate)));
    }

    [Fact]
    public void ABlankNoteAndABackwardsMeetingAreRefusedAndAMeetingIsNeverTrackedTime()
    {
        var model = Model(); var notes = model.Notes;
        Assert.NotNull(notes.AddNote("  ", null, null)); Assert.NotNull(notes.AddMeeting(Day, TimeSpan.FromHours(10), TimeSpan.FromHours(9), "Review", null, RepeatChoice.None));
        Assert.NotNull(notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), " ", null, RepeatChoice.None));
        Assert.Empty(notes.All);
        Assert.Null(notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Review", null, RepeatChoice.None));
        Assert.Single(notes.MeetingsOn(Day)); Assert.Empty(model.Engine.Sessions); Assert.Equal(TimeSpan.Zero, model.Engine.Totals(Day.AddHours(11)).Allocated);
    }

    [Fact]
    public void ARepeatingMeetingIsMadeAboutEightWeeksAheadAndOneChangeTouchesOnlyThatDayUnlessAllLaterIsChosen()
    {
        var model = Model(); var notes = model.Notes; var monday = new DateTime(2026, 10, 5);
        Assert.Null(notes.AddMeeting(monday, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Team sync", null, RepeatChoice.Week));
        var all = notes.All.Where(n => n.SeriesId is not null).OrderBy(n => n.Day).ToList();
        Assert.True(all.Count >= 8); Assert.All(all, n => Assert.Equal(DayOfWeek.Monday, n.Day.DayOfWeek));
        var second = all[1];
        Assert.Null(notes.EditMeeting(second.Id, "Team sync (moved)", null, second.Day, TimeSpan.FromHours(11), TimeSpan.FromHours(12), RepeatChoice.Week, allLater: false));
        Assert.Equal(TimeSpan.FromHours(11), notes.Find(second.Id)!.At); Assert.Equal(TimeSpan.FromHours(9), notes.Find(all[2].Id)!.At); // The others stand.
        Assert.Null(notes.EditMeeting(second.Id, "Team sync (all)", null, second.Day, TimeSpan.FromHours(8), TimeSpan.FromHours(9), RepeatChoice.Week, allLater: true));
        Assert.Equal(TimeSpan.FromHours(8), notes.Find(all[2].Id)!.At); Assert.Equal("Team sync (all)", notes.Text(notes.Find(all[3].Id)!)); Assert.Equal(TimeSpan.FromHours(9), notes.Find(all[0].Id)!.At); // The one before is left alone.
    }

    [Fact]
    public void TheRepeatListIsDailyWeeklyBiWeeklyAndMonthlyAndAnEveryWeekdaySeriesKeepsItsOwnWords()
    {
        Assert.Equal(new[] { "Does not repeat", "Daily", "Weekly", "Bi-weekly", "Monthly" }, NotesModel.Offered(RepeatChoice.None));
        Assert.Equal(new[] { "Does not repeat", "Daily", "Weekly", "Bi-weekly", "Monthly", "Every weekday" }, NotesModel.Offered(RepeatChoice.Weekday)); // Made while it was a choice.
        foreach (var choice in new[] { RepeatChoice.Day, RepeatChoice.Weekday, RepeatChoice.Week, RepeatChoice.BiWeek, RepeatChoice.Month }) Assert.Equal(choice, NotesModel.ChoiceOf(NotesModel.RuleOf(choice)));
        Assert.Equal(RepeatChoice.None, NotesModel.ChoiceOf(new Recurrence(3, 0))); Assert.Equal(RepeatChoice.None, NotesModel.ChoiceOf(new Recurrence(1, 0, null, 2))); // Still shown, not editable.
    }

    [Fact]
    public void ABiWeeklyMeetingFallsOnEverySecondWeekAndAMonthlyOneOnTheSameDateEachMonth()
    {
        var model = Model(); var notes = model.Notes; var monday = new DateTime(2026, 10, 5);
        Assert.Null(notes.AddMeeting(monday, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Planning", null, RepeatChoice.BiWeek));
        var days = notes.All.Where(n => n.SeriesId is not null).Select(n => n.Day.Date).OrderBy(d => d).ToList();
        Assert.True(days.Count >= 3); Assert.Equal(monday, days[0]); Assert.All(days.Zip(days.Skip(1)), p => Assert.Equal(14, (p.Second - p.First).Days));
        Assert.Equal(RepeatChoice.BiWeek, notes.RepeatOf(notes.MeetingsOn(monday).Single())); Assert.Equal("Bi-weekly", NotesModel.Label(RepeatChoice.BiWeek));
        var monthly = new DateTime(2026, 10, 15);
        Assert.Null(notes.AddMeeting(monthly, TimeSpan.FromHours(14), TimeSpan.FromHours(15), "Review", null, RepeatChoice.Month));
        var months = notes.All.Where(n => n.Text.StartsWith("Review")).Select(n => n.Day.Date).OrderBy(d => d).ToList();
        Assert.Equal(new[] { monthly, new DateTime(2026, 11, 15) }, months); // Notes are made eight weeks ahead, so December waits. Assert.Equal("repeats monthly", "repeats " + NotesModel.RepeatWords(RepeatChoice.Month));
    }

    [Fact]
    public void RemovingOneDayKeepsTheSeriesAndRemovingAllLaterEndsItAndItIsNotRemadeOnTheNextDay()
    {
        var now = Day.AddHours(8); var model = Model(() => now); var notes = model.Notes;
        Assert.Null(notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Stand-up", null, RepeatChoice.Day));
        var before = notes.All.Count; var third = notes.All.OrderBy(n => n.Day).ElementAt(2);
        notes.Remove(third.Id); Assert.Equal(before - 1, notes.All.Count);
        now = Day.AddDays(1).AddHours(8); notes.MakeSeriesNotes(); Assert.DoesNotContain(notes.All, n => n.Day.Date == third.Day.Date); // A removed day is not made again.
        var fifth = notes.All.OrderBy(n => n.Day).ElementAt(4); notes.Remove(fifth.Id, allLater: true);
        Assert.DoesNotContain(notes.All, n => n.Day.Date >= fifth.Day.Date); now = Day.AddDays(40).AddHours(8); notes.MakeSeriesNotes();
        Assert.DoesNotContain(notes.All, n => n.Day.Date >= fifth.Day.Date); // The series ended there.
    }

    [Fact]
    public void RenamingOrDeletingAnAccountFollowsInNoteTags()
    {
        var model = Model(); var notes = model.Notes;
        notes.AddNote("Draft the plan", "Account two", null); notes.AddNote("Call the client", "Account one", null);
        model.SetAccounts(new[] { new AccountSettings("Account one"), new AccountSettings("Second account") }, new Dictionary<string, string> { ["Account two"] = "Second account" });
        Assert.Equal("Second account", notes.AccountOf(notes.All.Single(n => notes.Text(n) == "Draft the plan")));
        model.SetAccounts(new[] { new AccountSettings("Second account") }, deleted: new[] { "Account one" });
        Assert.Null(notes.AccountOf(notes.All.Single(n => notes.Text(n).StartsWith("Call the client")))); // The tag became an ordinary word.
    }

    [Fact]
    public void ANoteTheDatabaseWillNotTakeStaysOnScreenAndIsWrittenAgainLater()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-notes-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => Day.AddHours(10)); var preferences = store.Load(engine);
                var notes = new NotesModel(store, () => Day.AddHours(10), () => new[] { "Account one" });
                notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Kept", null, RepeatChoice.None);
                store.Database.Execute("CREATE TRIGGER block_notes BEFORE INSERT ON day_notes BEGIN SELECT RAISE(ABORT, 'disk full'); END");
                Assert.Null(notes.AddNote("Not saved yet", null, null));
                Assert.True(notes.Unsaved); Assert.NotNull(notes.Fault); Assert.Contains(notes.All, n => n.Id < 0 && n.Text == "Not saved yet");
                store.Database.Execute("DROP TRIGGER block_notes");
                notes.RetryUnsaved();
                Assert.False(notes.Unsaved); Assert.Null(notes.Fault); Assert.All(notes.All, n => Assert.True(n.Id > 0));
            }
            using (var store = new TimerStore(folder))
            {
                var reopened = new NotesModel(store, () => Day.AddHours(10), () => new[] { "Account one" });
                Assert.Equal(new[] { "Kept", "Not saved yet" }, reopened.All.Select(n => n.Text).OrderBy(t => t));
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    // ---- The Notes card ----

    [AvaloniaFact]
    public void AddingANoteFromTheCardWithAnAccountTagListsItAndSaysSo()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.Equal(0, Listed(window)); Assert.False(window.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "NoteField"));
            Click(window, "AddNote"); Type(window, "NoteField", "Draft the plan #Accountt");
            var suggestion = Find<Button>(window, "Suggest_Accounttwo"); suggestion.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Assert.Equal("Draft the plan", Find<TextBox>(window, "NoteField").Text); // The tag went into a chip, and the words stay.
            Click(window, "SubmitNote");
            Assert.Equal(1, Listed(window)); Assert.Equal("Account two", model.Notes.AccountOf(model.Notes.All.Single()));
            Assert.Equal("Note added", Find<TextBlock>(window, "MessageText").Text); Assert.False(window.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "NoteField")); // The form closed.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AnAccountChipInTheCardsFormCanBeTakenOffByItsCrossOrByBackspaceInAnEmptyBox()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddNote"); Type(window, "NoteField", "Draft the plan #Accountt");
            Find<Button>(window, "Suggest_Accounttwo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Assert.Equal("Remove account", ToolTip.GetTip(Find<Button>(window, "FormAccountChip")));
            Click(window, "FormAccountChip");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "FormAccountChip"); Assert.Equal("Draft the plan", Find<TextBox>(window, "NoteField").Text); // The words stay.
            Click(window, "SubmitNote"); Assert.Null(model.Notes.AccountOf(model.Notes.All.Single())); // And it is saved with no account.
            // Backspace in an empty box does the same.
            Click(window, "AddNote"); Type(window, "NoteField", "#Accountt");
            Find<Button>(window, "Suggest_Accounttwo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
            var box = Find<TextBox>(window, "NoteField"); box.Text = ""; box.Focus(); Settle(window);
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Back }); Settle(window);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "FormAccountChip");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ABlankNoteIsRefusedInTheToastAndTheFormStaysOpen()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddNote"); Click(window, "SubmitNote");
            Assert.Equal("Write a note first", model.EditError); Assert.Empty(model.Notes.All); Assert.NotNull(Find<TextBox>(window, "NoteField"));
            Click(window, "AddNote"); Assert.False(window.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "NoteField")); // The button again closes it.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AddingARepeatingMeetingDrawsItOnTheTimelineAsAPlannedDashedCard()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddMeeting"); Type(window, "NoteField", "Team sync"); Type(window, "MeetingStart", "09:15"); Type(window, "MeetingEnd", "10:00");
            Find<ComboBox>(window, "MeetingRepeat").SelectedItem = "Weekly"; Dispatcher.UIThread.RunJobs();
            Click(window, "SubmitNote");
            var meeting = model.Notes.MeetingsOn(Day).Single(); Assert.Equal(TimeSpan.FromHours(9.25), meeting.At); Assert.Equal(RepeatChoice.Week, model.Notes.RepeatOf(meeting));
            Assert.Contains("repeats weekly", model.Notice);
            var card = Find<Grid>(window, "TimelineMeeting" + meeting.Id);
            Assert.True(Canvas.GetTop(card) < 92); // In the meeting row above the blocks.
            Assert.Equal(0.75 * model.TimelinePixelsPerHour - 2, card.Width, 1);
            Assert.Empty(model.Engine.Sessions); // Planned, never tracked.
            Assert.Equal(1, Listed(window));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ADraggedMeetingLandsOnTheFiveMinuteGridAndStaysInsideTheDay()
    {
        var nine = TimeSpan.FromHours(9); var ten = TimeSpan.FromHours(10);
        Assert.Equal((new TimeSpan(9, 30, 0), new TimeSpan(10, 30, 0)), MeetingGestures.Resolve(nine, ten, 0, 31)); // The middle moves it, length kept.
        Assert.Equal((nine, ten), MeetingGestures.Resolve(nine, ten, 0, 2)); // Less than half a step changes nothing.
        Assert.Equal((new TimeSpan(9, 30, 0), ten), MeetingGestures.Resolve(nine, ten, -1, 28)); // The start edge.
        Assert.Equal((nine, new TimeSpan(11, 0, 0)), MeetingGestures.Resolve(nine, ten, 1, 58)); // The end edge.
        Assert.Equal((new TimeSpan(9, 55, 0), ten), MeetingGestures.Resolve(nine, ten, -1, 500)); // Never shorter than five minutes.
        Assert.Equal((nine, new TimeSpan(9, 5, 0)), MeetingGestures.Resolve(nine, ten, 1, -500));
        Assert.Equal((TimeSpan.Zero, TimeSpan.FromHours(1)), MeetingGestures.Resolve(nine, ten, 0, -900)); // Not before midnight.
        Assert.Equal((new TimeSpan(22, 59, 0), new TimeSpan(23, 59, 0)), MeetingGestures.Resolve(nine, ten, 0, 900)); // Nor after 23:59.
        Assert.Equal((new TimeSpan(9, 5, 0), new TimeSpan(10, 0, 0)), MeetingGestures.Resolve(new TimeSpan(9, 7, 0), ten, -1, -4)); // An off-grid start lands on the grid once really moved.
    }

    [AvaloniaFact]
    public void DraggingAMeetingOnTheTimelineMovesAndResizesItEverywhere()
    {
        var model = Model(); Assert.Null(model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Planning", null, RepeatChoice.None));
        var id = model.Notes.MeetingsOn(Day).Single().Id; var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Point Where(Control c, double x, double y) => c.TranslatePoint(new Point(x, y), window)!.Value;
            var card = Find<Grid>(window, "TimelineMeeting" + id); var pph = model.TimelinePixelsPerHour;
            // Drag the middle half an hour later.
            var from = Where(card, card.Width / 2, card.Height / 2); var to = from + new Vector(pph / 2, 0);
            window.MouseDown(from, MouseButton.Left); window.MouseMove(from + new Vector(pph / 4, 0)); window.MouseMove(to); window.MouseUp(to, MouseButton.Left); Settle(window);
            var moved = model.Notes.Find(id)!; Assert.Equal(TimeSpan.FromHours(9.5), moved.At); Assert.Equal(TimeSpan.FromHours(10.5), moved.End);
            Assert.Contains("Meeting moved", model.Notice); Assert.Contains("09:30–10:30", model.Notice);
            // The card, the Notes card's row and its form all follow.
            card = Find<Grid>(window, "TimelineMeeting" + id); Assert.Contains("09:30–10:30", ToolTip.GetTip(card)!.ToString());
            // Drag the end edge half an hour later.
            var edge = Where(card, card.Width - 3, card.Height / 2); var edgeTo = edge + new Vector(pph / 2, 0);
            window.MouseDown(edge, MouseButton.Left); window.MouseMove(edge + new Vector(pph / 4, 0)); window.MouseMove(edgeTo); window.MouseUp(edgeTo, MouseButton.Left); Settle(window);
            moved = model.Notes.Find(id)!; Assert.Equal(TimeSpan.FromHours(9.5), moved.At); Assert.Equal(TimeSpan.FromHours(11), moved.End);
            // A press that goes nowhere still opens the meeting for editing, with the new times in its form.
            card = Find<Grid>(window, "TimelineMeeting" + id); var middle = Where(card, card.Width / 2, card.Height / 2);
            window.MouseDown(middle, MouseButton.Left); window.MouseUp(middle, MouseButton.Left); Settle(window);
            Assert.Equal("09:30", Find<TextBox>(window, "MeetingStart").Text); Assert.Equal("11:00", Find<TextBox>(window, "MeetingEnd").Text);
            // Moved while the form is open, the form's boxes follow, so saving does not put the old times back.
            card = Find<Grid>(window, "TimelineMeeting" + id); from = Where(card, card.Width / 2, card.Height / 2); to = from - new Vector(pph / 2, 0);
            window.MouseDown(from, MouseButton.Left); window.MouseMove(from - new Vector(pph / 4, 0)); window.MouseMove(to); window.MouseUp(to, MouseButton.Left); Settle(window);
            Assert.Equal("09:00", Find<TextBox>(window, "MeetingStart").Text); Assert.Equal("10:30", Find<TextBox>(window, "MeetingEnd").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ADraggedMeetingInASeriesMovesForThatDayOnly()
    {
        var model = Model(); Assert.Null(model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Stand-up", null, RepeatChoice.Day));
        var first = model.Notes.MeetingsOn(Day).Single(); var next = model.Notes.MeetingsOn(Day.AddDays(1)).Single();
        Assert.Null(model.Notes.MoveMeeting(first.Id, TimeSpan.FromHours(9.5), TimeSpan.FromHours(10.5)));
        Assert.Equal(TimeSpan.FromHours(9.5), model.Notes.Find(first.Id)!.At); Assert.Equal(TimeSpan.FromHours(9), model.Notes.Find(next.Id)!.At);
        Assert.Equal("The end has to be after the start.", model.Notes.MoveMeeting(first.Id, TimeSpan.FromHours(10), TimeSpan.FromHours(9)));
    }

    [Fact]
    public void DoneNotesDropToTheBottomOfTheCardsListWhateverTheirTime()
    {
        var model = Model(); var notes = model.Notes;
        Assert.Null(notes.AddNote("Early", null, Day.AddHours(9))); Assert.Null(notes.AddNote("Middle", null, Day.AddHours(11))); Assert.Null(notes.AddNote("Late", null, Day.AddHours(15)));
        Assert.Null(notes.AddNote("Someday", null, null));
        string[] Order() => notes.ForDay(Day).Select(n => notes.Text(n)).ToArray();
        Assert.Equal(new[] { "Early", "Middle", "Late", "Someday" }, Order());
        notes.Toggle(notes.All.Single(n => notes.Text(n) == "Early").Id); notes.Toggle(notes.All.Single(n => notes.Text(n) == "Someday").Id);
        Assert.Equal(new[] { "Middle", "Late", "Early", "Someday" }, Order()); // Done last, in the order they had.
        notes.Toggle(notes.All.Single(n => notes.Text(n) == "Early").Id);
        Assert.Equal(new[] { "Early", "Middle", "Late", "Someday" }, Order().Take(3).Concat(new[] { "Someday" }).ToArray()); // Ticking back lifts it to its place.
    }

    [AvaloniaFact]
    public void ABadMeetingTimeIsRefusedWithAShortLine()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddMeeting"); Type(window, "NoteField", "Review"); Type(window, "MeetingStart", "ten"); Click(window, "SubmitNote");
            Assert.Equal("Start: try 09:30", model.EditError);
            Type(window, "MeetingStart", "10:00"); Type(window, "MeetingEnd", "09:00"); Click(window, "SubmitNote");
            Assert.Equal("The end has to be after the start.", model.EditError); Assert.Empty(model.Notes.All);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AClickInTheMeetingRowOpensAMeetingFormAtThatTimeSnappedToFifteenMinutes()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.Notes.RequestNewMeeting(Day.AddHours(13).AddMinutes(8)); Settle(window);
            Assert.Equal("13:15", Find<TextBox>(window, "MeetingStart").Text); Assert.Equal("13:45", Find<TextBox>(window, "MeetingEnd").Text); // Half an hour long.
            Assert.Equal("Add meeting", Assert.IsType<TextBlock>(Find<Button>(window, "SubmitNote").Content).Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ChangingARepeatingMeetingAsksWhichOnesAndThisOneOnlyLeavesTheRestAlone()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var monday = new DateTime(2026, 10, 5); model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Stand-up", null, RepeatChoice.Day); Settle(window);
            var today = model.Notes.MeetingsOn(Day).Single(); var tomorrow = model.Notes.MeetingsOn(Day.AddDays(1)).Single();
            model.Notes.RequestEditMeeting(today.Id); Settle(window);
            Type(window, "NoteField", "Stand-up (long)"); Type(window, "MeetingEnd", "10:30"); Click(window, "SubmitNote");
            Assert.NotNull(Find<Border>(window, "ScopeQuestion")); Assert.Equal(TimeSpan.FromHours(10), model.Notes.Find(today.Id)!.End); // Nothing changed until answered.
            Click(window, "ScopeOne");
            Assert.Equal(TimeSpan.FromHours(10.5), model.Notes.Find(today.Id)!.End); Assert.Equal(TimeSpan.FromHours(10), model.Notes.Find(tomorrow.Id)!.End);
            Assert.Contains("this day only", model.Notice);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RemovingARepeatingMeetingAsksAndAllLaterRemovesTheRest()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Stand-up", null, RepeatChoice.Day); Settle(window);
            var today = model.Notes.MeetingsOn(Day).Single(); Click(window, "CardKindMeetings"); // Meetings are on the card's other side.
            Click(window, "NoteRemove_" + today.Id); Assert.NotNull(Find<Border>(window, "ScopeQuestion")); Click(window, "ScopeAll");
            Assert.Empty(model.Notes.All); Assert.Equal(0, Listed(window));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ATickMarksANoteDoneAndARemoveButtonTakesItAway()
    {
        var model = Model(); model.Notes.AddNote("Send the budget", null, null); var id = model.Notes.All.Single().Id;
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NoteDone_" + id); Assert.True(model.Notes.Find(id)!.Done);
            Assert.Equal(Avalonia.Media.TextDecorations.Strikethrough, Find<TextBlock>(window, "NoteText_" + id).TextDecorations);
            Click(window, "NoteRemove_" + id); Assert.Empty(model.Notes.All); Assert.Equal("Note removed", model.Notice);
        }
        finally { window.Close(); }
    }

    // ---- The reminder pop-up ----

    [AvaloniaFact]
    public void SettingAReminderOnANoteMovesItToThatDayAndRemovingItMakesItUndatedAgain()
    {
        var model = Model(); model.Notes.AddNote("Send the budget", null, null); var id = model.Notes.All.Single().Id;
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NoteAddReminder_" + id);
            Assert.True(Find<Avalonia.Controls.Primitives.Popup>(window, "ReminderHost").IsOpen);
            Click(window, "ReminderCell_20261010"); Click(window, "hourUp"); Click(window, "ReminderTime9"); Click(window, "ReminderSet");
            Assert.Equal(Day.AddDays(1).AddHours(9), NotesModel.ReminderOf(model.Notes.Find(id)!)); Assert.False(Find<Avalonia.Controls.Primitives.Popup>(window, "ReminderHost").IsOpen);
            Assert.Equal(0, Listed(window)); // Today's card no longer lists it.
            model.ChangeDate(1); Settle(window); Assert.Equal(1, Listed(window));
            Click(window, "NoteReminder_" + id); Click(window, "ReminderRemove");
            Assert.False(model.Notes.Find(id)!.HasDate); Assert.Null(NotesModel.ReminderOf(model.Notes.Find(id)!));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePopupStartsAtMiddayStepsInFiveMinutesTurnsPastDaysOffAndIsClosedByItsCross()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddNote"); Click(window, "FormBell");
            Assert.False(Find<Button>(window, "ReminderCell_20261008").IsEnabled); Assert.True(Find<Button>(window, "ReminderCell_20261010").IsEnabled); // Yesterday is off; tomorrow is on.
            Assert.Equal("12", Find<TextBox>(window, "ReminderHour").Text); Click(window, "minutesUp"); Assert.Equal("05", Find<TextBox>(window, "ReminderMinute").Text);
            Click(window, "minutesDown"); Click(window, "minutesDown"); Assert.Equal("55", Find<TextBox>(window, "ReminderMinute").Text);
            Click(window, "ReminderTime9"); Assert.Equal("Today, 09:00", Find<TextBlock>(window, "ReminderSummary").Text); // No warning that it has passed.
            Click(window, "ReminderCell_20261010"); Assert.StartsWith("Tomorrow, 09:00", Find<TextBlock>(window, "ReminderSummary").Text);
            Click(window, "ReminderClose"); Assert.False(Find<Avalonia.Controls.Primitives.Popup>(window, "ReminderHost").IsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AReminderSetFromTheFormIsShownAsAChipAndSavedWithTheNote()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddNote"); Type(window, "NoteField", "Call back"); Click(window, "FormBell"); Click(window, "ReminderCell_20261010"); Click(window, "ReminderSet");
            Assert.NotNull(Find<Button>(window, "FormReminder")); Click(window, "SubmitNote");
            var note = model.Notes.All.Single(); Assert.Equal(Day.AddDays(1).AddHours(12), NotesModel.ReminderOf(note)); Assert.Contains("reminder Tomorrow, 12:00", model.Notice);
        }
        finally { window.Close(); }
    }
}
