using System.Globalization;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

public sealed record WeekTableRow(string Label, string[] Cells, string Total, string Style);
public sealed record BudgetTableRow(string Account, string Budget, string Used, string Remaining, double Fraction);

// The hours table in the Week tab as plain data, so the screen and "Copy hours table" always show the same figures.
public sealed class WeekTable
{
    public string[] Headers { get; init; } = Array.Empty<string>();
    public List<WeekTableRow> Rows { get; } = new();
    public List<BudgetTableRow> Budgets { get; } = new();

    public static WeekTable Build(TimerModel model, WeekExport reconciled, DateTime weekStart, int[] days)
    {
        var table = new WeekTable { Headers = new[] { "Account" }.Concat(days.Select(i => weekStart.AddDays(i).ToString("ddd dd/MM", CultureInfo.GetCultureInfo("en-GB")))).Append("Total").ToArray() };
        // Decimal figures use the reconciled hundredths so rows and columns add up exactly. While a timer runs they are provisional.
        var exact = model.DecimalHours && model.Engine.Active is null;
        string Units(long value) => WeekExport.Hours(value) + "h";
        long[] UnitsFor(string name) => reconciled.Accounts.GetValueOrDefault(name) ?? new long[7];
        var accounts = model.ReviewAccounts.Where(a => !BreakAccount.Is(a.Name)).ToList();

        void Add(string label, Func<int, TimeSpan> time, Func<int, long> units, string style)
        {
            var cells = days.Select(i => exact ? Units(units(i)) : model.Format(time(i))).ToArray();
            var total = exact ? Units(days.Sum(units)) : model.Format(days.Aggregate(TimeSpan.Zero, (sum, i) => sum + time(i)));
            table.Rows.Add(new(label, cells, total, style));
        }
        foreach (var account in accounts)
        {
            var values = UnitsFor(account.Name);
            Add(account.Display, i => model.OnDay(account, weekStart.AddDays(i)), i => values[i], "normal");
        }
        var perAccount = accounts.Select(a => UnitsFor(a.Name)).ToList();
        Add("Total (excluding breaks)", i => accounts.Aggregate(TimeSpan.Zero, (sum, a) => sum + model.OnDay(a, weekStart.AddDays(i))), i => perAccount.Sum(v => v[i]), "bold");
        var breakUnits = UnitsFor(BreakAccount.Name);
        var breakAccount = new TimerAccount(BreakAccount.Name, "", null);
        Add(BreakAccount.Label, i => model.OnDay(breakAccount, weekStart.AddDays(i)), i => breakUnits[i], "faint");
        // Unallocated is working time not given to any account, so it is shown as time even in decimal mode.
        var unallocated = days.Select(i => model.Engine.Totals(weekStart.AddDays(i)).Unallocated).ToArray();
        table.Rows.Add(new("Unallocated", unallocated.Select(model.Format).ToArray(), model.Format(unallocated.Aggregate(TimeSpan.Zero, (a, b) => a + b)), "faint"));

        foreach (var account in accounts.Where(a => a.Budget is not null))
        {
            var used = model.BudgetUsed(account); var target = account.Budget!.Value;
            table.Budgets.Add(new(account.Display, $"{model.Format(target)} per {account.Period}", model.Format(used),
                model.Format(used >= target ? TimeSpan.Zero : target - used), used.TotalSeconds / target.TotalSeconds));
        }
        return table;
    }

    /// <summary>Tab separated, so it pastes into a spreadsheet or timesheet as separate cells.</summary>
    public string ToTsv() => string.Join("\r\n", new[] { string.Join("\t", Headers) }
        .Concat(Rows.Select(r => string.Join("\t", new[] { r.Label }.Concat(r.Cells).Append(r.Total)))));
}
