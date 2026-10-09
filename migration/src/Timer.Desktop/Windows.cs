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
    private readonly TextBlock saved = Ui.Text("", 11, "muted");
    public PillWindow(TimerModel model)
    {
        this.model = model;
        Title = "Basic’s Advanced Timer migration";
        ContextMenu = Ui.SettingsMenu(model);
        Width = 360;
        Height = 88;
        CanResize = false;
        Topmost = true;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Avalonia.Media.Brushes.Transparent;
        var root = Ui.Grid("*", "88,*");
        var head = Ui.Grid("*,Auto,Auto", "*");
        head.Margin = new Thickness(16, 0);
        var labels = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(account);
        labels.Children.Add(new TrackingLabels(model));
        Ui.Put(head, labels);
        Ui.Put(head, timer, 1).Margin = new Thickness(8, 0);
        var toggle = Ui.Button("⌄", ToggleExpansion, "ExpandPill");
        toggle.Classes.Add("round");
        Ui.Put(head, toggle, 2);
        head.PointerPressed += (_, e) => { if (e.Source is TextBlock && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        Ui.Put(root, head);
        expansion.Children.Add(Ui.Rule());
        expansion.Children.Add(new AccountList(model));
        day = Ui.Button("Start day", model.ToggleDay, "PillDay");
        var actions = Ui.Horizontal(day, Ui.Button("Dashboard", () => OpenDashboard?.Invoke(), "OpenDashboard"), Ui.Button("×", Close));
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
        Height = expanded ? 560 : 88;
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
        saved.Text = model.LastError is null ? (model.Persistent ? "Saved locally" : "Test session") : "Save failed; retrying";
        ToolTip.SetTip(saved, model.LastError);
        account.Text = model.AccountLabel;
        timer.Text = model.ClockText;
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
    private bool weekMode;
    public DashboardWindow(TimerModel model)
    {
        this.model = model;
        Title = "Basic’s Advanced Timer migration";
        ContextMenu = Ui.SettingsMenu(model);
        Width = 960;
        Height = 640;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        var root = Ui.Grid("*", "Auto,Auto,*,Auto");
        var header = Ui.Grid("*,Auto");
        header.Margin = new Thickness(24, 18);
        Ui.Put(header, today);
        Ui.Put(header, Ui.Horizontal(Ui.SettingsButton(model), Ui.Button("Back to pill", () => OpenPill?.Invoke(), "BackToPill"), Ui.Button("×", Close)), 1);
        header.PointerPressed += (_, e) => { if (e.Source == today && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
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
        var tools = Ui.Horizontal(format);
        tools.Margin = new Thickness(0, 12);
        Ui.Put(review, tools, row: 1);
        Ui.Put(review, workspace, row: 2);
        Ui.Put(body, review, 2);
        Ui.Put(root, body, row: 2);
        var footer = Ui.Text(model.Persistent ? "Migration build · saved locally · timeline editing follows" : "Test session", 11, "muted");
        model.PropertyChanged += (_, _) => footer.Text = model.LastError ?? (model.Persistent ? "Migration build · saved locally · timeline editing follows" : "Test session");
        footer.Margin = new Thickness(24, 10);
        Ui.Put(root, footer, row: 3);
        Content = root;
        model.PropertyChanged += Changed;
        Closed += (_, _) => model.PropertyChanged -= Changed;
        Refresh(true);
    }
    public void SelectTab(bool showWeek) { weekMode = showWeek; Refresh(true); }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh(e.PropertyName != nameof(TimerModel.Elapsed));
    private void Refresh(bool refreshWorkspace)
    {
        clock.Text = model.ClockText;
        account.Text = model.AccountLabel;
        today.Text = model.Engine.Now.ToString("dddd, d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        date.Text = model.SelectedDate.ToString("ddd d MMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        allocated.Text = "Today allocated   " + model.Format(model.Allocated);
        unallocated.Text = "Unallocated   " + model.Format(model.Unallocated);
        day.Content = model.DayStarted ? "Finish day" : "Start day";
        format.Content = model.DecimalHours ? "Decimal hours" : "Hours and minutes";
        timeline.Classes.Set("selected", !weekMode);
        week.Classes.Set("selected", weekMode);
        if (refreshWorkspace)
        {
            var previous = (workspace.Content as Control)?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset;
            workspace.Content = weekMode ? ReviewViews.Week(model) : ReviewViews.Timeline(model);
            if (previous is { } offset && workspace.Content is Control control)
            {
                var scroll = control is ScrollViewer direct ? direct : control.GetLogicalDescendants().OfType<ScrollViewer>().FirstOrDefault();
                if (scroll is not null) scroll.Offset = offset;
            }
        }
    }
}
