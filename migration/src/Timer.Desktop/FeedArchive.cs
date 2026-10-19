using Microsoft.Data.Sqlite;
using System.Text.Json;
using Timer.Core;

namespace Timer.Desktop;

public sealed record FeedSource(string Id, string Name, string Kind, string Location, string? Account = null,
    bool Paused = false, DateTime? LastSuccess = null, string? Error = null, bool General = false);
public sealed record FeedReport(string Key, string Title, string Original, IReadOnlyList<FeedEntry> Entries);
public sealed record FeedImportResult(FeedSource Source, IReadOnlyList<FeedReport> Reports, string? Error = null);

/// <summary>External content has its own database; imports never write to the time ledger or source files.</summary>
public sealed class FeedArchive : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly bool transient;
    private int leases;
    // One operation at a time, and one transaction at a time: a SqliteConnection is not thread safe, and a second BeginTransaction on it throws.
    private readonly object gate = new();
    private bool inTransaction;
    public FeedArchive(string? dataFolder)
    {
        transient = dataFolder is not null;
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dataFolder is null ? ":memory:" : Path.Combine(dataFolder, "feed.db"),
            Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            using var version = connection.CreateCommand(); version.CommandText = "PRAGMA user_version";
            if (Convert.ToInt32(version.ExecuteScalar()) > 1) throw new InvalidDataException("This Feed database needs a newer version of the app.");
            using var lease = Open();
            Execute("""
            CREATE TABLE IF NOT EXISTS sources (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS reports (id TEXT PRIMARY KEY, source TEXT NOT NULL, title TEXT NOT NULL, original TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS entries (id TEXT PRIMARY KEY, report TEXT NOT NULL, account TEXT, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS marks (id TEXT PRIMARY KEY, was_read INTEGER NOT NULL, done INTEGER NOT NULL);
            PRAGMA user_version=1;
            """);
        }
        catch { connection.Dispose(); throw; }
    }

    // Disk connections are scoped to one operation, so shutdown and data-folder moves never retain a file handle.
    // An in-memory archive stays open for demo models and tests.
    private IDisposable Open()
    {
        Monitor.Enter(gate);
        try { if (connection.State != System.Data.ConnectionState.Open) connection.Open(); leases++; }
        catch { Monitor.Exit(gate); throw; }
        return new Lease(() => { try { if (--leases == 0 && transient) connection.Close(); } finally { Monitor.Exit(gate); } });
    }
    private SqliteTransaction Begin()
    {
        if (inTransaction) throw new InvalidOperationException("A Feed save began while another Feed save was still open.");
        try { return Started(connection.BeginTransaction()); }
        catch (InvalidOperationException) when (transient)
        {
            // Nothing of ours is open, so a transaction the connection still remembers was left behind by a failed operation. A new connection clears it.
            connection.Close(); connection.Open();
            return Started(connection.BeginTransaction());
        }
    }
    private SqliteTransaction Started(SqliteTransaction transaction) { inTransaction = true; return transaction; }
    private sealed class Lease(Action close) : IDisposable { public void Dispose() => close(); }

    private void Execute(string sql, params (string, object?)[] values)
    {
        using var lease = Open();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
    public IReadOnlyList<FeedSource> Sources()
    {
        using var lease = Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT json FROM sources ORDER BY id";
        using var reader = command.ExecuteReader(); var result = new List<FeedSource>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<FeedSource>(reader.GetString(0))!);
        return result;
    }
    public void SaveSource(FeedSource source) => Execute("INSERT OR REPLACE INTO sources VALUES ($id,$json)", ("$id", source.Id), ("$json", JsonSerializer.Serialize(source)));
    public void RemoveSource(string id) => Execute("DELETE FROM sources WHERE id=$id", ("$id", id));
    public IReadOnlyList<FeedEntry> Entries()
    {
        using var lease = Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT json FROM entries ORDER BY id";
        using var reader = command.ExecuteReader(); var result = new List<FeedEntry>();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<FeedEntry>(reader.GetString(0))!);
        return result.OrderByDescending(e => e.When).ToList();
    }
    public string? Account(string id) => Scalar("SELECT account FROM entries WHERE id=$id", id);
    public IReadOnlyDictionary<string, string> Accounts()
    {
        using var lease = Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT id,account FROM entries WHERE account IS NOT NULL";
        using var reader = command.ExecuteReader(); var result = new Dictionary<string, string>();
        while (reader.Read()) result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }
    /// <summary>The entries that came from a source marked as general news. Worked out from the source as it is now, so changing the mark applies to what was already imported.</summary>
    public IReadOnlySet<string> GeneralEntries()
    {
        var ids = new HashSet<string>();
        using var lease = Open();
        foreach (var source in Sources().Where(s => s.General))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT entries.id FROM entries JOIN reports ON entries.report=reports.id WHERE reports.source=$source";
            command.Parameters.AddWithValue("$source", source.Id);
            using var reader = command.ExecuteReader();
            while (reader.Read()) ids.Add(reader.GetString(0));
        }
        return ids;
    }
    public string? Original(string id) => Scalar("SELECT original FROM reports JOIN entries ON entries.report=reports.id WHERE entries.id=$id", id);
    private string? Scalar(string sql, string id)
    {
        using var lease = Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() as string;
    }
    public FeedMarks Marks()
    {
        using var lease = Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT id,was_read,done FROM marks";
        using var reader = command.ExecuteReader(); var read = new HashSet<string>(); var done = new HashSet<string>();
        while (reader.Read()) { if (reader.GetBoolean(1)) read.Add(reader.GetString(0)); if (reader.GetBoolean(2)) done.Add(reader.GetString(0)); }
        return new FeedMarks(read, done);
    }
    public void SaveMarks(FeedMarks marks)
    {
        using var lease = Open();
        using var transaction = Begin(); try {
        Execute("DELETE FROM marks");
        foreach (var id in marks.Read.Union(marks.Done))
            Execute("INSERT INTO marks VALUES ($id,$read,$done)", ("$id", id), ("$read", marks.Read.Contains(id)), ("$done", marks.Done.Contains(id)));
        transaction.Commit(); } finally { inTransaction = false; }
    }
    public void Import(FeedImportResult result, DateTime now)
    {
        using var lease = Open();
        using var transaction = Begin(); try {
        foreach (var report in result.Reports.Reverse())
        {
            var reportId = result.Source.Id + ":" + report.Key + ":" + FeedImporters.Fingerprint(report.Original);
            Execute("INSERT OR REPLACE INTO reports VALUES ($id,$source,$title,$original)",
                ("$id", reportId), ("$source", result.Source.Id), ("$title", report.Title), ("$original", report.Original));
            foreach (var entry in report.Entries)
            {
                // Keep the old folder's IDs so existing read/done marks migrate without guesswork.
                var item = entry with { Id = result.Source.Id == "legacy" ? entry.Id : result.Source.Id + ":" + entry.Id };
                // Without an explicit ID, an unambiguous heading in the same report can survive a body edit.
                if (entry.Id.StartsWith("h:", StringComparison.Ordinal))
                {
                    var prefix = result.Source.Id + ":" + report.Key + ":";
                    using var candidates = connection.CreateCommand(); candidates.Transaction = transaction;
                    candidates.CommandText = "SELECT json FROM entries WHERE substr(report,1,length($prefix))=$prefix";
                    candidates.Parameters.AddWithValue("$prefix", prefix);
                    using var reader = candidates.ExecuteReader(); var matches = new List<FeedEntry>();
                    while (reader.Read())
                    {
                        var previous = JsonSerializer.Deserialize<FeedEntry>(reader.GetString(0))!;
                        if (previous.Headline == item.Headline && previous.HasDate == item.HasDate && (!item.HasDate || previous.When == item.When)) matches.Add(previous);
                    }
                    if (matches.Count == 1 && report.Entries.Count(e => e.Headline == item.Headline) == 1) item = item with { Id = matches[0].Id };
                }
                using var existing = connection.CreateCommand(); existing.Transaction = transaction;
                existing.CommandText = "SELECT json FROM entries WHERE id=$id"; existing.Parameters.AddWithValue("$id", item.Id);
                if (existing.ExecuteScalar() is string json)
                {
                    var previous = JsonSerializer.Deserialize<FeedEntry>(json)!;
                    if (item.HasDate && previous.When > item.When) continue;
                    if (!item.HasDate && item.Body == previous.Body && item.Headline == previous.Headline) item = item with { When = previous.When };
                }
                Execute("INSERT OR REPLACE INTO entries VALUES ($id,$report,$account,$json)",
                    ("$id", item.Id), ("$report", reportId), ("$account", result.Source.General ? null : result.Source.Account), ("$json", JsonSerializer.Serialize(item)));
            }
        }
        SaveSource(result.Source with { LastSuccess = result.Error is null ? now : result.Source.LastSuccess, Error = result.Error });
        transaction.Commit(); } finally { inTransaction = false; }
    }
    public void Dispose() { lock (gate) connection.Dispose(); }
}
