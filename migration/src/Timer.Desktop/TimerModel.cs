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
    public LedgerEditor Editor { get; }
    public string? EditError { get; private set; }
    public event Action<SessionRow?, DateTime, DateTime>? EntryRequested;
    public event Action<CreditPosition?>? PositionRequested;
    public event Action<string>? GroupRequested;
    public string? ExportPath { get; private set; }
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
    public void TogglePause()
    {
        if (Engine.Active is { } active) { SelectedAccount = active.Account; Engine.StopAccount(); }
        else if (SelectedAccount is { } name && Accounts.Any(a => a.Name == name)) Engine.ToggleAccount(name);
        Save(); Notify();
    }
    public void SetAccounts(IEnumerable<AccountSettings> accounts)
    {
        var values = accounts.ToList();
        if (values.Count == 0 || values.Any(a => string.IsNullOrWhiteSpace(a.Name) || a.Name != a.Name.Trim()
            || a.Name.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0 || a.Name.Equals("Lunch", StringComparison.OrdinalIgnoreCase)
            || a.BudgetPeriod is not ("week" or "month" or "quarter")
       …4803 tokens truncated…ingsMenu(model);
        Width = 960;
        Height = 640;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        var root = Ui.Grid("*", "Auto,Auto,*,Auto");
        var header = Ui.Grid("*,Auto");
        header.Margin = new Thickness(24, 18);
        Ui.Put(header, today);
        Ui.Put(header, Ui.Horizontal(Ui.SettingsButton(model), Ui.Button("Back to pill", () => OpenPill?.Invoke(), "BackToPill"), Ui.Button("×", Close)), 1);
        Ui.DragHeader(header, this);
        Ui.Put(root, header);
        var strip = Ui.Grid("Auto,*,Auto");
        strip.Margin = new Thickness(24, 0, 24, 18);
        Ui.Put(strip, clock);
        var live = new StackPanel { Spacing = 7, Margin = new Thickness(20, 0), VerticalAlignment = VerticalAlignment.Center };
        live.Children.Add(account);
        live.Children.Add(new TrackingLabels(model));
        Ui.Put(strip, live, 1);
        day = Ui.Button("Start day", model.ToggleDay, "DashboardDay");
        Ui.Put(strip, day, 2);
        Ui.Put(root, strip, row: 1);
        var body = Ui.Grid("300,1,*", "*");
        var sidebar = Ui.Grid("*", "Auto,*,Auto");
        var title = Ui.Text("Client accounts", 13, "muted");
        title.Margin = new Thickness(18, 16);
        Ui.Put(sidebar, title);
        Ui.Put(sidebar, new ScrollViewer { Name = "AccountScroll", Content = new AccountList(model), AllowAutoHide = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, row: 1);
        var summary = new StackPanel { Margin = new Thickness(18, 12, 18, 18), Spacing = 10 };
        summary.Children.Add(Ui.Rule());
        summary.Children.Add(allocated);
        summary.Children.Add(unallocated);
        Ui.Put(sidebar, summary, row: 2);
        var panel = new Border { Child = sidebar };
        panel.Classes.Add("panel");
        Ui.Put(body, panel);
        var divider = new Border(); divider.Classes.Add("rule"); Ui.Put(body, divider, 1);
        var review = Ui.Grid("*", "Auto,Auto,*");
        review.Margin = new Thickness(22, 14, 22, 18);
        var nav = Ui.Grid("Auto,*,Auto");
        timeline = Ui.Button("Timeline", () => SelectTab(false), "TimelineTab"); timeline.Classes.Add("tab");
        week = Ui.Button("Week", () => SelectTab(true), "WeekTab"); week.Classes.Add("tab");
        Ui.Put(nav, Ui.Horizontal(timeline, week));
        Ui.Put(nav, Ui.Horizontal(Ui.Button("‹", () => model.ChangeDate(-1)), date, Ui.Button("›", () => model.ChangeDate(1))), 2);
        Ui.Put(review, nav);
        format = Ui.Button("Hours and minutes", model.ToggleFormat, "HoursFormat");
        undo = Ui.Button("Undo", () => model.UndoEdit(), "UndoEdit");
        var more = Ui.Button("Tools", () => { }, "ReviewTools");
        var place = new MenuItem { Header = "Position existing credit…" }; place.Click += (_, _) => new PositionWindow(model).Show(this);
        var group = new MenuItem { Header = "Group entries…" }; group.Click += (_, _) => new GroupWindow(model).Show(this);
        var tidy = new MenuItem { Header = "Tidy day…" }; tidy.Click += (_, _) => new TidyWindow(model).Show(this);
        var export = new MenuItem { Header = "Export week…" }; export.Click += (_, _) => new ExportWindow(model).Show(this);
        more.ContextMenu = new ContextMenu { ItemsSource = new[] { place, group, tidy, export } };
        more.Click += (_, _) => more.ContextMenu.Open(more);
        var tools = Ui.Horizontal(format, Ui.Button("Add time", () => model.OpenGap(), "AddTime"), undo, more);
        tools.Margin = new Thickness(0, 12);
        Ui.Put(review, tools, row: 1);
        Ui.Put(review, workspace, row: 2);
        Ui.Put(body, review, 2);
        Ui.Put(root, body, row: 2);
        var footer = Ui.Text(model.Persistent ? "Migration build · saved locally" : model.IsDemo ? "Sample data · demo" : "Test session", 11, "muted");
        model.PropertyChanged += (_, _) => footer.Text = model.LastError ?? model.EditError ?? (model.Persistent ? "Migration build · saved locally" : model.IsDemo ? "Sample data · demo" : "Test session");
        footer.Margin = new Thickness(24, 10);
        Ui.Put(root, footer, row: 3);
        Content = root;
        model.PropertyChanged += Changed;
        model.EntryRequested += ShowEntry;
        model.PositionRequested += ShowPosition; model.GroupRequested += ShowGroup;
        Closed += (_, _) => { model.PropertyChanged -= Changed; model.EntryRequested -= ShowEntry; model.PositionRequested -= ShowPosition; model.GroupRequested -= ShowGroup; };
        Refresh(true);
    }
    private void ShowEntry(Timer.Core.SessionRow? row, DateTime start, DateTime end)
    {
        if (IsVisible) new EntryWindow(model, row, start, end).Show(this);
    }
    private void ShowPosition(Timer.Storage.CreditPosition? position) { if (IsVisible) new PositionWindow(model, position).Show(this); }
    private void ShowGroup(string id) { if (IsVisible) new GroupWindow(model, id).Show(this); }
    public void SelectTab(bool showWeek) { weekMode = showWeek; Refresh(true); }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh(e.PropertyName != nameof(TimerModel.Elapsed));
    private void Refresh(bool refreshWorkspace)
    {
        clock.Text = model.ClockText;
        clock.Classes.Set("live", model.IsTracking);
        clock.Classes.Set("idle", !model.IsTracking);
        account.Text = model.AccountLabel;
        today.Text = model.Engine.Now.ToString("dddd, d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        date.Text = model.SelectedDate.ToString("ddd d MMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        allocated.Text = "Today allocated   " + model.Format(model.Allocated);
        unallocated.Text = "Unallocated   " + model.Format(model.Unallocated);
        day.Content = model.DayStarted ? "Finish day" : "Start day";
        undo.IsEnabled = model.Editor.CanUndo;
        ToolTip.SetTip(undo, model.Editor.UndoLabel is { } label ? "Undo: " + label : "No edit to undo");
        format.Content = model.DecimalHours ? "Decimal hours" : "Hours and minutes";
        timeline.Classes.Set("selected", !weekMode);
        week.Classes.Set("selected", weekMode);
        if (refreshWorkspace)
        {
            var oldScroll = workspace.Content is ScrollViewer directOld ? directOld
                : (workspace.Content as Control)?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            var previous = renderedWeekMode == weekMode && renderedDate == model.SelectedDate
                ? oldScroll?.Tag is Vector pending ? pending : oldScroll?.Offset : null;
            workspace.Content = weekMode ? ReviewViews.Week(model) : ReviewViews.Timeline(model);
            if (previous is { } offset && workspace.Content is Control control)
            {
                var scroll = control is ScrollViewer direct ? direct : control.GetLogicalDescendants().OfType<ScrollViewer>().FirstOrDefault();
                if (scroll is not null) scroll.Tag = offset;
            }
            renderedWeekMode = weekMode;
            renderedDate = model.SelectedDate;
        }
    }
}
