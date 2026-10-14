using Microsoft.Data.Sqlite;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class DayNotesTests
{
    [Theory]
    [InlineData("Call the client 14:30", "Call the client", 14, 30)]
    [InlineData("@9:05 stand-up", "stand-up", 9, 5)]
    [InlineData("Lunch 12:00 with Sam", "Lunch with Sam", 12, 0)]
    public void ATimeInTheWordsBecomesTheNotesTime(string typed, string text, int hour, int minute)
    {
        var (words, at) = DayNotes.Parse(typed); Assert.Equal(text, words); Assert.Equal(new TimeSpan(hour, minute, 0), at);
    }

    [Theory]
    [InlineData("Just a note")]
    [InlineData("Version 25:99 is not a time")]
    [InlineData("Ratio 3:1 stays")]
    public void WordsWithoutATimeStayWhole(string typed) { var (words, at) = DayNotes.Parse(typed); Assert.Equal(typed, words); Assert.Null(at); }

    [Fact]
    public void ComingUpListsTodaysAndLaterNotesSoonestFirstAndSkipsDoneAndPastOnes()
    {
        var now = new DateTime(2026, 10, 14, 10, 0, 0);
        var notes = new[]
        {
            new DayNote(1, now.Date, new TimeSpan(15, 0, 0), "this afternoon"), new DayNote(2, now.Date, new TimeSpan(8, 0, 0), "this morning, gone"),
            new DayNote(3, now.Date.AddDays(1), null, "tomorrow"), new DayNote(4, now.Date.AddDays(2), null, "done", true),
            new DayNote(5, now.Date.AddDays(30), null, "too far"), new DayNote(6, now.Date.AddDays(-1), null, "yesterday"), new DayNote(7, now.Date, null, "today, no time"),
        };
        Assert.Equal(new long[] { 1, 7, 3 }, DayNotes.ComingUp(notes, now, 14).Select(n => n.Id));
    }

    [Fact]
    public void NotesAreKeptInTheDatabaseAndComeBackAfterReopening()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bat-notes-" + Guid.NewGuid().ToString("N"));
        try
        {
            long id;
            using (var store = new TimerStore(folder))
            {
                store.Load(new TrackingEngine());
                id = store.AddDayNote(new DateTime(2026, 10, 20), new TimeSpan(14, 30, 0), "Board meeting");
                store.AddDayNote(new DateTime(2026, 10, 21), null, "No time");
                store.UpdateDayNote(new DayNote(id, new DateTime(2026, 10, 20), new TimeSpan(15, 0, 0), "Board meeting moved", true));
            }
            using (var store = new TimerStore(folder))
            {
                var notes = store.LoadDayNotes(); Assert.Equal(2, notes.Count);
                Assert.Equal(new DayNote(id, new DateTime(2026, 10, 20), new TimeSpan(15, 0, 0), "Board meeting moved", true), notes[0]);
                store.DeleteDayNote(id); Assert.Single(store.LoadDayNotes());
            }
        }
        finally { SqliteConnection.ClearAllPools(); try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void AFormat2DatabaseUpgradesToHoldNotesWithItsRecordsKept()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bat-notes-up-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var db = TimerDatabase.Open(folder, () => new DateTime(2026, 10, 1)))
            {
                db.Execute("INSERT INTO accounts (id, name, colour, position) VALUES (3, 'Account one', '#336699', 0)");
                db.Execute("INSERT INTO entries (kind, account_id, day, start_at, end_at, seconds, created_at) VALUES ('work', 3, '2026-10-01', '2026-10-01T09:00:00', '2026-10-01T09:10:00', 600, '2026-10-01T09:10:00')");
            }
            SqliteConnection.ClearAllPools();
            // Back to how an existing format 2 file looks: no notes table.
            using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, TimerDatabase.FileName), Pooling = false }.ToString()))
            {
                raw.Open();
                foreach (var sql in new[] { "DROP TABLE day_notes", "PRAGMA user_version = 2" }) { using var command = raw.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
            }
            SqliteConnection.ClearAllPools();
            using var upgraded = TimerDatabase.Open(folder, () => new DateTime(2026, 10, 2));
            Assert.Equal(3, upgraded.Version);
            Assert.Equal(0L, upgraded.Scalar("SELECT count(*) FROM day_notes"));
            upgraded.Execute("INSERT INTO day_notes (day, text) VALUES ('2026-10-02', 'ok')");
            Assert.Equal("Account one", upgraded.Scalar("SELECT a.name FROM entries e JOIN accounts a ON a.id = e.account_id"));
            Assert.Single(Directory.GetFiles(Path.Combine(folder, "Backups"), "timer-before-format-3-*.db"));
        }
        finally { SqliteConnection.ClearAllPools(); try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void ANewerBuildsDatabaseIsStillRefusedByThisOne()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bat-notes-new-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (TimerDatabase.Open(folder)) { }
            SqliteConnection.ClearAllPools();
            using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, TimerDatabase.FileName), Pooling = false }.ToString()))
            { raw.Open(); using var command = raw.CreateCommand(); command.CommandText = "PRAGMA user_version = 4"; command.ExecuteNonQuery(); }
            SqliteConnection.ClearAllPools();
            Assert.Throws<DatabaseTooNewException>(() => TimerDatabase.Open(folder));
        }
        finally { SqliteConnection.ClearAllPools(); try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
