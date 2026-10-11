using System.Globalization;
using Timer.Core;

namespace Timer.Storage;

/// <summary>
/// Where an adjustment may sit on a day. An adjustment is drawn as a block as tall as the minutes it adds or removes, and it should settle against
/// the blocks around it and not lie across them. Nothing recorded ever moves to make room: if the wanted place is taken, it snaps to the nearest
/// free spot, flush against the end of the block before or the start of the block after.
/// </summary>
public static class AdjustmentSpace
{
    public static TimeSpan Length(SessionRow row)
        => TimeSpan.FromMinutes(Math.Min(24 * 60, Math.Abs((double)decimal.Parse(row.Minutes, CultureInfo.InvariantCulture))));

    /// <summary>Everything an adjustment on this day must stay clear of: recorded and added time, the running stretch and the other adjustments.</summary>
    public static List<(DateTime From, DateTime To)> Occupied(TrackingEngine engine, DateTime day, SessionRow? except)
    {
        day = day.Date;
        var spans = engine.Sessions.Where(s => !ReferenceEquals(s, except) && s.Day == day && s.IsPositioned && s.Duration > TimeSpan.Zero).Select(s => (s.From, s.To)).ToList();
        spans.AddRange(engine.Sessions.Where(s => !ReferenceEquals(s, except) && s.Day == day && s.IsAdjustment).Select(s => (s.From, s.From + Length(s))));
        if (engine.Active is { } active && active.Start < day.AddDays(1) && engine.Now > day)
            spans.Add((active.Start < day ? day : active.Start, engine.Now < day.AddDays(1) ? engine.Now : day.AddDays(1)));
        return spans;
    }

    public static bool IsFree(DateTime start, TimeSpan length, DateTime day, IReadOnlyList<(DateTime From, DateTime To)> occupied)
        => start >= day.Date && start + length <= day.Date.AddDays(1) && !occupied.Any(o => o.From < start + length && o.To > start);

    private static DateTime Ceiling(DateTime time) => new(((time.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond) * TimeSpan.TicksPerSecond, time.Kind);
    private static DateTime Floor(DateTime time) => new((time.Ticks / TimeSpan.TicksPerSecond) * TimeSpan.TicksPerSecond, time.Kind);

    /// <summary>
    /// Chooses a start for an adjustment of <paramref name="length"/> near <paramref name="wanted"/>. A free boundary within
    /// <paramref name="snapWithin"/> wins, so the block settles against its neighbour. Otherwise the wanted spot is used if it is free, and if not
    /// the nearest free boundary. Returns null when there is no room anywhere on the day.
    /// </summary>
    public static DateTime? Choose(DateTime wanted, TimeSpan length, DateTime day, IReadOnlyList<(DateTime From, DateTime To)> occupied, TimeSpan snapWithin, TimeSpan? grid)
    {
        day = day.Date;
        var latest = day.AddDays(1) - length;
        if (latest < day) return null;
        if (wanted < day) wanted = day; else if (wanted > latest) wanted = latest;
        var boundaries = occupied.SelectMany(o => new[] { Ceiling(o.To), Floor(o.From - length) }).Where(b => IsFree(b, length, day, occupied)).Distinct().ToList();
        if (boundaries.Where(b => (b - wanted).Duration() <= snapWithin).OrderBy(b => (b - wanted).Duration()).Cast<DateTime?>().FirstOrDefault() is { } near) return near;
        var spot = grid is { } step ? day.AddTicks((long)(Math.Round((wanted - day).Ticks / (double)step.Ticks) * step.Ticks)) : Floor(wanted);
        if (spot > latest) spot = latest;
        if (IsFree(spot, length, day, occupied)) return spot;
        return boundaries.OrderBy(b => (b - wanted).Duration()).Cast<DateTime?>().FirstOrDefault();
    }
}
