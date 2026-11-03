namespace Timer.Desktop;

/// <summary>A place the sun's times are worked out for. The list is small and bundled, so nothing needs the network or the computer's location.</summary>
public sealed record SunCity(string Name, double Latitude, double Longitude, double StandardOffset);

/// <summary>
/// Sunrise and sunset for a place and a day, by the usual almanac formula (accurate to a few minutes, which is plenty for a theme). The times are worked out
/// in UTC and shown in the computer's own time zone, so a city chosen in another zone still gives the right moment on this clock.
/// </summary>
public static class SunTimes
{
    /// <summary>Written where the place would be, to mean the two times are set by hand instead.</summary>
    public const string ByHand = "hand";

    // Roughly the largest cities first, so the guess from the time zone prefers a well known one when several are close.
    public static readonly IReadOnlyList<SunCity> Cities = new SunCity[]
    {
        new("London", 51.51, -0.13, 0), new("Dublin", 53.35, -6.26, 0), new("Edinburgh", 55.95, -3.19, 0), new("Manchester", 53.48, -2.24, 0), new("Paris", 48.86, 2.35, 1), new("Madrid", 40.42, -3.70, 1),
        new("Lisbon", 38.72, -9.14, 0), new("Reykjavik", 64.15, -21.94, 0), new("Berlin", 52.52, 13.40, 1), new("Amsterdam", 52.37, 4.90, 1), new("Brussels", 50.85, 4.35, 1), new("Rome", 41.90, 12.50, 1),
        new("Zurich", 47.38, 8.54, 1), new("Vienna", 48.21, 16.37, 1), new("Copenhagen", 55.68, 12.57, 1), new("Stockholm", 59.33, 18.07, 1), new("Oslo", 59.91, 10.75, 1), new("Warsaw", 52.23, 21.01, 1),
        new("Prague", 50.08, 14.44, 1), new("Athens", 37.98, 23.73, 2), new("Helsinki", 60.17, 24.94, 2), new("Istanbul", 41.01, 28.98, 3), new("Cairo", 30.04, 31.24, 2), new("Johannesburg", -26.20, 28.05, 2),
        new("Cape Town", -33.92, 18.42, 2), new("Nairobi", -1.29, 36.82, 3), new("Lagos", 6.52, 3.38, 1), new("Moscow", 55.76, 37.62, 3), new("Dubai", 25.20, 55.27, 4), new("Tehran", 35.69, 51.39, 3.5),
        new("Karachi", 24.86, 67.01, 5), new("Mumbai", 19.08, 72.88, 5.5), new("Delhi", 28.61, 77.21, 5.5), new("Dhaka", 23.81, 90.41, 6), new("Bangkok", 13.76, 100.50, 7), new("Jakarta", -6.21, 106.85, 7),
        new("Singapore", 1.35, 103.82, 8), new("Hong Kong", 22.32, 114.17, 8), new("Shanghai", 31.23, 121.47, 8), new("Beijing", 39.90, 116.41, 8), new("Manila", 14.60, 120.98, 8), new("Seoul", 37.57, 126.98, 9),
        new("Tokyo", 35.68, 139.69, 9), new("Perth", -31.95, 115.86, 8), new("Adelaide", -34.93, 138.60, 9.5), new("Brisbane", -27.47, 153.03, 10), new("Sydney", -33.87, 151.21, 10), new("Melbourne", -37.81, 144.96, 10),
        new("Auckland", -36.85, 174.76, 12), new("Honolulu", 21.31, -157.86, -10), new("Anchorage", 61.22, -149.90, -9), new("Vancouver", 49.28, -123.12, -8), new("Seattle", 47.61, -122.33, -8),
        new("San Francisco", 37.77, -122.42, -8), new("Los Angeles", 34.05, -118.24, -8), new("Phoenix", 33.45, -112.07, -7), new("Denver", 39.74, -104.99, -7), new("Mexico City", 19.43, -99.13, -6),
        new("Dallas", 32.78, -96.80, -6), new("Chicago", 41.88, -87.63, -6), new("Houston", 29.76, -95.37, -6), new("Minneapolis", 44.98, -93.27, -6), new("Toronto", 43.65, -79.38, -5), new("New York", 40.71, -74.01, -5),
        new("Boston", 42.36, -71.06, -5), new("Washington", 38.91, -77.04, -5), new("Miami", 25.76, -80.19, -5), new("Atlanta", 33.75, -84.39, -5), new("Montreal", 45.50, -73.57, -5), new("Halifax", 44.65, -63.58, -4),
        new("Bogota", 4.71, -74.07, -5), new("Lima", -12.05, -77.04, -5), new("Santiago", -33.45, -70.67, -4), new("Buenos Aires", -34.60, -58.38, -3), new("Sao Paulo", -23.55, -46.63, -3),
    };

    public static SunCity? Find(string? name) => Cities.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A place to start from when none is chosen: the city whose longitude best matches this computer's own time zone. It is only a guess, and shown so it can be changed.</summary>
    public static SunCity Guess(TimeZoneInfo zone)
    {
        // The cities on the zone's standard offset come first and the one listed first among them wins (the list puts the larger ones first); OrderBy keeps the list's order for ties.
        var hours = zone.BaseUtcOffset.TotalHours;
        return Cities.OrderBy(c => Math.Abs(c.StandardOffset - hours)).First();
    }

    /// <summary>The moments the sun rises and sets on a local day, in the zone's own time, or null for one that does not happen (the polar day and night).</summary>
    public static (DateTime? Rise, DateTime? Set) On(DateTime day, SunCity city, TimeZoneInfo zone)
    {
        DateTime? rise = null, set = null;
        // The event can fall on the day before or after in UTC, so each of the three days around it is tried and the ones landing on this local day are kept.
        for (var offset = -1; offset <= 1; offset++)
        {
            var utcDay = DateTime.SpecifyKind(day.Date.AddDays(offset), DateTimeKind.Utc);
            foreach (var rising in new[] { true, false })
            {
                if (Event(utcDay, city, rising) is not { } moment) continue;
                var local = TimeZoneInfo.ConvertTimeFromUtc(moment, zone);
                if (local.Date != day.Date) continue;
                if (rising) rise ??= local; else set ??= local;
            }
        }
        return (rise, set);
    }

    private static DateTime? Event(DateTime utcDay, SunCity city, bool rising)
    {
        const double zenith = 90.833; // The sun's edge on the horizon, with the air bending its light.
        double Rad(double degrees) => degrees * Math.PI / 180;
        double Deg(double radians) => radians * 180 / Math.PI;
        double Wrap(double value, double range) => value - Math.Floor(value / range) * range;
        var lngHour = city.Longitude / 15;
        var t = utcDay.DayOfYear + ((rising ? 6 : 18) - lngHour) / 24;
        var m = 0.9856 * t - 3.289;
        var l = Wrap(m + 1.916 * Math.Sin(Rad(m)) + 0.020 * Math.Sin(Rad(2 * m)) + 282.634, 360);
        var ra = Wrap(Deg(Math.Atan(0.91764 * Math.Tan(Rad(l)))), 360);
        ra += Math.Floor(l / 90) * 90 - Math.Floor(ra / 90) * 90; ra /= 15;
        var sinDec = 0.39782 * Math.Sin(Rad(l)); var cosDec = Math.Cos(Math.Asin(sinDec));
        var cosH = (Math.Cos(Rad(zenith)) - sinDec * Math.Sin(Rad(city.Latitude))) / (cosDec * Math.Cos(Rad(city.Latitude)));
        if (cosH > 1 || cosH < -1) return null;
        var h = (rising ? 360 - Deg(Math.Acos(cosH)) : Deg(Math.Acos(cosH))) / 15;
        var hours = Wrap(h + ra - 0.06571 * t - 6.622 - lngHour, 24);
        return utcDay.Date.AddHours(hours);
    }
}
