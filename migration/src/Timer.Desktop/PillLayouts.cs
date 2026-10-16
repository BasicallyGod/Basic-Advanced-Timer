namespace Timer.Desktop;

/// <summary>The pill's layouts. Each is the same pill in a different arrangement, picked in Settings, with the width it needs.</summary>
public static class PillLayouts
{
    public sealed record Layout(string Key, string Title, string Description, double Width);
    public static readonly IReadOnlyList<Layout> All = new[]
    {
        new Layout("full", "Full", "Clock first, with the account, today's time and the day's progress.", PillWindow.PillWidth),
        new Layout("compact", "Compact", "Account first, then the clock. No progress line.", 520),
        new Layout("minimal", "Minimal", "Just the account's initials, the clock and pause. Break is in the right-click menu.", 360),
    };
    public static Layout For(string key) => All.FirstOrDefault(l => l.Key == key) ?? All[0];
    /// <summary>The badge's letters in the minimal layout: the first letters of up to two words, so Account three reads AT, Northwind Traders reads NT and Client 7 reads C7.</summary>
    public static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "";
        if (words.Length == 1) return words[0].Length >= 2 && char.IsDigit(words[0][^1]) ? char.ToUpperInvariant(words[0][0]).ToString() + words[0][^1] : char.ToUpperInvariant(words[0][0]).ToString();
        return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }
}
