using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desk;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class NoteShorthandTests
{
    // A Wednesday.
    private static readonly DateTime Today = new(2026, 10, 14);
    private static readonly string[] Accounts = { "Account one", "Account two", "Internal" };

    [Theory]
    [InlineData("call Sam @tomorrow", "call Sam", 15)]
    [InlineData("@today review", "review", 14)]
    [InlineData("send invoice @fri", "send invoice", 16)]
    [InlineData("send invoice @friday", "send invoice", 16)]
    [InlineData("standup @wed", "standup", 21)] // The same weekday means next week; today has @today.
    [InlineData("pay rent @21oct", "pay rent", 21)]
    [InlineData("pay rent @21-oct", "pay rent", 21)]
    [InlineData("tax @2026-11-02", "tax", 2)]
    public void ADayAfterAtBecomesTheNotesDay(string typed, string text, int dayOfMonth)
    {
        var parts = NoteShorthand.Parse(typed, Today);
        Assert.Equal(text, parts.Text); Assert.Equal(dayOfMonth, parts.Day!.Value.Day);
    }

    [Theory]
    [InlineData("see Friday report")]
    [InlineData("tomorrow is another day")]
    [InlineData("meet @nobody")]
    [InlineData("email me@example.com")]
    public void OrdinaryWordsAndUnknownAtWordsAreNeverTakenForADate(string typed)
    {
        var parts = NoteShorthand.Parse(typed, Today);
        Assert.Null(parts.Day); Assert.Equal(typed, parts.Text);
    }

    [Fact]
    public void ADateInThePastIsNextYearAndOnlyADateKeepsItsWord()
    {
        Assert.Equal(new DateTime(2027, 3, 5), NoteShorthand.Parse("x @5mar", Today).Day);
        Assert.Equal(Today, NoteShorthand.DayFor("today", Today));
        var only = NoteShorthand.Parse("@fri", Today); Assert.Equal("fri", only.Text); Assert.Equal(new DateTime(2026, 10, 16), only.Day);
        Assert.Null(NoteShorthand.DayFor("31feb", Today));
    }

    [Fact]
    public void ADayAndATimeCanComeTogetherInAnyOrder()
    {
        var a = NoteShorthand.Parse("call @fri 14:30 back", Today); var b = NoteShorthand.Parse("14:30 call back @fri", Today);
        Assert.Equal("call back", a.Text); Assert.Equal("call back", b.Text);
        Assert.Equal(new TimeSpan(14, 30, 0), a.At); Assert.Equal(a.At, b.At); Assert.Equal(a.Day, b.Day);
    }

    [Fact]
    public void ToDosAndNotesReadAnAccountTagTheSameWay()
    {
        Assert.Equal("Account one", NoteShorthand.AccountOf("write up #AccountOne", Accounts));
        Assert.Equal("Internal", NoteShorthand.AccountOf("plan #int", Accounts)); // The start of a name, when only one begins so.
        Assert.Null(NoteShorthand.AccountOf("plan #account", Accounts)); // Two accounts begin so, so it names neither.
        Assert.Null(NoteShorthand.AccountOf("plan #nobody", Accounts));
        Assert.Equal(Trinkets.ParseTodo("write up #AccountOne", Accounts).Account, NoteShorthand.AccountOf("write up #AccountOne", Accounts));
    }

    [Fact]
    public void TakingARecognisedPartOutPutsItBackIntoTheWords()
    {
        Assert.Equal("14:30 call back", NoteShorthand.WithTimeInWords("call back", new TimeSpan(14, 30, 0)));
        Assert.Equal("! 14:30 file report", NoteShorthand.WithTimeInWords("!file report", new TimeSpan(14, 30, 0))); // The marker stays first.
        Assert.Equal("file report", NoteShorthand.WithoutDeadline("! file report"));
        Assert.Equal("write up AccountOne", NoteShorthand.WithoutAccountTag("write up #AccountOne", Accounts));
        Assert.Equal("#nobody stays", NoteShorthand.WithoutAccountTag("#nobody stays", Accounts)); // A tag that names no account was never recognised.
    }

    [Fact]
    public void TheDisplayLeavesOutWhatHasItsOwnChipAndKeepsTheAccountWhenNothingElseIsLeft()
    {
        Assert.Equal("file report", NoteShorthand.Display(new DayNote(1, Today, null, "!file report #AccountOne"), Accounts));
        Assert.Equal("Account one", NoteShorthand.Display(new DayNote(2, Today, null, "#AccountOne"), Accounts));
        Assert.Equal("see #nobody", NoteShorthand.Display(new DayNote(3, Today, null, "see #nobody"), Accounts));
    }

    private static TimerModel Model() { var m = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0))); m.CompleteSetup(); return m; }
    private static void Click(Control c) => c.RaiseEvent(new RoutedEventArgs(c is MenuItem ? MenuItem.ClickEvent : Button.ClickEvent));

    [AvaloniaFact]
    public void ANoteWithADayGoesToThatDayWithANoticeAndFeedsComingUp()
    {
        var model = Model(); model.AddDayNote(model.SelectedDate, "send invoice @fri 09:30");
        Assert.Empty(model.NotesOn(model.SelectedDate)); // It left the day showing,
        Assert.Equal("Added to Friday 16 October.", model.Notice); // and the notice says where it went.
        var note = Assert.Single(model.ComingUp()); Assert.Equal(new DateTime(2026, 10, 16), note.Day); Assert.Equal(new TimeSpan(9, 30, 0), note.At);
        model.AddDayNote(model.SelectedDate, "plain note"); Assert.Single(model.NotesOn(model.SelectedDate)); // No day, so the day showing.
    }

    [AvaloniaFact]
    public void ANoteCanBeMovedToAnotherDayAndTheNoticeSaysWhereItWent()
    {
        var model = Model(); model.AddDayNote(model.SelectedDate, "14:30 call back"); var id = model.NotesOn(model.SelectedDate).Single().Id;
        model.MoveDayNote(id, model.SelectedDate); Assert.Single(model.NotesOn(model.SelectedDate)); // The same day changes nothing.
        model.MoveDayNote(id, new DateTime(2026, 10, 20, 8, 0, 0));
        Assert.Empty(model.NotesOn(model.SelectedDate)); Assert.Equal("Moved to Tuesday 20 October.", model.Notice);
        var moved = Assert.Single(model.NotesOn(new DateTime(2026, 10, 20))); Assert.Equal(new TimeSpan(14, 30, 0), moved.At); Assert.Equal("call back", moved.Text);
    }

    [AvaloniaFact]
    public void EachNoteInTheNotesTileHasACalendarThatMovesIt()
    {
        var model = Model(); model.AddDayNote(model.SelectedDate, "plain note"); var id = model.NotesOn(model.SelectedDate).Single().Id;
        var view = new NotesView(model, false); var window = new Window { Content = view }; window.Show();
        try
        {
            var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NoteMove_" + id); Assert.NotNull(button.Flyout);
            var calendar = (Calendar)((Flyout)button.Flyout!).Content!; calendar.SelectedDate = new DateTime(2026, 10, 21);
            Assert.Empty(model.NotesOn(model.SelectedDate)); Assert.Single(model.NotesOn(new DateTime(2026, 10, 21)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheNotesTileShowsWhatWasRecognisedAndEachChipTakesItsPartBackOut()
    {
        var model = Model(); model.AddDayNote(model.SelectedDate, "!file report 14:30 #AccountOne");
        var view = new NotesView(model, false); var window = new Window { Width = 500, Height = 400, Content = view }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var id = model.NotesOn(model.SelectedDate).Single().Id;
            Control Find(string name) => view.GetVisualDescendants().OfType<Control>().Single(c => c.Name == name);
            Assert.NotNull(Find("NoteTimeChip_" + id)); Assert.NotNull(Find("NoteDeadlineChip_" + id)); Assert.NotNull(Find("NoteAccountChip_" + id));
            Click(Find("NoteTimeClear_" + id)); Dispatcher.UIThread.RunJobs(); view.Rebuild(); Dispatcher.UIThread.RunJobs();
            var after = model.NotesOn(model.SelectedDate).Single(); Assert.Null(after.At); Assert.Equal("! 14:30 file report #AccountOne", after.Text); Assert.True(DayNotes.IsDeadline(after));
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Control>(), c => c.Name == "NoteTimeChip_" + id);
            Click(Find("NoteAccountClear_" + id)); view.Rebuild(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("! 14:30 file report AccountOne", model.NotesOn(model.SelectedDate).Single().Text); Assert.Null(NoteShorthand.AccountOf(model.NotesOn(model.SelectedDate).Single().Text, model.AccountNames));
            Click(Find("NoteDeadlineClear_" + id)); view.Rebuild(); Dispatcher.UIThread.RunJobs();
            var plain = model.NotesOn(model.SelectedDate).Single(); Assert.Equal("14:30 file report AccountOne", plain.Text); Assert.False(DayNotes.IsDeadline(plain));
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Control>(), c => c.Name is not null && c.Name.StartsWith("Note") && c.Name.Contains("Chip_")); // Nothing is recognised any more.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClearingAPartReachesComingUpAndTheDeadlineTileAtOnce()
    {
        var model = Model(); model.AddDayNote(model.SelectedDate, "!file report @tomorrow 14:30 #AccountOne");
        var id = model.AllDayNotes.Single().Id;
        var coming = new ComingUpView(model); var deadline = new DeadlineView(model, TileForm.Normal); var panel = new StackPanel { Children = { coming, deadline } };
        var window = new Window { Width = 500, Height = 600, Content = panel }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(coming.GetVisualDescendants().OfType<Control>(), c => c.Name == "ComingAccount_" + id); // The account shows beside the words in Coming up.
            Assert.Equal("file report", deadline.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DeadlineWhat").Text);
            model.ClearNoteDeadline(id); coming.Rebuild(); deadline.Refresh(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Nothing due", deadline.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DeadlineWhat").Text); // It is no longer a deadline,
            Assert.Contains(coming.GetVisualDescendants().OfType<Control>(), c => c.Name == "ComingEvent_" + id); // and with its time it is an event.
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ATaskIsANoteWithNoTimeThatIsNotADeadlineAndTheListCarriesOpenOnesOver()
    {
        var yesterday = Today.AddDays(-1); var tomorrow = Today.AddDays(1);
        var notes = new[]
        {
            new DayNote(1, yesterday, null, "open from yesterday"), new DayNote(2, yesterday, null, "done yesterday", true), new DayNote(3, Today, null, "today"),
            new DayNote(4, Today, null, "done today", true), new DayNote(5, tomorrow, null, "later"), new DayNote(6, Today, new TimeSpan(14, 0, 0), "event"), new DayNote(7, Today, null, "!deadline"),
        };
        Assert.Equal(new long[] { 1, 3, 4 }, DayNotes.Todos(notes, Today.AddHours(10)).Select(n => n.Id)); // Carried over, today's, and today's ticked; not later, events or deadlines.
        Assert.True(DayNotes.IsTask(notes[0])); Assert.False(DayNotes.IsTask(notes[5])); Assert.False(DayNotes.IsTask(notes[6]));
    }

    [Fact]
    public void ATaskTypedInTheTodoListKeepsATimeLikeWordAndDropsALeadingMarker()
    {
        var parts = NoteShorthand.Parse("call at 14:30 #AccountOne @fri", Today, task: true);
        Assert.Equal("call at 14:30 #AccountOne", parts.Text); Assert.Null(parts.At); Assert.Equal(new DateTime(2026, 10, 16), parts.Day);
        Assert.Equal("ring Sam", NoteShorthand.Parse("!ring Sam", Today, task: true).Text);
    }

    [Fact]
    public void ATagFollowsARenameAndBecomesAnOrdinaryWordWhenItsAccountIsDeleted()
    {
        var renamed = new Dictionary<string, string> { ["Account two"] = "Beta Co" };
        Assert.Equal("call #BetaCo back", NoteShorthand.Relink("call #AccountTwo back", Accounts, renamed, Array.Empty<string>()));
        Assert.Equal("call AccountTwo back", NoteShorthand.Relink("call #AccountTwo back", Accounts, new Dictionary<string, string>(), new[] { "Account two" }));
        Assert.Equal("call #AccountOne", NoteShorthand.Relink("call #AccountOne", Accounts, renamed, new[] { "Internal" })); // Another account's change leaves it alone.
        Assert.Equal("#BetaCo", NoteShorthand.TagFor("Beta Co"));
    }

    [AvaloniaFact]
    public void ATodoIsTheSameItemInTodoNotesAndComingUp()
    {
        var model = Model(); model.AddTodo("send invoice #AccountOne");
        var todo = Assert.Single(model.Todos);
        Assert.Contains(model.NotesOn(model.SelectedDate), n => n.Id == todo.Id); // Notes shows it,
        Assert.Contains(model.ComingUp(), n => n.Id == todo.Id); // and so does Coming up.
        model.ToggleDayNote(todo.Id); // Ticked in Notes,
        Assert.True(model.Todos.Single().Done); Assert.DoesNotContain(model.ComingUp(), n => n.Id == todo.Id); // it is ticked in the list and gone from Coming up.
        model.ToggleTodo(todo.Id); Assert.False(model.Todos.Single().Done); // And back from the list.
        model.ToggleTodo(todo.Id); model.ClearDoneTodos(); Assert.Empty(model.Todos); Assert.Empty(model.AllDayNotes); // Clearing the done ones removes the notes themselves.
    }

    [AvaloniaFact]
    public void ATodoLeftOverFromYesterdayCarriesOnAndSaysWhen()
    {
        var model = Model(); model.AddDayNote(model.Engine.Now.Date.AddDays(-1), "chase payment", task: true);
        var view = new TodoView(model, false); var window = new Window { Width = 400, Height = 300, Content = view }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); var id = model.Todos.Single().Id;
            Assert.Equal("chase payment", model.Todos.Single().Text);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "TodoFrom_" + id);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ToDosAnEarlierBuildSavedBecomeTaskNotesOnceAfterABackup()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-todos-move-" + Guid.NewGuid());
        try
        {
            var clock = new DateTime(2026, 10, 14, 10, 0, 0);
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => clock); var prefs = store.Load(engine);
                var old = new TrinketData(new[] { new TodoItem("send invoice", false, "Account one"), new TodoItem("order tea", true), new TodoItem("plan #AccountTwo", false, "Account two") }, TrinketData.Empty.PostIts);
                store.Save(engine, prefs.WithApp(a => a with { Trinkets = old }));
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => clock); var model = new TimerModel(engine, store.Load(engine), store);
                var todos = model.Todos; Assert.Equal(3, todos.Count);
                Assert.Equal("send invoice #Accountone", todos[0].Text); Assert.Equal("Account one", NoteShorthand.AccountOf(todos[0].Text, model.AccountNames));
                Assert.True(todos[1].Done); Assert.Equal("order tea", todos[1].Text);
                Assert.Equal("plan #AccountTwo", todos[2].Text); // A tag already in the words is not added twice.
                Assert.All(todos, t => Assert.Equal(clock.Date, t.Day));
                Assert.True(model.Trinkets.TodosMoved); Assert.Equal(3, model.Trinkets.Todos.Count); // The old list is still in the file.
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => clock); var model = new TimerModel(engine, store.Load(engine), store);
                Assert.Equal(3, model.AllDayNotes.Count); // Opening again does not add them twice.
            }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public void PreviewPictures()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return; Directory.CreateDirectory(folder);
        var model = TimerModel.CreateDemo();
        model.AddDayNote(model.SelectedDate, "!file report 14:30 #AccountOne"); model.AddDayNote(model.SelectedDate, "call Sam 16:00"); model.AddDayNote(model.SelectedDate, "tidy desk #int");
        model.AddDayNote(model.SelectedDate, "send invoice @fri #AccountTwo"); model.AddDayNote(model.SelectedDate, "!tax return @21oct");
        var panel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Avalonia.Thickness(20) };
        var notes = new Border { Child = new NotesView(model, false), Margin = new Avalonia.Thickness(0, 0, 12, 0) }; var coming = new ScrollViewer { Content = new ComingUpView(model) };
        panel.Children.Add(notes); Avalonia.Controls.Grid.SetColumn(coming, 1); panel.Children.Add(coming);
        var window = new Window { Width = 900, Height = 420, Content = panel }; window.Show();
        try { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame(); if (i == 2) f?.Save(Path.Combine(folder, "notes.png")); } }
        finally { window.Close(); }
    }
}
