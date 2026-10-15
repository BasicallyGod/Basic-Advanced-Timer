using System.Text.Json;
using System.Text.RegularExpressions;

using Timer.Core;

namespace Timer.Desk;

/// <summary>A to-do item. The account it belongs to is kept by name, so a rename moves it along, and it is cleared if that account is deleted.</summary>
public sealed record TodoItem(string Text, bool Done = false, string? Account = null);

/// <summary>A post-it: some text on a coloured square. The colour is one of <see cref="Trinkets.PostItColours"/>.</summary>
public sealed record PostIt(string Text = "", string Colour = "lemon", bool Tape = true);

/// <summary>What the desk's trinkets hold. It is plain data, saved as one small piece of JSON beside the other settings.</summary>
/// <param name="TodosMoved">Set once the saved to-dos have been turned into notes, which now hold every task. The old list stays in the file as it was.</param>
public sealed record TrinketData(IReadOnlyList<TodoItem> Todos, IReadOnlyList<PostIt> PostIts, bool TodosMoved = false)
{
    /// <summary>Three post-its, the first on the desk and the others kept in the drawer until wanted.</summary>
    public static readonly TrinketData Empty = new(Array.Empty<TodoItem>(), new[] { new PostIt(), new PostIt("", "lilac", false), new PostIt("", "rose") });
}

public static class Trinkets
{
    public const int PostItCount = 3;
    public static readonly IReadOnlyList<string> PostItColours = new[] { "lemon", "lilac", "rose", "sky", "mint" };
    private static readonly Regex Tag = new(@"(?:^|\s)#([\p{L}\p{N}_-]+)", RegexOptions.Compiled);

    /// <summary>
    /// Splits a typed to-do into its words and the account a #Tag names. The tag is the account's name with its spaces taken out, in any
    /// case (#AccountOne for "Account one"), or the start of one when only a single account begins that way. A tag that names no account stays in the text.
    /// </summary>
    public static TodoItem ParseTodo(string typed, IEnumerable<string> accounts)
    {
        if (FindAccountTag(typed, accounts) is not { } found) return new TodoItem(typed.Trim());
        var text = Regex.Replace((typed.Remove(found.Index, found.Length)).Trim(), @"\s{2,}", " ");
        return new TodoItem(text.Length == 0 ? found.Account : text, false, found.Account);
    }

    /// <summary>
    /// Finds the first #Tag in some words that names exactly one account (the account's name with its spaces taken out, in any case, or the start of
    /// one when only a single account begins that way), with where the tag sits. The same rule serves to-dos and notes, so one shorthand means one thing.
    /// </summary>
    public static (string Account, int Index, int Length, string Tag)? FindAccountTag(string typed, IEnumerable<string> accounts)
    {
        var names = accounts.Where(n => !BreakAccount.Is(n)).ToList();
        foreach (Match match in Tag.Matches(typed))
        {
            var tag = match.Groups[1].Value;
            string Squash(string name) => new string(name.Where(c => !char.IsWhiteSpace(c)).ToArray());
            var exact = names.Where(n => Squash(n).Equals(tag, StringComparison.OrdinalIgnoreCase)).ToList();
            var starts = exact.Count > 0 ? exact : names.Where(n => Squash(n).StartsWith(tag, StringComparison.OrdinalIgnoreCase)).ToList();
            if (starts.Count == 1) return (starts[0], match.Index, match.Length, tag);
        }
        return null;
    }

    public static string ToJson(TrinketData data) => JsonSerializer.Serialize(data);

    /// <summary>Reads saved trinkets, falling back to an empty desk when there are none or the text is damaged, so a bad file never stops the app opening.</summary>
    public static TrinketData FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return TrinketData.Empty;
        try
        {
            var data = JsonSerializer.Deserialize<TrinketData>(json);
            if (data is null) return TrinketData.Empty;
            var posts = (data.PostIts ?? Array.Empty<PostIt>()).Select(p => new PostIt(p.Text ?? "", PostItColours.Contains(p.Colour) ? p.Colour : "lemon", p.Tape)).ToList();
            while (posts.Count < PostItCount) posts.Add(new PostIt("", PostItColours[posts.Count % PostItColours.Count]));
            return new TrinketData((data.Todos ?? Array.Empty<TodoItem>()).Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToList(), posts, data.TodosMoved);
        }
        catch (JsonException) { return TrinketData.Empty; }
    }

    /// <summary>Follows an account rename or removal: renamed accounts keep their to-dos, and a deleted account's to-dos stay but lose the link.</summary>
    public static TrinketData Relink(TrinketData data, IReadOnlyDictionary<string, string> renamed, IReadOnlyCollection<string> deleted)
        => data with
        {
            Todos = data.Todos.Select(t => t.Account is not { } a ? t
                : renamed.TryGetValue(a, out var now) ? t with { Account = now }
                : deleted.Contains(a) ? t with { Account = null } : t).ToList()
        };
}
