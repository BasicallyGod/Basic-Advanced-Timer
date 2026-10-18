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

/// <summary>
/// The prompt a person gives a scheduled ChatGPT or Claude task so that what it writes is something the Feed can read. It is shown in full in Settings, Feed,
/// every time (so it can be read again when setting up another task) and copied from there. It names the folder the files must end up in and the person's own
/// account tags, and asks for a new file each run, so earlier files are never overwritten and the folder keeps the history the age limit then hides.
/// </summary>
public static class FeedPrompt
{
    /// <summary>What stands in for the folder until one is chosen.</summary>
    public const string NoFolder = "(the feed folder chosen in Settings)";

    public static string Build(IReadOnlyList<(string Name, string Display)> accounts, string? folder)
    {
        var tags = accounts.Count == 0 ? new[] { "#AccountName  (the client's name)" } : accounts.Select(a => NoteShorthand.TagFor(a.Name) + "  (" + a.Display + ")").ToArray();
        return string.Join('\n', new[]
        {
            "Save each report as a new .md or .txt file in this folder: " + (string.IsNullOrWhiteSpace(folder) ? NoFolder : folder),
            "Use a different file name each run, for example feed-<task>-YYYY-MM-DD-HHMM.md. Keep links and publication dates.",
            "Ordinary text is fine. Optionally use headings for separate entries, Date: YYYY-MM-DD HH:MM and ID: a-stable-item-id to preserve identity through edits.",
            "Optional account tags (otherwise the source's default account applies):",
        }.Concat(tags).Concat(new[]
        {
            "First verify that a scheduled run can save a file here without intervention. If it cannot, paste the report using Add source in Feed settings."
        }));
    }
}
