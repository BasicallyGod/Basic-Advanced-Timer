using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;

namespace Timer.Desktop;

public sealed class WindowCoordinator
{
    public PillWindow Pill { get; }
    public DashboardWindow Dashboard { get; }
    public WindowCoordinator(TimerModel model, Action exit)
    {
        Pill = new PillWindow(model);
        Dashboard = new DashboardWindow(model);
        Pill.OpenDashboard = () =>
        {
            Fit(Dashboard, Pill, 960, 640);
            Dashboard.Show();
            Pill.Hide();
            Dashboard.Activate();
        };
        Dashboard.OpenPill = () => { Pill.Show(); Dashboard.Hide(); Pill.Activate(); };
        Pill.Closed += (_, _) => exit();
        Dashboard.Closed += (_, _) => exit();
        Pill.Closing += (_, e) => e.Cancel = !model.StopForClose();
        Dashboard.Closing += (_, e) => e.Cancel = !model.StopForClose();
    }
    private static void Fit(Window target, Window source, double width, double height)
    {
        var screen = source.Screens.ScreenFromWindow(source);
        if (screen is null) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        target.Width = Math.Min(width, area.Width / scale);
        target.Height = Math.Min(height, area.Height / scale);
        target.Position = new PixelPoint(
            Math.Clamp(source.Position.X, area.X, Math.Max(area.X, area.Right - (int)(target.Width * scale))),
            Math.Clamp(source.Position.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)(target.Height * scale))));
    }
}

public sealed class PillWindow : Window
{
    public Action? OpenDashboard { get; set; }
    private readonly TimerModel model;
    private readonly TextBlock account = Ui.Text("", 15);
    private readonly TextBlock timer = Ui.Text("00:00:00", 22);
    private readonly StackPanel expansion = new() { IsVisible = false, Spacing = 8 };
    private readonly Button day;
    private bool expanded;
    public const double CollapsedHeight = 64;
    private readonly TextBlock credited = Ui.Text("", 12, "muted");
    private readonly TextBlock dot = Ui.Text("●", 11);
    private readonly TextBlock overtime = Ui.Text("", 11, "overtime");
    private readonly Button pause;
    private readonly PauseGlyph pauseGlyph = new();
    private readonly TextBlock saved = Ui.Text("", 11, "muted");
    public PillWindow(TimerModel model)
    {
        this.model = model;
        Title = "Basic’s Advanced Timer migration";
        ContextMenu = Ui.SettingsMenu(model);
        Width = 420;
        Height = CollapsedHeight;
        CanResize = false;
        Topmost = true;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Avalonia.Media.Brushes.Transparent;
        var root = Ui.Grid("*", "64,*");
        var head = Ui.Grid("Auto,1,*,36", "*");
        head.Margin = new Thickness(16, 0);
        var timerButton = Ui.Button("", () => OpenDashboard?.Invoke(), "PillDashboard");
        timerButton.Content = timer;
        timerButton.Classes.Add("pillAction");
        ToolTip.SetTip(timerButton, "Click for dashboard; drag to move");
        Ui.Put(head, timerButton).Margin = new Thickness(4, 0, 14, 0);
        var divider = new Border { Width = 1, Height = 36 }; divider.Classes.Add("rule");
        Ui.Put(head, divider, 1);
        var labels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        account.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        labels.Children.Add(account); labels.Children.Add(credited); labels.Children.Add(overtime);
        var accountContent = Ui.Grid("12,*");
        dot.VerticalAlignment = VerticalAlignment.Top; dot.Margin = new Thickness(0, 3, 0, 0);
        Ui.Put(accountContent, dot); Ui.Put(accountContent, labels, 1);
        var accountButton = Ui.Button("", ToggleExpansion, "PillAccounts");
        accountButton.Content = accountContent;
        accountButton.Classes.Add("pillAction");
        accountButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        accountButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(accountButton, "Click for accounts; drag to move");
        Ui.Put(head, accountButton, 2).Margin = new Thickness(10, 0);
        pause = Ui.Button("", model.TogglePause, "PillPause");
        pause.Content = pauseGlyph;
        pause.Classes.Add("round");
        pause.Width = pause.Height = 36;
        Ui.Put(head, pause, 3);
        Ui.PillGestures(head, this);
        Ui.Put(root, head);
        expansion.Children.Add(Ui.Rule());
        expansion.Children.Add(new AccountList(model));
        day = Ui.Button("Start day", model.ToggleDay, "PillDay");
        var actions = Ui.Horizontal(day, Ui.Button("×", Close));
        actions.Margin = new Thickness(16, 0);
        expansion.Children.Add(actions);
        var bottom = Ui.Horizontal(Ui.SettingsButton(model), saved);
        bottom.Margin = new Thickness(16, 0, 16, 12);
        expansion.Children.Add(bottom);
        Ui.Put(root, new ScrollViewer { Content = expansion,
            AllowAutoHide = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, row: 1);
        var surface = new Border { CornerRadius = new CornerRadius(24), Child = root, ClipToBounds = true };
        surface.Classes.Add("surface");
        Content = surface;
        model.PropertyChanged += Changed;
        Closed += (_, _) => model.PropertyChanged -= Changed;
        Refresh();
    }
    public void ToggleExpansion()
    {
        expanded = !expanded;
        expansion.IsVisible = expanded;
        Height = expanded ? CollapsedHeight + model.Accounts.Count * 68 + 110 : CollapsedHeight;
        if (Screens.ScreenFromWindow(this) is { } screen)
        {
            Height = Math.Min(Height, screen.WorkingArea.Height / screen.Scaling);
            Position = new PixelPoint(Position.X, Math.Clamp(Position.Y, screen.WorkingArea.Y,
                Math.Max(screen.WorkingArea.Y, screen.WorkingArea.Bottom - (int)(Height * screen.Scaling))));
        }
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
    private void Refresh()
    {
        saved.Text = model.LastError is null ? (model.Persistent ? "Saved locally" : model.IsDemo ? "Sample data · demo" : "Test session") : "Save failed; retrying";
        ToolTip.SetTip(saved, model.LastError);
        account.Text = model.AccountLabel;
        timer.Text = model.ClockText;
        var selected = model.Accounts.FirstOrDefault(a => a.Name == model.SelectedAccount);
        account.Text = model.ActiveAccount ?? (selected?.Name ?? model.AccountLabel);
        credited.Text = model.IsTracking && selected is not null ? model.Format(model.Today(selected)) + " today" : model.Status;
        dot.Classes.Set("live", model.IsTracking); dot.Classes.Set("idle", !model.IsTracking);
        overtime.IsVisible = model.IsOvertime;
        overtime.Text = "OT +" + model.Format(model.Engine.Totals(model.Engine.Now).Overtime);
        pauseGlyph.Playing = model.IsTracking; pauseGlyph.InvalidateVisual();
        pause.IsEnabled = model.IsTracking || selected is not null;
        ToolTip.SetTip(pause, model.IsTracking ? "Pause account; drag to move" : "Resume account; drag to move");
        timer.Classes.Set("live", model.IsTracking);
        timer.Classes.Set("idle", !model.IsTracking);
        day.Content = model.DayStarted ? "Finish day" : "Start day";
    }
}

public sealed class DashboardWindow : Window
{
    public Action? OpenPill { get; set; }
    private readonly TimerModel model;
    private readonly TextBlock clock = Ui.Text("00:00:00", 40);
    private readonly TextBlock account = Ui.Text("", 16);
    private readonly TextBlock allocated = Ui.Text("");
    private readonly TextBlock unallocated = Ui.Text("");
    private readonly TextBlock date = Ui.Text("", 14);
    private readonly TextBlock today = Ui.Text("", 23);
    private readonly ContentControl workspace = new();
    private readonly Button day;
    private readonly Button format;
    private readonly Button timeline;
    private readonly Button week;
    private readonly Button undo;
    private readonly Button merge;
    private bool weekMode;
    private bool? renderedWeekMode;
    private DateTime? renderedDate;
    public DashboardWindow(TimerModel model)
    {
        this.model = model;
        Title = "Basic’s Advanced Timer migration";
        ContextMenu = Ui.SettingsMenu(model);
        Width = 960;
        Height = 640;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Avalonia.Media.Brushes.Transparent;
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
        merge = Ui.Button("Merge", () => model.MergeSelection(), "MergeSelected");
        var more = Ui.Button("Tools", () => { }, "ReviewTools");
        var place = new MenuItem { Header = "Position existing credit…" }; place.Click += (_, _) => new PositionWindow(model).Show(this);
        var group = new MenuItem { Header = "Group entries…" }; group.Click += (_, _) => new GroupWindow(model).Show(this);
        var tidy = new MenuItem { Header = "Tidy day…" }; tidy.Click += (_, _) => new TidyWindow(model).Show(this);
        var export = new MenuItem { Header = "Export week…" }; export.Click += (_, _) => new ExportWindow(model).Show(this);
        more.ContextMenu = new ContextMenu { ItemsSource = new[] { place, group, tidy, export } };
        more.Click += (_, _) => more.ContextMenu.Open(more);
        var tools = Ui.Horizontal(format, Ui.Button("Add time", () => model.OpenGap(), "AddTime"), undo, merge, more);
        tools.Margin = new Thickness(0, 12);
        Ui.Put(review, tools, row: 1);
        Ui.Put(review, workspace, row: 2);
        Ui.Put(body, review, 2);
        Ui.Put(root, body, row: 2);
        var footer = Ui.Text(model.Persistent ? "Migration build · saved locally" : model.IsDemo ? "Sample data · demo" : "Test session", 11, "muted");
        model.PropertyChanged += (_, _) => footer.Text = model.LastError ?? model.EditError ?? (model.Persistent ? "Migration build · saved locally" : model.IsDemo ? "Sample data · demo" : "Test session");
        footer.Margin = new Thickness(24, 10);
        Ui.Put(root, footer, row: 3);
        var surface = new Border { Name = "DashboardSurface", CornerRadius = new CornerRadius(20), Child = root, ClipToBounds = true };
        surface.Classes.Add("surface"); Content = surface;
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { model.ClearSelection(); e.Handled = true; } };
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
        merge.IsEnabled = model.CanMergeSelection; ToolTip.SetTip(merge, model.MergeHint);
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
