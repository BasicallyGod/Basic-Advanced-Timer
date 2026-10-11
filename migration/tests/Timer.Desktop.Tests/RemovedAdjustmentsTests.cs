using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The Adjust time feature has gone. A database made while it existed must still open: those rows are left out, a copy is kept first, and the rest is untouched.
public sealed class RemovedAdjustmentsTests
{
    private static DateTime Day => new(2026, 10, 9);

    [Fact]
    public void OldAdjustmentsAreDroppedWithABackupTheirNotesAndTheUndoHistoryAndEverythingElseSurvives()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-adjustments-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => Day.AddHours(18)); var prefs = store.Load(engine);
                engine.AddSession("Account one", Day.AddHours(9), Day.AddHours(10));
                store.Editor!.Add("Account one", Day.AddHours(11), Day.AddHours(12), "Real note"); // Gives the database an undo step to clear.
                store.Save(engine, prefs);
                var account = Convert.ToInt64(store.Database.Scalar("SELECT id FROM accounts WHERE name = 'Account one'"));
                store.Database.Execute("INSERT INTO entries (kind, account_id, day, start_at, end_at, seconds, origin, created_at, minutes_text) VALUES ('adjustment', $a, '2026-10-09', '2026-10-09T14:00:00', NULL, 900, 'recorded', '2026-10-09T14:00:00', '15')", ("$a", account));
                var notes = (string?)store.Database.Scalar("SELECT value FROM meta WHERE key = 'notes'") ?? "Date,Account,Start,Note\n";
                store.Database.Execute("INSERT OR REPLACE INTO meta (key, value) VALUES ('notes', $n)", ("$n", notes.TrimEnd('\n') + "\n2026-10-09,Account one,14:00:00,Call ran over\n"));
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => Day.AddHours(18)); var prefs = store.Load(engine);
                Assert.Equal(1, store.AdjustmentsRemoved);
                Assert.Equal(2, engine.Sessions.Count); Assert.Equal(TimeSpan.FromHours(2), engine.Credited("Account one", Day, Day.AddDays(1))); // The 15 minutes are gone.
                Assert.Contains(Directory.GetDirectories(Path.Combine(folder, "Backups")), d => Path.GetFileName(d).StartsWith("before-removing-adjustments"));
                Assert.False(store.Editor!.CanUndo); // Undo could have brought one back.
                Assert.DoesNotContain("Call ran over", store.Editor.NotesCsv); Assert.Contains("Real note", store.Editor.NotesCsv);
                store.Save(engine, prefs);
            }
            using (var store = new TimerStore(folder))
            {
                store.Load(new TrackingEngine());
                Assert.Equal(0, store.AdjustmentsRemoved); // Gone for good after the save.
                Assert.Equal(0L, Convert.ToInt64(store.Database.Scalar("SELECT COUNT(*) FROM entries WHERE kind = 'adjustment'")));
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
