using System.Globalization;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using Timer.Core;

namespace Timer.Storage;

// The full-snapshot form used by schema 1 and 2 edit history. It is only read now, to import an older
// history; new steps are the much smaller UndoStep splices (schema 3).
public sealed record LedgerUndo(string Description, List<Dictionary<string, string>> BeforeRows,
    List<SessionRow> AfterRows, string BeforeNotes, string? BeforeGroups, string? BeforePositions = null);
public sealed record LedgerEditState(int SchemaVersion, LedgerUndo? Undo, List<LedgerUndo>? History = null, List<UndoStep>? Steps = null);

// Sessions remain the credited ledger. Extra CSV fields and reversible edits
// travel with it; Undo restores the edited stretch and retains later recordings.
public sealed partial class LedgerEditor
{
    private readonly TrackingEngine engine;
    private readonly string[] headers;
    private readonly Dictionary<SessionRow, Dictionary<string, string>> raw = new(ReferenceEqualityComparer.Instance);
    private CsvTable notes;
    private string? groups;
    // Most recent step last. Each step stores only what its edit changed.
    private readonly List<UndoStep> history = new();
    private readonly Dictionary<UndoStep, int> stepBytes = new(ReferenceEqualityComparer.Instance);
    private string? stateJson;
    public const int MaxUndoSteps = 50;
    // A few very large edits could still add up, so the saved history has a size limit as well.
    public const int MaxHistoryBytes = 2_000_000;
    private readonly int historyBudget;
    public UndoStep? UndoState => history.LastOrDefault();
    public int UndoCount => history.Count;
    public bool Changed { get; private set; }
    // Offered only while the ledger, notes, groups and positions still match what the last edit left behind.
    public bool CanUndo => UndoState is { } u && engine.Sessions.Count >= u.AfterCount
        && UndoMath.HashSessions(engine.Sessions, u.AfterCount) == u.AfterHash
        && UndoMath.HashText(NotesCsv) == u.AfterNotesHash
        && UndoMath.HashGroups(groups) == u.AfterGroupsHash
        && UndoMath.HashText(PositionsCsv) == u.AfterPositionsHash;
    public string? UndoLabel => CanUndo ? UndoState?.Description : null;
    public LedgerEditor(TrackingEngine engine, string[]? headers = null, IEnumerable<Dictionary<string, string>>? rows = null,
        string? notesCsv = null, string? groupsCsv = null, string? stateJson = null, string? positionsCsv = null, int maxHistoryBytes = MaxHistoryBytes)
    {
        this.engine = engine; historyBudget = maxHistoryBytes; this.headers = headers ?? new[] { "Date", "Account", "Start", "End", "Minutes" };
        if (rows is not null) foreach (var pair in engine.Sessions.Zip(rows)) raw[pair.First] = new(pair.Second);
        notes = CsvTable.Parse(notesCsv ?? "Date,Account,Start,Note\n", new[] { "Date", "Account", "Start", "Note" });
        groups = groupsCsv;
        positions = ParsePositions(positionsCsv);
        if (stateJson is not null)
        {
            var state = JsonSerializer.Deserialize<LedgerEditState>(stateJson) ?? throw new InvalidDataException("Invalid edit history.");
            switch (state.SchemaVersion)
            {
                case 1 or 2:
                    // Older files kept whole-ledger snapshots. Only the latest step is carried over, as splices.
                    var latest = state.History?.LastOrDefault() ?? state.Undo;
                    if (latest is not null && FromSnapshot(latest) is { } converted) history.Add(converted);
                    break;
                case 3:
                    foreach (var step in (state.Steps ?? new()).TakeLast(MaxUndoSteps))
                    { CheckStep(step); history.Add(step); }
                    break;
                default: throw new InvalidDataException("Unsupported edit history.");
            }
            TrimHistory();
        }
        ValidateCredits();
    }
    private static void CheckStep(UndoStep? step)
    {
        if (step is null || step.Rows is null || step.Notes is null || step.Groups is null || step.Positions is null
            || step.Rows.Before is null || step.Rows.Start < 0 || step.Rows.AfterLength < 0
            || step.Rows.Start + step.Rows.AfterLength > step.AfterCount
            || step.Rows.Before.Any(r => r is null || UndoMath.Core.Any(k => !r.ContainsKey(k))))
            throw new InvalidDataException("Invalid edit history.");
    }
    // Turn an old whole-ledger step into a splice against the current ledger, if the ledger still matches it.
    private UndoStep? FromSnapshot(LedgerUndo old)
    {
        var count = old.AfterRows.Count;
        if (engine.Sessions.Count < count || !engine.Sessions.Take(count).SequenceEqual(old.AfterRows)) return null;
        var after = engine.Sessions.Take(count).Select(Fields).ToList();
        return new(old.Description, count, UndoMath.HashSessions(engine.Sessions, count),
            UndoMath.Diff(old.BeforeRows, after, UndoMath.SameCore),
            UndoMath.DiffText(old.BeforeNotes, NotesCsv), UndoMath.DiffText(old.BeforeGroups, groups),
            UndoMath.DiffText(old.BeforePositions ?? PositionsCsv, PositionsCsv),
            UndoMath.HashText(NotesCsv), UndoMath.HashGroups(groups), UndoMath.HashText(PositionsCsv));
    }
    private int Bytes(UndoStep step)
    {
        if (!stepBytes.TryGetValue(step, out var size)) stepBytes[step] = size = JsonSerializer.SerializeToUtf8Bytes(step).Length;
        return size;
    }
    // Keep at most 50 steps and about 2 MB of history, dropping the oldest first. The newest step always stays.
    private void TrimHistory()
    {
        while (history.Count > MaxUndoSteps) history.RemoveAt(0);
        while (history.Count > 1 && history.Sum(Bytes) > historyBudget) history.RemoveAt(0);
        foreach (var gone in stepBytes.Keys.Where(k => !history.Contains(k)).ToList()) stepBytes.Remove(gone);
        stateJson = null;
    }
    private static bool SameKey(Dictionary<string, string> item, SessionRow row) =>
        item["Date"] == row.Date && item["Start"] == row.Start && item["Account"].Equals(row.Account, StringComparison.OrdinalIgnoreCase);
    public string Note(SessionRow row) => notes.Rows.LastOrDefault(n => SameKey(n, row))?.GetValueOrDefault("Note", "") ?? "";
    public List<Dictionary<string, string>> Rows() => engine.Sessions.Select(Fields).ToList();
    private Dictionary<string, string> Fields(SessionRow row)
    {
        var fields = raw.TryGetValue(row, out var original) ? new Dictionary<string, string>(original) : new Dictionary<string, string>();
        fields["Date"] = row.Date; fields["Account"] = row.Account; fields["Start"] = row.Start;
        fields["End"] = row.End; fields["Minutes"] = row.Minutes;
        return fields;
    }
    public string SessionsCsv => new CsvTable(headers, Rows()).Serialize();
    public string NotesCsv => notes.Serialize();
    public string? GroupsCsv => groups;
    private static readonly JsonSerializerOptions StateOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    public string StateJson => stateJson ??= JsonSerializer.Serialize(new LedgerEditState(3, null, null, history), StateOptions);
    private int Index(SessionRow row)
    {
        var index = engine.Sessions.FindIndex(s => ReferenceEquals(s, row));
        if (index < 0) throw new InvalidOperationException("This entry changed. Reopen it before editing.");
        return index;
    }
    public static SessionRow TimedRow(string account, DateTime start, DateTime end, string origin)
    {
        if (string.IsNullOrWhiteSpace(account) || end <= start || (end - start).TotalSeconds < 3
            || end > start.Date.AddDays(1)) throw new ArgumentException("Use an interval of at least three seconds within one day.");
        return new(start.ToString("yyyy-MM-dd"), account, start.ToString("HH:mm:ss"), origin,
            Math.Round((end - start).TotalMinutes, 2, MidpointRounding.ToEven).ToString("0.00", CultureInfo.InvariantCulture));
    }
    private void ValidateInterval(SessionRow row, SessionRow? except)
    {
        if (engine.Sessions.Any(s => !ReferenceEquals(s, except) && s.IsPositioned && s.Duration > TimeSpan.Zero && s.From < row.To && s.To > row.From)
            || Positions.Any(p => p.Start < row.To && p.End > row.From)
            || (engine.Active is { } active && active.Start < row.To && engine.Now > row.From))
            throw new ArgumentException("This interval overlaps recorded or running time. Choose a free gap.");
    }
    private void CheckNoteKey(SessionRow row)
    {
        if (engine.Sessions.Count(s => s.Date == row.Date && s.Start == row.Start && s.Account.Equals(row.Account, StringComparison.OrdinalIgnoreCase)) > 1)
            throw new ArgumentException("These entries share a legacy note key. They cannot be edited individually here.");
    }
    private void SetNote(SessionRow? old, SessionRow row, string text)
    {
        var fields = old is null ? null : notes.Rows.LastOrDefault(n => SameKey(n, old));
        var value = fields is null ? new Dictionary<string, string>() : new Dictionary<string, string>(fields);
        if (old is not null) notes.Rows.RemoveAll(n => SameKey(n, old));
        notes.Rows.RemoveAll(n => SameKey(n, row));
        if (!string.IsNullOrWhiteSpace(text))
        { value["Date"] = row.Date; value["Account"] = row.Account; value["Start"] = row.Start; value["Note"] = text.Trim(); notes.Rows.Add(value); }
    }
    // Whether the ledger, as it was before a new edit, is still what the last step left behind.
    private static bool LedgerMatches(UndoStep step, List<Dictionary<string, string>> rows)
        => rows.Count >= step.AfterCount && UndoMath.HashRows(rows, step.AfterCount) == step.AfterHash;
    private void Change(string description, Action mutation)
    {
        var before = Rows(); var beforeNotes = NotesCsv; var beforeGroups = groups; var beforePositions = PositionsCsv;
        // Validate group metadata before touching the ledger; keep unrelated fields.
        CsvTable? table;
        try { table = groups is null ? null : CsvTable.Parse(groups, new[] { "GroupId", "Date", "Account", "Start", "End", "Minutes" }); }
        catch (MalformedLineException error) { throw new InvalidDataException("Could not read entry-group metadata; no edit was applied.", error); }
        mutation();
        if (table is not null && groups == beforeGroups)
        {
            var present = engine.Sessions.Select(s => string.Join("\u001f", s.Date, s.Account, s.Start, s.End, s.Minutes)).ToHashSet(StringComparer.Ordinal);
            table.Rows.RemoveAll(r => !present.Contains(string.Join("\u001f", r["Date"], r["Account"], r["Start"], r["End"], r["Minutes"])));
            var singletons = table.Rows.GroupBy(r => r["GroupId"]).Where(g => g.Count() < 2).Select(g => g.Key).ToHashSet();
            table.Rows.RemoveAll(r => singletons.Contains(r["GroupId"]));
            groups = table.Serialize();
        }
        if (history.Count > 0 && !LedgerMatches(history[^1], before)) history.Clear();
        var notesNow = NotesCsv; var positionsNow = PositionsCsv;
        history.Add(new(description, engine.Sessions.Count, UndoMath.HashSessions(engine.Sessions, engine.Sessions.Count),
            UndoMath.Diff(before, Rows(), UndoMath.SameFields),
            UndoMath.DiffText(beforeNotes, notesNow), UndoMath.DiffText(beforeGroups, groups), UndoMath.DiffText(beforePositions, positionsNow),
            UndoMath.HashText(notesNow), UndoMath.HashGroups(groups), UndoMath.HashText(positionsNow)));
        TrimHistory();
        Changed = true;
    }
    public void Edit(SessionRow old, string account, DateTime start, DateTime end, string note)
    {
        var index = Index(old);
        var row = old;
        if (old.IsPositioned && (account != old.Account || start != old.From || end != old.To))
        { row = TimedRow(account, start, end, "edited"); ValidateInterval(row, old); }
        else if (!old.IsPositioned && (account != old.Account || start != old.From || end != old.To))
            throw new ArgumentException("Merged credit and adjustments support notes only at this stage.");
        CheckNoteKey(old);
        if (engine.Sessions.Any(s => !ReferenceEquals(s, old) && s.Date == row.Date && s.Start == row.Start && s.Account.Equals(row.Account, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Another entry uses that account and start time.");
        if (row == old && Note(old) == note.Trim()) return;
        Change("Edit entry", () => { raw[row] = Fields(old); engine.Sessions[index] = row; SetNote(old, row, note); });
    }
    public void Add(string account, DateTime start, DateTime end, string note)
    {
        var row = TimedRow(account, start, end, "added"); ValidateInterval(row, null);
        if (engine.Sessions.Any(s => s.Date == row.Date && s.Start == row.Start && s.Account.Equals(row.Account, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Another entry uses that account and start time.");
        Change("Add time", () => { engine.Sessions.Add(row); SetNote(null, row, note); });
    }
    public void Delete(SessionRow row)
    {
        var index = Index(row); CheckNoteKey(row);
        if (!row.IsPositioned) throw new ArgumentException("Merged credit and adjustments cannot be deleted here.");
        Change("Delete entry", () => { engine.Sessions.RemoveAt(index); notes.Rows.RemoveAll(n => SameKey(n, row)); });
    }
    /// <summary>Deletes several entries as one edit, so a single Undo brings them all back.</summary>
    public void Delete(IReadOnlyList<SessionRow> rows)
    {
        if (rows.Count == 0) return;
        foreach (var row in rows)
        {
            Index(row); CheckNoteKey(row);
            if (!row.IsPositioned) throw new ArgumentException("Merged credit and adjustments cannot be deleted here.");
        }
        Change(rows.Count == 1 ? "Delete entry" : "Delete entries", () =>
        {
            engine.Sessions.RemoveAll(s => rows.Any(r => ReferenceEquals(r, s)));
            notes.Rows.RemoveAll(n => rows.Any(r => SameKey(n, r)));
        });
    }
    public void Undo()
    {
        if (!CanUndo) throw new InvalidOperationException("No matching edit is available to undo.");
        var undo = UndoState!;
        var start = undo.Rows.Start; var after = start + undo.Rows.AfterLength;
        // Rows the edit did not touch keep their objects. Only the changed stretch is rebuilt, and everything is
        // checked before anything is replaced, so a bad history leaves the ledger as it was.
        var restoredRows = new List<(SessionRow Row, Dictionary<string, string> Fields)>();
        foreach (var fields in undo.Rows.Before)
        {
            var row = new SessionRow(fields["Date"], fields["Account"], fields["Start"], fields["End"], fields["Minutes"]);
            _ = row.From; _ = row.To;
            restoredRows.Add((row, new(fields)));
        }
        var restoredNotes = CsvTable.Parse(UndoMath.RevertText(NotesCsv, undo.Notes) ?? "Date,Account,Start,Note\n", new[] { "Date", "Account", "Start", "Note" });
        var restoredGroups = UndoMath.RevertText(groups, undo.Groups);
        var restoredPositions = ParsePositions(UndoMath.RevertText(PositionsCsv, undo.Positions));
        var tail = engine.Sessions.Skip(undo.AfterCount).ToArray();
        var rows = new List<SessionRow>(engine.Sessions.Take(start));
        foreach (var item in restoredRows) { raw[item.Row] = item.Fields; rows.Add(item.Row); }
        rows.AddRange(engine.Sessions.Skip(after).Take(undo.AfterCount - after));
        engine.Sessions.Clear(); engine.Sessions.AddRange(rows); engine.Sessions.AddRange(tail);
        notes = restoredNotes; groups = restoredGroups; positions = restoredPositions;
        history.RemoveAt(history.Count - 1); TrimHistory(); Changed = true;
    }
}

internal sealed record CsvTable(string[] Headers, List<Dictionary<string, string>> Rows)
{
    public static CsvTable Parse(string content, string[] required)
    {
        using var parser = new TextFieldParser(new StringReader(content)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException("Missing metadata CSV header.");
        if (required.Except(headers).Any() || headers.Distinct().Count() != headers.Length) throw new InvalidDataException("Invalid metadata CSV header.");
        var rows = new List<Dictionary<string, string>>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields()!;
            if (fields.Length != headers.Length) throw new InvalidDataException("Invalid metadata CSV row.");
            rows.Add(headers.Zip(fields).ToDictionary(p => p.First, p => p.Second));
        }
        return new(headers, rows);
    }
    public string Serialize()
    {
        string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        return string.Join(",", Headers.Select(Quote)) + "\r\n" + string.Concat(Rows.Select(r => string.Join(",", Headers.Select(h => Quote(r.GetValueOrDefault(h, "")))) + "\r\n"));
    }
}
