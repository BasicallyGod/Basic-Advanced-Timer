using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualBasic.FileIO;
using Timer.Core;

namespace Timer.Storage;

public sealed record AccountSettings(string Name, double? BudgetHours = null, string BudgetPeriod = "week");
public sealed record StoreSettings(List<AccountSettings> Accounts, bool DecimalHours, string Theme, DayOfWeek WeekStart);
public sealed record RecoveryState(int SchemaVersion, ActiveSession? Active, DateTime LastSeen);
public sealed record SaveJournal(int SchemaVersion, Dictionary<string, string> Files);

// A folder lock plus write-ahead journal makes sessions/day state/recovery a recoverable transaction.
public sealed class TimerStore : IDisposable
{
    private readonly string directory;
    private readonly FileStream folderLock;
    private JsonObject settings = new();
    private readonly List<string> headers = new() { "Date", "Account", "Start", "End", "Minutes" };
    private readonly List<Dictionary<string, string>> originalRows = new();
    private int originalCount;
    public TimerStore(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        if (File.Exists(Path.Combine(this.directory, "AccountTimer2.ps1")) || File.Exists(Path.Combine(this.directory, "StartAccountTimer2.vbs")))
            throw new IOException("Use a separate data folder or a copy of your timesheet data, rather than the running PowerShell application folder.");
        Directory.CreateDirectory(this.directory);
        folderLock = new FileStream(Path.Combine(this.directory, ".migration.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { RecoverJournal(); } catch { folderLock.Dispose(); throw; }
    }
    public StoreSettings Load(TrackingEngine engine)
    {
        var settingsPath = Path.Combine(directory, "basics-advanced-timer.settings.json");
        if (File.Exists(settingsPath))
        {
            settings = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? throw new InvalidDataException("Settings must be a JSON object.");
            if (settings["schemaVersion"]?.GetValue<int>() != 1 || settings["sections"] is not JsonObject)
                throw new InvalidDataException("Unsupported or incomplete settings schema; no files have been reset.");
        }
        else settings = new() { ["schemaVersion"] = 1, ["sections"] = new JsonObject() };
        var accounts = Lines("accounts").Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Split('\t'))
            .Where(s => s[0] != "Lunch" && (s.Length < 2 || s[1] != "hidden")).Select(s => new AccountSettings(s[0])).DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (accounts.Count == 0 && !Lines("accounts").Any()) accounts = new() { new("Account one"), new("Account two"), new("Internal") };
        foreach (var line in Lines("budgets"))
        {
            var p = line.Split('\t');
            if (p.Length < 3 || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) || hours <= 0 || !double.IsFinite(hours))
                throw new InvalidDataException("Invalid budget settings.");
            if (p[2] is not ("week" or "month" or "quarter")) throw new InvalidDataException("Unknown budget period.");
            var i = accounts.FindIndex(a => a.Name.Equals(p[0], StringComparison.OrdinalIgnoreCase));
            if (i >= 0) accounts[i] = accounts[i] with { BudgetHours = hours, BudgetPeriod = p[2] };
        }
        var decimalHours = false;
        var weekStart = DayOfWeek.Monday;
        foreach (var line in Lines("workingHours"))
        {
            var p = line.Split('=', 2); if (p.Length != 2) continue;
            switch (p[0].ToLowerInvariant())
            {
                case "start": engine.Schedule.Default = engine.Schedule.Default with { Start = Clock(p[1]) }; break;
                case "end": engine.Schedule.Default = engine.Schedule.Default with { End = Clock(p[1]) }; break;
                case "hours": decimalHours = p[1] == "decimal"; break;
                case "weekstart": weekStart = Dow(p[1]); break;
                case "days": engine.Schedule.Days = p[1].Split(',').Select(Dow).ToHashSet(); break;
                default:
                    if (p[0].StartsWith("shift."))
                    { var parts = p[1].Split(','); engine.Schedule.Shifts[Dow(p[0][6..])] = new(Clock(parts[0]), Clock(parts[1])); }
                    break;
            }
        }
        foreach (var line in Lines("dayOverrides"))
        {
            var p = line.Split('\t'); var day = DateTime.ParseExact(p[0], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            engine.Schedule.Overrides[day] = p[1] == "off" ? new(true, null) : new(false, new(Clock(p[1]), Clock(p[2])));
        }
        foreach (var line in Lines("dayStarts"))
        {
            var p = line.Split('\t'); engine.DayStarts[DateTime.ParseExact(p[0], "yyyy-MM-dd", CultureInfo.InvariantCulture)] = DateTime.Parse(p[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }
        var shifts = engine.Schedule.Shifts.Values.Append(engine.Schedule.Default)
            .Concat(engine.Schedule.Overrides.Values.Where(o => !o.Off).Select(o => o.Shift!));
        if (shifts.Any(s => s.End <= s.Start)) throw new InvalidDataException("A working shift must finish after it starts.");
        var sessionsPath = Path.Combine(directory, "Timesheets", "sessions.csv");
        if (File.Exists(sessionsPath))
        {
            using var parser = new TextFieldParser(sessionsPath, Encoding.UTF8) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
            parser.SetDelimiters(",");
            var columns = parser.ReadFields() ?? throw new InvalidDataException("Missing sessions header.");
            if (headers.Except(columns).Any() || columns.Distinct().Count() != columns.Length) throw new InvalidDataException("Invalid sessions header.");
            headers.Clear(); headers.AddRange(columns);
            while (!parser.EndOfData)
            {
                var values = parser.ReadFields()!;
                if (values.Length != headers.Count) throw new InvalidDataException("Invalid sessions row.");
                var raw = headers.Zip(values).ToDictionary(x => x.First, x => x.Second);
                var row = new SessionRow(raw["Date"], raw["Account"], raw["Start"], raw["End"], raw["Minutes"]);
                _ = row.From; _ = row.To; // Validate before any writes.
                originalRows.Add(raw); engine.Sessions.Add(row);
            }
        }
        originalCount = engine.Sessions.Count;
        var awayPath = Path.Combine(directory, "account-timer-away.txt");
        if (File.Exists(awayPath)) foreach (var line in File.ReadAllLines(awayPath).Where(s => !string.IsNullOrWhiteSpace(s)))
        { var p = line.Split('\t'); engine.Away.Add(new(DateTime.Parse(p[0], CultureInfo.InvariantCulture), p.Length > 1 && p[1].Length > 0 ? DateTime.Parse(p[1], CultureInfo.InvariantCulture) : null)); }
        var recoveryPath = Path.Combine(directory, "migration-recovery.json");
        if (File.Exists(recoveryPath))
        {
            var recovery = JsonSerializer.Deserialize<RecoveryState>(File.ReadAllText(recoveryPath)) ?? throw new InvalidDataException("Invalid recovery state.");
            if (recovery.SchemaVersion != 1) throw new InvalidDataException("Unsupported recovery schema.");
            engine.Recover(recovery.Active, recovery.LastSeen);
        }
        else
        {
            var legacyState = Path.Combine(directory, "account-timer-state.txt");
            if (File.Exists(legacyState))
            {
                var p = File.ReadAllLines(legacyState);
                if (p.Length >= 4 && p[1].Length > 0) engine.Recover(new(p[2], DateTime.Parse(p[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)), DateTime.Parse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
            }
        }
        var theme = Lines("migrationAppearance").FirstOrDefault() ?? "System";
        if (theme is not ("System" or "Light" or "Dark")) theme = "System";
        return new(accounts, decimalHours, theme, weekStart);
    }
    public void Save(TrackingEngine engine, StoreSettings preferences)
    {
        if (engine.Sessions.Count < originalCount) throw new InvalidOperationException("This migration stage cannot edit or remove source sessions.");
        var sections = (JsonObject)settings["sections"]!;
        sections["dayStarts"] = Array(engine.DayStarts.OrderBy(p => p.Key).Select(p => $"{p.Key:yyyy-MM-dd}\t{p.Value:o}"));
        sections["migrationAppearance"] = Array(new[] { preferences.Theme });
        if (sections["accounts"] is null) sections["accounts"] = Array(preferences.Accounts.Select(a => a.Name));
        var hours = Lines("workingHours").Where(s => !s.StartsWith("hours=", StringComparison.OrdinalIgnoreCase)).ToList();
        hours.Add("hours=" + (preferences.DecimalHours ? "decimal" : "hm")); sections["workingHours"] = Array(hours);
        var rows = new List<Dictionary<string, string>>(originalRows);
        foreach (var s in engine.Sessions.Skip(originalCount)) rows.Add(new() { ["Date"] = s.Date, ["Account"] = s.Account, ["Start"] = s.Start, ["End"] = s.End, ["Minutes"] = s.Minutes });
        string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var csv = string.Join(",", headers.Select(Q)) + "\r\n" + string.Concat(rows.Select(row => string.Join(",", headers.Select(h => Q(row.GetValueOrDefault(h, "")))) + "\r\n"));
        var files = new Dictionary<string, string>
        {
            ["Timesheets/sessions.csv"] = csv,
            ["basics-advanced-timer.settings.json"] = settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            ["account-timer-away.txt"] = string.Join("\n", engine.Away.Select(a => $"{a.Start:s}\t{(a.End is { } end ? end.ToString("s") : "")}")),
            ["migration-recovery.json"] = JsonSerializer.Serialize(new RecoveryState(1, engine.Active, engine.Now)),
            // Clearing the legacy checkpoint prevents the same recovery being credited on rollback.
            ["account-timer-state.txt"] = $"{engine.Now:o}\n\n\n\n"
        };
        Atomic("migration-journal.json", JsonSerializer.Serialize(new SaveJournal(1, files)), false);
        foreach (var file in files) Atomic(file.Key, file.Value, true);
        File.Delete(Path.Combine(directory, "migration-journal.json"));
        originalRows.Clear(); originalRows.AddRange(rows); originalCount = engine.Sessions.Count;
    }
    private void RecoverJournal()
    {
        var path = Path.Combine(directory, "migration-journal.json");
        if (!File.Exists(path)) return;
        var journal = JsonSerializer.Deserialize<SaveJournal>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid save journal.");
        var allowed = new[] { "Timesheets/sessions.csv", "basics-advanced-timer.settings.json", "account-timer-away.txt", "migration-recovery.json", "account-timer-state.txt" };
        if (journal.SchemaVersion != 1 || journal.Files.Count != allowed.Length || journal.Files.Keys.Except(allowed).Any()) throw new InvalidDataException("Unsupported save journal.");
        foreach (var file in journal.Files) Atomic(file.Key, file.Value, true);
        File.Delete(path);
    }
    private void Atomic(string relative, string content, bool backup)
    {
        var path = Path.Combine(directory, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { var bytes = new UTF8Encoding(true).GetBytes("\uFEFF" + content); stream.Write(bytes); stream.Flush(true); }
        if (backup && File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(temporary, path, true);
    }
    private IEnumerable<string> Lines(string key) => settings["sections"]?[key] is JsonArray array
        ? array.Select(s => s?.GetValue<string>() ?? "").ToArray() : System.Array.Empty<string>();
    private static JsonArray Array(IEnumerable<string> lines) => new(lines.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
    private static TimeSpan Clock(string s) => TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1)
        ? time : throw new InvalidDataException("Invalid working-hours clock.");
    private static DayOfWeek Dow(string s) => s.Trim().ToLowerInvariant() switch
    { "sun" => DayOfWeek.Sunday, "mon" => DayOfWeek.Monday, "tue" => DayOfWeek.Tuesday, "wed" => DayOfWeek.Wednesday, "thu" => DayOfWeek.Thursday, "fri" => DayOfWeek.Friday, "sat" => DayOfWeek.Saturday, _ => throw new InvalidDataException("Invalid weekday.") };
    public void Dispose() => folderLock.Dispose();
}
