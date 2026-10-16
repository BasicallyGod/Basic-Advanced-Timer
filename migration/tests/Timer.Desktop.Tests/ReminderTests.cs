using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class ReminderTests
{
    private static readonly DateTime Day = new(2026, 10, 16);
    private static DayNote At(string time, string text = "Call", long id = 1, bool done = false) => new(id, Day, TimeSpan.Parse(time), text, done);
    private static IReadOnlyList<Reminder> Rules(DateTime now, int lead, params DayNote[] notes) => ReminderRules.For(notes, now, lead);

    [Fact]
    public void ANoteWithATimeIsSoonThenDueThenOverdue()
    {
        var note = At("14:30");
        Assert.Empty(Rules(Day.AddHours(14).AddMinutes(10), 10, note));
        Assert.Equal(ReminderState.Soon, Assert.Single(Rules(Day.AddHours(14).AddMinutes(21), 10, note)).State);
        Assert.Equal(ReminderState.Due, Assert.Single(Rules(Day.AddHours(14).AddMinutes(30), 10, note)).State);
        Assert.Equal(ReminderState.Due, Assert.Single(Rules(Day.AddHours(14).AddMinutes(34), 10, note)).State);
        Assert.Equal(ReminderState.Overdue, Assert.Single(Rules(Day.AddHours(14).AddMinutes(36), 10, note)).State);
    }

    [Fact]
    public void NoLeadTimeMeansNoHeadsUp()
    {
        Assert.Empty(Rules(Day.AddHours(14).AddMinutes(29), 0, At("14:30")));
        Assert.Equal(ReminderState.Due, Assert.Single(Rules(Day.AddHours(14).AddMinutes(30), 0, At("14:30"))).State);
    }

    [Fact]
    public void OnlyOpenNotesWithATimeOrADeadlineRemind()
    {
        var now = Day.AddHours(18);
        Assert.Empty(Rules(now, 10, At("14:30", done: true)));
        Assert.Empty(Rules(now, 10, new DayNote(2, Day, null, "Plain task")));
        // A deadline with no time falls due at 17:00.
        var deadline = Assert.Single(Rules(now, 10, new DayNote(3, Day, null, "! Timesheet")));
        Assert.Equal(Day.AddHours(17), deadline.DueAt); Assert.Equal(ReminderState.Overdue, deadline.State);
    }

    [Fact]
    public void ASnoozedNoteIsHiddenUntilTheSnoozeEndsThenDueAgain()
    {
        var note = At("14:30"); var snoozed = new Dictionary<long, DateTime> { [1] = Day.AddHours(14).AddMinutes(50) };
        Assert.Empty(ReminderRules.For(new[] { note }, Day.AddHours(14).AddMinutes(45), 10, snoozed));
        Assert.Equal(ReminderState.Due, Assert.Single(ReminderRules.For(new[] { note }, Day.AddHours(14).AddMinutes(51), 10, snoozed)).State);
    }

    [Fact]
    public void ANoteMoreThanADayOldIsLeftAlone()
    {
        Assert.Empty(ReminderRules.For(new[] { new DayNote(1, Day.AddDays(-2), TimeSpan.FromHours(9), "Old") }, Day.AddHours(10), 10));
        Assert.Single(ReminderRules.For(new[] { new DayNote(1, Day.AddDays(-1), TimeSpan.FromHours(23), "Late") }, Day.AddHours(9), 10)); // Fell due while the app was closed.
    }

    [Fact]
    public void TheCardSaysWhatIsHappening()
    {
        var note = At("14:30");
        string Line(int minute) => ReminderRules.Describe(Assert.Single(Rules(Day.AddHours(14).AddMinutes(minute), 10, note)), Day.AddHours(14).AddMinutes(minute));
        Assert.Equal("In 9 minutes · 14:30", Line(21)); Assert.Equal("Due now · 14:30", Line(31)); Assert.Equal("Overdue by 12 minutes", Line(42));
    }

    private static (TimerModel Model, Func<TimeSpan, TimeSpan> Move) Demo()
    {
        var offset = TimeSpan.Zero; var start = DateTime.Now;
        var model = TimerModel.CreateDemo(() => start + offset);
        return (model, by => offset += by);
    }

    [Fact]
    public void TheDemosTimedNoteFallsDueAndIsAnnouncedOnce()
    {
        var (model, move) = Demo(); var heard = 0; model.ReminderDue += _ => heard++;
        model.Tick(); Assert.Equal(0, heard); Assert.Null(model.ActiveReminder); // 14:15, and the note is at 14:25: a heads-up only.
        Assert.Equal(ReminderState.Soon, Assert.Single(model.Reminders).State);
        move(TimeSpan.FromMinutes(11)); model.Tick(); model.Tick();
        Assert.Equal(1, heard); Assert.Equal(ReminderState.Due, model.ActiveReminder!.State);
        move(TimeSpan.FromMinutes(10)); model.Tick(); Assert.Equal(ReminderState.Overdue, model.ActiveReminder!.State); Assert.Equal(1, heard);
    }

    [Fact]
    public void SnoozeHidesTheCardAndDoneTicksTheNote()
    {
        var (model, move) = Demo(); move(TimeSpan.FromMinutes(11)); model.Tick();
        var id = model.ActiveReminder!.Note.Id;
        model.SnoozeReminder(id); Assert.Null(model.ActiveReminder);
        move(TimeSpan.FromMinutes(model.ReminderSnooze + 1)); model.Tick(); Assert.Equal(id, model.ActiveReminder!.Note.Id);
        model.DoneReminder(id); Assert.Null(model.ActiveReminder); Assert.True(model.AllDayNotes.Single(n => n.Id == id).Done);
    }

    [Fact]
    public void TheSettingsAreKeptAndRemindersCanBeTurnedOff()
    {
        var (model, move) = Demo(); move(TimeSpan.FromMinutes(11)); model.Tick();
        Assert.True(model.RemindersOn); Assert.Equal(10, model.ReminderLead); Assert.Equal(10, model.ReminderSnooze);
        model.SetReminderLead(15); model.SetReminderSnooze(30); Assert.Equal(15, model.ReminderLead); Assert.Equal(30, model.ReminderSnooze);
        model.SetRemindersOn(false); Assert.Empty(model.Reminders); Assert.Null(model.ActiveReminder);
        model.SetRemindersOn(true); Assert.NotEmpty(model.Reminders);
    }

    [AvaloniaFact]
    public void ADueReminderShowsACardInThePillAndOnTheDashboard()
    {
        var (model, move) = Demo(); move(TimeSpan.FromMinutes(11));
        var pill = new PillWindow(model); pill.Show(); var dashboard = new DashboardWindow(model); dashboard.Show(); model.Tick(); Dispatcher.UIThread.RunJobs();
        try
        {
            var pillCard = pill.GetVisualDescendants().OfType<ReminderCard>().Single(); var dashCard = dashboard.GetVisualDescendants().OfType<ReminderCard>().Single();
            Assert.True(pillCard.IsVisible && dashCard.IsVisible);
            Assert.Contains("revised budget", pillCard.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ReminderText").Text);
            Assert.True(pill.Height / pill.Scale > PillWindow.CollapsedWindowHeight + 20); // The window grew to hold the card under the capsule.
            Assert.True(dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NotesBadge").IsVisible);
            dashCard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ReminderDone").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.False(pillCard.IsVisible); Assert.False(dashCard.IsVisible);
        }
        finally { pill.Close(); dashboard.Close(); }
    }
}
