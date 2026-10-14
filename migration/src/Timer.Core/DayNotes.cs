using System.Globalization;
using System.Text.RegularExpressions;

namespace Timer.Core;

/// <summary>A note on a day, with an optional time. Notes with a date feed Coming up, and later the timeline.</summary>
public sealed record DayNote(long Id, DateTime Day, TimeSpan? At, string Text, bool Done = false);

public static class DayNotes
{
    private static readonly Regex Time = new(@"(?:^|\s)@?([01]?\d|2[0-3]):([0-5]\d)(?=\s|$)", RegexOptions.Compiled);

    /// <summary>Pulls a time such as 14:30 (or @14:30) out of what was typed. A note with no time is just a note for the day.</summary>
    public static (string Text, TimeSpan? At) Parse(string typed)
    {
        var match = Time.Match(typed);
        if (!match.Success) return (typed.Trim(), null);
        var at = new TimeSpan(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), 0);
        var text = Regex.Replace(typed.Remove(match.Index, match.Length).Trim(), @"\s{2,}", " ");
        return (text.Length == 0 ? typed.Trim() : text, at);
    }

    /// <summary>The notes still to come: those today or later that are not done, soonest first, within a number of days.</summary>
    public static List<DayNote> ComingUp(IEnumerable<DayNote> notes, DateTime now, int days)
        => notes.Where(n => !n.Done && n.Day.Date >= now.Date && n.Day.Date <= now.Date.AddDays(days) && (n.Day.Date > now.Date || n.At is null || n.Day.Date + n.At.Value >= now.AddHours(-1)))
            .OrderBy(n => n.Day.Date).ThenBy(n => n.At ?? TimeSpan.FromDays(1)).ThenBy(n => n.Id).ToList();
}
