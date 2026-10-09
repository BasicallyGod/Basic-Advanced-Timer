using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Styling;

namespace Timer.Desktop;

public sealed record PreviewAccount(string Name, string Colour, TimeSpan Today, TimeSpan Week, TimeSpan? Budget);

// A UI demonstrator, deliberately separate from the future authoritative tracking core.
// No file APIs, no import of the user's settings and no session persistence.
public sealed class PreviewModel : INotifyPropertyChanged
{
    private readonly Func<DateTimeOffset> clock;
    private DateTimeOffset? started;
    private TimeSpan currentElapsed;
    private readonly Dictionary<string, TimeSpan> additional = new();
    public PreviewModel(Func<DateTimeOffset>? clock = null)
    {
        this.clock = clock ?? (() => DateTimeOffset.Now);
        SelectedDate = this.clock().Date;
    }

    public IReadOnlyList<PreviewAccount> Accounts { get; } = new[]
    {
        new PreviewAccount("Account one", "#91B7D5", TimeSpan.FromMinutes(105), TimeSpan.FromHours(9), TimeSpan.FromHours(15)),
        new PreviewAccount("Account two", "#94BF9B", TimeSpan.FromMinutes(90), TimeSpan.FromHours(7), TimeSpan.FromHours(10)),
        new PreviewAccount("Account three", "#D9A18A", TimeSpan.FromMinutes(45), TimeSpan.FromHours(4.5), TimeSpan.FromHours(5)),
        new PreviewAccount("Internal", "#BAA4CE", TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), null),
        new PreviewAccount("Lunch", "#D6C48C", TimeSpan.FromMinutes(30), TimeSpan.FromHours(2.5), null)
    };
    public string? ActiveAccount { get; private set; }
    public string AccountLabel => ActiveAccount ?? "Ready when you are";
    public string Status => ActiveAccount is null ? "Not tracking" : "Tracking";
    public bool IsTracking => ActiveAccount is not null;
    // Overtime is independent of recording; the shell exposes it for visual verification.
    public bool IsOvertime { get; private set; }
    public bool DayStarted { get; private set; }
    public bool DecimalHours { get; private set; }
    public DateTime SelectedDate { get; private set; }
    public string Theme { get; private set; } = "System";
    public TimeSpan Elapsed => currentElapsed + (started is { } at ? clock() - at : TimeSpan.Zero);
    public string ClockText => $"{(int)Elapsed.TotalHours:00}:{Elapsed.Minutes:00}:{Elapsed.Seconds:00}";
    public event PropertyChangedEventHandler? PropertyChanged;

    public void ToggleAccount(string name)
    {
        if (!Accounts.Any(a => a.Name == name)) throw new ArgumentException("Unknown preview account", nameof(name));
        var stop = ActiveAccount == name;
        CommitElapsed();
        ActiveAccount = stop ? null : name;
        currentElapsed = TimeSpan.Zero;
        started = stop ? null : clock();
        if (!stop) DayStarted = true;
        Notify();
    }
    private void CommitElapsed()
    {
        if (ActiveAccount is { } name) additional[name] = additional.GetValueOrDefault(name) + Elapsed;
        started = null;
        currentElapsed = TimeSpan.Zero;
    }
    public void ToggleDay()
    {
        DayStarted = !DayStarted;
        if (!DayStarted) { CommitElapsed(); ActiveAccount = null; }
        Notify();
    }
    public TimeSpan Today(PreviewAccount a) => a.Today + additional.GetValueOrDefault(a.Name) + (ActiveAccount == a.Name ? Elapsed : TimeSpan.Zero);
    public TimeSpan Week(PreviewAccount a) => a.Week + additional.GetValueOrDefault(a.Name) + (ActiveAccount == a.Name ? Elapsed : TimeSpan.Zero);
    public TimeSpan Allocated => Accounts.Where(a => a.Name != "Lunch").Aggregate(TimeSpan.Zero, (sum, a) => sum + Today(a));
    public string Format(TimeSpan value) => DecimalHours
        ? value.TotalHours.ToString("0.00", CultureInfo.GetCultureInfo("en-GB")) + "h"
        : $"{(int)value.TotalHours}h {Math.Abs(value.Minutes):00}m";
    public string BudgetText(PreviewAccount a) => a.Budget is { } target
        ? $"{Format(Week(a))} of {Format(target)} this week"
        : "No budget";
    public void ToggleFormat() { DecimalHours = !DecimalHours; Notify(); }
    public void ToggleOvertime() { IsOvertime = !IsOvertime; Notify(); }
    public void ChangeDate(int days) { SelectedDate = SelectedDate.AddDays(days); Notify(); }
    public void SetTheme(string theme)
    {
        if (theme is not ("Light" or "Dark" or "System")) throw new ArgumentException("Unknown theme", nameof(theme));
        Theme = theme;
        if (Application.Current is { } app) app.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default
        };
        Notify();
    }
    public void Tick() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Elapsed)));
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
