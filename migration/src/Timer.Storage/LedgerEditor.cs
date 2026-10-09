using System.Globalization;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using Timer.Core;

namespace Timer.Storage;

public sealed record LedgerUndo(string Description, List<Dictionary<string, string>> BeforeRows,
    List<SessionRow> AfterRows, string BeforeNotes, string? BeforeGroups, string? BeforePositions = null);
public sealed record LedgerEditState(int SchemaVersion, LedgerUndo? Undo);

// Sessions remain the credited ledger. Extra CSV fields and one reversible edit
// travel with it; Undo restores the edited prefix and retains later recordings.
public sealed partial class LedgerEditor
{
    private readonly TrackingEngine engine;
    private readonly string[] headers;
    private readonly Dictionary<SessionRow, Dictionary<string, string>> raw = new(ReferenceEqualityComparer.Instance);
    private CsvTable notes;
    private string? groups;
    public LedgerUndo? UndoState { get; private set; }
    public bool Changed { get; private set; }
    public bool CanUndo => UndoState is { } u && engine.Sessions.Count >= u.AfterRows.Count
        && engine.Sessions.Take(u.AfterRows.Count).SequenceEqual(u.AfterRows);
    public string? UndoLabel => CanUndo ? UndoState?.Description : null;
    public LedgerEditor(TrackingEngine engine, string[]? headers = null, IEnumerable<Dictionary<string, string>>? rows = null,
        string? notesCsv = null, string? groupsCsv = null, string? stateJson = null, string? positionsCsv = null)
    {
        this.engine = engine; this.headers = headers ?? new[] { "Date", "Account", "Start", "End", "Minutes" };
        if (rows is not null) foreach (var pair in engine.Sessions.Zip(rows)) raw[pair.First] = new(pair.Second);
        notes = CsvTable.Parse(notesCsv ?? "Date,Account,Start,Note\n", new[] { "Date", "Account", "Start", "Note" });
        groups = groupsCsv;
        positions = ParsePositions(positionsCsv);
        if (stateJson is not null)
        {
            var state = JsonSerializer.Deserialize<LedgerEditState>(stateJson) ?? throw new InvalidDataException("Invalid edit history.");
            if (state.SchemaVersion != 1) throw new InvalidDataException("Unsupported edit history.");
            UndoState = state.Undo;
        }
        ValidateCredits();
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
    public string StateJson => JsonSerializer.Serialize(new LedgerEditState(1, UndoState));
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
        UndoState = new(description, before, engine.Sessions.ToList(), beforeNotes, beforeGroups, beforePositions); Changed = true;
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
    public void Undo()
    {
        if (!CanUndo) throw new InvalidOperationException("No matching edit is available to undo.");
        var undo = UndoState!;
        var tail = engine.Sessions.Skip(undo.AfterRows.Count).ToArray();
        var restored = undo.BeforeRows.Select(r => new SessionRow(r["Date"], r["Account"], r["Start"], r["End"], r["Minutes"])).ToArray();
        foreach (var row in restored) { _ = row.From; _ = row.To; }
        var restoredNotes = CsvTable.Parse(undo.BeforeNotes, new[] { "Date", "Account", "Start", "Note" });
        for (var i = 0; i < restored.Length; i++) raw[restored[i]] = new(undo.BeforeRows[i]);
        engine.Sessions.Clear(); engine.Sessions.AddRange(restored); engine.Sessions.AddRange(tail);
        notes = restoredNotes; groups = undo.BeforeGroups;
        if (undo.BeforePositions is not null) positions = ParsePositions(undo.BeforePositions);
        UndoState = null; Changed = true;
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
