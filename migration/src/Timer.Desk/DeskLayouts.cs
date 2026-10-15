using System.Text.Json;

using Timer.Core;

namespace Timer.Desk;

/// <summary>
/// A named arrangement of the desk: which tiles are out, their order, sizes and cells. It holds only the arrangement, never what is inside the tiles,
/// so applying one never touches notes, post-it words or any recorded time. Sizes and cells are kept in the same written forms the settings use.
/// </summary>
public sealed record SavedLayout(string Name, IReadOnlyList<string> Order, IReadOnlyList<string> Hidden, IReadOnlyList<string> Sizes, IReadOnlyList<string> Positions);

public static class DeskLayouts
{
    public const int MaxLayouts = 12, MaxNameLength = 24;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>A name as it will be kept: trimmed, one line, and not too long.</summary>
    public static string CleanName(string? name)
    {
        var clean = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length > MaxNameLength ? clean[..MaxNameLength].TrimEnd() : clean;
    }

    /// <summary>Adds a layout, or replaces the one with the same name (any case) in its place. Returns the list unchanged when the name is empty or the list is full.</summary>
    public static List<SavedLayout> Upsert(IEnumerable<SavedLayout> layouts, SavedLayout layout)
    {
        var list = layouts.ToList(); var name = CleanName(layout.Name); if (name.Length == 0) return list;
        var fresh = layout with { Name = name };
        var at = list.FindIndex(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) list[at] = fresh; else if (list.Count < MaxLayouts) list.Add(fresh);
        return list;
    }

    public static List<SavedLayout> Remove(IEnumerable<SavedLayout> layouts, string name)
        => layouts.Where(l => !l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();

    public static string ToJson(IEnumerable<SavedLayout> layouts) => JsonSerializer.Serialize(layouts, Options);

    /// <summary>Reads saved layouts; anything unreadable gives none, and an entry with no name is dropped, so a damaged file never stops the desk opening.</summary>
    public static List<SavedLayout> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var read = JsonSerializer.Deserialize<List<SavedLayout>>(json, Options) ?? new();
            return read.Where(l => l is not null && CleanName(l.Name).Length > 0)
                .Select(l => new SavedLayout(CleanName(l.Name), l.Order ?? Array.Empty<string>(), l.Hidden ?? Array.Empty<string>(), l.Sizes ?? Array.Empty<string>(), l.Positions ?? Array.Empty<string>()))
                .Take(MaxLayouts).ToList();
        }
        catch (JsonException) { return new(); }
    }
}
