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
    public double TimelinePixelsPerHour { get; private set; } = 160;
    public double? TimelineOffset { get; set; }
    public void ZoomTimeline(double wheel, double pointerY, double offset)
    {
        if (wheel == 0) return;
        var previous = TimelinePixelsPerHour;
        TimelinePixelsPerHour = Math.Clamp(previous * Math.Pow(1.2, wheel), 60, 640);
        TimelineOffset = Math.Max(0, (offset + pointerY) / previous * TimelinePixelsPerHour - pointerY);
        Notify();
    }
    public TrackingEngine Engine { get; }
    public LedgerEditor Editor { get; }
    public string? EditError { get; private set; }
    public event Action<SessionRow?, DateTime, DateTime>? EntryRequested;
    public event Action<CreditPosition?>? PositionRequested;
    public event Action<string>? GroupRequested;
    public string? ExportPath { get; private set; }
    private readonly HashSet<SessionRow> selectedRows = new(ReferenceEqualityComparer.Instance);
    public IReadOnlyCollection<SessionRow> SelectedRows => selectedRows;
    public bool IsSelected(IReadOnlyList<SessionRow> rows) => rows.Count > 0 && rows.All(selectedRows.Contains);
    public int SelectedBlockCount => selectedRows.Count == 0 ? 0 : Editor.Entries(WeekStart(SelectedDate), WeekStart(SelectedDate).AddDays(7))
        .Count(e => e.Sources.Count > 0 && e.Sources.All(selectedRows.Contains));
    public bool CanMergeSelection => SelectedBlockCount >= 2 && selectedRows.Select(s => s.Day).Distinct().Count() == 1
        && selectedRows.Select(s => s.Account).Distinct(StringComparer.Ordinal).Count() == 1 && Editor.CanCombine(selectedRows.ToArray());
    public string MergeHint => SelectedBlockCount < 2 ? "Click at least two blocks to merge." : CanMergeSelection
        ? $"Merge {SelectedBlockCount} blocks · adds {Format(TimeSpan.FromMinutes((double)Editor.MergeGapMinutes(selectedRows.ToArray())))} of gap time." : "Select blocks for the same account/day, with only blank gaps between them.";
    public void ToggleSelection(IReadOnlyList<SessionRow> rows)
    {
        if (rows.Count == 0 || rows.Any(s => s.IsMerged || !Engine.Sessions.Any(r => ReferenceEquals(r, s)))) return;
        var remove = IsSelected(rows);
        foreach (var row in rows) { if (remove) selectedRows.Remove(row); else selectedRows.Add(row); }
        lastToggled = rows;
        EditError = null; Notify();
    }
    // The first click of a double-click has already toggled the block, and the timeline is
    // rebuilt after it, so the model remembers that toggle and undoes it before opening the editor.
    private IReadOnlyList<SessionRow>? lastToggled;
    public void OpenEntryFromDoubleClick(SessionRow row)
    {
        if (lastToggled is { Count: 1 } last && ReferenceEquals(last[0], row)) { lastToggled = null; ToggleSelection(last); }
        lastToggled = null;
        OpenEntry(row);
    }
    public void ClearSelection() { if (selectedRows.Count == 0) return; selectedRows.Clear(); Notify(); }
    public bool MergeSelection()
    {
        if (!CanMergeSelection) { EditError = MergeHint; Notify(); return false; }
        var rows = Engine.Sessions.Where(selectedRows.Contains).ToArray();
        return ChangeLog(() => { Editor.Group(rows); selectedRows.Clear(); });
    }
    private StoreSettings preferences;
    private readonly TimerStore? store;
    private int ticks;
    public TimerModel(TrackingEngine? engine = null, StoreSettings? preferences = null, TimerStore? store = null)
    {
        Engine = engine ?? new();
        this.preferences = preferences ?? new(new() { new("Account one"), new("Account two"), new("Internal") }, false, "System", DayOfWeek.Monday);
        this.store = store;
        Editor = store?.Editor ?? new LedgerEditor(Engine);
        SelectedAccount = Engine.Active?.Account ?? this.preferences.Accounts.FirstOrDefault(a => !a.Hidden)?.Name;
        SelectedDate = Engine.Now.Date;
        ApplyTheme();
    }
    public IReadOnlyList<AccountSettings> AccountSettings => preferences.Accounts.AsReadOnly();
    private TimerAccount[] Catalogue()
    {
        var colours = new[] { "#91B7D5", "#94BF9B", "#D9A18A", "#BAA4CE", "#D6C48C" };
        return preferences.Accounts.Select((a, i) => new TimerAccount(a.Name, colours[i % colours.Length],
            a.BudgetHours is { } hours ? TimeSpan.FromHours(hours) : null, a.BudgetPeriod)).Append(new("Lunch", "#D6C48C", null)).ToArray();
    }
    public IReadOnlyList<TimerAccount> ReviewAccounts => Catalogue().Concat(Engine.Sessions.Select(s => s.Account).Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(name => !Catalogue().Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).Select(name => new TimerAccount(name, "#91B7D5", null))).ToArray();
    public IReadOnlyList<TimerAccount> Accounts => Catalogue().Where(a => !preferences.Accounts.Any(p => p.Name == a.Name && p.Hidden)).ToArray();
    public string? SelectedAccount { get; private set; }
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
    public bool IsDemo { get; private set; }
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
        SelectedAccount = name; Engine.ToggleAccount(name); Save(); Notify();
    }
    // The block being recorded has no ledger row yet, so it cannot be dragged or edited until tracking stops.
    public void ExplainRunning() { EditError = "This block is still recording. Stop tracking to move or edit it."; Notify(); }
    public void TogglePause()
    {
        if (Engine.Active is { } active) { SelectedAccount = active.Account; Engine.StopAccount(); }
        else if (SelectedAccount is { } name && Accounts.Any(a => a.Name == name)) Engine.ToggleAccount(name);
        Save(); Notify();
    }
    /// <summary>Applies the account list. <paramref name="renames"/> maps an account's old name to its new one (the list holds the new
    /// names). With <paramref name="renameHistory"/> the past entries move to the new name too, after a backup copy is saved; without
    /// it the old name stays in the history as a separate historical account and the new name starts empty.</summary>
    public void SetAccounts(IEnumerable<AccountSettings> accounts, IReadOnlyDictionary<string, string>? renames = null, bool renameHistory = false)
    {
        renames ??= new Dictionary<string, string>();
        var values = accounts.ToList();
        if (values.Count == 0 || values.Any(a => string.IsNullOrWhiteSpace(a.Name) || a.Name != a.Name.Trim()
            || a.Name.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0 || a.Name.Equals("Lunch", StringComparison.OrdinalIgnoreCase)
            || a.BudgetPeriod is not ("week" or "month" or "quarter")
            || (a.BudgetHours is { } h && (!double.IsFinite(h) || h <= 0)))
            || values.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            throw new ArgumentException("Use unique account names and positive budgets; Lunch is reserved.");
        if (preferences.Accounts.Any(a => !values.Any(v => v.Name == (renames.TryGetValue(a.Name, out var to) ? to : a.Name))))
            throw new ArgumentException("Hide existing accounts instead of removing them.");
        var renamed = renames.Where(r => !r.Value.Equals(r.Key, StringComparison.Ordinal)).ToList();
        foreach (var (from, to) in renamed)
        {
            if (!preferences.Accounts.Any(a => a.Name == from)) throw new ArgumentException("Unknown account.");
            // A name that already appears anywhere would silently join two histories, so it is refused.
            // Changing only the capitals of the same account is fine.
            if (!to.Equals(from, StringComparison.OrdinalIgnoreCase) && (ReviewAccounts.Any(a => a.Name.Equals(to, StringComparison.OrdinalIgnoreCase))
                || renamed.Any(r => r.Key != from && r.Value.Equals(to, StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException($"\"{to}\" is already used by another account or in the history.");
        }
        if (ActiveAccount is { } active && (values.Any(a => a.Name == active && a.Hidden) || renamed.Any(r => r.Key == active))) Engine.StopAccount();
        if (renamed.Count > 0 && renameHistory)
        {
            // Save first so the backup holds the latest entries, then rewrite everything in one go.
            if (store is not null) { store.Save(Engine, preferences); store.Backup("before-rename"); }
            foreach (var (from, to) in renamed) Editor.RenameAccount(from, to);
        }
        preferences = preferences with { Accounts = values };
        if (renamed.Count > 0 && SelectedAccount is { } picked && renames.TryGetValue(picked, out var now)) SelectedAccount = now;
        if (!Accounts.Any(a => a.Name == SelectedAccount)) SelectedAccount = values.FirstOrDefault(a => !a.Hidden)?.Name;
        Save(); Notify();
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
    public void ChangeDate(int days) { selectedRows.Clear(); TimelineOffset = null; SelectedDate = SelectedDate.AddDays(days); Notify(); }
    public void SetTheme(string theme)
    {
        if (theme is not ("Light" or "Dark" or "System")) throw new ArgumentException("Unknown theme", nameof(theme));
        preferences = preferences with { Theme = theme }; ApplyTheme(); Save(); Notify();
    }
    public static TimerModel CreateDemo(Func<DateTime>? clock = null)
    {
        var realClock = clock ?? (() => DateTime.Now);
        var wallStart = realClock();
        var anchor = wallStart.Date.AddHours(14).AddMinutes(15);
        var engine = new TrackingEngine(clock: () => anchor + (realClock() - wallStart));
        engine.Schedule.Days.Add(anchor.DayOfWeek);
        var prefs = new StoreSettings(new() { new("Account one", 15), new("Account two", 10), new("Account three", 5), new("Internal") }, false, "System", DayOfWeek.Monday);
        var start = anchor.Date.AddHours(9);
        var names = new[] { "Account one", "Account two", "Lunch", "Account three", "Internal" };
        var minutes = new[] { 105, 90, 30, 45, 30 };
        for (var i = 0; i < names.Length; i++)
        {
            if (i == 0)
            {
                engine.AddSession(names[i], start, start.AddMinutes(50)); start = start.AddMinutes(53);
                engine.AddSession(names[i], start, start.AddMinutes(55)); start = start.AddMinutes(55);
            }
            else { engine.AddSession(names[i], start, start.AddMinutes(minutes[i])); start = start.AddMinutes(minutes[i]); }
        }
        engine.Sessions.Add(new(anchor.ToString("yyyy-MM-dd"), "Account one", "00:00:00", "merged", "10.00"));
        var monday = anchor.Date.AddDays(-((7 + (int)anchor.DayOfWeek - 1) % 7));
        if (monday < anchor.Date) engine.AddSession("Account one", monday.AddHours(9), monday.AddHours(16));
        return new TimerModel(engine, prefs) { IsDemo = true };
    }
    public void ForceOvertime()
    {
        if (!IsDemo) throw new InvalidOperationException("Overtime simulation is available only in the demo.");
        Engine.Schedule.Overrides[Engine.Now.Date] = new(false, new(TimeSpan.FromHours(9), TimeSpan.FromHours(13)));
        Notify();
    }
    public void SetShifts(Dictionary<DayOfWeek, Shift> shifts, bool resetToday = false)
    {
        if (shifts.Count == 0 || shifts.Values.Any(s => s.Start < TimeSpan.Zero || s.End >= TimeSpan.FromDays(1) || s.End <= s.Start))
            throw new ArgumentException("Choose at least one working day, with an end after its start.");
        Engine.Schedule.Days = shifts.Keys.ToHashSet();
        Engine.Schedule.Shifts = new(shifts);
        if (resetToday) { Engine.Schedule.Overrides.Remove(Engine.Now.Date); Engine.DayStarts.Remove(Engine.Now.Date); }
        Save(); Notify();
    }
    public void OpenEntry(SessionRow row) => EntryRequested?.Invoke(row, row.From, row.To);
    public void OpenGap(DateTime? at = null)
    {
        var day = SelectedDate;
        var limit = day.AddDays(1);
        var start = at ?? (Engine.Plan(day).Working ? Engine.Plan(day).Start : day.AddHours(9));
        var occupied = Engine.Sessions.Where(s => s.IsPositioned && s.Duration > TimeSpan.Zero && s.From < day.AddDays(1) && s.To > day)
            .Select(s => (Start: s.From, End: s.To)).ToList();
        occupied.AddRange(Editor.Positions.Select(p => (p.Start, p.End)));
        if (Engine.Active is { } active) occupied.Add((active.Start, Engine.Now));
        foreach (var span in occupied.OrderBy(s => s.Start))
        {
            if (span.End <= start) continue;
            if (span.Start > start) break;
            start = span.End;
        }
        var next = occupied.Where(s => s.Start > start).Select(s => s.Start).DefaultIfEmpty(limit).Min();
        var end = new[] { start.AddMinutes(30), next, limit, day.AddDays(1) }.Min();
        if (end <= start) { EditError = "There is no free recorded-time gap here."; Notify(); return; }
        EditError = null; EntryRequested?.Invoke(null, start, end);
    }
    public bool EditEntry(SessionRow? row, string account, DateTime start, DateTime end, string note)
        => ChangeLog(() => { ValidateAccount(account); if (row is null) Editor.Add(account, start, end, note); else Editor.Edit(row, account, start, end, note); });
    public bool DragEntry(SessionRow row, DateTime start, DateTime end, bool insert)
        => ChangeLog(() => Editor.EditTimeline(row, start, end, insert));
    /// <summary>Moves an account to a new place among the accounts (hidden ones keep their own places).</summary>
    public void MoveAccount(string name, int visibleIndex)
    {
        var list = preferences.Accounts.ToList();
        var visible = list.Where(a => !a.Hidden).ToList();
        var item = visible.FirstOrDefault(a => a.Name == name);
        if (item is null) return;
        visibleIndex = Math.Clamp(visibleIndex, 0, visible.Count - 1);
        var current = visible.IndexOf(item);
        if (current == visibleIndex) return;
        visible.RemoveAt(current); visible.Insert(visibleIndex, item);
        // Put the visible accounts back into the slots the visible accounts held, so hidden ones stay where they were.
        var next = 0;
        for (var i = 0; i < list.Count; i++) if (!list[i].Hidden) list[i] = visible[next++];
        preferences = preferences with { Accounts = list };
        Save(); Notify();
    }
    public bool DeleteEntry(SessionRow row) => ChangeLog(() => Editor.Delete(row));
    /// <summary>Deletes the selected blocks as one undoable edit. Selection that cannot be deleted is left alone with an explanation.</summary>
    public bool DeleteSelection()
    {
        var rows = Engine.Sessions.Where(selectedRows.Contains).ToArray();
        if (rows.Length == 0) return false;
        return ChangeLog(() => { Editor.Delete(rows); selectedRows.Clear(); });
    }
    public bool UndoEdit() => ChangeLog(Editor.Undo);
    public void OpenPosition(CreditPosition? position = null) => PositionRequested?.Invoke(position);
    public void OpenGroup(string id) => GroupRequested?.Invoke(id);
    public bool PlaceCredit(string account, DateTime start, decimal minutes, string note, CreditPosition? old = null)
        => ChangeLog(() => Editor.Place(account, start, minutes, note, old));
    public bool UnplaceCredit(CreditPosition position) => ChangeLog(() => Editor.Unplace(position));
    public bool GroupEntries(IReadOnlyList<SessionRow> rows) => ChangeLog(() => Editor.Group(rows));
    public bool UngroupEntries(string id) => ChangeLog(() => Editor.Ungroup(id));
    public bool TidyDay(IReadOnlyList<GapChoice> choices, int limit) => ChangeLog(() => Editor.Tidy(choices, limit));
    public bool ExportWeek()
    {
        try
        {
            if (!Save()) return false;
            var export = new WeekExport(Editor, Engine, WeekStart(SelectedDate));
            ExportPath = export.Write(store?.ExportDirectory ?? Path.Combine(Path.GetTempPath(), "BasicsAdvancedTimer-demo-exports"), ReviewAccounts.Select(a => a.Name));
            EditError = null; Notify(); return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { EditError = error.Message; Notify(); return false; }
    }
    private void ValidateAccount(string account)
    {
        if (!ReviewAccounts.Any(a => a.Name.Equals(account, StringComparison.OrdinalIgnoreCase))
            && !Engine.Sessions.Any(s => s.Account.Equals(account, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose an existing account.");
    }
    private bool ChangeLog(Action change)
    {
        try
        {
            // Save any pending recording/edit before replacing the undo snapshot.
            if (!Save()) { EditError = LastError; Notify(); return false; }
            change();
            selectedRows.RemoveWhere(s => !Engine.Sessions.Any(r => ReferenceEquals(r, s)));
            EditError = null;
            var saved = Save(); Notify(); return saved;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
        { EditError = error.Message; Notify(); return false; }
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
