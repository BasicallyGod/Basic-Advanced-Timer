using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop.Tests;

// Makes the database refuse new entries, to stand in for a full disk or a locked file.
internal static class StorageFaults
{
    public static void Block(TimerStore store) => store.Database.Execute("CREATE TRIGGER block_entries BEFORE INSERT ON entries BEGIN SELECT RAISE(ABORT, 'disk full'); END");
    public static void Unblock(TimerStore store) => store.Database.Execute("DROP TRIGGER block_entries");
    public static List<SessionRow> Saved(TimerStore store)
    {
        var engine = new TrackingEngine();
        engine.Sessions.AddRange(store.Database.Query("SELECT day, start_at, seconds FROM entries ORDER BY id",
            r => new SessionRow(r.GetString(0), "x", r.GetString(1)[11..], "y", (r.GetDouble(2) / 60).ToString(System.Globalization.CultureInfo.InvariantCulture))));
        return engine.Sessions;
    }
}
