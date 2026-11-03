using System.Globalization;

namespace Timer.Desktop;

/// <summary>Reading and writing the times typed into the Add time dialog and the Selected block card: clock times, and lengths in minutes or hours and minutes.</summary>
public static class TimeText
{
    /// <summary>A clock time on the given day. HH:mm or HH:mm:ss; 24:00 is accepted for an end and means midnight at the end of the day.</summary>
    public static DateTime ParseClock(string? value, DateTime day, bool finish)
    {
        value = value?.Trim();
        if (finish && value is "24:00" or "24:00:00") return day.Date.AddDays(1);
        var time = TimeSpan.ParseExact(value ?? "", new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture);
        if (time >= TimeSpan.FromDays(1)) throw new FormatException();
        return day.Date + time;
    }
    public static bool TryParseClock(string? value, DateTime day, bool finish, out DateTime result)
    {
        try { result = ParseClock(value, day, finish); return true; }
        catch (Exception ex) when (ex is FormatException or OverflowException) { result = default; return false; }
    }
    /// <summary>Minutes (45 or 7.5), H:MM or H:MM:SS. Null when it is not a length or is not positive.</summary>
    public static TimeSpan? ParseLength(string? value)
    {
        value = value?.Trim(); if (string.IsNullOrEmpty(value)) return null;
        if (value.Contains(':'))
            return TimeSpan.TryParseExact(value, new[] { @"h\:mm", @"h\:mm\:ss", @"hh\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var span) && span > TimeSpan.Zero ? span : null;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) && minutes > 0 && minutes < 24 * 60 ? TimeSpan.FromMinutes(minutes) : null;
    }
    public static string ShowLength(TimeSpan span) => span.Seconds == 0 ? $"{(int)span.TotalHours}:{span.Minutes:00}" : $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
    /// <summary>The time as typed back, with midnight at the end of the day written 24:00:00.</summary>
    public static string ShowClock(DateTime time, DateTime day) => time == day.Date.AddDays(1) ? "24:00:00" : time.ToString("HH:mm:ss");
}
