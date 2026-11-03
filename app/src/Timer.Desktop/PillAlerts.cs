using System.Globalization;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>What an alert on the pill is about.</summary>
public enum AlertKind { Reminder, Budget, BudgetSpent, Break }

/// <summary>
/// One thing waiting for the user, as the pill's card shows it. <see cref="Key"/> is the same from one tick to the next while the thing is the same, so the
/// pill can tell what it has already shown. <see cref="Token"/> is the theme colour of its heading and <see cref="LightToken"/> the colour it gives the pill's second light.
/// </summary>
public sealed record AlertItem(string Key, AlertKind Kind, int Rank, DateTime Since, string Headline, string Detail, string Words, string? Account, string? When,
    long? NoteId, string Caption, double Ratio)
{
    public string Token => Kind switch { AlertKind.Reminder => "Overtime", AlertKind.BudgetSpent => "Overtime", _ => "BudgetWarning" };
    public string LightToken => Kind switch { AlertKind.Reminder => "Accent", AlertKind.BudgetSpent => "Overtime", AlertKind.Break => "Break", _ => "BudgetWarning" };
}

/// <summary>What the user has put away for now: budgets and breaks they pressed OK or Stay on break on. Kept for the session only, since a budget alert is about this week's use.</summary>
public sealed class AlertState
{
    private readonly HashSet<string> dismissed = new();
    private readonly Dictionary<string, DateTime> snoozed = new();
    public void Dismiss(string key) => dismissed.Add(key);
    public void Snooze(string key, DateTime until) => snoozed[key] = until;
    public bool IsHidden(string key, DateTime now) => dismissed.Contains(key) || snoozed.TryGetValue(key, out var until) && now < until;
}

/// <summary>
/// Works out what is waiting for the pill, most urgent first: a reminder a day or more late, a spent budget, a reminder passed today, a budget at 80 per cent,
/// a break running past the day's allowance; the oldest first within each.
/// </summary>
public static class PillAlertItems
{
    public static IReadOnlyList<AlertItem> Build(TimerModel model)
    {
        var now = model.Engine.Now; var items = new List<AlertItem>();
        foreach (var note in model.Notes.Overdue)
        {
            if (NotesModel.ReminderOf(note) is not { } due) continue;
            var late = LateBy(now - due); var days = due.Date < now.Date;
            items.Add(new AlertItem("note:" + note.Id, AlertKind.Reminder, days ? 0 : 2, due, "Reminder", late, model.Notes.Text(note), model.Notes.AccountOf(note),
                NoteDates.When(due, now), note.Id, "", 0));
        }
        foreach (var account in model.Accounts)
        {
            if (account.Budget is not { } target || target <= TimeSpan.Zero || BreakAccount.Is(account.Name) || model.BudgetRatio(account) is not { } ratio || ratio < .8) continue;
            var spent = ratio >= 1; var window = model.BudgetWindowAt(account, now).Start;
            var used = model.BudgetUsed(account); var left = target - used;
            var key = $"budget:{account.Name}:{(spent ? "spent" : "80")}:{window:yyyyMMdd}";
            var period = account.Period == "week" ? "" : " this " + account.Period;
            items.Add(new AlertItem(key, spent ? AlertKind.BudgetSpent : AlertKind.Budget, spent ? 1 : 3, window, "Budget", spent ? "Spent" : "80 per cent used", account.Name, account.Name, null, null,
                spent ? $"{model.FormatCompact(used)} of {model.FormatCompact(target)}{period}" : $"{model.FormatCompact(used)} of {model.FormatCompact(target)}{period} · {model.FormatCompact(left)} left", ratio));
        }
        var allowance = model.BreakAllowance;
        if (model.IsOnBreak && allowance > TimeSpan.Zero && model.Engine.Active is { } active && model.BreakTimeOn(now.Date) > allowance)
        {
            var back = model.ResumeAccount;
            items.Add(new AlertItem("break:" + active.Start.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), AlertKind.Break, 4, active.Start, "Break",
                $"{Math.Max(1, (int)(now - active.Start).TotalMinutes)} min so far", back is null ? "Break is running long" : $"Back to {back}?", back, null, null,
                $"Break started {active.Start:HH:mm} · {model.FormatCompact(allowance)} allowed", 0));
        }
        return items.Where(i => !model.AlertState.IsHidden(i.Key, now)).OrderBy(i => i.Rank).ThenBy(i => i.Since).ThenBy(i => i.Key, StringComparer.Ordinal).ToList();
    }
    /// <summary>"3 min late", "2 h late", "2 days late".</summary>
    public static string LateBy(TimeSpan late)
    {
        if (late < TimeSpan.Zero) late = TimeSpan.Zero;
        if (late.TotalHours < 1) return $"{Math.Max(1, (int)late.TotalMinutes)} min late";
        if (late.TotalDays < 1) return $"{(int)late.TotalHours} h late";
        var days = (int)late.TotalDays; return days == 1 ? "1 day late" : $"{days} days late";
    }
}
