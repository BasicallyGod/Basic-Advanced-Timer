using Timer.Core;

namespace Timer.Storage;

public sealed record TimelineChange(SessionRow Source, SessionRow Updated);

public sealed partial class LedgerEditor
{
    public IReadOnlyList<TimelineChange> PlanTimeline(SessionRow source, DateTime start, DateTime end, bool insert = false)
    {
        Index(source);
        var changed = TimedRow(source.Account, start, end, "edited");
        var plan = new Dictionary<SessionRow, SessionRow>(ReferenceEqualityComparer.Instance) { [source] = changed };
        var neighbours = engine.Sessions.Where(s => !ReferenceEquals(s, source) && s.IsPositioned && s.Duration > TimeSpan.Zero && s.Day == source.Day).ToArray();
        if (insert)
        {
            var crossed = start < source.From
                ? neighbours.Where(s => s.From < source.From && s.To > start).OrderBy(s => s.From).ToArray()
                : neighbours.Where(s => s.From >= source.To && s.From < end).OrderBy(s => s.From).ToArray();
            if (crossed.Length > 0)
            {
                var upward = start < source.From;
                var target = upward ? crossed[0].From : crossed[^1].To - source.Duration;
                changed = source with { Start = target.ToString("HH:mm:ss"), End = "edited" };
                plan[source] = changed;
                foreach (var other in crossed)
                {
                    var shifted = other.From + (upward ? source.Duration : -source.Duration);
                    var seconds = upward ? Math.Ceiling((shifted - source.Day).TotalSeconds) : Math.Floor((shifted - source.Day).TotalSeconds);
                    plan[other] = other with { Start = source.Day.AddSeconds(seconds).ToString("HH:mm:ss"), End = "edited" };
                }
            }
        }
        var final = engine.Sessions.Where(s => s.IsPositioned && s.Duration > TimeSpan.Zero).Select(s => plan.GetValueOrDefault(s, s)).ToArray();
        foreach (var pair in plan)
        {
            var row = pair.Value;
            CheckNoteKey(pair.Key);
            if (row.From < source.Day || row.To > source.Day.AddDays(1))
                throw new ArgumentException("There is not enough room to insert these blocks within this day.");
            if (final.Count(s => s.Date == row.Date && s.Start == row.Start && s.Account.Equals(row.Account, StringComparison.OrdinalIgnoreCase)) > 1)
                throw new ArgumentException("Another entry uses that account and start time.");
            if (final.Any(s => !ReferenceEquals(s, row) && s.From < row.To && s.To > row.From)
                || Positions.Any(p => p.Start < row.To && p.End > row.From)
                || (engine.Active is { } active && active.Start < row.To && engine.Now > row.From))
                throw new ArgumentException("These blocks cannot be moved into recorded, positioned or running time.");
        }
        return plan.Select(p => new TimelineChange(p.Key, p.Value)).ToArray();
    }
    public void EditTimeline(SessionRow source, DateTime start, DateTime end, bool insert = false)
    {
        var plan = PlanTimeline(source, start, end, insert);
        var notesBefore = plan.Select(p => Note(p.Source)).ToArray();
        var indices = plan.Select(p => Index(p.Source)).ToArray();
        Change(insert ? "Insert and reorder blocks" : "Edit entry", () =>
        {
            for (var i = 0; i < plan.Count; i++)
            {
                var item = plan[i]; raw[item.Updated] = Fields(item.Source);
                engine.Sessions[indices[i]] = item.Updated;
                notes.Rows.RemoveAll(n => SameKey(n, item.Source));
            }
            for (var i = 0; i < plan.Count; i++) SetNote(null, plan[i].Updated, notesBefore[i]);
        });
    }
}
