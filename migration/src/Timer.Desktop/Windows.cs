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
    private readonly TimerModel model;
    private bool quitting, dashboardWasShown;
    // Whether closing a window hides the timer to the tray. Without a tray the window really closes, as it always did.
    public bool HideToTray { get; set; }
    public bool IsShown => Pill.IsVisible || Dashboard.IsVisible;
    public WindowCoordinator(TimerModel model, Action exit)
    {
        this.model = model;
        Pill = new PillWindow(model);
        Dashboard = new DashboardWindow(model);
        // The timer lives in the tray, so neither window needs a taskbar button.
        Pill.ShowInTaskbar = false; Dashboard.ShowInTaskbar = false;
        Pill.OpenDashboard = () =>
        {
            Fit(Dashboard, Pill, 960 + 2 * Ui.ShadowMargin, 640 + 2 * Ui.ShadowMargin);
            Dashboard.Show();
            Pill.Hide();
            Dashboard.Activate();
        };
        Dashboard.OpenPill = () => { Pill.Show(); Dashboard.Hide(); Pill.Activate(); };
        Pill.Closed += (_, _) => exit();
        Dashboard.Closed += (_, _) => exit();
        Pill.Closing += OnClosing; Dashboard.Closing += OnClosing;
        model.QuitRequested += Quit;
    }
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // The close button hides the timer and leaves tracking running. Quitting, or Windows shutting down, saves and really closes.
        if (HideToTray && !quitting && e.CloseReason == WindowCloseReason.WindowClosing) { e.Cancel = true; Hide(); return; }
        e.Cancel = !model.StopForClose();
    }
    public void Hide()
    {
        dashboardWasShown = Dashboard.IsVisible;
        Pill.Hide(); Dashboard.Hide();
    }
    public void ShowTimer()
    {
        if (IsShown) { (Dashboard.IsVisible ? (Window)Dashboard : Pill).Activate(); return; }
        if (dashboardWasShown) ShowDashboard(); else { Pill.Show(); BringToFront(Pill); }
    }
    public void ShowDashboard()
    {
        if (!Dashboard.IsVisible) { Fit(Dashboard, Pill, 960 + 2 * Ui.ShadowMargin, 640 + 2 * Ui.ShadowMargin); Dashboard.Show(); }
        Pill.Hide(); BringToFront(Dashboard);
    }
    // The tray icon's left click shows the timer, or hides it when it is already in front.
    public void ToggleFromTray()
    {
        if ((Pill.IsVisible && Pill.IsActive) || (Dashboard.IsVisible && Dashboard.IsActive)) Hide(); else ShowTimer();
    }
    private static void BringToFront(Window window)
    {
        window.Topmost = false; window.Topmost = true; window.Activate();
    }
    public void ResetPosition()
    {
        // Back to the top right of the main screen, which also recovers a window dragged off a disconnected monitor.
        var screen = Pill.Screens.Primary;
        if (screen is not null)
        {
            var area = screen.WorkingArea;
            Pill.Position = new PixelPoint(area.Right - (int)(Pill.Width * screen.Scaling) - 24, area.Y + 24);
        }
        Dashboard.Hide(); Pill.Show(); BringToFront(Pill);
    }
    public void Quit()
    {
        if (!model.StopForClose()) { ShowTimer(); return; }
        quitting = true;
        Pill.Close(); Dashboard.Close();
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
    public const double CollapsedHeight = 80;
    public static double CollapsedWindowHeight => CollapsedHeight + 2 * Ui.ShadowMargin;
    private readonly TextBlock credited = Ui.Text("", 12, "muted");
    private readonly TextBlock dot = Ui.Text("●", 11);
    private readonly TextBlock overtime = Ui.Text("", 11, "overtime");
    private readonly Button pause;
    private readonly PauseGlyph pauseGlyph = new();
    private readonly TextBlock saved = Ui.Text("", 11, "muted");
    private readonly Grid progressBar = new();
    private Border progressFill = null!, shadowHost = null!;
    private (bool, string?, Avalonia.Styling.ThemeVariant)? lastShadow;
    public PillWindow(TimerModel model)
    {
        this.model = model;
        Title = "Basic’s Advanced Timer migration";
        ContextMenu = Ui.SettingsMenu(model);
        Width = 420 + 2 * Ui.ShadowMargin;
        Height = CollapsedHeight + 2 * Ui.ShadowMargin;
        CanResize = false;
        Topmost = true;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Avalonia.Media.Brushes.Transparent;
        var root = Ui.Grid("*", "80,*");
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
        // How far through the working day it is, as a thin line along the bottom of the pill's main row.
        progressBar.Height = 4; progressBar.VerticalAlignment = VerticalAlignment.Bottom; progressBar.Margin = new Thickness(22, 0, 22, 6);
        progressBar.ColumnDefinitions = new ColumnDefinitions("0*,1*");
        var fill = new Border { Name = "DayProgressFill", CornerRadius = new CornerRadius(2) };
        var rest = new Border { CornerRadius = new CornerRadius(2), Opacity = .8 }; rest.Classes.Add("rule");
        progressFill = fill; Grid.SetColumn(fill, 0); Grid.SetColumn(rest, 1);
        progressBar.Children.Add(rest); progressBar.Children.Add(fill);
        progressBar.Name = "DayProgress"; progressBar.IsHitTestVisible = false;
        Ui.Put(root, progressBar);
        expansion.Children.Add(Ui.Rule());
        expansion.Children.Add(new AccountList(model));
        day = Ui.Button("Start day", model.ToggleDay, "PillDay");
        var actions = Ui.Horizontal(day);
        actions.Margin = new Thickness(16, 0);
        expansion.Children.Add(actions);
        var bottom = Ui.Horizontal(Ui.SettingsButton(model), saved);
        bottom.Margin = new Thickness(16, 0, 16, 12);
        expansion.Children.Add(bottom);
        Ui.Put(root, new ScrollViewer { Content = expansion,
            AllowAutoHide = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, row: 1);
        var surface = new Border { Name = "PillSurface", Child = root, ClipToBounds = true };
        surface.Classes.Add("surface");
        shadowHost = Ui.Shadowed(surface, 24); Content = shadowHost;
        model.PropertyChanged += Changed;
        ActualThemeVariantChanged += (_, _) => { lastShadow = null; Refresh(); };
        Closed += (_, _) => model.PropertyChanged -= Changed;
        Refresh();
    }
    public void ToggleExpansion()
    {
        expanded = !expanded;
        expansion.IsVisible = expanded;
        Height = (expanded ? CollapsedHeight + model.Accounts.Count * 68 + 110 : CollapsedHeight) + 2 * Ui.ShadowMargin;
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
        dot.Classes.Set("live", model.IsTracking && !model.IsOnBreak); dot.Classes.Set("break", model.IsOnBreak); dot.Classes.Set("idle", !model.IsTracking);
        Ui.Pulse(dot, model.IsTracking);
        overtime.IsVisible = model.IsOvertime;
        overtime.Text = "OT +" + model.Format(model.Engine.Totals(model.Engine.Now).Overtime);
        pauseGlyph.Playing = model.IsTracking; pauseGlyph.InvalidateVisual();
        pause.IsEnabled = model.IsTracking || selected is not null;
        ToolTip.SetTip(pause, model.IsTracking ? "Pause account; drag to move" : "Resume account; drag to move");
        timer.Classes.Set("live", model.IsTracking && !model.IsOnBreak); timer.Classes.Set("break", model.IsOnBreak);
        timer.Classes.Set("idle", !model.IsTracking);
        day.Content = model.DayStarted ? "Finish day" : "Start day";
        var progress = model.DayProgress;
        progressBar.IsVisible = progress is not null;
        if (progress is { } done)
        {
            progressBar.ColumnDefinitions[0].Width = new GridLength(done, GridUnitType.Star);
            progressBar.ColumnDefinitions[1].Width = new GridLength(1.0 - done, GridUnitType.Star);
            progressFill.Classes.Set("progressOvertime", model.IsOvertime); progressFill.Classes.Set("progressBreak", model.IsOnBreak);
            ToolTip.SetTip(progressBar, $"{(int)(done * 100)}% of the working day");
        }
        // The pill glows for a break, overtime and not tracking, so its state shows from across the room.
        var glow = model.IsOnBreak ? "Break" : model.IsOvertime ? "Overtime" : !model.IsTracking ? "Idle" : null;
        var shadow = (model.Shadows, glow, ActualThemeVariant);
        if (lastShadow != shadow) { lastShadow = shadow; Ui.SetShadow(shadowHost, model.Shadows, glow); }
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
    private EntryWindow? entryEditor;
    private bool weekMode;
    private bool? renderedWeekMode;
    private DateTime? renderedDate;
    public DashboardWindow(TimerModel model)
    {
        this.model = model;
        Title = "Basic’s Advanced Timer migration";
        ContextMenu = Ui.SettingsMenu(model);
        Width = 960 + 2 * Ui.ShadowMargin;
        Height = 640 + 2 * Ui.ShadowMargin;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Avalonia.Media.Brushes.Transparent;
        var root = Ui.Grid("*", "Auto,Auto,*,Auto");
        var header = Ui.Grid("*,Auto");
        header.Margin = new Thickness(24, 18);
        Ui.Put(header, today);
        Ui.Put(header, Ui.Horizontal(Ui.SettingsButton(model), Ui.Button("Back to pill", () => OpenPill?.Invoke(), "BackToPill")), 1);
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
        Ui.Put(sidebar, new ScrollViewer { Name = "AccountScroll", Content = new AccountList(model, reorderable: true), AllowAutoHide = false,
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
        var surface = new Border { Name = "DashboardSurface", Child = root, ClipToBounds = true };
        surface.Classes.Add("surface");
        var host = Ui.Shadowed(surface, 20); Content = host;
        (bool, Avalonia.Styling.ThemeVariant)? lastShadow = null;
        void UpdateShadow()
        {
            var state = (model.Shadows, ActualThemeVariant);
            if (lastShadow != state) { lastShadow = state; Ui.SetShadow(host, model.Shadows, null); }
        }
        model.PropertyChanged += (_, _) => UpdateShadow();
        ActualThemeVariantChanged += (_, _) => UpdateShadow();
        UpdateShadow();
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) { model.ClearSelection(); e.Handled = true; }
            // Backspace and Delete remove the selected blocks, but never while typing in a text box.
            else if (e.Key is Avalonia.Input.Key.Back or Avalonia.Input.Key.Delete && FocusManager?.GetFocusedElement() is not TextBox)
            { if (model.SelectedRows.Count > 0) { model.DeleteSelection(); e.Handled = true; } }
        };
        model.PropertyChanged += Changed;
        model.EntryRequested += ShowEntry;
        model.PositionRequested += ShowPosition; model.GroupRequested += ShowGroup;
        Closed += (_, _) => { model.PropertyChanged -= Changed; model.EntryRequested -= ShowEntry; model.PositionRequested -= ShowPosition; model.GroupRequested -= ShowGroup; };
        Refresh(true);
    }
    private void ShowEntry(Timer.Core.SessionRow? row, DateTime start, DateTime end)
    {
        if (!IsVisible) return;
        if (entryEditor is { IsVisible: true }) { entryEditor.Activate(); return; }
        entryEditor = new EntryWindow(model, row, start, end);
        entryEditor.Closed += (_, _) => entryEditor = null;
        entryEditor.Show(this);
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
        undo.Content = model.Editor.CanUndo ? $"Undo ({model.Editor.UndoCount})" : "Undo";
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
                ? !weekMode && model.TimelineOffset is double timelineOffset ? new Vector(0, timelineOffset)
                    : oldScroll?.Tag is Vector pending ? pending : oldScroll?.Offset : null;
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
