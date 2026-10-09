using System.Globalization;
using Timer.Core;

namespace Timer.Storage;

public sealed record CreditPosition(string Id, string Account, DateTime Start, decimal Minutes, string Note)
{
    public DateTime End => Start.AddMinutes((double)Minutes);
}
public sealed record ReviewEntry(string Account, DateTime Day, DateTime? Start, DateTime? End,
    decimal Minutes, string Type, string Note, IReadOnlyList<SessionRow> Sources, string? PositionId = null, string? GroupId = null);
public sealed record TidyGap(DateTime Start, DateTime End, string Previous, string Next);
public sealed record GapChoice(TidyGap Gap, string Account);

public sealed partial class LedgerEditor
{
    private CsvTable positions;
    private static readonly string[] positionHeaders = { "Id", "Date", "Account", "Start", "Minutes", "Note" };
    private static readonly string[] groupHeaders = { "GroupId", "Date", "Account", "Start", "End", "Minutes" };
    private static decimal Minutes(SessionRow row) => decimal.Parse(row.Minutes, CultureInfo.InvariantCulture);
    private static string Key(SessionRow row) => string.Join("\u001f", row.Date, row.Account, row.Start, row.End, row.Minutes);
    private static string Key(Dictionary<string, string> row) => string.Join("\u001f", groupHeaders.Skip(1).Select(h => row[h]));
    private static CsvTable ParsePositions(string? csv)
    {
        var table = CsvTable.Parse(csv ?? string.Join(",", positionHeaders) + "\n", positionHeaders.Take(5).ToArray());
        return table.Headers.Contains("Note") ? table : new CsvTable(table.Headers.Append("Note").ToArray(), table.Rows);
    }
    public string PositionsCsv => positions.Serialize();
    public IReadOnlyList<CreditPosition> Positions => positions.Rows.Select(r => new CreditPosition(r["Id"], r["Account"],
        DateTime.ParseExact(r["Date"], "yyyy-MM-dd", CultureInfo.InvariantCulture) + TimeSpan.ParseExact(r["Start"], @"hh\:mm\:ss", CultureInfo.InvariantCulture),
        decimal.Parse(r["Minutes"], CultureInfo.InvariantCulture), r.GetValueOrDefault("Note", ""))).ToArray();
    public IReadOnlyDictionary<string, decimal> Unplaced(DateTime day)
    {
        var result = engine.Sessions.Where(s => s.Day == day.Date && s.IsMerged).GroupBy(s => s.Account, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(Minutes), StringComparer.OrdinalIgnoreCase);
        foreach (var p in Positions.Where(p => p.Start.Date == day.Date)) result[p.Account] = result.GetValueOrDefault(p.Account) - p.Minutes;
        return result;
    }
    public void ValidateCredits()
    {
        var items = Positions;
        if (items.Select(p => p.Id).Distinct().Count() != items.Count || items.Any(p => string.IsNullOrWhiteSpace(p.Id)
            || string.IsNullOrWhiteSpace(p.Account) || p.Minutes <= 0 || p.End > p.Start.Date.AddDays(1)))
            throw new InvalidDataException("Invalid credit positions. No time was changed.");
        foreach (var day in items.Select(p => p.Start.Date).Distinct())
            if (Unplaced(day).Any(p => p.Value < 0)) throw new InvalidDataException("Positioned credit exceeds the merged ledger credit.");
    }
    public void Place(string account, DateTime start, decimal minutes, string note, CreditPosition? old = null)
    {
        var items = Positions;
        if (old is not null && !items.Contains(old)) throw new InvalidOperationException("This position changed. Reopen it.");
        var available = Unplaced(start.Date).GetValueOrDefault(account) + (old is not null && old.Start.Date == start.Date
            && old.Account.Equals(account, StringComparison.OrdinalIgnoreCase) ? old.Minutes : 0);
        if (minutes < .05m || minutes != decimal.Round(minutes, 2) || minutes > available)
            throw new ArgumentException("Choose at least 0.05 minutes, up to the available credit, with at most two decimal places.");
        var end = start.AddMinutes((double)minutes);
        if (end > start.Date.AddDays(1)) throw new ArgumentException("Credit must fit within one day.");
        if (engine.Sessions.Any(s => s.IsPositioned && s.Duration > TimeSpan.Zero && s.From < end && s.To > start)
            || items.Any(p => p.Id != old?.Id && p.Start < end && p.End > start)
            || (engine.Active is { } active && active.Start < end && engine.Now > start))
            throw new ArgumentException("This placement overlaps recorded, positioned or running time.");
        Change(old is null ? "Position existing credit" : "Edit credit position", () =>
        {
            var previous = old is null ? null : positions.Rows.Single(r => r["Id"] == old.Id);
            var value = previous is null ? new Dictionary<string, string>() : new(previous);
            value["Id"] = old?.Id ?? Guid.NewGuid().ToString("N"); value["Date"] = start.ToString("yyyy-MM-dd");
            value["Account"] = account; value["Start"] = start.ToString("HH:mm:ss");
            value["Minutes"] = minutes.ToString("0.00", CultureInfo.InvariantCulture); value["Note"] = note.Trim();
            if (previous is not null) positions.Rows.Remove(previous); positions.Rows.Add(value);
        });
    }
    public void Unplace(CreditPosition position)
    {
        if (!Positions.Contains(position)) throw new InvalidOperationException("This position changed. Reopen it.");
        Change("Unplace credit", () => positions.Rows.RemoveAll(r => r["Id"] == position.Id));
    }
    // CSV starts have whole-second precision and durations are rounded to 0.01
    // minutes. Permit only that sub-second representation error at a join.
    public static bool Adjoining(IReadOnlyList<SessionRow> rows)
    {
        if (rows.Count == 0 || rows.Any(s => !s.IsPositioned || s.Duration <= TimeSpan.Zero)) return false;
        var ordered = rows.OrderBy(s => s.From).ToArray();
        return ordered.Skip(1).Select((s, i) => Math.Abs((s.From - ordered[i].To).TotalSeconds)).All(gap => gap <= .6 + 1e-8);
    }
    public bool CanCombine(IReadOnlyList<SessionRow> rows)
    {
        if (!Adjoining(rows)) return false;
        var from = rows.Min(s => s.From); var until = rows.Max(s => s.To);
        return !engine.Sessions.Any(s => !rows.Any(r => ReferenceEquals(r, s)) && s.IsPositioned && s.Duration > TimeSpan.Zero && s.From < until && s.To > from)
            && !Positions.Any(p => p.Start < until && p.End > from)
            && !(engine.Active is { } active && active.Start < until && engine.Now > from);
    }
    public void Group(IReadOnlyList<SessionRow> selected)
    {
        foreach (var row in selected) Index(row);
        if (selected.Count < 2 || selected.Select(Key).Distinct().Count() != selected.Count
            || selected.Any(s => s.Day != selected[0].Day || s.Account != selected[0].Account || s.IsMerged)
            || selected.Any(s => engine.Sessions.Count(r => Key(r) == Key(s)) != 1))
            throw new ArgumentException("Select at least two distinct entries for the same account and day. Merged credit is placed separately.");
        if (!CanCombine(selected)) throw new ArgumentException("Merge adjoining blocks only. Fill gaps first; overlapping or intervening time cannot be merged.");
        var table = CsvTable.Parse(groups ?? string.Join(",", groupHeaders) + "\n", groupHeaders);
        var keys = selected.Select(Key).ToHashSet();
        var oldGroups = table.Rows.Where(r => keys.Contains(Key(r))).Select(r => r["GroupId"]).ToHashSet();
        if (table.Rows.Any(r => oldGroups.Contains(r["GroupId"]) && !keys.Contains(Key(r))))
            throw new ArgumentException("Select every part of an existing group before merging it.");
        Change("Group entries", () =>
        {
            table.Rows.RemoveAll(r => oldGroups.Contains(r["GroupId"]));
            var id = Guid.NewGuid().ToString("N");
            foreach (var row in selected) { var value = Fields(row); value["GroupId"] = id; table.Rows.Add(value); }
            groups = table.Serialize();
        });
    }
    public void Ungroup(string id)
    {
        var table = CsvTable.Parse(groups ?? string.Join(",", groupHeaders) + "\n", groupHeaders);
        if (!table.Rows.Any(r => r["GroupId"] == id)) throw new InvalidOperationException("This group changed. Reopen it.");
        Change("Ungroup entries", () => { table.Rows.RemoveAll(r => r["GroupId"] == id); groups = table.Serialize(); });
    }
    public IReadOnlyList<ReviewEntry> Entries(DateTime from, DateTime until)
    {
        ValidateCredits();
        var result = new List<ReviewEntry>();
        var relevant = engine.Sessions.Where(s => s.Day >= from.Date && s.Day < until.Date).ToArray();
        var used = new HashSet<SessionRow>(ReferenceEqualityComparer.Instance);
        // Old files are read lazily so unknown metadata remains untouched until a review/edit is requested.
        if (groups is not null)
        {
            var table = CsvTable.Parse(groups, groupHeaders);
            foreach (var group in table.Rows.GroupBy(r => r["GroupId"]))
            {
                var members = relevant.Where(s => group.Any(r => Key(r) == Key(s)) && !s.IsMerged).ToArray();
                if (members.Length < 2) continue;
                if (members.Any(s => s.Day != members[0].Day || s.Account != members[0].Account || used.Contains(s)))
                    throw new InvalidDataException("Invalid entry group: members must share an account and day and belong to one group.");
                result.Add(new(members[0].Account, members[0].Day, null, null, members.Sum(Minutes), "Grouped sessions",
                    string.Join("\n", members.Select(Note).Where(n => n.Length > 0).Distinct()), members, GroupId: group.Key));
                foreach (var member in members) used.Add(member);
            }
        }
        var placed = Positions.Where(p => p.Start >= from.Date && p.Start < until.Date).ToArray();
        var consumed = placed.GroupBy(p => (p.Start.Date, Account: p.Account.ToUpperInvariant())).ToDictionary(g => g.Key, g => g.Sum(p => p.Minutes));
        foreach (var row in relevant.Where(s => !used.Contains(s)))
        {
            var minutes = Minutes(row);
            if (row.IsMerged && minutes > 0)
            {
                var key = (row.Day, row.Account.ToUpperInvariant()); var amount = Math.Min(minutes, consumed.GetValueOrDefault(key));
                minutes -= amount; consumed[key] = consumed.GetValueOrDefault(key) - amount;
                if (minutes == 0) continue;
            }
            result.Add(new(row.Account, row.Day, row.IsPositioned ? row.From : null, row.IsPositioned ? row.To : null, minutes,
                row.IsMerged ? "Merged credit (unplaced)" : row.IsAdjustment ? "Adjustment" : row.End == "edited" ? "Edited by hand" : row.End == "added" ? "Added by hand" : "Time", Note(row), new[] { row }));
        }
        result.AddRange(placed.Select(p => new ReviewEntry(p.Account, p.Start.Date, p.Start, p.End, p.Minutes,
            "Positioned existing credit", p.Note, Array.Empty<SessionRow>(), PositionId: p.Id)));
        return result.OrderBy(r => r.Day).ThenBy(r => r.Account, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Start).ToArray();
    }
    public IReadOnlyList<TidyGap> TidyCandidates(DateTime day, int maximumMinutes = 5)
    {
        if (maximumMinutes is < 1 or > 60) throw new ArgumentException("Choose a gap limit of 1–60 minutes.");
        if (day.Date > engine.Now.Date) return Array.Empty<TidyGap>();
        var spans = engine.Sessions.Where(s => s.Day == day.Date && s.IsPositioned && s.Duration > TimeSpan.Zero)
            .Select(s => (Start: s.From, End: s.To, s.Account, Live: false)).ToList();
        spans.AddRange(Positions.Where(p => p.Start.Date == day.Date).Select(p => (p.Start, p.End, p.Account, false)));
        if (engine.Active is { } active && active.Start < day.Date.AddDays(1) && engine.Now > day.Date)
            spans.Add((active.Start, engine.Now, active.Account, true));
        var ordered = spans.OrderBy(s => s.Start).ToArray(); var result = new List<TidyGap>();
        for (var i = 1; i < ordered.Length; i++)
        {
            var previous = ordered[i - 1]; var next = ordered[i];
            if (previous.Live || next.Live || previous.Account.Equals("Lunch", StringComparison.OrdinalIgnoreCase)
                || next.Account.Equals("Lunch", StringComparison.OrdinalIgnoreCase) || next.Start > engine.Now
                || next.Start - previous.End < TimeSpan.FromSeconds(3) || next.Start - previous.End > TimeSpan.FromMinutes(maximumMinutes)
                || spans.Count(s => s.End == previous.End) != 1 || spans.Count(s => s.Start == next.Start) != 1
                || spans.Any(s => s.Start < next.Start && s.End > previous.End)
                || engine.Away.Any(a => a.Start < next.Start && (a.End ?? engine.Now) > previous.End)) continue;
            var start = day.Date.AddSeconds(Math.Ceiling((previous.End - day.Date).TotalSeconds - 1e-6));
            var end = day.Date.AddSeconds(Math.Floor((next.Start - day.Date).TotalSeconds + 1e-6));
            if ((end - start).TotalSeconds >= 3) result.Add(new(start, end, previous.Account, next.Account));
        }
        return result;
    }
    public void Tidy(IReadOnlyList<GapChoice> choices, int maximumMinutes = 5)
    {
        if (choices.Count == 0) return;
        var current = choices.Select(c => c.Gap.Start.Date).Distinct().SelectMany(d => TidyCandidates(d, maximumMinutes)).ToHashSet();
        if (choices.Select(c => c.Gap).Distinct().Count() != choices.Count || choices.Any(c => !current.Contains(c.Gap)
            || (c.Account != c.Gap.Previous && c.Account != c.Gap.Next)))
            throw new InvalidOperationException("The gaps changed. Reopen Tidy day; no time was added.");
        var rows = choices.Select(c => TimedRow(c.Account, c.Gap.Start,
            c.Gap.Start.AddSeconds(Math.Floor((c.Gap.End - c.Gap.Start).TotalSeconds / .6 + 1e-8) * .6), "added")).ToArray();
        foreach (var row in rows) ValidateInterval(row, null);
        Change("Tidy day", () => engine.Sessions.AddRange(rows));
    }
}
