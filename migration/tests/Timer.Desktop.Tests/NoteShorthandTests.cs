using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
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
            Assert.Contains("No deadline", deadline.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DeadlineWhat").Text); // It is no longer a deadline,
            Assert.Contains(coming.GetVisualDescendants().OfType<Control>(), c => c.Name == "ComingEvent_" + id); // and with its time it is an event.
        }
        finally { window.Close(); }
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
