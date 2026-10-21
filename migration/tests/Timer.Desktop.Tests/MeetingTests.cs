using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>A note typed 14:00 - 15:30 is a meeting: planned time on the timeline, a reminder before it, and one question after it.</summary>
public sealed class MeetingTests
{
    private static readonly DateTime Day = new(2026, 10, 21);
    private DateTime now = Day.AddHours(12);

    private TimerModel Model() { var model = new TimerModel(new TrackingEngine(clock: () => now)); model.CompleteSetup(); return model; }
    private static string FirstAccount(TimerModel model) => model.AccountNames[0];

    [Fact]
    public void ARangeInANoteMakesAMeetingAndABackwardsRangeDoesNot()
    {
        var model = Model();
        model.AddDayNote(Day, "Client call 14:00 - 15:30");
        var meeting = Assert.Single(model.AllDayNotes);
        Assert.Equal("Client call", meeting.Text); Assert.Equal(new TimeSpan(14, 0, 0), meeting.At); Assert.Equal(new TimeSpan(15, 30, 0), meeting.End);
        Assert.Equal(meeting, Assert.Single(model.MeetingsOn(Day)));
        Assert.Equal("14:00–15:30", NoteTimes.Of(meeting));
        model.AddDayNote(Day, "Odd 15:30 - 14:00");
        Assert.Single(model.MeetingsOn(Day)); // The backwards one is only a timed note.
    }

    [Fact]
    public void AMeetingRemindsBeforeItAndAsksOnceAfterItButIsNeverOverdueInBetween()
    {
        var model = Model();
        model.AddDayNote(Day, "Client call 14:00 - 15:30");
        Assert.Null(model.ActiveReminder);                                  // Still hours away.
        now = Day.AddHours(13).AddMinutes(55); Assert.Null(model.ActiveReminder); // A heads-up has no card.
        now = Day.AddHours(14).AddMinutes(2); Assert.Equal(ReminderState.Due, model.ActiveReminder!.State);
        now = Day.AddHours(14).AddMinutes(30); Assert.Null(model.ActiveReminder); // Under way: not overdue.
        now = Day.AddHours(15).AddMinutes(31);
        var question = model.ActiveReminder!;
        Assert.Equal(ReminderState.Followup, question.State);
        Assert.Contains("Did it take place?", model.ReminderLine(question)); Assert.Contains("14:00–15:30", model.ReminderLine(question));
        model.DoneReminder(question.Note.Id); // No.
        Assert.Null(model.ActiveReminder); Assert.Empty(model.MeetingsOn(Day)); Assert.Empty(model.Engine.Sessions);
        now = Day.AddHours(18); Assert.Null(model.ActiveReminder); // Asked once, answered, never again.
    }

    [Fact]
    public void YesOpensAddTimeOnTheMeetingsHoursAndSavingItTicksTheMeetingOffAsTrackedTime()
    {
        var model = Model(); var account = FirstAccount(model);
        model.AddDayNote(Day, $"Client call 14:00 - 15:30 {NoteShorthand.TagFor(account)}");
        now = Day.AddHours(15).AddMinutes(40);
        DateTime? from = null, to = null; model.EntryRequested += (_, start, end) => { from = start; to = end; };
        Assert.Empty(model.Engine.Sessions); // Planned time is not tracked time.
        model.LogMeeting(model.ActiveReminder!.Note.Id);
        Assert.Equal(Day.AddHours(14), from); Assert.Equal(Day.AddHours(15.5), to); Assert.True(model.MeetingLogPending);
        Assert.Equal(account, model.TakeEntryAccountHint()); Assert.Null(model.TakeEntryAccountHint()); // Offered once.
        Assert.True(model.EditEntry(null, account, from!.Value, to!.Value, "Client call"));
        Assert.False(model.MeetingLogPending); Assert.Null(model.ActiveReminder); Assert.Empty(model.MeetingsOn(Day));
        Assert.Contains(model.Engine.Sessions, s => s.Account == account && s.From == Day.AddHours(14) && s.To == Day.AddHours(15.5));
    }

    [Fact]
    public void CancellingAddTimeLeavesTheMeetingForANewEntryNotToTickItOff()
    {
        var model = Model(); var account = FirstAccount(model);
        model.AddDayNote(Day, "Client call 14:00 - 15:30"); now = Day.AddHours(16);
        model.LogMeeting(model.ActiveReminder!.Note.Id); model.EndMeetingLog(); // The dialog closed without saving.
        Assert.True(model.EditEntry(null, account, Day.AddHours(9), Day.AddHours(10), "Unrelated"));
        Assert.Single(model.MeetingsOn(Day)); Assert.Equal(ReminderState.Followup, model.ActiveReminder!.State);
    }

    [Fact]
    public void ClearingAMeetingsTimePutsTheWholeRangeBackIntoItsWords()
    {
        var model = Model(); model.AddDayNote(Day, "Client call 14:00 - 15:30");
        model.ClearNoteTime(model.AllDayNotes.Single().Id);
        var note = model.AllDayNotes.Single(); Assert.Null(note.At); Assert.Null(note.End); Assert.Equal("14:00 - 15:30 Client call", note.Text);
    }

    [AvaloniaFact]
    public void ThePlannedMeetingIsDrawnOnTheTimelineAsItsOwnThingAndTheCardAsksYesOrNo()
    {
        var model = Model(); model.AddDayNote(Day, "Client call 14:00 - 15:30");
        var window = new DashboardWindow(model); window.Show();
        try
        {
            for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
            var id = model.AllDayNotes.Single().Id;
            Assert.Single(window.GetVisualDescendants().OfType<Panel>(), p => p.Name == "TimelineMeeting_" + id);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), b => b.Name?.StartsWith("TimelineEntry") == true); // Nothing tracked.
            now = Day.AddHours(16); model.Tick(); Dispatcher.UIThread.RunJobs();
            var yes = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ReminderDone"); var no = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ReminderSnooze");
            Assert.Equal("Yes", yes.Content?.ToString()); Assert.Equal("No", no.Content?.ToString());
            no.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
            Assert.Empty(window.GetVisualDescendants().OfType<Panel>().Where(p => p.Name == "TimelineMeeting_" + id)); // Answered, so the planned block goes.
        }
        finally { window.Close(); }
    }
}
