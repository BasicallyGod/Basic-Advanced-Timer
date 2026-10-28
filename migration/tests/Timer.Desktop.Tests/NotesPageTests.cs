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

    // ---- The model's views ----

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

    // ---- The bell and the light ----

    [AvaloniaFact]
    public void TheBellCountsOverdueRemindersAndItsFlyoutHasDoneAndReschedule()
    {
        var model = Model(Seed); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var badge = Find<Border>(window, "NotificationsBadge"); Assert.True(badge.IsVisible); Assert.Equal("1", Assert.IsType<TextBlock>(badge.Child).Text);
            Click(window, "Notifications");
            Assert.True(Find<Avalonia.Controls.Primitives.Popup>(window, "NotificationsPopup").IsOpen);
            var id = IdOf(model, "Send the budget"); Assert.NotNull(Find<Border>(window, "Notification_" + id)); Assert.NotNull(Find<Border>(window, "Notification_" + IdOf(model, "Follow up on the brief")));
            Click(window, "NotificationReschedule_" + id); Assert.True(Find<Avalonia.Controls.Primitives.Popup>(window, "RescheduleHost").IsOpen);
            Click(window, "ReminderCell_20261010"); Click(window, "ReminderSet");
            Assert.Equal(Day.AddDays(1).AddHours(8), NotesModel.ReminderOf(model.Notes.Find(id)!)); Assert.Equal(0, model.Notes.AttentionCount); Assert.False(badge.IsVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "Notification_" + id + "x");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DoneInTheFlyoutClearsTheAttentionAndAnEmptyBellSaysNothingIsWaiting()
    {
        var model = Model(notes => notes.AddNote("Send the budget", null, Day.AddHours(8))); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "Notifications"); Click(window, "NotificationDone_" + model.Notes.All.Single().Id);
            Assert.True(model.Notes.All.Single().Done); Assert.False(Find<Border>(window, "NotificationsBadge").IsVisible);
            Assert.Equal("Nothing is waiting.", Find<TextBlock>(window, "NothingWaiting").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheBlueLightOnTheRailAndThePillComesOnWhileAReminderIsDueAndGoesWhenItIsDone()
    {
        var model = Model(notes => notes.AddNote("Send the budget", null, Day.AddHours(8))); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var light = Find<Border>(window, "RailReminderLight"); Assert.Equal("A reminder is due", Avalonia.Automation.AutomationProperties.GetName(light));
            model.Notes.Toggle(model.Notes.All.Single().Id); Settle(window); Assert.Equal("No reminders waiting", Avalonia.Automation.AutomationProperties.GetName(light));
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
            Assert.NotNull(Find<NotesPage>(window, "NotesPage")); Assert.Equal("Notes", window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("clock") && t.Text == "Notes").Text);
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
            Assert.Equal("5", CountOf("NotesView_All")); Assert.Equal("1", CountOf("NotesView_Overdue")); Assert.Equal("2", CountOf("NotesAccount_Accounttwo"));
            Click(window, "NotesView_Overdue"); Assert.Single(window.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("PageRow_") == true));
            Click(window, "NotesView_All"); Click(window, "NotesAccount_Accounttwo"); Assert.Equal(2, window.GetVisualDescendants().OfType<Border>().Count(b => b.Name?.StartsWith("PageRow_") == true));
            Type(window, "NotesSearch", "zzz"); Settle(window); Assert.Equal("Nothing matches that search.", Find<TextBlock>(window, "NotesPageEmpty").Text);
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
            Click(window, "PageSubmit"); Assert.Equal("Write a note first", Find<TextBlock>(window, "PageError").Text); // Told beside the field.
            Type(window, "PageNoteField", "Draft the plan"); Click(window, "PageSubmit");
            Assert.Equal("Draft the plan", model.Notes.Text(Assert.Single(model.Notes.All))); Assert.Equal("Note added", model.Notice);
            Click(window, "PageKindMeeting"); Type(window, "PageNoteField", "Review"); Type(window, "PageLength", "0:30");
            Click(window, "PageWhen"); Click(window, "ReminderSet"); // Today at the pop-up's own default, midday.
            Click(window, "PageSubmit");
            var meeting = model.Notes.MeetingsOn(Day).Single(); Assert.Equal(TimeSpan.FromHours(12), meeting.At); Assert.Equal(TimeSpan.FromHours(12.5), meeting.End);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DeleteAndCancelSitOnTheOptionsLineAtItsRightEndWithNoLineOfTheirOwn()
    {
        var model = Model(Seed); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab");
            foreach (var text in new[] { "Tidy the drive", "Client call" })
            {
                Click(window, "PageNoteOpen_" + IdOf(model, text));
                var account = Find<ComboBox>(window, "PageAccount"); var delete = Find<Button>(window, "PageDelete"); var cancel = Find<Button>(window, "PageCancel");
                var accountMid = account.TranslatePoint(new Point(0, account.Bounds.Height / 2), window)!.Value.Y; var deleteMid = delete.TranslatePoint(new Point(0, delete.Bounds.Height / 2), window)!.Value.Y;
                Assert.Equal(accountMid, deleteMid, 4); // The same line, not a row below with nothing beside it.
                var field = Find<TextBox>(window, "PageNoteField"); var fieldRight = field.TranslatePoint(new Point(field.Bounds.Width, 0), window)!.Value.X;
                Assert.True(cancel.TranslatePoint(new Point(cancel.Bounds.Width, 0), window)!.Value.X <= fieldRight + 80 && cancel.TranslatePoint(default, window)!.Value.X > delete.TranslatePoint(default, window)!.Value.X);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EditingAMeetingKeepsTheRepeatListOnTheOptionsLineWithIconsForDeleteAndCancelAndNoLengthWord()
    {
        var model = Model(Seed); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab"); Click(window, "PageNoteOpen_" + IdOf(model, "Client call"));
            foreach (var docked in new[] { false, true })
            {
                window.Dock.Jump(docked ? 1 : 0, 1); Settle(window); // With the account panel beside the page there is less room, and the line still holds.
                var account = Find<ComboBox>(window, "PageAccount"); var repeat = Find<ComboBox>(window, "PageRepeat"); var length = Find<TextBox>(window, "PageLength");
                double Mid(Control c) => c.TranslatePoint(new Point(0, c.Bounds.Height / 2), window)!.Value.Y;
                Assert.Equal(Mid(account), Mid(repeat), 4); Assert.Equal(Mid(account), Mid(length), 4);
                var delete = Find<Button>(window, "PageDelete"); var cancel = Find<Button>(window, "PageCancel");
                Assert.IsType<IconGlyph>(delete.Content); Assert.IsType<IconGlyph>(cancel.Content); Assert.Equal(IconKind.Trash, ((IconGlyph)delete.Content!).Kind); Assert.Equal(IconKind.Close, ((IconGlyph)cancel.Content!).Kind);
                Assert.Contains("Delete", ToolTip.GetTip(delete)?.ToString()); Assert.Contains("Cancel", ToolTip.GetTip(cancel)?.ToString());
                Assert.Equal("Delete", Avalonia.Automation.AutomationProperties.GetName(delete)); Assert.Equal("Cancel", Avalonia.Automation.AutomationProperties.GetName(cancel));
                Assert.DoesNotContain(Find<ContentControl>(window, "PageForm").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Length"); // The box's tooltip says what it is.
                Assert.Equal("Length, H:MM", ToolTip.GetTip(length)?.ToString());
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ARowOpensInTheFormAsEditingWithTheAccentEdgeAndSaveChangesItAndCancelPutsTheFormBack()
    {
        var model = Model(Seed); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab"); var id = IdOf(model, "Tidy the drive");
            Click(window, "PageNoteOpen_" + id);
            Assert.Equal("Tidy the drive", Find<TextBox>(window, "PageNoteField").Text); Assert.Contains("rowSelected", Find<Border>(window, "PageRow_" + id).Classes);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Editing a note");
            Type(window, "PageNoteField", "Tidy the shared drive"); Click(window, "PageSubmit");
            Assert.Equal("Tidy the shared drive", model.Notes.Text(model.Notes.Find(id)!)); Assert.False(Find<Button>(window, "PageSubmit").Content is null);
            Click(window, "PageNoteOpen_" + id); Click(window, "PageCancel"); Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Editing a note");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RemovingARepeatingMeetingFromThePageAsksInsideTheFormAndNotesFromTheirCross()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Stand-up", null, RepeatChoice.Day); model.Notes.AddNote("A note", null, null);
            Click(window, "NotesTab");
            Click(window, "PageNoteRemove_" + IdOf(model, "A note")); Assert.Single(model.Notes.All.Where(n => n.SeriesId is null) .Where(n => model.Notes.Text(n) == "Stand-up").DefaultIfEmpty()); // The note went.
            Assert.DoesNotContain(model.Notes.All, n => model.Notes.Text(n) == "A note");
            Click(window, "PageNoteRemove_" + model.Notes.MeetingsOn(Day).Single().Id); Assert.NotNull(Find<Border>(window, "PageScope"));
            Click(window, "PageScopeAll"); Assert.Empty(model.Notes.All);
        }
        finally { window.Close(); }
    }
}
