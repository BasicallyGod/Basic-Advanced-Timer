namespace Timer.Desktop;

/// <summary>A holiday the pill can be dressed for. Only Christmas exists so far; the others on the Seasonal Easter eggs board wait for their own rounds.</summary>
public sealed record Season(string Id, string Name, string Dates);

/// <summary>
/// Which season, if any, the calendar is in. Each season is one record and a date rule, so adding a holiday is data and not a new code path. Moving holidays
/// (Easter, Lunar New Year and the like) will need a table of dates inside the app, since the app needs no network.
/// </summary>
public static class Seasons
{
    public static readonly Season Christmas = new("christmas", "Christmas", "1 December to 6 January");
    /// <summary>Every season the app knows, in the order Settings lists them.</summary>
    public static IReadOnlyList<Season> All { get; } = new[] { Christmas };

    /// <summary>Christmas runs from 1 December to 6 January, both days included.</summary>
    public static bool InChristmas(DateTime day) => day.Month == 12 || (day.Month == 1 && day.Day <= 6);

    /// <summary>The season a date falls in, or null on an ordinary day.</summary>
    public static Season? On(DateTime day) => InChristmas(day) ? Christmas : null;

    public static Season? Find(string? id) => All.FirstOrDefault(s => s.Id == id);
}
