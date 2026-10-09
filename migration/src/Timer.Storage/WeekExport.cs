using System.Globalization;
using Timer.Core;

namespace Timer.Storage;

// Integer hundredths are allocated first to account/day buckets, then to their
// entries. This keeps detail rows, daily cells and the grand total identical.
public sealed class WeekExport
{
    private readonly DateTime week;
    public IReadOnlyList<ReviewEntry> Entries { get; }
    public IReadOnlyList<long> EntryUnits { get; }
    public IReadOnlyDictionary<string, long[]> Accounts { get; }
    public long TotalUnits { get; }
    public long LunchUnits { get; }
    public WeekExport(LedgerEditor editor, TrackingEngine engine, DateTime week)
    {
        this.week = week.Date;
        Entries = editor.Entries(this.week, this.week.AddDays(7));
        var source = engine.Sessions.Where(s => s.Day >= this.week && s.Day < this.week.AddDays(7)).ToArray();
        foreach (var bucket in source.GroupBy(s => (s.Day, Account: s.Account.ToUpperInvariant())))
        {
            var expected = bucket.Sum(s => decimal.Parse(s.Minutes, CultureInfo.InvariantCulture));
            var actual = Entries.Where(e => e.Day == bucket.Key.Day && e.Account.ToUpperInvariant() == bucket.Key.Account).Sum(e => e.Minutes);
            if (expected != actual) throw new InvalidDataException("Review entries do not match credited time; export was stopped.");
        }
        var indexed = Entries.Select((e, i) => (Entry: e, Index: i)).ToArray();
        var units = new long[Entries.Count]; var accounts = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var lunch in new[] { false, true })
        {
            var buckets = indexed.Where(p => p.Entry.Account.Equals("Lunch", StringComparison.OrdinalIgnoreCase) == lunch)
                .GroupBy(p => (p.Entry.Day, Account: p.Entry.Account.ToUpperInvariant()))
                .OrderBy(g => g.Key.Day).ThenBy(g => g.Key.Account, StringComparer.Ordinal).ToArray();
            var target = (long)decimal.Round(buckets.Sum(g => g.Sum(p => p.Entry.Minutes)) / 60m * 100m, 0, MidpointRounding.AwayFromZero);
            if (lunch) LunchUnits = target; else TotalUnits = target;
            var bucketUnits = Allocate(buckets.Select(g => g.Sum(p => p.Entry.Minutes) / 60m * 100m).ToArray(), target);
            for (var b = 0; b < buckets.Length; b++)
            {
                var parts = buckets[b].ToArray();
                var partUnits = Allocate(parts.Select(p => p.Entry.Minutes / 60m * 100m).ToArray(), bucketUnits[b]);
                for (var i = 0; i < parts.Length; i++) units[parts[i].Index] = partUnits[i];
                var name = parts[0].Entry.Account;
                if (!accounts.TryGetValue(name, out var days)) accounts[name] = days = new long[7];
                days[(int)(parts[0].Entry.Day - this.week).TotalDays] = bucketUnits[b];
            }
        }
        EntryUnits = units; Accounts = accounts;
    }
    public static long[] Allocate(IReadOnlyList<decimal> ideal, long target)
    {
        var result = ideal.Select(n => (long)decimal.Floor(n)).ToArray();
        var remainder = target - result.Sum();
        if (remainder < 0 || remainder > ideal.Count) throw new InvalidDataException("Unable to reconcile decimal hours.");
        foreach (var i in Enumerable.Range(0, ideal.Count).OrderByDescending(i => ideal[i] - result[i]).ThenBy(i => i).Take((int)remainder)) result[i]++;
        return result;
    }
    public static string Hours(long units) => (units / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    public string DetailCsv()
    {
        var headers = new[] { "Day", "Date", "Account", "Start", "End", "Duration", "Hours", "Type", "Parts", "Note" };
        var rows = Entries.Select((e, i) => new Dictionary<string, string>
        {
            ["Day"] = e.Day.ToString("ddd dd/MM", CultureInfo.GetCultureInfo("en-GB")), ["Date"] = e.Day.ToString("yyyy-MM-dd"),
            ["Account"] = e.Account, ["Start"] = e.Start?.ToString("HH:mm:ss") ?? "", ["End"] = e.End == e.Day.AddDays(1) ? "24:00:00" : e.End?.ToString("HH:mm:ss") ?? "",
            ["Duration"] = Duration(e.Minutes), ["Hours"] = Hours(EntryUnits[i]), ["Type"] = e.Type,
            ["Parts"] = Math.Max(1, e.Sources.Count).ToString(CultureInfo.InvariantCulture), ["Note"] = e.Note
        }).Where(r => !r["Account"].Equals("Lunch", StringComparison.OrdinalIgnoreCase)).ToList();
        return new CsvTable(headers, rows).Serialize();
    }
    private static string Duration(decimal minutes)
    {
        var value = Math.Abs(minutes); return (minutes < 0 ? "-" : "") + (int)(value / 60) + ":" + ((int)value % 60).ToString("00");
    }
    public string SummaryCsv(IEnumerable<string> configured)
    {
        var labels = Enumerable.Range(0, 7).Select(i => week.AddDays(i).ToString("ddd dd/MM", CultureInfo.GetCultureInfo("en-GB"))).ToArray();
        var headers = new[] { "Account" }.Concat(labels).Append("Total").ToArray();
        var rows = new List<Dictionary<string, string>>();
        Dictionary<string, string> Row(string account, long[] units)
        {
            var row = new Dictionary<string, string> { ["Account"] = account, ["Total"] = Hours(units.Sum()) };
            for (var i = 0; i < 7; i++) row[labels[i]] = Hours(units[i]); return row;
        }
        foreach (var account in configured.Concat(Accounts.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Where(a => !a.Equals("Lunch", StringComparison.OrdinalIgnoreCase)))
            rows.Add(Row(account, Accounts.GetValueOrDefault(account) ?? new long[7]));
        if (LunchUnits != 0) rows.Add(Row("Lunch", Accounts.GetValueOrDefault("Lunch") ?? new long[7]));
        rows.Add(Row("Total (excluding lunch)", Enumerable.Range(0, 7).Select(i => Accounts.Where(a => !a.Key.Equals("Lunch", StringComparison.OrdinalIgnoreCase)).Sum(a => a.Value[i])).ToArray()));
        return new CsvTable(headers, rows).Serialize();
    }
    public string Write(string directory, IEnumerable<string> configured)
    {
        Directory.CreateDirectory(directory);
        var name = "Week-" + week.ToString("yyyyMMdd") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var staging = Path.Combine(directory, "." + name); var destination = Path.Combine(directory, name);
        Directory.CreateDirectory(staging);
        try
        {
            File.WriteAllText(Path.Combine(staging, "Detail.csv"), DetailCsv());
            File.WriteAllText(Path.Combine(staging, "Summary.csv"), SummaryCsv(configured));
            Directory.Move(staging, destination); return destination;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
