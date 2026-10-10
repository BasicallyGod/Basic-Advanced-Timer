using Avalonia;
using Timer.Core;
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
    private readonly BreakGlyph breakGlyph = new();
    private Button breakButton = null!;
    private readonly TextBlock saved = Ui.Text("", 11, "muted");
    private readonly Grid progressBar = new();
    private Border progressFill = null!, shadowHost = null!;
    private ScrollViewer pillScroll = null!;
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
        var head = Ui.Grid("Auto,1,*,36,36", "*");
        // The progress line takes the bottom of the row, so the content is centred in what is left above it.
        head.Margin = new Thickness(16, 0, 16, 12);
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
        var accountContent = Ui.Grid("24,*");
        dot.VerticalAlignment = VerticalAlignment.Top; dot.Margin = new Thickness(8, 3, 0, 0);
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
        Ui.Put(head, pause, 4);
        breakButton = Ui.Button("", model.ToggleBreak, "PillBreak");
        breakButton.Content = breakGlyph; breakButton.Classes.Add("round"); breakButton.Classes.Add("breakButton");
        breakButton.Width = breakButton.Height = 36; breakButton.Margin = new Thickness(0, 0, 8, 0);
        Ui.Put(head, breakButton, 3);
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
        // The day button is the one thing at the bottom, wide and filled so it is easy to find. Settings are on the right-click menu.
        day.HorizontalAlignment = HorizontalAlignment.Stretch; day.Height = 44;
        day.Margin = new Thickness(16, 6, 16, 16);
        expansion.Children.Add(day);
        saved.HorizontalAlignment = HorizontalAlignment.Center; saved.Margin = new Thickness(0, -8, 0, 12);
        expansion.Children.Add(saved);
        pillScroll = new ScrollViewer { Content = expansion, AllowAutoHide = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Ui.Put(root, pillScroll, row: 1);
        var surface = new Border { Name = "PillSurface", Child = root, ClipToBounds = true };
        surface.Classes.Add("surface");
        shadowHost = Ui.Shadowed(surface, 24); Content = shadowHost;
        model.PropertyChanged += Changed;
        ActualThemeVariantChanged += (_, _) => { lastShadow = null; Refresh(); };
        Closed += (_, _) => model.PropertyChanged -= Changed;
        Refresh();
    }
    // The expanded pill grows by a line when a note such as a failed save is showing under the buttons.
    private double WantedHeight()
    {
        if (!expanded) return CollapsedHeight + 2 * Ui.ShadowMargin;
        // Measured, so there is no dead space whatever the number of accounts or notes showing.
        expansion.Measure(new Size(420, double.PositiveInfinity));
        return CollapsedHeight + expansion.DesiredSize.Height + 2 * Ui.ShadowMargin;
    }
    private (int, bool) expandedKey;
    public void ToggleExpansion()
    {
        expanded = !expanded;
        expansion.IsVisible = expanded;
        expandedKey = (model.Accounts.Count(a => !BreakAccount.Is(a.Name)), saved.IsVisible);
        ApplyHeight();
        if (Screens.ScreenFromWindow(this) is { } screen)
            Position = new PixelPoint(Position.X, Math.Clamp(Position.Y, screen.WorkingArea.Y,
                Math.Max(screen.WorkingArea.Y, screen.WorkingArea.Bottom - (int)(Height * screen.Scaling))));
    }
    // The window takes the height its content needs. Only when the screen is too short to hold it all does the list scroll,
    // so there is never a scroll bar that has nothing to scroll.
    private void ApplyHeight()
    {
        var wanted = WantedHeight(); var limit = wanted;
        if (Screens.ScreenFromWindow(this) is { } screen) limit = screen.WorkingArea.Height / screen.Scaling;
        Height = Math.Min(wanted, limit);
        pillScroll.VerticalScrollBarVisibility = wanted > Height + 1 ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
    private void Refresh()
    {
        // A break or overtime tints the pill alone, so the dashboard stays calm.
        var wanted = model.PillVariant;
        if (!ReferenceEquals(RequestedThemeVariant, wanted)) RequestedThemeVariant = wanted;
        // Only worth a line when something is unusual, so the bottom edge stays just the two buttons.
        saved.Text = model.LastError is not null ? "Save failed; retrying" : model.IsDemo ? "Sample data · demo" : "";
        saved.IsVisible = saved.Text.Length > 0;
        // The expanded pill follows its content: it resizes when the rows or the note under the button change.
        var key = (model.Accounts.Count(a => !BreakAccount.Is(a.Name)), saved.IsVisible);
        if (expanded && key != expandedKey) { expandedKey = key; Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (expanded) ApplyHeight(); }); }
        ToolTip.SetTip(saved, model.LastError);
        account.Text = model.AccountLabel;
        timer.Text = model.ClockText;
        var selected = model.Accounts.FirstOrDefault(a => a.Name == model.SelectedAccount);
        account.Text = model.IsOnBreak ? model.AccountLabel : model.ActiveAccount ?? (selected?.Name ?? model.AccountLabel);
        credited.Text = model.IsTracking && selected is not null ? model.Format(model.Today(selected)) + " today" : model.Status;
        dot.Classes.Set("live", model.IsTracking && !model.IsOnBreak); dot.Classes.Set("break", model.IsOnBreak); dot.Classes.Set("idle", !model.IsTracking);
        Ui.Pulse(dot, model.IsTracking);
        overtime.IsVisible = model.IsOvertime;
        overtime.Text = "OT +" + model.Format(model.Engine.Totals(model.Engine.Now).Overtime);
        pauseGlyph.Playing = model.IsTracking && !model.IsOnBreak; pauseGlyph.InvalidateVisual();
        breakButton.Classes.Set("onBreak", model.IsOnBreak); breakGlyph.Filled = model.IsOnBreak; breakGlyph.SetSteaming(model.IsOnBreak); breakGlyph.InvalidateVisual();
        ToolTip.SetTip(breakButton, model.IsOnBreak ? "End break" + (model.ResumeAccount is { } resumeTo ? " and resume " + resumeTo : "") : "Take a break");
        pause.IsEnabled = model.IsTracking || selected is not null;
        ToolTip.SetTip(pause, model.IsOnBreak ? "End break; drag to move" : model.IsTracking ? "Pause account; drag to move" : "Resume account; drag to move");
        timer.Classes.Set("live", model.IsTracking && !model.IsOnBreak); timer.Classes.Set("break", model.IsOnBreak);
        timer.Classes.Set("idle", !model.IsTracking);
        day.Content = model.DayStarted ? "Finish day" : "Start day";
        // Starting the day is the green main action; finishing it is red, so ending the day is never done by accident.
        day.Classes.Set("finishDay", model.DayStarted); day.Classes.Set("primary", !model.DayStarted);
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
    private Button breakButton = null!, previous = null!, next = null!, overviewTab = null!, hoursTab = null!, entriesTab = null!, addTime = null!;
    private readonly BreakGlyph dashboardCup = new();
    private readonly TextBlock dashboardBreakText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button timeline;
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
        day = Ui.Button("Start day", model.ToggleDay, "DashboardDay"); day.Classes.Add("outlined");
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
        // The break is its own control, apart from the account rows above it, because it stops time going to any account.
        breakButton = Ui.Button("", model.ToggleBreak, "DashboardBreak");
        breakButton.Classes.Add("breakButton"); breakButton.HorizontalAlignment = HorizontalAlignment.Stretch; breakButton.Height = 42;
        breakButton.Content = Ui.Horizontal(dashboardCup, dashboardBreakText);
        summary.Children.Add(breakButton);
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
        // One row of tabs: the day's timeline, then the week's three parts, so there is no second row of tabs.
        timeline = Ui.Button("Timeline", () => SelectTab(false), "TimelineTab"); timeline.Classes.Add("tab");
        overviewTab = Ui.Button("Overview", () => SelectSection("overview"), "WeekOverviewTab"); overviewTab.Classes.Add("tab");
        hoursTab = Ui.Button("Hours table", () => SelectSection("hours"), "WeekHoursTab"); hoursTab.Classes.Add("tab");
        entriesTab = Ui.Button("Entries", () => SelectSection("entries"), "WeekEntriesTab"); entriesTab.Classes.Add("tab");
        Ui.Put(nav, Ui.Horizontal(timeline, overviewTab, hoursTab, entriesTab));
        // In the Week tab the arrows move a whole week, and forward stops at the current week. In Timeline they move a day.
        // A fixed width, so the arrows stay put whatever the date or week text says.
        date.Width = 150; date.TextAlignment = Avalonia.Media.TextAlignment.Center;
        previous = Ui.Button("‹", () => { if (weekMode) model.ChangeWeek(-1); else model.ChangeDate(-1); }, "PreviousPeriod");
        next = Ui.Button("›", () => { if (weekMode) model.ChangeWeek(1); else model.ChangeDate(1); }, "NextPeriod");
        Ui.Put(nav, Ui.Horizontal(previous, date, next), 2);
        Ui.Put(review, nav);
        undo = Ui.Button("Undo", () => model.UndoEdit(), "UndoEdit");
        merge = Ui.Button("Merge", () => model.MergeSelection(), "MergeSelected");
        var more = Ui.Button("Tools", () => { }, "ReviewTools");
        var place = new MenuItem { Header = "Position existing credit…" }; place.Click += (_, _) => new PositionWindow(model).Show(this);
        var group = new MenuItem { Header = "Group entries…" }; group.Click += (_, _) => new GroupWindow(model).Show(this);
        var tidy = new MenuItem { Header = "Tidy day…" }; tidy.Click += (_, _) => new TidyWindow(model).Show(this);
        var export = new MenuItem { Header = "Export week…" }; export.Click += (_, _) => new ExportWindow(model).Show(this);
        more.ContextMenu = new ContextMenu { ItemsSource = new[] { place, group, tidy, export } };
        more.Click += (_, _) => more.ContextMenu.Open(more);
        // Editing buttons sit on the left and only where they apply; Tools stays on the right. Hours and minutes is in Settings.
        addTime = Ui.Button("Add time", () => model.OpenGap(), "AddTime");
        var edits = Ui.Horizontal(addTime, undo, merge);
        var tools = Ui.Grid("*,Auto"); Ui.Put(tools, edits); Ui.Put(tools, more, 1);
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
        // Left and right arrow keys step by a week or a day, like the buttons, unless a box is being typed in.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.KeyModifiers != Avalonia.Input.KeyModifiers.None || e.Key is not (Avalonia.Input.Key.Left or Avalonia.Input.Key.Right)) return;
            if (FocusManager?.GetFocusedElement() is TextBox or ComboBox) return;
            var step = e.Key == Avalonia.Input.Key.Left ? -1 : 1;
            if (weekMode) { if (step < 0 || model.CanStepForwardAWeek) model.ChangeWeek(step); } else model.ChangeDate(step);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
    private void SelectSection(string section)
    {
        weekMode = true;
        if (model.WeekSection != section) model.SetWeekSection(section); else Refresh(true);
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh(e.PropertyName != nameof(TimerModel.Elapsed));
    private void Refresh(bool refreshWorkspace)
    {
        clock.Text = model.ClockText;
        clock.Classes.Set("live", model.IsTracking);
        clock.Classes.Set("idle", !model.IsTracking);
        account.Text = model.IsOnBreak ? (model.ResumeAccount is { } backTo ? "Then back to " + backTo : "No account running") : model.AccountLabel;
        today.Text = model.Engine.Now.ToString("dddd, d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        var british = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        date.Text = weekMode ? "Week of " + model.WeekStart(model.SelectedDate).ToString("d MMM", british) : model.SelectedDate.ToString("ddd d MMM", british);
        next.IsEnabled = !weekMode || model.CanStepForwardAWeek;
        ToolTip.SetTip(previous, weekMode ? "Previous week" : "Previous day"); ToolTip.SetTip(next, weekMode ? "Next week" : "Next day");
        allocated.Text = "Today allocated   " + model.Format(model.Allocated);
        unallocated.Text = "Unallocated   " + model.Format(model.Unallocated);
        day.Content = model.DayStarted ? "Finish day" : "Start day";
        day.Classes.Set("finishDay", model.DayStarted); // Red when it would finish the day; the dashboard keeps it as an outline.
        // Editing buttons show only where they apply: Merge when blocks are selected, and neither on the overview or the hours table.
        var editing = !weekMode || model.WeekSection == "entries";
        addTime.IsVisible = editing; undo.IsVisible = editing; merge.IsVisible = editing && model.SelectedBlockCount > 0;
        merge.IsEnabled = model.CanMergeSelection; ToolTip.SetTip(merge, model.MergeHint);
        undo.Content = "Undo";
        dashboardCup.Filled = model.IsOnBreak; dashboardCup.SetSteaming(model.IsOnBreak); dashboardCup.InvalidateVisual();
        dashboardBreakText.Text = model.IsOnBreak ? "End break" : "Take a break";
        breakButton.Classes.Set("onBreak", model.IsOnBreak);
        ToolTip.SetTip(breakButton, model.IsOnBreak ? "End the break" + (model.ResumeAccount is { } resumeTo ? " and go back to " + resumeTo : "") : "Stop timing any account while you take a break");
        undo.IsEnabled = model.Editor.CanUndo;
        ToolTip.SetTip(undo, model.Editor.UndoLabel is { } label ? $"Undo: {label} ({model.Editor.UndoCount} available)" : "No edit to undo");
        timeline.Classes.Set("selected", !weekMode);
        overviewTab.Classes.Set("selected", weekMode && model.WeekSection == "overview");
        hoursTab.Classes.Set("selected", weekMode && model.WeekSection == "hours");
        entriesTab.Classes.Set("selected", weekMode && model.WeekSection == "entries");
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
