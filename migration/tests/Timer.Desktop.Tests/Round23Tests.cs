using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 23: new timers start with window shadows off, and the demo holds enough to try every function.
public sealed class Round23Tests
{
    [Fact]
    public void ANewDatabaseStartsWithWindowShadowsOffAndAChoiceMadeAfterwardsIsKept()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-shadows-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store);
                Assert.False(model.Shadows); // The first run turns them off...
                model.SetShadows(true); // ...and the setting still works.
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store);
                Assert.True(model.Shadows); // A choice is not undone the next time it opens.
            }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void ADatabaseThatWasAlreadySetUpKeepsItsShadows()
    {
        var engine = new TrackingEngine();
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one") }, false, "Light", DayOfWeek.Monday, SetupDone: true), null);
        Assert.True(model.Shadows);
    }

    [Fact]
    public void TheDemoHoldsEveryKindOfBlockMeetingAndNote()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 14, 14, 15, 0));
        var today = new DateTime(2026, 10, 14);
        // Accounts: a budget per day, one hidden from the tracker, and the others.
        Assert.Contains(model.AccountSettings, a => a.BudgetPeriod == "day"); Assert.Contains(model.AccountSettings, a => a.Hidden); Assert.True(model.AccountSettings.Count >= 9);
        // Time: weeks of history, overlapping blocks today, a block planned for later, notes on blocks and a merged block.
        Assert.True(model.Engine.Sessions.Select(s => s.Day).Distinct().Count() > 40);
        var todays = model.Engine.Sessions.Where(s => s.Day == today).ToList();
        Assert.Contains(todays, a => todays.Any(b => !ReferenceEquals(a, b) && a.From < b.To && b.From < a.To)); // Overlap.
        Assert.Contains(todays, s => s.From > new DateTime(2026, 10, 14, 14, 15, 0)); // Planned for later.
        Assert.Contains(todays, s => model.Editor.Note(s).Length > 0);
        Assert.NotEmpty(model.Editor.GroupsCsv ?? "") ; Assert.Contains(model.Engine.Schedule.Overrides, o => o.Value.Off);
        // Meetings repeat every way and overlap; notes are late, coming, important, linked, done and undated.
        var notes = model.Notes.All;
        var repeats = notes.Where(n => NotesModel.IsMeeting(n)).Select(n => model.Notes.RepeatOf(n)).Distinct().ToList();
        Assert.Contains(RepeatChoice.Day, repeats); Assert.Contains(RepeatChoice.Week, repeats); Assert.Contains(RepeatChoice.BiWeek, repeats); Assert.Contains(RepeatChoice.Month, repeats);
        var meetings = model.Notes.MeetingsOn(today); Assert.True(meetings.Count >= 8);
        Assert.Contains(meetings, a => meetings.Any(b => a.Id != b.Id && a.At < b.End && b.At < a.End));
        Assert.True(model.Notes.Overdue.Count >= 4);
        Assert.Contains(notes, n => NotesModel.IsImportant(n)); Assert.Contains(notes, n => n.Text.Contains("](https://")); Assert.Contains(notes, n => n.Done);
        Assert.Contains(notes, n => !n.HasDate); Assert.Contains(notes, n => n.Text.Length > 120);
        Assert.True(model.Notes.PinnedOn(today).Count(n => NotesModel.ReminderOf(n) is { } due && due.Hour == 15 && due.Minute is >= 30 and <= 35) >= 3); // A cluster at one minute.
        Assert.True(notes.Count >= 35);
    }
}
