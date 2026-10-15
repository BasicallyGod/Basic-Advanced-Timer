using System.Text.Json;

using Timer.Core;

namespace Timer.Desk;

/// <summary>
/// A desk tile that has been detached to float on the screen in a window of its own. Its size is in desk cells, so it shows the same version of itself
/// as it would on the desk; the position is in screen pixels, or null until it has been placed.
/// </summary>
public sealed record FloatingWidget(string Id, int? X, int? Y, int Columns, int Rows);

public static class FloatingWidgets
{
    /// <summary>How many tiles may float at once, besides the pill, so the screen never fills with small windows.</summary>
    public const int Max = 3;

    /// <summary>Adds a floating widget. A tile already floating is left as it was, and nothing is added once <see cref="Max"/> are floating.</summary>
    public static (List<FloatingWidget> List, bool Added) Add(IEnumerable<FloatingWidget> floating, FloatingWidget widget)
    {
        var list = floating.ToList();
        if (list.Any(f => f.Id == widget.Id) || list.Count >= Max) return (list, false);
        list.Add(widget); return (list, true);
    }

    public static List<FloatingWidget> Remove(IEnumerable<FloatingWidget> floating, string id) => floating.Where(f => f.Id != id).ToList();

    public static string ToJson(IEnumerable<FloatingWidget> floating) => JsonSerializer.Serialize(floating);

    /// <summary>Reads the saved widgets, keeping only known tiles (when a list of them is given), each once, and no more than <see cref="Max"/>. Anything unreadable gives none.</summary>
    public static List<FloatingWidget> FromJson(string? json, IReadOnlyCollection<string>? knownIds = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var read = JsonSerializer.Deserialize<List<FloatingWidget>>(json) ?? new();
            return read.Where(f => f is not null && f.Id is not null && (knownIds is null || knownIds.Contains(f.Id))).GroupBy(f => f.Id).Select(g => g.First())
                .Select(f => f with { Columns = Math.Clamp(f.Columns, 1, 12), Rows = Math.Clamp(f.Rows, 1, 24) }).Take(Max).ToList();
        }
        catch (JsonException) { return new(); }
    }
}
