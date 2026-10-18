using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>Adapters return reports; only FeedArchive decides how they are stored.</summary>
public static class FeedImporters
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    public static string Fingerprint(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static bool SafeUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo);

    public static FeedReport TextReport(string text, string title, DateTime date, string? key = null)
    {
        if (Encoding.UTF8.GetByteCount(text) > Feed.MaxFileBytes) throw new InvalidDataException("Reports must be smaller than 1 MB.");
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("The report is empty.");
        return new FeedReport(key ?? Fingerprint(text.Replace("\r\n", "\n").Trim()), title, text, Feed.Parse(text, title, date));
    }

    public static async Task<FeedImportResult> ReadAsync(FeedSource source)
    {
        try
        {
            return source.Kind switch
            {
                "folder" => await ReadFolderAsync(source).ConfigureAwait(false),
                "rss" => new(source, new[] { await ReadRssAsync(source).ConfigureAwait(false) }),
                _ => new(source, Array.Empty<FeedReport>())
            };
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or XmlException or ArgumentException)
        { return new(source, Array.Empty<FeedReport>(), error.Message); }
    }

    private static async Task<FeedImportResult> ReadFolderAsync(FeedSource source)
    {
        if (!Directory.Exists(source.Location)) return new(source, Array.Empty<FeedReport>(), "Folder unavailable. Saved reports are still available.");
        var files = new DirectoryInfo(source.Location).EnumerateFiles()
            .Where(f => f.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Take(Feed.MaxFiles + 1).ToArray();
        var reports = new List<FeedReport>(); var errors = new List<string>();
        if (files.Length > Feed.MaxFiles) errors.Add($"Only the newest {Feed.MaxFiles} files were checked.");
        var snapshots = new List<(FileInfo File, string Text, DateTime Written)>();
        foreach (var file in files.Take(Feed.MaxFiles))
        {
            try { snapshots.Add((file, ReadBounded(file.FullName), file.LastWriteTimeUtc)); }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { errors.Add(file.Name + ": " + e.Message); }
        }
        // Cloud sync may replace a file or finish writing it after the notification. Compare complete bounded reads.
        if (snapshots.Count > 0) await Task.Delay(400).ConfigureAwait(false);
        foreach (var (file, text, written) in snapshots)
        {
            try
            {
                if (text != ReadBounded(file.FullName)) { errors.Add(file.Name + ": still changing; will retry."); continue; }
                if (string.IsNullOrWhiteSpace(text)) { errors.Add(file.Name + ": empty; will retry."); continue; }
                var title = source.Id == "legacy" ? Path.GetFileNameWithoutExtension(file.Name) : source.Name;
                reports.Add(TextReport(text, title, written.ToLocalTime(), file.Name.ToUpperInvariant()));
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { errors.Add(file.Name + ": " + e.Message); }
        }
        return new(source, reports, errors.Count == 0 ? null : string.Join("\n", errors));
    }
    private static string ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > Feed.MaxFileBytes) throw new InvalidDataException("Larger than 1 MB.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }
    private static async Task<FeedReport> ReadRssAsync(FeedSource source)
    {
        if (!SafeUrl(source.Location)) throw new InvalidDataException("Enter an HTTP or HTTPS feed URL without credentials.");
        using var response = await Client.GetAsync(source.Location, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream(); var bytes = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(bytes, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > Feed.MaxFileBytes) throw new InvalidDataException("Feed is larger than 1 MB.");
            buffer.Write(bytes, 0, count);
        }
        return ParseRss(Encoding.UTF8.GetString(buffer.ToArray()), source.Name, DateTime.Now);
    }
    private static readonly Regex Markup = new("<[a-zA-Z/!][^>]*>", RegexOptions.Compiled);
    private static readonly Regex Hidden = new("<(script|style)\\b.*?</\\1\\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex ListItem = new("<li\\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Breaks = new("<(br|/p|/div|/li|/h[1-6]|/tr)\\b[^>]*>|<(p|div|h[1-6]|tr)\\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>Publishers often put HTML in a description (Google News escapes a whole list of links), and the Feed shows text only, so tags are turned into line breaks and bullets and the rest dropped. Nothing is executed or fetched.</summary>
    public static string PlainText(string value)
    {
        if (!Markup.IsMatch(value)) return WebUtility.HtmlDecode(value).Trim();
        var text = Hidden.Replace(value, "");
        text = ListItem.Replace(text, "\n• ");
        text = Breaks.Replace(text, "\n");
        text = Markup.Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace('\u00a0', ' ');
        text = Regex.Replace(text, "[ \\t]+", " ");
        text = Regex.Replace(text, " ?\\n ?", "\n");
        return Regex.Replace(text, "\\n{3,}", "\n\n").Trim();
    }
    public static FeedReport ParseRss(string xml, string source, DateTime now)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = Feed.MaxFileBytes });
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName is not ("rss" or "feed" or "RDF")) throw new InvalidDataException("This URL did not return RSS or Atom.");
        var entries = new List<FeedEntry>();
        foreach (var item in document.Descendants().Where(e => e.Name.LocalName is "item" or "entry").Take(500))
        {
            string Value(params string[] names) => item.Elements().FirstOrDefault(e => names.Contains(e.Name.LocalName))?.Value.Trim() ?? "";
            var linkNode = item.Elements().FirstOrDefault(e => e.Name.LocalName == "link" && (e.Attribute("rel") is null || (string?)e.Attribute("rel") == "alternate"));
            var link = (string?)linkNode?.Attribute("href") ?? linkNode?.Value;
            var title = Value("title"); if (title.Length == 0) title = source;
            var body = PlainText(Value("description", "summary", "content", "encoded"));
            if (SafeUrl(link)) body += "\n\n" + link;
            var id = Value("guid", "id"); if (id.Length == 0) id = SafeUrl(link) ? link! : title + "\n" + body;
            var hasDate = DateTimeOffset.TryParse(Value("pubDate", "published", "updated", "date"), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date);
            entries.Add(new FeedEntry("rss:" + Fingerprint(id), title, body, hasDate ? date.LocalDateTime : now, hasDate, false, source));
        }
        return new FeedReport("rss", source, xml, entries);
    }
}
