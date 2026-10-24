namespace Timer.Desktop;

/// <summary>
/// How the three rail icons move while the pointer is over them, as the Round 7 board draws it. Plain functions of the seconds since the pointer arrived, so the
/// icon can draw any moment of the loop and the tests can read the numbers. At 0 seconds every icon is at rest, and each loop returns to its start, so it
/// repeats with no jump. With less motion asked for the icons are never played.
/// </summary>
public static class RailMotion
{
    /// <summary>The seconds each timeline bar takes to slide out and back, so the three never move together.</summary>
    public static readonly double[] BarSeconds = { 2.2, 2.7, 2.4 };
    private static readonly double[] barReach = { 1.6, -1.6, 1.5 };
    /// <summary>How far the timeline bar <paramref name="bar"/> is from its place, in icon units (an 18 unit grid), after <paramref name="seconds"/>.</summary>
    public static double BarOffset(int bar, double seconds) => barReach[bar] * Math.Sin(2 * Math.PI * seconds / BarSeconds[bar]);

    /// <summary>How long the week columns take to run through their spring.</summary>
    public const double ColumnSeconds = 2.6;
    private static readonly double[] columnDelay = { 0, .45, .9 };
    private static readonly (double At, double Scale)[] spring = { (0, 1), (.10, 1.34), (.22, .94), (.32, 1.08), (.42, 1), (1, 1) };
    /// <summary>How tall column <paramref name="column"/> is, as a multiple of its resting height, standing on the line: it springs up, dips and settles, a beat after the one before.</summary>
    public static double ColumnScale(int column, double seconds)
    {
        var t = seconds - columnDelay[column];
        if (t < 0) return 1;
        var u = t % ColumnSeconds / ColumnSeconds;
        for (var i = 1; i < spring.Length; i++)
        {
            if (u > spring[i].At) continue;
            var (a, b) = (spring[i - 1], spring[i]); var f = (u - a.At) / (b.At - a.At); f = f * f * (3 - 2 * f);
            return a.Scale + (b.Scale - a.Scale) * f;
        }
        return 1;
    }

    /// <summary>How long the notes icon takes to tick one box and cross the other, and back.</summary>
    public const double NotesSeconds = 3.2;
    private static double Smooth(double f) { f = Math.Clamp(f, 0, 1); return f * f * (3 - 2 * f); }
    /// <summary>How much of the tick in the top box is drawn, from 0 to 1: it holds, is taken back, and draws itself in again.</summary>
    public static double TickProgress(double seconds)
    {
        var u = seconds % NotesSeconds / NotesSeconds;
        return u < .22 ? 1 : u < .30 ? 1 - Smooth((u - .22) / .08) : u < .78 ? 0 : u < .90 ? Smooth((u - .78) / .12) : 1;
    }
    /// <summary>How much of the cross in the bottom box is drawn, from 0 to 1, while the tick is away.</summary>
    public static double CrossProgress(double seconds)
    {
        var u = seconds % NotesSeconds / NotesSeconds;
        return u < .30 ? 0 : u < .40 ? Smooth((u - .30) / .10) : u < .70 ? 1 : u < .78 ? 1 - Smooth((u - .70) / .08) : 0;
    }
}
