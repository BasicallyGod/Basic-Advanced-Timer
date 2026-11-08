using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The reminders that are waiting (the bell, the blue light) and the Notes page.
public sealed class NotesPageTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TimerModel Model(Action<NotesModel>? seed = null)
    {
        var engine = new TrackingEngine(clock: () => Day.AddHours(10));
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        seed?.Invoke(model.Notes); return model;
    }
    // Two notes with reminders (one late, one tomorrow), one undated, a meeting today and a ticked one.
    private static void Seed(NotesModel notes)
    {
        notes.AddNote("Send the budget", "Account one", Day.AddHours(8));
        notes.AddNote("Follow up on the brief", "Account two", Day.AddDays(1).AddHours(9));
        notes.AddNote("Tidy the drive", null, null);
        notes.AddMeeting(Day, TimeSpan.FromHours(14), TimeSpan.FromHours(15), "Client call", "Account two", RepeatChoice.None);
        notes.AddNote("Old thing", null, null); notes.Toggle(notes.All.Single(n => notes.Text(n) == "Old thing").Id);
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window); }
    private static void Type(Window window, string name, string text) { Find<TextBox>(window, name).Text = text; Dispatcher.UIThread.RunJobs(); }
    private static long IdOf(TimerModel model, string text) => model.Notes.All.Single(n => model.Notes.Text(n) == text).Id;

    [AvaloniaFact]
    public void LargeListsOnlyRealizeNearbyRowsAndKeepTheDraftWhenTheEditorScrollsOut()
    {
        var model = Model(notes =>
        {
            for (var i = 0; i < 300; i++) notes.AddNote($"Note {i:000}", null, null);
        });
        var page = new NotesPage(model);
        var window = new Window { Width = 950, Height = 480, Content = page };
        window.Show();
        try
        {
            Settle(window);
            var scroll = Find<ScrollViewer>(window, "NotesPageScroll");
            var first = model.Notes.All[0].Id;
            var last = model.Notes.All[^1].Id;
            Border[] RealizedRows() => page.GetVisualDescendants().OfType<Border>()
                .Where(row => row.Name?.StartsWith("PageRow_") == true).ToArray();
            Assert.InRange(RealizedRows().Length, 1, 30);
            Assert.DoesNotContain(RealizedRows(), row => row.Name == "PageRow_" + last);

            page.OpenNote(last);
            Settle(window);
            var editor = Find<TextBox>(Find<Border>(window, "PageRow_" + last), "PageNoteField");
            editor.Text = "A draft to keep";
            Assert.True(scroll.Offset.Y > 0);

            scroll.Offset = default;
            Settle(window);
            Assert.Contains(RealizedRows(), row => row.Name == "PageRow_" + first);
            Assert.DoesNotContain(RealizedRows(), row => row.Name == "PageRow_" + last);
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Settle(window);
            Assert.Equal("A draft to keep", Find<TextBox>(Find<Border>(window, "PageRow_" + last), "PageNoteField").Text);
            Assert.InRange(RealizedRows().Length, 1, 30);
            Click(window, "PageSubmit");
            Assert.Equal("A draft to keep", model.Notes.Words(model.Notes.Find(last)!));
        }
        finally { window.Close(); }
    }

    // ---- The model's views ----

    [Fact]
    public void SearchAndAccountCountsEachReadTheLedgerAccountCatalogueOnce()
    {
        var accountReads = 0;
        var notes = new NotesModel(null, () => Day.AddHours(10), () =>
        {
            accountReads++;
            return new[] { "Account one", "Account two" };
        });
        notes.AddNote("Read the [brief](https://example.com/brief)", "Account one", null);
        notes.AddNote("Review the invoice", "Account two", null);
        notes.AddNote("Unassigned", null, null);

        accountReads = 0;
        var found = Assert.Single(notes.MatchingAny("brief", new[] { "account ONE" }));
        Assert.Equal(1, accountReads);
        Assert.Equal("Read the brief", notes.Text(found));
        accountReads = 0;
        var counts = notes.AccountCounts();
        Assert.Equal(1, accountReads);
        Assert.Equal(1, counts["account one"]);
        Assert.Equal(1, counts["Account two"]);
    }

    [Fact]
    public void OverdueRemindersAreCountedAndStayUntilTickedOffAndComingUpListsTheFutureOnes()
    {
        var model = Model(Seed); var notes = model.Notes;
        Assert.Equal(1, notes.AttentionCount); Assert.Equal("Send the budget", notes.Text(notes.Overdue.Single())); Assert.True(model.ReminderDue);
        Assert.Equal("Follow up on the brief", notes.Text(notes.ComingUp().Single()));
        notes.Toggle(notes.Overdue.Single().Id); Assert.Equal(0, notes.AttentionCount); Assert.False(model.ReminderDue);
    }

    [Fact]
    public void TheViewsAndAccountFiltersCountWhatTheyShouldAndSearchNarrowsThem()
    {
        var model = Model(Seed); var notes = model.Notes; var all = notes.Matching("", null);
        int Count(NoteView v, IReadOnlyList<DayNote>? from = null) => (from ?? all).Count(n => notes.InView(n, v));
        Assert.Equal(5, all.Count); Assert.Equal(1, Count(NoteView.Overdue)); Assert.Equal(2, Count(NoteView.Today)); Assert.Equal(1, Count(NoteView.Upcoming));
        Assert.Equal(1, Count(NoteView.Meetings)); Assert.Equal(1, Count(NoteView.Done));
        var two = notes.Matching("", "Account two"); Assert.Equal(2, two.Count);
        var found = notes.Matching("budget", null); Assert.Equal("Send the budget", notes.Text(Assert.Single(found))); Assert.Empty(notes.Matching("zzz", null));
        Assert.Single(notes.Matching("two", null).Where(n => NotesModel.IsMeeting(n))); // The account's name is searched too.
    }

    [Fact]
    public void TheListIsGroupedOverdueThenEachDayThenNoDateThenDone()
    {
        var model = Model(Seed); var notes = model.Notes;
        var groups = notes.Groups(notes.Matching("", null));
        Assert.Equal(new[] { "Overdue", "Today · 9 Oct", "Tomorrow · 10 Oct", "No date", "Done" }, groups.Select(g => g.Label));
        Assert.True(groups[0].Bad); Assert.False(groups[1].Bad);
    }

    [Fact]
    public void ARepeatingMeetingIsListedOnceNotForEveryDayItWasMadeFor()
    {
        var model = Model(); var notes = model.Notes;
        notes.AddMeeting(Day.AddDays(1), TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Stand-up", null, RepeatChoice.Day);
        Assert.True(notes.All.Count(n => n.SeriesId is not null) > 30); Assert.Single(notes.Listed());
    }


    [AvaloniaFact]
    public void TheBlueLightOnTheRailAndThePillComesOnWhileAReminderIsDueAndGoesWhenItIsDone()
    {
        var model = Model(notes => notes.AddNote("Send the budget", null, Day.AddHours(8))); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var light = Find<Button>(window, "SidebarAlertsButton"); Assert.Equal("A reminder is due", Avalonia.Automation.AutomationProperties.GetName(light));
            model.Notes.Toggle(model.Notes.All.Single().Id); Settle(window); Assert.Equal("Nothing waiting", Avalonia.Automation.AutomationProperties.GetName(light));
        }
        finally { window.Close(); }
    }

    // ---- The page ----

    [AvaloniaFact]
    public void TheNotesTabShowsThePageWithTheSearchBoxAndNoDateControlsAndTheTimelineComesBack()
    {
        var model = Model(Seed); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab");
            Assert.NotNull(Find<NotesPage>(window, "NotesPage"));
            Assert.True(Find<TextBox>(window, "NotesSearch").IsEffectivelyVisible); Assert.False(Find<Button>(window, "PreviousPeriod").IsEffectivelyVisible);
            Assert.Contains("selected", Find<Button>(window, "NotesTab").Classes); Assert.DoesNotContain("selected", Find<Button>(window, "TimelineTab").Classes);
            Assert.False(Find<Border>(window, "TimelineCard").IsEffectivelyVisible);
            Click(window, "TimelineTab"); Assert.True(Find<Border>(window, "TimelineCard").IsEffectivelyVisible); Assert.False(Find<TextBox>(window, "NotesSearch").IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheViewListShowsCountsAndAClickFiltersTheListAndTheSearchBoxNarrowsIt()
    {
        var model = Model(Seed); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab");
            string CountOf(string name) => Find<Button>(window, name).GetVisualDescendants().OfType<TextBlock>().Last().Text!;
            Assert.Equal("5", CountOf("NotesView_All")); Assert.Equal("1", CountOf("NotesView_Overdue")); Click(window, "NotesAccountFilter"); Assert.Equal("2", CountOf("NotesAccount_Accounttwo")); Click(window, "NotesAccountFilter");
            Click(window, "NotesView_Overdue"); Assert.Single(window.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("PageRow_") == true));
            Click(window, "NotesView_All"); Click(window, "NotesAccountFilter"); Click(window, "NotesAccount_Accounttwo"); Assert.Equal(2, window.GetVisualDescendants().OfType<Border>().Count(b => b.Name?.StartsWith("PageRow_") == true));
            Type(window, "NotesSearch", "zzz"); Settle(window); Assert.Equal("No match", Find<TextBlock>(window, "NotesPageEmpty").Text);
            Type(window, "NotesSearch", "call"); Settle(window); Assert.Single(window.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("PageRow_") == true));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AddingANoteAndAMeetingOnThePageThroughOneForm()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab");
            Click(window, "PageSubmit"); Assert.Equal("Write a note first", Find<TextBlock>(window, "PageError").Text); // Told under the line.
            Type(window, "PageNoteField", "Draft the plan"); Click(window, "PageSubmit");
            Assert.Equal("Draft the plan", model.Notes.Text(Assert.Single(model.Notes.All))); Assert.Equal("Note added", model.Notice);
            window.GetVisualDescendants().OfType<NotesPage>().Single().SetKind(true); Settle(window); Type(window, "PageNoteField", "Review"); Type(window, "PageStart", "12:00"); Type(window, "PageEnd", "12:30");
            Click(window, "PageWhen"); Click(window, "ReminderSet"); // Today at the pop-up's own default, midday.
            Click(window, "PageSubmit");
            var meeting = model.Notes.MeetingsOn(Day).Single(); Assert.Equal(TimeSpan.FromHours(12), meeting.At); Assert.Equal(TimeSpan.FromHours(12.5), meeting.End);
        }
        finally { window.Close(); }
    }


    [AvaloniaFact]
    public void TheOpenRowHasDeleteCancelAndSaveOnItsFootLineWithDeleteAsAnIcon()
    {
        var model = Model(Seed); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab");
            foreach (var text in new[] { "Tidy the drive", "Client call" })
            {
                Click(window, "PageNoteOpen_" + IdOf(model, text));
                var delete = Find<Button>(window, "PageDelete"); var cancel = Find<Button>(window, "PageCancel"); var save = Find<Button>(window, "PageSubmit");
                double Mid(Control c) => c.TranslatePoint(new Point(0, c.Bounds.Height / 2), window)!.Value.Y; double Left(Control c) => c.TranslatePoint(default, window)!.Value.X;
                Assert.Equal(Mid(delete), Mid(cancel), 4); Assert.Equal(Mid(cancel), Mid(save), 4); // One line, in the order Delete, Cancel, Save.
                Assert.True(Left(delete) < Left(cancel) && Left(cancel) < Left(save));
                Assert.IsType<IconGlyph>(delete.Content); Assert.Equal(IconKind.Trash, ((IconGlyph)delete.Content!).Kind); Assert.Contains("Delete", ToolTip.GetTip(delete)?.ToString());
                Assert.Contains(save.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "Save"); Assert.Contains("primary", save.Classes);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EditingAMeetingKeepsDayStartEndRepeatAndAccountOnOneLine()
    {
        var model = Model(Seed); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab"); Click(window, "PageNoteOpen_" + IdOf(model, "Client call"));
            double Mid(Control c) => c.TranslatePoint(new Point(0, c.Bounds.Height / 2), window)!.Value.Y;
            var day = Find<Button>(window, "PageWhen"); var start = Find<TextBox>(window, "PageStart"); var end = Find<TextBox>(window, "PageEnd"); var repeat = Find<ChoiceStrip>(window, "PageRepeat"); var account = Find<ChoiceStrip>(window, "PageAccount");
            Assert.NotNull(repeat); Assert.NotNull(account); // chips and segments may wrap onto a second line
            foreach (var c in new Control[] { start, end }) Assert.Equal(Mid(day), Mid(c), 4);
            Assert.Equal("14:00", start.Text); Assert.Equal("15:00", end.Text);
            Assert.Equal("Start, HH:mm", ToolTip.GetTip(start)?.ToString());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressingARowOpensItInPlaceAndTheAddLineRestsUntilItIsClosed()
    {
        var model = Model(Seed); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab"); var id = IdOf(model, "Send the budget"); var other = IdOf(model, "Tidy the drive");
            Type(window, "PageNoteField", "A draft in the add line");
            double Top(string name) => Find<Border>(window, name).TranslatePoint(default, window)!.Value.Y;
            var before = Top("PageRow_" + id);
            Click(window, "PageNoteOpen_" + id);
            // The row is its own editor, where it was: it holds the words and the controls, and has the accent edge.
            var row = Find<Border>(window, "PageRow_" + id); Assert.Contains("rowSelected", row.Classes);
            Assert.Equal("Send the budget", row.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PageNoteField").Text);
            Assert.Equal(before, Top("PageRow_" + id), 1.5); // Nothing above it moved.
            foreach (var name in new[] { "PageNoteOpen_", "PageNoteImportant_", "PageNoteRemove_", "PageNoteReminder_" }) Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == name + id); // No second copy of its controls.
            Assert.NotNull(Find<Border>(window, "PageAddResting")); Assert.Single(window.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == "PageNoteField")); // One place to type.
            Assert.NotNull(Find<Button>(window, "PageNoteOpen_" + other)); // Other rows stay as they were.
            Click(window, "PageCancel");
            Assert.NotNull(Find<Button>(window, "PageNoteOpen_" + id)); Assert.Equal("A draft in the add line", Find<TextBox>(window, "PageNoteField").Text); // The draft comes back.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SavingARowChangesItAndEscapePutsTheSavedWordsBack()
    {
        var model = Model(Seed); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab"); var id = IdOf(model, "Tidy the drive");
            Click(window, "PageNoteOpen_" + id);
            Assert.Equal("Tidy the drive", Find<TextBox>(window, "PageNoteField").Text); Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Editing a note"); // There is no form at the top to be told about.
            Type(window, "PageNoteField", "Tidy the shared drive"); Find<TextBox>(window, "PageNoteField").Focus(); HeadlessWindowExtensions.KeyPressQwerty(window, Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None); Settle(window);
            Assert.Equal("Tidy the drive", model.Notes.Text(model.Notes.Find(id)!)); Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "PageAddResting"); // Escape closed it without saving.
            Click(window, "PageNoteOpen_" + id); Type(window, "PageNoteField", "Tidy the shared drive"); Click(window, "PageSubmit");
            Assert.Equal("Tidy the shared drive", model.Notes.Text(model.Notes.Find(id)!)); Assert.Equal("Note saved", model.Notice);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ARowsStarAndBinAppearOnHoverAndAnImportantNoteKeepsItsStar()
    {
        var model = Model(Seed); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab"); var plain = IdOf(model, "Tidy the drive"); model.Notes.SetImportant(IdOf(model, "Send the budget"), true); Settle(window);
            var bin = Find<Button>(window, "PageNoteRemove_" + plain); Assert.Equal(0, bin.Opacity); Assert.False(bin.IsHitTestVisible);
            Assert.Equal(0, Find<ToggleButton>(window, "PageNoteImportant_" + plain).Opacity);
            Assert.Equal(1, Find<ToggleButton>(window, "PageNoteImportant_" + IdOf(model, "Send the budget")).Opacity); // Shown without a hover.
            var row = Find<Border>(window, "PageRow_" + plain); var at = row.TranslatePoint(new Point(40, 20), window)!.Value;
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, at); Settle(window);
            Assert.Equal(1, bin.Opacity); Assert.True(bin.IsHitTestVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RemovingARepeatingMeetingAsksInTheRowAndNotesGoAtOnce()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Stand-up", null, RepeatChoice.Day); model.Notes.AddNote("A note", null, null);
            Click(window, "NotesTab");
            Click(window, "PageNoteRemove_" + IdOf(model, "A note"));
            Assert.DoesNotContain(model.Notes.All, n => model.Notes.Text(n) == "A note"); // The note went at once.
            Click(window, "PageNoteRemove_" + model.Notes.MeetingsOn(Day).Single().Id); Assert.NotNull(Find<Border>(window, "PageScope")); // The meeting asks first, in its own row.
            Click(window, "PageRemoveKeep"); Assert.NotEmpty(model.Notes.All);
            Click(window, "PageNoteRemove_" + model.Notes.MeetingsOn(Day).Single().Id); Click(window, "PageScopeAll"); Click(window, "PageRemoveConfirm"); Assert.Empty(model.Notes.All);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheViewsAreOneRowOfTabsWithCountsAndTheAccountFilterIsOneButton()
    {
        var model = Model(Seed); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab");
            var tabs = Find<Border>(window, "NotesViews"); Assert.Contains("viewTabs", tabs.Classes);
            Assert.Equal(7, tabs.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("NotesView_") == true)); // All, Important, Today, Overdue, Upcoming, Meetings, Done.
            Assert.Contains("selected", Find<Button>(window, "NotesView_All").Classes);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<WrapPanel>(), w => w.Name == "NotesAccountPills"); // The row of account pills is gone.
            Assert.Contains(Find<Button>(window, "NotesAccountFilter").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "All accounts");
            Click(window, "NotesAccountFilter"); Click(window, "NotesAccount_Accountone");
            Assert.Contains(Find<Button>(window, "NotesAccountFilter").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Account one");
            Click(window, "NotesAccount_Accounttwo");
            Assert.Contains(Find<Button>(window, "NotesAccountFilter").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "2 accounts");
            Assert.Contains(Find<Button>(window, "NotesAccountFilter").Classes, c => c == "on");
        }
        finally { window.Close(); }
    }
}
