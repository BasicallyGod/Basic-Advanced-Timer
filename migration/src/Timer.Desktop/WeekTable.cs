using System.Globalization;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

// Cells and Total are the short forms shown on screen (7:00, with a dash for nothing). CopyCells and CopyTotal keep the full wording for pasting.
public sealed record WeekTableRow(string Label, string[] Cells, string Total, string Style, string[] CopyCells, string CopyTotal);
/// <summary>One budgeted account in a week view. Period names the budget period the Used figure belongs to.</summary>
public sealed record BudgetTableRow(string Account, string Budget, string Used, string Remaining, double Fraction, string Period = "");

// The hours table in the Week tab as plain data, so the screen and "Copy hours table" always show the same figures.
public sealed class WeekTable
{
    public const string AllocatedLabel = "Allocated", UnallocatedLabel = "Unallocated", WorkingLabel = "Working time", BreaksLabel = "Breaks";
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

        // On screen the table uses the short form, so seven days fit the width, and a dash where nothing was recorded.
        string Short(TimeSpan time) => time == TimeSpan.Zero ? "–" : model.FormatShort(time);
        string ShortUnits(long value) => value == 0 ? "–" : WeekExport.Hours(value);
        void Add(string label, Func<int, TimeSpan> time, Func<int, long> units, string style)
        {
            var cells = days.Select(i => exact ? ShortUnits(units(i)) : Short(time(i))).ToArray();
            var copy = days.Select(i => exact ? Units(units(i)) : model.Format(time(i))).ToArray();
            var sum = days.Aggregate(TimeSpan.Zero, (acc, i) => acc + time(i));
            table.Rows.Add(new(label, cells, exact ? ShortUnits(days.Sum(units)) : Short(sum), style, copy, exact ? Units(days.Sum(units)) : model.Format(sum)));
        }
        foreach (var account in accounts)
        {
            var values = UnitsFor(account.Name);
            Add(account.Display, i => model.OnDay(account, weekStart.AddDays(i)), i => values[i], "normal");
        }
        var perAccount = accounts.Select(a => UnitsFor(a.Name)).ToList();
        // One meaning throughout: Working time is what was given to accounts (Allocated) plus what was not (Unallocated). Breaks are never part of it.
        TimeSpan AllocatedOn(int i) => accounts.Aggregate(TimeSpan.Zero, (sum, a) => sum + model.OnDay(a, weekStart.AddDays(i)));
        TimeSpan UnallocatedOn(int i)
        {
            var totals = model.Engine.Totals(weekStart.AddDays(i));
            var left = totals.Working + totals.Overtime - AllocatedOn(i);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
        // Whole hundredths of an hour, so the decimal rows still add up down and across.
        long UnallocatedUnits(int i) => (long)Math.Round(UnallocatedOn(i).TotalHours * 100);
        Add(AllocatedLabel, AllocatedOn, i => perAccount.Sum(v => v[i]), "subtotal");
        Add(UnallocatedLabel, UnallocatedOn, UnallocatedUnits, "normal");
        Add(WorkingLabel, i => AllocatedOn(i) + UnallocatedOn(i), i => perAccount.Sum(v => v[i]) + UnallocatedUnits(i), "bold");
        var breakUnits = UnitsFor(BreakAccount.Name);
        var breakAccount = new TimerAccount(BreakAccount.Name, "", null);
        Add(BreaksLabel, i => model.OnDay(breakAccount, weekStart.AddDays(i)), i => breakUnits[i], "faint");

        foreach (var account in accounts.Where(a => a.Budget is not null))
        {
            // The budget period is the one that holds this week (the present one when it is the current week), and is named so it cannot be mistaken for another.
            var reference = model.BudgetReference(weekStart);
            var used = model.BudgetUsedAt(account, reference); var target = account.Budget!.Value;
            var name = model.BudgetPeriodNameAt(account, reference);
            table.Budgets.Add(new(account.Display, model.FormatShort(target), model.FormatShort(used),
                model.FormatShort(used >= target ? TimeSpan.Zero : target - used), used.TotalSeconds / target.TotalSeconds,
                char.ToUpper(name[0]) + name[1..]));
        }
        return table;
    }

    /// <summary>Tab separated, so it pastes into a spreadsheet or timesheet as separate cells.</summary>
    public string ToTsv() => string.Join("\r\n", new[] { string.Join("\t", Headers) }
        .Concat(Rows.Select(r => string.Join("\t", new[] { r.Label }.Concat(r.CopyCells).Append(r.CopyTotal)))));
}
