using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

public sealed record TimerAccount(string Name, string Colour, TimeSpan? Budget, string Period = "week");

public sealed class TimerModel : INotifyPropertyChanged
{
    public TrackingEngine Engine { get; }
    private StoreSettings preferences;
    private readonly TimerStore? store;
    private int ticks;
    public TimerModel(TrackingEngine? engine = null, StoreSettings? preferences = null, TimerStore? store = null)
    {
        Engine = engine ?? new();
        this.preferences = preferences ?? new(new() { new("Account one"), new("Account two"), new("Internal") }, false, "System", DayOfWeek.Monday);
        this.store = store;
        var colours = new[] { "#91B7D5", "#94BF9B", "#D9A18A", "#BAA4CE", "#D6C48C" };
        Accounts = this.preferences.Accounts.Select((a, i) => new TimerAccount(a.Name, colours[i % colours.Length],
            a.BudgetHours is { } hours ? TimeSpan.FromHours(hours) : null, a.BudgetPeriod)).Append(new("Lunch", "#D6C48C", null)).ToArray();
        SelectedDate = Engine.Now.Date;
        ApplyTheme();
    }
    public IReadOnlyList<TimerAccount> Accounts { get; }
    public string? ActiveAccount => Engine.Active?.Account;
    public string AccountLabel => ActiveAccount ?? (Engine.Finished ? "Day finished" : "Ready when you are");
    public string Status => ActiveAccount == "Lunch" ? "On lunch" : IsTracking ? "Tracking" : "Not tracking";
    public bool IsTracking => Engine.Active is not null;
    public bool IsOvertime => Engine.Totals(Engine.Now).Overtime > TimeSpan.Zero;
    public bool DayStarted => Engine.DayRunning;
    public bool DecimalHours => preferences.DecimalHours;
    public DateTime SelectedDate { get; private set; }
    public string Theme => preferences.Theme;
    public string? LastError { get; private set; }
    public bool Persistent => store is not null;
    public TimeSpan Elapsed => Engine.Totals(Engine.Now).Elapsed;
    public TimeSpan AccountElapsed => Engine.ActiveElapsed;
    public string ClockText => $"{(int)Elapsed.TotalHours:00}:{Elapsed.Minutes:00}:{Elapsed.Seconds:00}";
    public TimeSpan Allocated => Engine.Totals(Engine.Now).Allocated;
    public TimeSpan Unallocated => Engine.Totals(Engine.Now).Unallocated;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void ToggleAccount(string name)
    {
        if (!Accounts.Any(a => a.Name == name)) throw new ArgumentException("Unknown account", nameof(name));
        Engine.ToggleAccount(name); Save(); Notify();
    }
    public void ToggleDay()
    {
        if (Engine.DayRunning) Engine.FinishDay(); else Engine.StartDay();
        Save(); Notify();
    }
    public DateTime WeekStart(DateTime date) => date.Date.AddDays(-((7 + (int)date.DayOfWeek - (int)preferences.WeekStart) % 7));
    public TimeSpan Today(TimerAccount a) => Engine.Credited(a.Name, Engine.Now.Date, Engine.Now.Date.AddDays(1));
    public TimeSpan OnDay(TimerAccount a, DateTime day) => Engine.Credited(a.Name, day, day.AddDays(1));
    public TimeSpan Week(TimerAccount a) { var start = WeekStart(SelectedDate); return Engine.Credited(a.Name, start, start.AddDays(7)); }
    public TimeSpan BudgetUsed(TimerAccount a)
    {
        var now = Engine.Now;
        var start = a.Period switch
        {
            "month" => new DateTime(now.Year, now.Month, 1),
            "quarter" => new DateTime(now.Year, ((now.Month - 1) / 3) * 3 + 1, 1), _ => WeekStart(now)
        };
        var end = a.Period switch { "month" => start.AddMonths(1), "quarter" => start.AddMonths(3), _ => start.AddDays(7) };
        return Engine.Credited(a.Name, start, end);
    }
    public string Format(TimeSpan value)
    {
        if (DecimalHours) return value.TotalHours.ToString("0.00", CultureInfo.GetCultureInfo("en-GB")) + "h";
        var sign = value < TimeSpan.Zero ? "-" : ""; value = value.Duration();
        return $"{sign}{(int)value.TotalHours}h {value.Minutes:00}m";
    }
    public string BudgetText(TimerAccount a) => a.Budget is { } target ? $"{Format(BudgetUsed(a))} of {Format(target)} this {a.Period}" : "No budget";
    public string BudgetSummary(TimerAccount a) => a.Budget is { } target ? $"{Format(BudgetUsed(a))} / {Format(target)}" : "No budget";
    public void ToggleFormat() { preferences = preferences with { DecimalHours = !DecimalHours }; Save(); Notify(); }
    public void ChangeDate(int days) { SelectedDate = SelectedDate.AddDays(days); Notify(); }
    public void SetTheme(string theme)
    {
        if (theme is not ("Light" or "Dark" or "System")) throw new ArgumentException("Unknown theme", nameof(theme));
        preferences = preferences with { Theme = theme }; ApplyTheme(); Save(); Notify();
    }
    private void ApplyTheme()
    {
        if (Application.Current is { } app) app.RequestedThemeVariant = Theme switch
        { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
    }
    public void Tick()
    {
        ticks++;
        if (ticks % 15 == 0) { Save(); Notify(); }
        else PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Elapsed)));
    }
    public bool Save()
    {
        try { store?.Save(Engine, preferences); LastError = null; return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { LastError = "Could not save. Time remains in memory; retrying. " + error.Message; return false; }
    }
    public bool StopForClose()
    { Engine.StopAccount(); var saved = Save(); if (!saved) Notify(); return saved; }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
