using System.Globalization;

namespace Timer.Core;

public sealed record Shift(TimeSpan Start, TimeSpan End);
public sealed record DayOverride(bool Off, Shift? Shift);
public sealed record AwayInterval(DateTime Start, DateTime? End);
public sealed record ActiveSession(string Account, DateTime Start);
public sealed record DayPlan(bool Working, DateTime Start, DateTime End);
public sealed record DayTotals(TimeSpan Working, TimeSpan Overtime, TimeSpan Allocated, TimeSpan Unallocated)
{
    public TimeSpan Elapsed => Working + Overtime;
}
public sealed class WorkSchedule
{
    public Shift Default { get; set; } = new(TimeSpan.FromHours(9), TimeSpan.FromHours(17));
    public HashSet<DayOfWeek> Days { get; set; } = new() { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
    public Dictionary<DayOfWeek, Shift> Shifts { get; set; } = new();
    public Dictionary<DateTime, DayOverride> Overrides { get; set; } = new();
    public DayPlan Scheduled(DateTime day)
    {
        day = day.Date;
        if (Overrides.TryGetValue(day, out var change))
            return change.Off ? new(false, day, day) : new(true, day + change.Shift!.Start, day + change.Shift.End);
        var hours = Shifts.GetValueOrDefault(day.DayOfWeek) ?? Default;
        return Days.Contains(day.DayOfWeek) ? new(true, day + hours.Start, day + hours.End) : new(false, day, day);
    }
}

// Minutes is the authoritative credited duration.
public sealed record SessionRow(string Date, string Account, string Start, string End, string Minutes)
{
    public DateTime Day => DateTime.ParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    public DateTime From => Day + TimeSpan.ParseExact(Start, "hh\\:mm\\:ss", CultureInfo.InvariantCulture);
    public TimeSpan Duration => TimeSpan.FromMinutes(double.Parse(Minutes, CultureInfo.InvariantCulture));
    public DateTime To => From + Duration;
}

public sealed class TrackingEngine
{
    private readonly Func<DateTime> clock;
    public WorkSchedule Schedule { get; }
    public List<SessionRow> Sessions { get; } = new();
    public Dictionary<DateTime, DateTime> DayStarts { get; } = new();
    public List<AwayInterval> Away { get; } = new();
    public ActiveSession? Active { get; private set; }
    public DateTime Now => clock();
    public TrackingEngine(WorkSchedule? schedule = null, Func<DateTime>? clock = null)
    { Schedule = schedule ?? new(); this.clock = clock ?? (() => DateTime.Now); }

    public DayPlan Plan(DateTime day)
    {
        var plan = Schedule.Scheduled(day);
        if (!DayStarts.TryGetValue(day.Date, out var manual)) return plan;
        var end = plan.Working ? plan.End : day.Date + Schedule.Default.End;
        return new(true, manual, end < manual ? manual : end);
    }
    public bool Finished => Away.Any(a => a.Start.Date == Now.Date && a.End is null);
    public bool DayRunning => Plan(Now).Working && Now >= Plan(Now).Start && !Finished;
    public TimeSpan ActiveElapsed => Active is { } active ? Positive(Now - active.Start) : TimeSpan.Zero;

    public void StartDay()
    {
        var now = Now;
        var plan = Plan(now);
        if (!plan.Working || now < plan.Start) DayStarts[now.Date] = now;
        for (var i = 0; i < Away.Count; i++)
            if (Away[i].Start.Date == now.Date && Away[i].End is null) Away[i] = Away[i] with { End = now };
    }
    public void FinishDay()
    {
        if (!DayRunning) return;
        StopAccount();
        Away.Add(new(Now, null));
    }
    /// <summary>The account to go back to when the current break ends, or null when no break is running or nothing was running before it.</summary>
    public string? ResumeAfterBreak { get; private set; }
    public void ToggleAccount(string account)
    {
        var stop = string.Equals(Active?.Account, account, StringComparison.OrdinalIgnoreCase);
        var before = Active?.Account;
        var stoppedEnd = Active is null ? (DateTime?)null : Now;
        StopAccount();
        ResumeAfterBreak = null;
        if (!stop)
        {
            StartDay();
            if (BreakAccount.Is(account) && before is not null && !BreakAccount.Is(before)) ResumeAfterBreak = before;
            var start = Now;
            // A block's length is kept to hundredths of a minute and its start to the second, so the block just stopped can end a fraction of a second
            // after the new one begins, which drew the two side by side as if they overlapped. The new block starts at the whole second
            // after the old one's end instead (a second or so later at most), so consecutive blocks always meet cleanly.
            if (stoppedEnd is not null && LastStoredEnd is { } end && end.Date == start.Date && WholeSecond(start) < end) start = WholeSecond(end, up: true);
            Active = new(account, start);
        }
    }
    private static DateTime WholeSecond(DateTime time, bool up = false)
    {
        var whole = new DateTime(time.Ticks - time.Ticks % TimeSpan.TicksPerSecond, time.Kind);
        return up && whole < time ? whole.AddSeconds(1) : whole;
    }
    public void StopAccount()
    {
        if (Active is { } active) AddSession(active.Account, active.Start, Now);
        Active = null;
    }
    /// <summary>Where the last block written by <see cref="AddSession"/> ends, which can be a little after the moment it was stopped.</summary>
    public DateTime? LastStoredEnd { get; private set; }

    /// <summary>
    /// How short a block and how recent a switch back count as a slip of the finger, for continuing the earlier block.
    /// </summary>
    public static readonly TimeSpan MistakeWindow = TimeSpan.FromMinutes(3);
    /// <summary>
    /// After going from one account to another and back again within <see cref="MistakeWindow"/>, the earlier block of the account now running
    /// and the short block between, or null when the recent history does not look like that. The short block can be a break.
    /// </summary>
    public (SessionRow Earlier, SessionRow Between)? ContinuableBlocks()
    {
        if (Active is not { } active || BreakAccount.Is(active.Account) || Now - active.Start > MistakeWindow) return null;
        var between = Sessions.LastOrDefault(s => s.Duration > TimeSpan.Zero && s.Duration <= MistakeWindow && Math.Abs((s.To - active.Start).TotalSeconds) <= 2
            && !s.Account.Equals(active.Account, StringComparison.OrdinalIgnoreCase));
        if (between is null) return null;
        var earlier = Sessions.LastOrDefault(s => s.Duration > TimeSpan.Zero && s.Account.Equals(active.Account, StringComparison.OrdinalIgnoreCase) && s.Day == between.Day
            && Math.Abs((s.To - between.From).TotalSeconds) <= 2);
        return earlier is null ? null : (earlier, between);
    }
    /// <summary>
    /// Takes back the switch: the short block between is dropped and the earlier block is reopened, so the account carries on as one block
    /// that includes the time of the slip. The earlier block's note, which is kept by its start, stays with it. Returns the two blocks removed.
    /// </summary>
    public (SessionRow Earlier, SessionRow Between)? ContinuePrevious()
    {
        if (ContinuableBlocks() is not { } blocks || Active is not { } active) return null;
        Sessions.Remove(blocks.Between); Sessions.Remove(blocks.Earlier);
        Active = new(active.Account, blocks.Earlier.From);
        return blocks;
    }
    public void Recover(ActiveSession? active, DateTime? lastSeen)
    {
        if (active is not null && lastSeen > active.Start) AddSession(active.Account, active.Start, lastSeen.Value);
        Active = null;
        for (var i = 0; i < Away.Count; i++)
            if (Away[i].End is null && Away[i].Start.Date < Now.Date) Away[i] = Away[i] with { End = Away[i].Start.Date.AddDays(1) };
    }
    public void AddSession(string account, DateTime start, DateTime end)
    {
        // Match the established midnight split and minimum three-second recording threshold.
        while (start < end)
        {
            var cap = end < start.Date.AddDays(1) ? end : start.Date.AddDays(1);
            var minutes = (cap - start).TotalMinutes;
            if (minutes >= .05)
            {
                var row = new SessionRow(start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), account, start.ToString("HH:mm:ss", CultureInfo.InvariantCulture), cap.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    Math.Round(minutes, 2, MidpointRounding.ToEven).ToString(CultureInfo.InvariantCulture));
                Sessions.Add(row); LastStoredEnd = row.To;
            }
            start = cap;
        }
    }
    public TimeSpan Credited(string account, DateTime from, DateTime until)
    {
        var credited = Sessions.Where(s => s.Account.Equals(account, StringComparison.OrdinalIgnoreCase) && s.Day >= from.Date && s.Day < until.Date)
            .Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
        if (Active is { } active && active.Account.Equals(account, StringComparison.OrdinalIgnoreCase))
            credited += Intersection(active.Start, Now, from.Date, until.Date);
        return credited;
    }
    public DayTotals Totals(DateTime day)
    {
        day = day.Date;
        var plan = Plan(day);
        var now = Now;
        var cap = now < day.AddDays(1) ? now : day.AddDays(1);
        // A past day has no "now" to run overtime up to. Without this it counted everything after the shift ended, right up to
        // midnight, as overtime. Its overtime stops at the last recorded activity, or the end of the shift if that came later.
        if (day < now.Date)
        {
            var lastActivity = Sessions.Where(s => s.Day == day && s.Duration > TimeSpan.Zero).Select(s => s.To).DefaultIfEmpty(DateTime.MinValue).Max();
            var end = plan.Working && plan.End > lastActivity ? plan.End : lastActivity;
            cap = end < cap ? end : cap;
        }
        var allocated = Sessions.Where(s => s.Day == day && !BreakAccount.Is(s.Account)).Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
        if (Active is { } active && !BreakAccount.Is(active.Account)) allocated += Intersection(active.Start, now, day, day.AddDays(1));
        // On a day that is off in the schedule there is no working day to measure against, so the day is just the time actually tracked.
        if (!Schedule.Scheduled(day).Working) return new(allocated, TimeSpan.Zero, allocated, TimeSpan.Zero);
        if (!plan.Working || cap <= plan.Start) return new(TimeSpan.Zero, TimeSpan.Zero, allocated, TimeSpan.Zero);
        var blocked = Sessions.Where(s => s.Day == day && BreakAccount.Is(s.Account) && s.Duration > TimeSpan.Zero)
            .Select(s => (s.From, s.To)).ToList();
        blocked.AddRange(Away.Select(a => (a.Start, a.End ?? (now < a.Start.Date.AddDays(1) ? now : a.Start.Date.AddDays(1)))));
        if (Active is { } lunch && BreakAccount.Is(lunch.Account)) blocked.Add((lunch.Start, now));
        var regularEnd = cap < plan.End ? cap : plan.End;
        var work = Positive(regularEnd - plan.Start - Blocked(blocked, plan.Start, regularEnd));
        var overtime = cap > plan.End ? Positive(cap - plan.End - Blocked(blocked, plan.End, cap)) : TimeSpan.Zero;
        return new(work, overtime, allocated, Positive(work + overtime - allocated));
    }
    private static TimeSpan Blocked(IEnumerable<(DateTime Start, DateTime End)> spans, DateTime from, DateTime until)
    {
        // Union exclusions before subtraction, so overlapping lunch/away cannot be counted twice.
        var intervals = spans.Select(s => (Start: s.Start < from ? from : s.Start, End: s.End > until ? until : s.End))
            .Where(s => s.End > s.Start).OrderBy(s => s.Start).ToArray();
        var total = TimeSpan.Zero;
        var end = from;
        foreach (var span in intervals)
        {
            var start = span.Start < end ? end : span.Start;
            if (span.End > start) total += span.End - start;
            if (span.End > end) end = span.End;
        }
        return total;
    }
    private static TimeSpan Intersection(DateTime start, DateTime end, DateTime from, DateTime until)
        => Positive((end < until ? end : until) - (start > from ? start : from));
    private static TimeSpan Positive(TimeSpan span) => span < TimeSpan.Zero ? TimeSpan.Zero : span;
}

/// <summary>
/// A break is a pause of any kind. In memory it is an entry under the reserved name Break, which no account may use.
/// The database stores it as its own kind of entry with no account.
/// </summary>
public static class BreakAccount
{
    public const string Name = "Break";
    public const string Label = "Break";
    public static bool Is(string? account) => string.Equals(account, Name, StringComparison.OrdinalIgnoreCase);
    public static string Display(string account) => Is(account) ? Label : account;
    /// <summary>Turns a name chosen from a list back into the stored name, so picking "Break" means the break account.</summary>
    public static string Stored(string account) => string.Equals(account, Label, StringComparison.OrdinalIgnoreCase) ? Name : account;
}
