using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

public sealed record TimerAccount(string Name, string Colour, TimeSpan? Budget, string Period = "week")
{
    public string Display => BreakAccount.Display(Name);
}

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
    /// <summary>Zooms and scrolls so the day's recorded time, with a little room either side, fills the height given.</summary>
    public void FitTimeline(double viewportHeight)
    {
        var day = SelectedDate.Date;
        var spans = Engine.Sessions.Where(s => s.Day == day && s.IsPositioned && s.Duration > TimeSpan.Zero).Select(s => (s.From, s.To)).ToList();
        if (Engine.Active is { } active && active.Start < day.AddDays(1) && Engine.Now > day) spans.Add((active.Start < day ? day : active.Start, Engine.Now < day.AddDays(1) ? Engine.Now : day.AddDays(1)));
        double start, end;
        if (spans.Count > 0) { start = (spans.Min(s => s.From) - day).TotalHours; end = (spans.Max(s => s.To) - day).TotalHours; }
        else { var plan = Engine.Plan(day); start = plan.Working ? (plan.Start - day).TotalHours : 9; end = plan.Working ? (plan.End - day).TotalHours : 17; }
        var padding = 0.25; start = Math.Max(0, start - padding); end = Math.Min(24, end + padding);
        var hours = Math.Max(end - start, 1.0);
        TimelinePixelsPerHour = Math.Clamp((viewportHeight > 0 ? viewportHeight : 400) / hours, 60, 640);
        TimelineOffset = start * TimelinePixelsPerHour;
        Notify();
    }
    public TrackingEngine Engine { get; }
    public LedgerEditor Editor { get; }
    public string? EditError { get; private set; }
    public event Action<SessionRow?, DateTime, DateTime>? EntryRequested;
    public event Action<string>? GroupRequested;
    public event Action? TidyRequested;
    public void OpenTidy() => TidyRequested?.Invoke();
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
        if (rows.Count == 0 || rows.Any(s => !Engine.Sessions.Any(r => ReferenceEquals(r, s)))) return;
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
        this.preferences = this.preferences with { Accounts = WithColours(this.preferences.Accounts) };
        SelectedAccount = Engine.Active?.Account ?? this.preferences.Accounts.FirstOrDefault(a => !a.Hidden)?.Name;
        SelectedDate = Engine.Now.Date;
        // The user's own themes need their colours registered too. The built-in ones were registered when the app started.
        if (Application.Current is { } current && Avalonia.Threading.Dispatcher.UIThread.CheckAccess() && this.preferences.Themes is { Count: > 0 } own) ThemeEngine.Register(current, own);
        ApplyTheme();
    }
    // Sixteen soft colours, so up to sixteen accounts each have their own before any repeat, none matching the break. The first eight are
    // the original palette and keep their order, so existing accounts are unchanged. They all take dark text.
    public static readonly string[] Palette =
    {
        "#91B7D5", "#94BF9B", "#D9A18A", "#BAA4CE", "#C9D17F", "#86C5C0", "#D79BB5", "#C4A38F",
        "#E58F8F", "#E6A86B", "#F0D060", "#8F94E6", "#C08FE0", "#7FD18F", "#B8BDB0", "#E87FA0"
    };
    // An account keeps its colour for good: it is given the first colour no other account is using, once, and saved with it.
    // Reordering, hiding or renaming an account therefore never changes how its blocks look on the timeline.
    private static List<AccountSettings> WithColours(IEnumerable<AccountSettings> accounts)
    {
        var list = accounts.ToList();
        foreach (var index in Enumerable.Range(0, list.Count).Where(i => list[i].Colour is null).ToList())
        {
            var used = list.Where(a => a.Colour is not null).Select(a => a.Colour!).ToList();
            var colour = Palette.FirstOrDefault(c => !used.Contains(c, StringComparer.OrdinalIgnoreCase))
                ?? Palette.OrderBy(c => used.Count(u => u.Equals(c, StringComparison.OrdinalIgnoreCase))).First();
            list[index] = list[index] with { Colour = colour };
        }
        return list;
    }
    public IReadOnlyList<AccountSettings> AccountSettings => preferences.Accounts.AsReadOnly();
    private TimerAccount[] Catalogue()
    {
        return preferences.Accounts.Select(a => new TimerAccount(a.Name, a.Colour ?? Palette[0],
            a.BudgetHours is { } hours ? TimeSpan.FromHours(hours) : null, a.BudgetPeriod)).Append(new(BreakAccount.Name, "#D6C48C", null)).ToArray();
    }
    public IReadOnlyList<TimerAccount> ReviewAccounts => Catalogue().Concat(Engine.Sessions.Select(s => s.Account).Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(name => !Catalogue().Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).Select(name => new TimerAccount(name, "#91B7D5", null))).ToArray();
    public IReadOnlyList<TimerAccount> Accounts => Catalogue().Where(a => !preferences.Accounts.Any(p => p.Name == a.Name && p.Hidden)).ToArray();
    public string? SelectedAccount { get; private set; }
    public string? ActiveAccount => Engine.Active?.Account;
    public string AccountLabel => IsOnBreak ? "On break" : ActiveAccount ?? (Engine.Finished ? "Day finished" : "Ready when you are");
    public string Label(string account) => BreakAccount.Display(account);
    public string Status => IsOnBreak ? "On break" : IsTracking ? "Tracking" : "Not tracking";
    public bool IsTracking => Engine.Active is not null;
    public bool IsOvertime => Engine.Totals(Engine.Now).Overtime > TimeSpan.Zero;
    public bool DayStarted => Engine.DayRunning;
    public bool DecimalHours => preferences.DecimalHours;
    public DateTime SelectedDate { get; private set; }
    public string Theme => preferences.Theme;
    public string? LastError { get; private set; }
    /// <summary>A one-off message for the footer, such as the note that old timesheet files are not used.</summary>
    public string? Notice { get; set; }
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
        if (BreakAccount.Is(name)) { ToggleBreak(); return; } // A break is never the selected account, so it can always hand back.
        SelectedAccount = name; Engine.ToggleAccount(name); Save(); Notify();
    }
    // The block being recorded has no ledger row yet, so it cannot be dragged or edited until tracking stops.
    public void ExplainRunning() { EditError = "This block is still recording. Stop tracking to move or edit it."; Notify(); }
    public void TogglePause()
    {
        // On a break, resume ends the break and goes back to the account that was running before it.
        if (IsOnBreak) { EndBreak(); return; }
        if (Engine.Active is { } active) { SelectedAccount = active.Account; Engine.StopAccount(); }
        else if (SelectedAccount is { } name && Accounts.Any(a => a.Name == name)) Engine.ToggleAccount(name);
        Save(); Notify();
    }
    // The account to go back to after a break: the one that stopped at the moment the break began. It is found from the
    // recorded time, so it still works after the timer has been closed and reopened in the middle of a break.
    public string? ResumeAccount
    {
        get
        {
            if (!IsOnBreak || Engine.Active is not { } active) return null;
            var before = Engine.Sessions.Where(s => s.IsPositioned && !BreakAccount.Is(s.Account) && Math.Abs((s.To - active.Start).TotalSeconds) <= 2)
                .OrderByDescending(s => s.To).FirstOrDefault()?.Account ?? Engine.ResumeAfterBreak;
            return before is { } name && Accounts.Any(a => a.Name == name && !BreakAccount.Is(a.Name)) ? name : null;
        }
    }
    public void ToggleBreak()
    {
        if (IsOnBreak) { EndBreak(); return; }
        Engine.ToggleAccount(BreakAccount.Name);
        Save(); Notify();
    }
    private void EndBreak()
    {
        var back = ResumeAccount;
        if (back is not null) { SelectedAccount = back; Engine.ToggleAccount(back); }
        else Engine.StopAccount();
        Save(); Notify();
    }
    /// <summary>Applies the account list. <paramref name="renames"/> maps an account's old name to its new one (the list holds the new
    /// names). A rename always applies everywhere, so all recorded time, notes, groups and positions move to the new name after a backup
    /// copy is saved. To start afresh, add a new account instead.</summary>
    public void SetAccounts(IEnumerable<AccountSettings> accounts, IReadOnlyDictionary<string, string>? renames = null)
    {
        renames ??= new Dictionary<string, string>();
        var values = accounts.ToList();
        if (values.Count == 0 || values.Any(a => string.IsNullOrWhiteSpace(a.Name) || a.Name != a.Name.Trim()
            || a.Name.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0 || (a.Name.Equals(BreakAccount.Name, StringComparison.OrdinalIgnoreCase) || a.Name.Equals(BreakAccount.Label, StringComparison.OrdinalIgnoreCase))
            || a.BudgetPeriod is not ("week" or "month" or "quarter")
            || (a.BudgetHours is { } h && (!double.IsFinite(h) || h <= 0)))
            || values.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            throw new ArgumentException("Use unique account names and positive budgets; Break is reserved.");
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
        if (renamed.Count > 0)
        {
            // Save first so the backup holds the latest entries, then rewrite everything in one go.
            if (store is not null) { store.Save(Engine, preferences); store.Backup("before-rename"); }
            foreach (var (from, to) in renamed) Editor.RenameAccount(from, to);
        }
        preferences = preferences with { Accounts = WithColours(values) };
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
    /// <summary>The short form for crowded places such as the week table: 7:00, or 7.00 in decimal hours.</summary>
    public string FormatShort(TimeSpan value)
    {
        if (DecimalHours) return value.TotalHours.ToString("0.00", CultureInfo.GetCultureInfo("en-GB"));
        var sign = value < TimeSpan.Zero ? "-" : ""; value = value.Duration();
        return $"{sign}{(int)value.TotalHours}:{value.Minutes:00}";
    }
    public string BudgetText(TimerAccount a) => a.Budget is { } target ? $"{Format(BudgetUsed(a))} of {Format(target)} this {a.Period}" : "No budget";
    public string BudgetSummary(TimerAccount a) => a.Budget is { } target ? $"{Format(BudgetUsed(a))} / {Format(target)}" : "No budget";
    public void ToggleFormat() { preferences = preferences with { DecimalHours = !DecimalHours }; Save(); Notify(); }
    public void ChangeDate(int days) { selectedRows.Clear(); TimelineOffset = null; SelectedDate = SelectedDate.AddDays(days); Notify(); }
    /// <summary>Steps the Week tab by whole weeks. It never goes past the current week, since there is nothing to review there.</summary>
    public void ChangeWeek(int weeks)
    {
        var target = WeekStart(SelectedDate).AddDays(7 * weeks);
        if (target > WeekStart(Engine.Now)) return;
        selectedRows.Clear(); TimelineOffset = null; SelectedDate = target; Notify();
    }
    /// <summary>Which part of the Week tab is showing: overview, hours or entries.</summary>
    public string WeekSection { get; private set; } = "overview";
    public void SetWeekSection(string section)
    {
        if (section is not ("overview" or "hours" or "entries") || section == WeekSection) return;
        WeekSection = section; Notify();
    }
    public bool CanStepForwardAWeek => WeekStart(SelectedDate) < WeekStart(Engine.Now);
    /// <summary>Adds or removes minutes on an account for a day, as one undoable edit.</summary>
    public bool AdjustTime(string account, DateTime day, decimal minutes, string note)
        => ChangeLog(() => { ValidateAccount(account); Editor.Adjust(account, day, minutes, note, Engine.Now); });
    /// <summary>Moves entries to another account as one undoable edit. Every selected block moves when the one clicked is selected.</summary>
    public bool MoveEntries(IReadOnlyList<SessionRow> rows, string account)
        => ChangeLog(() => { ValidateAccount(account); Editor.Reassign(rows, account); });
    /// <summary>Shows a theme as it is being edited, without saving it. <see cref="EndThemePreview"/> puts the saved look back.</summary>
    public void PreviewTheme(ThemeDefinition theme)
    {
        if (Application.Current is not { } app || !Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) return;
        ThemeEngine.Register(app, new[] { theme }); app.RequestedThemeVariant = ThemeEngine.VariantFor(theme); appliedVariant = null;
    }
    public void EndThemePreview()
    {
        if (Application.Current is { } app && Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) ThemeEngine.Register(app, Themes);
        appliedVariant = null; ApplyTheme(); Notify();
    }
    /// <summary>Saves one of the user's own themes. <paramref name="replacing"/> is its earlier name, so a rename replaces it, and the theme stays selected if it was.</summary>
    public void SaveTheme(ThemeDefinition theme, string? replacing = null)
    {
        if (!ThemeStorage.IsUsableName(theme.Name)) throw new ArgumentException("Choose a name of up to 40 characters for the theme. System is reserved.");
        if (ThemeLibrary.BuiltIn.Any(t => t.Name.Equals(theme.Name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"\"{theme.Name}\" is a built-in theme. Choose another name.");
        var own = (preferences.Themes ?? System.Array.Empty<ThemeDefinition>()).ToList();
        if (own.Any(t => t.Name.Equals(theme.Name, StringComparison.OrdinalIgnoreCase) && !t.Name.Equals(replacing, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"There is already a theme called \"{theme.Name}\".");
        if (ThemeTokens.Names.Any(n => !ThemeTokens.IsValid(n, theme.Colours.GetValueOrDefault(n)))) throw new ArgumentException("Every colour must be a valid hex colour.");
        var index = own.FindIndex(t => t.Name.Equals(replacing ?? theme.Name, StringComparison.OrdinalIgnoreCase));
        var saved = theme with { BuiltIn = false };
        if (index >= 0) own[index] = saved; else own.Add(saved);
        preferences = preferences with { Themes = own, Theme = Theme.Equals(replacing, StringComparison.OrdinalIgnoreCase) ? saved.Name : Theme };
        if (Application.Current is { } app && Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) ThemeEngine.Register(app, new[] { saved });
        appliedVariant = null; ApplyTheme(); Save(); Notify();
    }
    /// <summary>Copies a theme under a new name, so a built-in one can be made your own. Returns the new name, and selects it.</summary>
    public string DuplicateTheme(string name)
    {
        var source = Themes.First(t => t.Name == name);
        var copy = source.Name + " copy"; var number = 2;
        while (Themes.Any(t => t.Name.Equals(copy, StringComparison.OrdinalIgnoreCase))) copy = source.Name + " copy " + number++;
        SaveTheme(source with { Name = copy, BuiltIn = false }); SetTheme(copy); return copy;
    }
    public void DeleteTheme(string name)
    {
        var own = (preferences.Themes ?? System.Array.Empty<ThemeDefinition>()).ToList();
        if (own.RemoveAll(t => t.Name == name) == 0) throw new ArgumentException("Only your own themes can be deleted.");
        preferences = preferences with { Themes = own, Theme = Theme == name ? ThemeLibrary.System : Theme };
        appliedVariant = null; ApplyTheme(); Save(); Notify();
    }
    /// <summary>The colour to draw an account's blocks, dots and bars in: very dark colours lifted so they show, then softened if the theme asks for it.</summary>
    public string BlockColour(string accountColour) => ThemeDerivation.Soften(CurrentTheme, Ui.Visible(accountColour));
    public void SetTheme(string theme)
    {
        if (theme != ThemeLibrary.System && Themes.All(t => t.Name != theme)) throw new ArgumentException("Unknown theme", nameof(theme));
        preferences = preferences with { Theme = theme }; ApplyTheme(); Save(); Notify();
    }
    /// <summary>
    /// Builds the sample-data demo. Given a store, the demo runs through the real database (a throwaway one the caller deletes),
    /// so saving, editing and undo behave as they do for real data. Like a new database it holds no merged legacy credit.
    /// </summary>
    public static TimerModel CreateDemo(Func<DateTime>? clock = null, TimerStore? store = null)
    {
        var realClock = clock ?? (() => DateTime.Now);
        var wallStart = realClock();
        var anchor = wallStart.Date.AddHours(14).AddMinutes(15);
        var engine = new TrackingEngine(clock: () => anchor + (realClock() - wallStart));
        store?.Load(engine);
        engine.Schedule.Days.Add(anchor.DayOfWeek);
        // Seven accounts with a mix of budgets, so lists, rings and meters are tried at a realistic size: week, month and quarter
        // budgets, two with none, and the weekly ones chosen so one is comfortable, one nearly used and one over.
        var prefs = new StoreSettings(new()
        {
            new("Account one", 15), new("Account two", 4.5), new("Account three", 2.5), new("Account four", 20, "month"),
            new("Account five", 40, "quarter"), new("Account six"), new("Internal")
        }, false, "System", DayOfWeek.Monday);
        var start = anchor.Date.AddHours(9);
        // Today runs from 09:00 to the demo's 14:15, with a break in the middle and no gaps worth tidying except a few minutes.
        var today = new (string Account, int Minutes)[]
        {
            ("Account one", 50), ("Account one", 55), ("Account two", 60), ("Account four", 35), (BreakAccount.Name, 30),
            ("Account three", 40), ("Account five", 30), ("Account six", 12)
        };
        for (var i = 0; i < today.Length; i++)
        {
            engine.AddSession(today[i].Account, start, start.AddMinutes(today[i].Minutes));
            start = start.AddMinutes(today[i].Minutes + (i == 0 ? 3 : 0));
        }
        var monday = anchor.Date.AddDays(-((7 + (int)anchor.DayOfWeek - 1) % 7));
        if (monday < anchor.Date) engine.AddSession("Account one", monday.AddHours(9), monday.AddHours(16));
        // The days before today, so weekly, monthly and quarterly budgets and the week view have something to show.
        var earlier = new (string Account, int Minutes)[]
        {
            ("Account two", 60), ("Account three", 45), ("Account four", 120), ("Account five", 90), ("Account six", 30), ("Internal", 60)
        };
        for (var back = 1; back <= 6; back++)
        {
            var day = anchor.Date.AddDays(-back);
            if (day == monday) continue; // Monday already holds the long Account one stretch.
            var at = day.AddHours(9);
            foreach (var (account, minutes) in earlier) { engine.AddSession(account, at, at.AddMinutes(minutes)); at = at.AddMinutes(minutes + 5); }
        }
        if (store is not null) store.Save(engine, prefs);
        return new TimerModel(engine, prefs, store) { IsDemo = true };
    }
    // Overtime for testing: the demo's shift is cut short so the day is already past its end. It is only for the demo, and can be
    // switched off again, which puts back whatever shift was set for the day before.
    private (DateTime Day, DayOverride? Previous)? overtimeTest;
    public bool OvertimeTest => overtimeTest is not null;
    public void SetOvertimeTest(bool on)
    {
        if (!IsDemo) throw new InvalidOperationException("Overtime testing is available only in the demo.");
        if (on == OvertimeTest) return;
        if (on)
        {
            var day = Engine.Now.Date;
            overtimeTest = (day, Engine.Schedule.Overrides.TryGetValue(day, out var previous) ? previous : null);
            Engine.Schedule.Overrides[day] = new(false, new(TimeSpan.FromHours(9), TimeSpan.FromHours(13)));
        }
        else
        {
            var (day, previous) = overtimeTest!.Value;
            if (previous is null) Engine.Schedule.Overrides.Remove(day); else Engine.Schedule.Overrides[day] = previous;
            overtimeTest = null;
        }
        Notify();
    }
    public void ForceOvertime() => SetOvertimeTest(true);
    public void SetShifts(Dictionary<DayOfWeek, Shift> shifts, bool resetToday = false)
    {
        if (shifts.Count == 0 || shifts.Values.Any(s => s.Start < TimeSpan.Zero || s.End >= TimeSpan.FromDays(1) || s.End <= s.Start))
            throw new ArgumentException("Choose at least one working day, with an end after its start.");
        Engine.Schedule.Days = shifts.Keys.ToHashSet();
        Engine.Schedule.Shifts = new(shifts);
        if (resetToday) { Engine.Schedule.Overrides.Remove(Engine.Now.Date); Engine.DayStarts.Remove(Engine.Now.Date); }
        // A manual start was only needed while the day was off. Once the new hours make today or a later day a working day, the
        // usual start applies, so the old press must not keep overriding it.
        foreach (var day in Engine.DayStarts.Where(p => p.Key >= Engine.Now.Date && Engine.Schedule.Scheduled(p.Key) is { Working: true } plan && p.Value >= plan.Start).Select(p => p.Key).ToList()) Engine.DayStarts.Remove(day);
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
    /// <summary>Deletes the given entries as one undoable edit.</summary>
    public bool DeleteEntries(IReadOnlyList<SessionRow> rows) => ChangeLog(() => Editor.Delete(rows));
    public bool UndoEdit() => ChangeLog(Editor.Undo);
    public void OpenGroup(string id) => GroupRequested?.Invoke(id);
    public bool GroupEntries(IReadOnlyList<SessionRow> rows) => ChangeLog(() => Editor.Group(rows));
    public bool UngroupEntries(string id) => ChangeLog(() => Editor.Ungroup(id));
    public bool TidyDay(IReadOnlyList<GapChoice> choices, int limit) => ChangeLog(() => Editor.Tidy(choices, limit));
    public bool ExportWeek()
    {
        try
        {
            if (!Save()) return false;
            var export = new WeekExport(Editor, Engine, WeekStart(SelectedDate));
            if (store is not null) export.VerifyAgainst(store.Database);
            ExportPath = export.Write(store?.ExportDirectory ?? Path.Combine(Path.GetTempPath(), "BasicsAdvancedTimer-demo-exports"), ReviewAccounts.Select(a => a.Name));
            EditError = null; Notify(); return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or InvalidDataException)
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
    // Break and overtime themes are variants of Light and Dark that override colours. A break outranks overtime, because it is
    // the state you chose just now, and overtime outranks normal. Not tracking has no tint of its own: it keeps the ordinary
    // theme and shows itself through the grey glow, so it never fights the other two. Only the pill is tinted (see PillVariant),
    // so the dashboard stays calm: its own Break button turns amber to show a break is on.
    public string ThemeState => IsOnBreak ? "Break" : IsOvertime ? "Overtime" : "Normal";
    private ThemeVariant? appliedVariant;
    /// <summary>Every theme on offer: the built-in ones, then the user's own.</summary>
    public IReadOnlyList<ThemeDefinition> Themes => ThemeLibrary.BuiltIn.Concat(preferences.Themes ?? System.Array.Empty<ThemeDefinition>()).ToList();
    /// <summary>The theme actually in use. System is Light or Dark, whichever the computer is using.</summary>
    public ThemeDefinition CurrentTheme
    {
        get
        {
            if (Theme != ThemeLibrary.System && Themes.FirstOrDefault(t => t.Name == Theme) is { } chosen) return chosen;
            var dark = Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark;
            return dark ? ThemeLibrary.Dark : ThemeLibrary.Light;
        }
    }
    /// <summary>The theme the pill wears by itself: tinted amber on a break, red in overtime, otherwise null so it follows the rest of the app.</summary>
    public ThemeVariant? PillVariant => ThemeState != "Normal" && Application.Current is not null && Avalonia.Threading.Dispatcher.UIThread.CheckAccess()
        ? ThemeEngine.StateVariant(CurrentTheme, ThemeState == "Break" ? ThemeDerivation.Break : ThemeDerivation.Overtime) : null;
    private void ApplyTheme()
    {
        if (Application.Current is not { } app || !Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) return;
        // Following the system when nothing is overriding keeps the default variant, so a change of system theme still applies.
        var variant = Theme == ThemeLibrary.System || Themes.All(t => t.Name != Theme) ? ThemeVariant.Default : ThemeEngine.VariantFor(Themes.First(t => t.Name == Theme));
        if (ReferenceEquals(variant, appliedVariant)) return;
        appliedVariant = variant; app.RequestedThemeVariant = variant;
    }
    public bool IsOnBreak => BreakAccount.Is(ActiveAccount);
    public bool Shadows => preferences.Shadows;
    public void SetShadows(bool on) { preferences = preferences with { Shadows = on }; Save(); Notify(); }
    /// <summary>How far through the working day it is, from 0 to 1, or null on a day off.</summary>
    public double? DayProgress
    {
        get
        {
            var plan = Engine.Plan(Engine.Now);
            if (!plan.Working || plan.End <= plan.Start) return null;
            return Math.Clamp((Engine.Now - plan.Start).TotalSeconds / (plan.End - plan.Start).TotalSeconds, 0.0, 1.0);
        }
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
    /// <summary>Whether to start with Windows. Null in demo mode and tests, where it is not offered.</summary>
    public AutoStartSetting? AutoStart { get; set; }
    public void SetAutoStart(bool enabled)
    {
        try { AutoStart?.Set(enabled); LastError = null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or PlatformNotSupportedException)
        { LastError = "Could not change starting with Windows. " + error.Message; }
        Notify();
    }
    public event Action? QuitRequested;
    public void RequestQuit() => QuitRequested?.Invoke();
    public bool StopForClose()
    { Engine.StopAccount(); var saved = Save(); if (!saved) Notify(); return saved; }
    private void Notify() { ApplyTheme(); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
}
