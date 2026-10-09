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

// Minutes is the authoritative credited duration, including negative adjustments and merged credit.
public sealed record SessionRow(string Date, string Account, string Start, string End, string Minutes)
{
    public DateTime Day => DateTime.ParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    public DateTime From => Day + TimeSpan.ParseExact(Start, "hh\\:mm\\:ss", CultureInfo.InvariantCulture);
    public TimeSpan Duration => TimeSpan.FromMinutes(double.Parse(Minutes, CultureInfo.InvariantCulture));
    public DateTime To => From + Duration;
    public bool IsAdjustment => End.StartsWith("rounded", StringComparison.OrdinalIgnoreCase) || End == "adjusted";
    public bool IsPositioned => !IsAdjustment && End != "merged";
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
    public void ToggleAccount(string account)
    {
        var stop = Active?.Account == account;
        StopAccount();
        if (!stop) { StartDay(); Active = new(account, Now); }
    }
    public void StopAccount()
    {
        if (Active is { } active) AddSession(active.Account, active.Start, Now);
        Active = null;
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
            if (minutes >= .05) Sessions.Add(new(start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), account, start.ToString("HH:mm:ss", CultureInfo.InvariantCulture), cap.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                Math.Round(minutes, 2, MidpointRounding.ToEven).ToString(CultureInfo.InvariantCulture)));
            start = cap;
        }
    }
    public TimeSpan Credited(string account, DateTime from, DateTime until)
    {
        var credited = Sessions.Where(s => s.Account == account && s.Day >= from.Date && s.Day < until.Date)
            .Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
        if (Active is { } active && active.Account == account)
            credited += Intersection(active.Start, Now, from.Date, until.Date);
        return credited;
    }
    public DayTotals Totals(DateTime day)
    {
        day = day.Date;
        var plan = Plan(day);
        var now = Now;
        var cap = now < day.AddDays(1) ? now : day.AddDays(1);
        var allocated = Sessions.Where(s => s.Day == day && s.Account != "Lunch").Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
        if (Active is { Account: not "Lunch" } active) allocated += Intersection(active.Start, now, day, day.AddDays(1));
        if (!plan.Working || cap <= plan.Start) return new(TimeSpan.Zero, TimeSpan.Zero, allocated, TimeSpan.Zero);
        var blocked = Sessions.Where(s => s.Day == day && s.Account == "Lunch" && s.IsPositioned && s.Duration > TimeSpan.Zero)
            .Select(s => (s.From, s.To)).ToList();
        blocked.AddRange(Away.Select(a => (a.Start, a.End ?? (now < a.Start.Date.AddDays(1) ? now : a.Start.Date.AddDays(1)))));
        if (Active is { Account: "Lunch" } lunch) blocked.Add((lunch.Start, now));
        var regularEnd = cap < plan.End ? cap : plan.End;
        var lunchAdjustment = Sessions.Where(s => s.Day == day && s.Account == "Lunch" && s.IsAdjustment)
            .Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
        var work = Positive(regularEnd - plan.Start - Blocked(blocked, plan.Start, regularEnd) - lunchAdjustment);
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
