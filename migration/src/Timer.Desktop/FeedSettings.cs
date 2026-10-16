using System.Text.Json;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

/// <summary>Which feed entries have been read and which are done. Kept by the entry's ID, so they follow an entry through a rewritten file.</summary>
public sealed record FeedMarks(IReadOnlySet<string> Read, IReadOnlySet<string> Done)
{
    public static readonly FeedMarks Empty = new(new HashSet<string>(), new HashSet<string>());
}

/// <summary>
/// What the feed keeps in the settings file: the folder it reads, whether entries are kept 90 days instead of 30, whether opening an entry marks it read,
/// and the read and done marks. They travel in the library's extras (as feedfolder=, feedlong=on, feedautoread=off and a feed section), so the shared
/// storage never has to know about them, and the feed folder itself is only ever read.
/// </summary>
public sealed record FeedOptions(string? Folder, bool Longer, bool AutoRead, FeedMarks Marks)
{
    private const string FolderLine = "feedfolder=", LongLine = "feedlong=", AutoReadLine = "feedautoread=", Section = "feed";
    public static readonly FeedOptions Default = new(null, false, true, FeedMarks.Empty);

    public static FeedOptions From(StoreSettings settings)
    {
        var extras = settings.Extras ?? SettingsExtras.Empty; var lines = extras.AppearanceLines;
        string? Value(string key) => lines.FirstOrDefault(l => l.StartsWith(key, StringComparison.Ordinal))?[key.Length..];
        var folder = Value(FolderLine); if (string.IsNullOrWhiteSpace(folder)) folder = null;
        var marks = FeedMarks.Empty;
        if (extras.Sections.TryGetValue(Section, out var found) && found.FirstOrDefault() is { Length: > 0 } json)
        {
            // A damaged section never stops the app: the marks are simply lost and every entry looks new.
            try
            {
                using var document = JsonDocument.Parse(json);
                HashSet<string> Ids(string name) => document.RootElement.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToHashSet() : new HashSet<string>();
                marks = new FeedMarks(Ids("read"), Ids("done"));
            }
            catch (JsonException) { }
        }
        return new FeedOptions(folder, Value(LongLine) == "on", Value(AutoReadLine) != "off", marks);
    }

    /// <summary>The settings with these options written in, leaving every other line and section as it was.</summary>
    public StoreSettings Into(StoreSettings settings)
    {
        var extras = settings.Extras ?? SettingsExtras.Empty;
        var lines = extras.AppearanceLines.Where(l => !l.StartsWith(FolderLine, StringComparison.Ordinal) && !l.StartsWith(LongLine, StringComparison.Ordinal) && !l.StartsWith(AutoReadLine, StringComparison.Ordinal)).ToList();
        if (Folder is not null) lines.Add(FolderLine + Folder);
        if (Longer) lines.Add(LongLine + "on");
        if (!AutoRead) lines.Add(AutoReadLine + "off");
        var sections = extras.Sections.Where(p => p.Key != Section).ToDictionary(p => p.Key, p => p.Value);
        if (Marks.Read.Count > 0 || Marks.Done.Count > 0)
            sections[Section] = new[] { JsonSerializer.Serialize(new { read = Marks.Read.OrderBy(i => i, StringComparer.Ordinal), done = Marks.Done.OrderBy(i => i, StringComparer.Ordinal) }) };
        return settings with { Extras = new SettingsExtras(lines, sections) };
    }
}

/// <summary>The prompt that is copied from Settings for a scheduled ChatGPT or Claude task, with the person's own account tags in it.</summary>
public static class FeedPrompt
{
    public static string Build(IReadOnlyList<string> accounts)
    {
        var tags = accounts.Count == 0 ? "#AccountName" : string.Join(", ", accounts.Select(NoteShorthand.TagFor));
        return string.Join('\n', new[]
        {
            "Write my client updates as a Markdown file I can drop in a folder, newest first. One entry per item, in exactly this form:",
            "",
            "## Short headline (start it with ! if it needs action today)",
            "Date: 2026-10-16 08:30",
            "ID: a-short-stable-name-for-this-item",
            "#AccountName",
            "One to three plain sentences. Longer detail is fine when it is useful.",
            "",
            "Rules:",
            "- Put the client's tag on its own line in the entry. My tags are: " + tags + ". Use only these; leave the tag out if an item is not about one client.",
            "- Always include the Date line, as YYYY-MM-DD with an optional HH:MM.",
            "- Give every entry an ID line that stays the same if you later reword the entry or write it again.",
            "- Plain text and simple Markdown only (bold, italic, bullet lists). No HTML, no tables, no images.",
            "- Keep each entry about one thing. Do not repeat entries you have already written unless they changed.",
        });
    }
}
