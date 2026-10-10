using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The Undo history stores only what each edit changed. These tests check that Undo still puts back exactly what
// an edit replaced, keeps later recordings, survives a restart, and stays small however large the ledger is.
public sealed class UndoHistoryTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TrackingEngine Engine() => new(clock: () => Day.AddHours(15));
    private static SessionRow Row(string start, string minutes, string end = "added", string account = "Account one") => new("2026-10-09", account, start, end, minutes);
    private static readonly string[] Accounts = { "Account one", "Account two", "Account three" };
    private static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}:00";

    private sealed record Snapshot(List<Dictionary<string, string>> Rows, string Notes, string? Groups, int AfterCount);

    private static void AssertRowsEqual(List<Dictionary<string, string>> expected, IEnumerable<Dictionary<string, string>> actual, string where)
    {
        var rows = actual.ToList();
        Assert.True(expected.Count == rows.Count, $"{where}: expected {expected.Count} rows but found {rows.Count}");
        for (var i = 0; i < rows.Count; i++)
            Assert.True(expected[i].Count == rows[i].Count && expected[i].All(p => rows[i].TryGetValue(p.Key, out var v) && v == p.Value), $"{where}: row {i} differs");
    }

    // An oracle that does not trust the editor: a full snapshot is taken before every successful edit, and every Undo
    // must put the ledger, notes and groups back exactly as that snapshot, with later recordings kept.
    [Fact]
    public void RandomEditsUndoToTheExactEarlierStateAndKeepLaterRecordings()
    {
        var undos = 0; var restarts = 0; var edits = 0;
        for (var seed = 1; seed <= 40; seed++)
        {
            var rnd = new Random(seed); var engine = Engine();
            for (var i = 0; i < 12; i++) engine.Sessions.Add(Row(Clock(8 * 60 + i * 40), "20", account: Accounts[i % 3]));
            engine.Sessions.Add(Row("00:00:00", "30", "adjusted", "Account two"));
            var editor = new LedgerEditor(engine);
            var expected = new List<Snapshot>(); var recorded = 0;
            for (var step = 0; step < 120; step++)
            {
                var roll = rnd.Next(100);
                if (roll < 55)
                {
                    var before = new Snapshot(editor.Rows(), editor.NotesCsv, editor.GroupsCsv, 0);
                    var marker = editor.UndoState;
                    try
                    {
                        var positioned = engine.Sessions.Where(s => s.IsPositioned).ToList();
                        var row = positioned.Count == 0 ? null : positioned[rnd.Next(positioned.Count)];
                        var shift = TimeSpan.FromMinutes(rnd.Next(-8, 9) * 5);
                        switch (rnd.Next(5))
                        {
                            case 0: editor.Add(Accounts[rnd.Next(3)], Day.AddMinutes(8 * 60 + rnd.Next(0, 144) * 5), Day.AddMinutes(8 * 60 + rnd.Next(0, 144) * 5 + rnd.Next(1, 13) * 5), "n" + rnd.Next(1000)); break;
                            case 1: if (row is not null) editor.Edit(row, row.Account, row.From, row.To, "note " + rnd.Next(1000)); break;
                            case 2: if (row is not null) editor.Edit(row, Accounts[rnd.Next(3)], row.From + shift, row.To + shift, rnd.Next(2) == 0 ? "" : "moved " + rnd.Next(1000)); break;
                            case 3: if (row is not null) editor.Delete(row); break;
                            default: if (row is not null) editor.EditTimeline(row, row.From + shift, row.To + shift, rnd.Next(2) == 0); break;
                        }
                    }
                    catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
                    if (!ReferenceEquals(marker, editor.UndoState))
                    {
                        expected.Add(before with { AfterCount = engine.Sessions.Count }); edits++;
                        if (expected.Count > LedgerEditor.MaxUndoSteps) expected.RemoveAt(0);
                    }
                }
                else if (roll < 70)
                { engine.Sessions.Add(Row(Clock(21 * 60 + 5 + recorded), "5")); recorded++; }
                else if (roll < 92)
                {
                    Assert.Equal(expected.Count > 0, editor.CanUndo);
                    if (expected.Count == 0) continue;
                    var now = editor.Rows(); var snapshot = expected[^1]; expected.RemoveAt(expected.Count - 1);
                    var tail = now.Skip(snapshot.AfterCount).ToList();
                    editor.Undo(); undos++;
                    var after = editor.Rows();
                    AssertRowsEqual(snapshot.Rows, after.Take(snapshot.Rows.Count), $"seed {seed} step {step} restored rows");
                    AssertRowsEqual(tail, after.Skip(snapshot.Rows.Count), $"seed {seed} step {step} later recordings");
                    Assert.Equal(snapshot.Notes, editor.NotesCsv); Assert.Equal(snapshot.Groups, editor.GroupsCsv);
                }
                else
                {
                    // A restart: everything the editor needs is rebuilt from what would have been saved.
                    editor = new LedgerEditor(engine, null, editor.Rows(), editor.NotesCsv, editor.GroupsCsv, steps: editor.Steps.ToList());
                    restarts++;
                    Assert.Equal(expected.Count, editor.UndoCount); Assert.Equal(expected.Count > 0, editor.CanUndo);
                }
            }
        }
        Assert.True(edits > 800 && undos > 300 && restarts > 150, $"the random sessions should exercise the editor ({edits} edits, {undos} undos, {restarts} restarts)");
    }

    [Fact]
    public void UndoSurvivesARestartThroughTheStoreWhenThereIsNoGroupsFile()
    {
        // With no groups file the editor holds nothing, but the store saves an empty file, so after a restart it holds
        // that. Both must count as the same, or Undo would quietly stop working after every restart.
        var folder = Path.Combine(Path.GetTempPath(), "timer-undo-restart-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine);
                store.Editor!.Add("Account one", Day.AddHours(9), Day.AddHours(10), "first");
                store.Editor.Edit(engine.Sessions[0], "Account one", Day.AddHours(9), Day.AddHours(10.5), "second");
                store.Save(engine, prefs);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); _ = store.Load(engine); var editor = store.Editor!;
                Assert.Equal(2, editor.UndoCount); Assert.True(editor.CanUndo);
                editor.Undo(); Assert.Equal(TimeSpan.FromHours(1), engine.Sessions[0].Duration); Assert.Equal("first", editor.Note(engine.Sessions[0]));
                Assert.True(editor.CanUndo); editor.Undo(); Assert.Empty(engine.Sessions); Assert.False(editor.CanUndo);
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void ChangedNotesOrGroupsDisableUndoInsteadOfBeingOverwritten()
    {
        var engine = Engine(); engine.Sessions.Add(Row("09:00:00", "30"));
        var editor = new LedgerEditor(engine);
        editor.Edit(engine.Sessions[0], "Account one", Day.AddHours(9), Day.AddHours(9.75), "a note");
        Assert.True(editor.CanUndo);
        var state = editor.Steps.ToList();
        Assert.True(new LedgerEditor(engine, steps: state, notesCsv: editor.NotesCsv).CanUndo);
        var otherNotes = new LedgerEditor(engine, steps: state, notesCsv: "Date,Account,Start,Note\n2026-10-09,Account one,09:00:00,changed elsewhere\n");
        Assert.Equal(1, otherNotes.UndoCount); Assert.False(otherNotes.CanUndo);
        var otherGroups = new LedgerEditor(engine, steps: state, notesCsv: editor.NotesCsv,
            groupsCsv: "GroupId,Date,Account,Start,End,Minutes\ng1,2026-10-09,Account one,09:00:00,added,45\n");
        Assert.False(otherGroups.CanUndo);
        Assert.Throws<InvalidOperationException>(() => otherNotes.Undo());
    }

    [Fact]
    public void ALedgerChangedOutsideTheEditorDisablesUndoAndTheNextEditStartsAFreshHistory()
    {
        var engine = Engine(); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("10:00:00", "30") });
        var editor = new LedgerEditor(engine);
        editor.Edit(engine.Sessions[0], "Account one", Day.AddHours(9), Day.AddHours(9.75), "one");
        editor.Edit(engine.Sessions[1], "Account one", Day.AddHours(10), Day.AddHours(10.75), "two");
        Assert.Equal(2, editor.UndoCount); Assert.True(editor.CanUndo);
        engine.Sessions[0] = engine.Sessions[0] with { Minutes = "99" };
        Assert.False(editor.CanUndo);
        editor.Add("Account two", Day.AddHours(12), Day.AddHours(12.5), "");
        Assert.Equal(1, editor.UndoCount); Assert.True(editor.CanUndo);
    }

    [Fact]
    public void HistorySizeFollowsTheEditNotTheLedger()
    {
        var engine = Engine();
        for (var d = 0; d < 250; d++)
            for (var k = 0; k < 12; k++)
                engine.Sessions.Add(new SessionRow(Day.AddDays(-249 + d).ToString("yyyy-MM-dd"), Accounts[k % 3], Clock(8 * 60 + k * 30), "added", "20"));
        Assert.Equal(3000, engine.Sessions.Count);
        var editor = new LedgerEditor(engine);
        for (var i = 0; i < 50; i++)
        {
            var row = engine.Sessions[2990 + i % 10];
            if (i % 2 == 0) editor.Edit(row, row.Account, row.From, row.To, "note " + i);
            else editor.Edit(row, row.Account, row.From.AddMinutes(1), row.To.AddMinutes(1), "moved " + i);
        }
        Assert.Equal(50, editor.UndoCount);
        // Snapshots of 3,000 rows each would be about 30 MB. Splices are a few hundred bytes per edit.
        var size = JsonSerializer.Serialize(editor.Steps).Length;
        Assert.True(size < 100_000, $"history was {size} bytes");
    }

    [Fact]
    public void HistoryIsCappedByBytesAndAlwaysKeepsTheNewestStep()
    {
        // A small budget and a small ledger show the cap acting without the cost of a huge reorder.
        const int budget = 50_000;
        var engine = Engine();
        for (var k = 0; k < 100; k++) engine.Sessions.Add(Row(Clock(2 * 60 + k), "1"));
        var editor = new LedgerEditor(engine, maxHistoryBytes: budget);
        for (var i = 0; i < 45; i++)
        {
            // Each move leapfrogs a block across all 99 others, so every step is about 10 KB.
            var ordered = engine.Sessions.OrderBy(r => r.From).ToArray();
            if (i % 2 == 0) editor.EditTimeline(ordered[0], ordered[0].From.AddMinutes(100), ordered[0].To.AddMinutes(100), true);
            else editor.EditTimeline(ordered[99], ordered[99].From.AddMinutes(-100), ordered[99].To.AddMinutes(-100), true);
        }
        Assert.InRange(editor.UndoCount, 1, 8);
        Assert.True(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(editor.Steps)) < budget + 5_000);
        Assert.True(editor.CanUndo); var first = engine.Sessions[0]; editor.Undo(); Assert.NotEqual(first, engine.Sessions[0]);
        // A single step larger than the whole budget is still kept, so the latest edit can always be undone.
        var tiny = new LedgerEditor(engine, maxHistoryBytes: 1);
        var top = engine.Sessions.OrderBy(r => r.From).First();
        tiny.EditTimeline(top, top.From.AddMinutes(100), top.To.AddMinutes(100), true);
        Assert.Equal(1, tiny.UndoCount); Assert.True(tiny.CanUndo);
    }

    [Fact]
    public void AMalformedHistoryStopsLoadingInsteadOfBeingIgnored()
    {
        var engine = Engine(); engine.Sessions.Add(Row("09:00:00", "30"));
        var editor = new LedgerEditor(engine);
        editor.Edit(engine.Sessions[0], "Account one", Day.AddHours(9), Day.AddHours(9.75), "x");
        var step = editor.Steps[0];
        Assert.Throws<InvalidDataException>(() => new LedgerEditor(engine, steps: new[] { step with { Rows = step.Rows with { Start = 999 } } }));
        Assert.Throws<InvalidDataException>(() => new LedgerEditor(engine, steps: new[] { step with { Rows = null! } }));
    }

    [Fact]
    public void AQuietCheckpointDoesNotRewriteUnchangedRecordsButAnEditDoesAndOnlyNewEditsAreAdded()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-quiet-save-" + Guid.NewGuid());
        try
        {
            using var store = new TimerStore(folder);
            var engine = Engine(); var prefs = store.Load(engine);
            store.Editor!.Add("Account one", Day.AddHours(9), Day.AddHours(10), "first"); store.Save(engine, prefs);
            Assert.Equal(1L, store.Database.Scalar("SELECT COUNT(*) FROM entries")); Assert.Equal(1L, store.Database.Scalar("SELECT COUNT(*) FROM edits"));
            // If a later save rewrote the records, the marker would disappear.
            store.Database.Execute("UPDATE entries SET note = 'marker'"); store.Database.Execute("UPDATE edits SET description = 'marker'");
            store.Save(engine, prefs); store.Save(engine, prefs);
            Assert.Equal("marker", store.Database.Scalar("SELECT note FROM entries")); Assert.Equal("marker", store.Database.Scalar("SELECT description FROM edits ORDER BY id LIMIT 1"));
            store.Editor.Add("Account two", Day.AddHours(11), Day.AddHours(12), ""); store.Save(engine, prefs);
            Assert.Equal(2L, store.Database.Scalar("SELECT COUNT(*) FROM entries")); Assert.Equal(2L, store.Database.Scalar("SELECT COUNT(*) FROM edits")); Assert.Equal("marker", store.Database.Scalar("SELECT description FROM edits ORDER BY id LIMIT 1")); // Only the new edit was added.
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void UndoneAndTrimmedEditsLeaveTheDatabaseAndAFailedSaveKeepsTheListInStep()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-edits-table-" + Guid.NewGuid());
        try
        {
            using var store = new TimerStore(folder);
            var engine = Engine(); var prefs = store.Load(engine);
            store.Editor!.Add("Account one", Day.AddHours(9), Day.AddHours(10), "a"); store.Save(engine, prefs);
            store.Editor.Add("Account two", Day.AddHours(11), Day.AddHours(12), "b"); store.Save(engine, prefs);
            Assert.Equal(2L, store.Database.Scalar("SELECT COUNT(*) FROM edits")); Assert.Equal(6L, store.Database.Scalar("SELECT COUNT(*) FROM edit_changes"));
            store.Editor.Undo(); store.Save(engine, prefs);
            Assert.Equal(1L, store.Database.Scalar("SELECT COUNT(*) FROM edits")); Assert.Equal(3L, store.Database.Scalar("SELECT COUNT(*) FROM edit_changes")); // The undone edit's parts went with it.
            StorageFaults.Block(store);
            store.Editor.Add("Account two", Day.AddHours(13), Day.AddHours(14), "c");
            Assert.NotNull(Record.Exception(() => store.Save(engine, prefs)));
            StorageFaults.Unblock(store); store.Save(engine, prefs);
            Assert.Equal(2L, store.Database.Scalar("SELECT COUNT(*) FROM edits"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void AHistorySavedWithAPositionsPartByAnEarlierBuildStillLoads()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-old-positions-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); var prefs = store.Load(engine);
                store.Editor!.Add("Account one", Day.AddHours(9), Day.AddHours(10), "a"); store.Save(engine, prefs);
                var edit = store.Database.Scalar("SELECT id FROM edits");
                store.Database.Execute("INSERT INTO edit_changes (edit_id, table_name, row_id, before_json, after_json) VALUES ($e, 'positions', 0, '{}', 'x')", ("$e", edit));
                store.Database.Execute("INSERT INTO meta (key, value) VALUES ('positions', 'Id,Date,Account,Start,Minutes,Note')");
            }
            using (var store = new TimerStore(folder))
            {
                var engine = Engine(); store.Load(engine);
                Assert.True(store.Editor!.CanUndo); store.Editor.Undo(); Assert.Empty(engine.Sessions);
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
