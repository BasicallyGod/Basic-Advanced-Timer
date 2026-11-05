using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class ReliabilityTests
{
    private static readonly DateTime Day = new(2026, 11, 5);

    [Fact]
    public void ReschedulingAndRemovingAReminderKeepItsAccountAndImportance()
    {
        var notes = new NotesModel(null, () => Day, () => new[] { "Account one" });
        notes.AddNote("!! Call the client", "Account one", Day.AddHours(9));
        var id = notes.All.Single().Id;
        foreach (var reminder in new DateTime?[] { Day.AddHours(10), null })
        {
            notes.SetReminder(id, reminder);
            var note = notes.Find(id)!;
            Assert.Equal("Account one", notes.AccountOf(note));
            Assert.Equal("Call the client", notes.Text(note));
            Assert.True(NotesModel.IsImportant(note));
            Assert.Equal(reminder, NotesModel.ReminderOf(note));
        }
    }

    [Theory]
    [InlineData("INSERT")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    public void FailedSeriesWritesAreRetriedAndSurviveReopening(string operation)
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-series-retry-" + Guid.NewGuid());
        int count;
        try
        {
            using (var store = new TimerStore(folder))
            {
                store.Load(new TrackingEngine(clock: () => Day));
                var notes = new NotesModel(store, () => Day, () => Array.Empty<string>());
                if (operation != "INSERT") notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Original", null, RepeatChoice.Week);
                store.Database.Execute($"CREATE TRIGGER block_series BEFORE {operation} ON note_series BEGIN SELECT RAISE(ABORT, 'disk full'); END");
                if (operation == "INSERT") notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Changed", null, RepeatChoice.Week);
                else if (operation == "UPDATE") notes.EditMeeting(notes.All.First().Id, "Changed", null, Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), RepeatChoice.Week, true);
                else notes.Remove(notes.All.First().Id, true);
                count = notes.All.Count;
                Assert.True(notes.Unsaved);
                notes.RetryUnsaved();
                Assert.True(notes.Unsaved);
                Assert.Equal(count, notes.All.Count);
                store.Database.Execute("DROP TRIGGER block_series");
                notes.RetryUnsaved();
                Assert.False(notes.Unsaved);
                Assert.Null(notes.Fault);
                Assert.All(notes.All, n => { Assert.True(n.Id > 0); Assert.True(n.SeriesId > 0); });
            }
            using (var store = new TimerStore(folder))
            {
                var reopened = new NotesModel(store, () => Day, () => Array.Empty<string>());
                Assert.Equal(count, reopened.All.Count);
                Assert.All(reopened.All, n => Assert.Equal("Changed", reopened.Text(n)));
                Assert.Equal(operation == "DELETE" ? 0 : 1, store.LoadSeries().Count);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(14)]
    public void MovingThisAndLaterSplitsAtTheOriginalOccurrence(int days)
    {
        var notes = new NotesModel(null, () => Day, () => Array.Empty<string>());
        notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Original", null, RepeatChoice.Week);
        var occurrences = notes.All.OrderBy(n => n.Day).ToArray();
        var selected = occurrences[2];
        Assert.Null(notes.EditMeeting(selected.Id, "Changed", null, selected.Day.AddDays(days), selected.At!.Value, selected.End!.Value, RepeatChoice.Week, true));
        Assert.Equal(occurrences.Take(2).Select(n => n.Id).ToArray(),
            notes.All.Where(n => notes.Text(n) == "Original").Select(n => n.Id).ToArray());
        Assert.Equal(selected.Day.AddDays(days), notes.Find(selected.Id)!.Day);
        Assert.All(notes.All.Where(n => notes.Text(n) == "Changed"), n => Assert.True(n.Day >= selected.Day.AddDays(days)));
        Assert.DoesNotContain(notes.All, n => occurrences.Skip(3).Any(old => old.Id == n.Id));
        Assert.Equal(selected.Day.AddDays(days).AddDays(7), notes.All.Where(n => notes.Text(n) == "Changed").OrderBy(n => n.Day).Skip(1).First().Day);
    }

    [Fact]
    public void PendingNotesStayVisibleAsUnsavedEvenAfterALedgerSaveSucceeds()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-note-warning-" + Guid.NewGuid());
        try
        {
            using var store = new TimerStore(folder);
            var engine = new TrackingEngine(clock: () => Day);
            var model = new TimerModel(engine, store.Load(engine), store);
            store.Database.Execute("CREATE TRIGGER block_notes BEFORE INSERT ON day_notes BEGIN SELECT RAISE(ABORT, 'disk full'); END");
            model.Notes.AddNote("Kept locally", null, null);
            model.Tell("Note saved");
            Assert.Equal("Not saved yet · kept on screen, retrying", model.Notice);
            Assert.NotNull(model.SaveError);
            Assert.True(model.Save());
            Assert.NotNull(model.SaveError);
            store.Database.Execute("DROP TRIGGER block_notes");
            model.Notes.RetryUnsaved();
            Assert.Null(model.SaveError);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void FailedGenerationRollsBackTheOccurrencesAndTheCursorTogether()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-generation-retry-" + Guid.NewGuid());
        var now = Day;
        try
        {
            int count;
            using (var store = new TimerStore(folder))
            {
                store.Load(new TrackingEngine(clock: () => now));
                var notes = new NotesModel(store, () => now, () => Array.Empty<string>());
                notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Daily", null, RepeatChoice.Day);
                count = notes.All.Count;
                store.Database.Execute("CREATE TRIGGER block_cursor BEFORE UPDATE ON note_series BEGIN SELECT RAISE(ABORT, 'disk full'); END");
                now = now.AddDays(1);
                Assert.Equal(1, notes.MakeSeriesNotes());
                Assert.Equal(count, store.LoadDayNotes().Count);
                Assert.Equal(count + 1, notes.All.Count);
                Assert.True(notes.Unsaved);
                notes.RetryUnsaved();
                Assert.Equal(count, store.LoadDayNotes().Count);
                store.Database.Execute("DROP TRIGGER block_cursor");
                notes.RetryUnsaved();
                Assert.False(notes.Unsaved);
            }
            using (var store = new TimerStore(folder))
            {
                var reopened = new NotesModel(store, () => now, () => Array.Empty<string>());
                Assert.Equal(count + 1, reopened.All.Count);
                Assert.Equal(reopened.All.Count, reopened.All.Select(n => n.Day).Distinct().Count());
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void APartlyRefusedRetryPublishesNoIdsAndCreatesNoDuplicates()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-atomic-retry-" + Guid.NewGuid());
        try
        {
            using var store = new TimerStore(folder);
            store.Load(new TrackingEngine(clock: () => Day));
            var notes = new NotesModel(store, () => Day, () => Array.Empty<string>());
            store.Database.Execute("CREATE TRIGGER block_notes BEFORE INSERT ON day_notes BEGIN SELECT RAISE(ABORT, 'disk full'); END");
            notes.AddNote("First", null, null); notes.AddNote("Second", null, null);
            var ids = notes.All.Select(n => n.Id).ToArray();
            store.Database.Execute("DROP TRIGGER block_notes");
            store.Database.Execute("CREATE TRIGGER block_second BEFORE INSERT ON day_notes WHEN NEW.text = 'Second' BEGIN SELECT RAISE(ABORT, 'disk full'); END");
            notes.RetryUnsaved();
            Assert.Empty(store.LoadDayNotes());
            Assert.Equal(ids, notes.All.Select(n => n.Id).ToArray());
            store.Database.Execute("DROP TRIGGER block_second");
            notes.RetryUnsaved();
            Assert.False(notes.Unsaved);
            Assert.Equal(2, store.LoadDayNotes().Count);
            notes.SetReminder(ids[0], Day.AddHours(10));
            Assert.Equal(Day.AddHours(10), NotesModel.ReminderOf(notes.Find(ids[0])!));
        }
        finally { Directory.Delete(folder, true); }
    }
}
