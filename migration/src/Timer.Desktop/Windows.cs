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
    public bool HideToTray { get => hideToTray; set { hideToTray = value; ApplyTaskbar(); } }
    private bool hideToTray;
    private void ApplyTaskbar()
    {
        // Only a timer that can hide in the tray may leave the taskbar.
        var show = model.TaskbarButton || !hideToTray;
        if (Pill.ShowInTaskbar != show) Pill.ShowInTaskbar = show;
        if (Dashboard.ShowInTaskbar != show) Dashboard.ShowInTaskbar = show;
    }
    public bool IsShown => Pill.IsVisible || Dashboard.IsVisible;
    public WindowCoordinator(TimerModel model, Action exit)
    {
        this.model = model;
        Pill = new PillWindow(model);
        Dashboard = new DashboardWindow(model);
        // A taskbar button by default, so the timer can be pinned and launched like any app. It can be switched off to run from the tray alone.
        ApplyTaskbar();
        model.PropertyChanged += (_, _) => ApplyTaskbar();
        Pill.OpenDashboard = () =>
        {
            Fit(Dashboard, Pill, DashboardWindow.DashboardWidth + 2 * Ui.ShadowMargin, DashboardWindow.DashboardHeight + 2 * Ui.ShadowMargin);
            Dashboard.Show();
            Pill.Collapse(); // So it is a capsule again when you come back.
            Pill.Hide();
            Dashboard.Activate();
        };
        Dashboard.OpenPill = () => { Pill.Collapse(); Pill.Show(); Dashboard.Hide(); Pill.Activate(); };
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
        if (!Dashboard.IsVisible) { Fit(Dashboard, Pill, DashboardWindow.DashboardWidth + 2 * Ui.ShadowMargin, DashboardWindow.DashboardHeight + 2 * Ui.ShadowMargin); Dashboard.Show(); }
        Pill.Collapse(); Pill.Hide(); BringToFront(Dashboard);
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
            Pill.FitToScreen(screen);
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
    private readonly TextBlock account = Ui.Text("", 14);
    private readonly ClockText timer = new(42, 26) { Text = "00:00:00" };
    private readonly TextBlock pillUnallocated = Ui.Text("", 13, "muted");
    private readonly StackPanel expansion = new() { IsVisible = false, Spacing = 8 };
    private readonly Button day;
    private readonly IconGlyph pillDayFlag = new(IconKind.Flag);
    private readonly TextBlock pillDayText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private bool expanded;
    public const double CollapsedHeight = 82, PillWidth = 516, CapsuleRadius = 41, CardRadius = 28;
    public static double CollapsedWindowHeight => CollapsedHeight + 2 * Ui.ShadowMargin;
    // The whole pill is drawn smaller than its design size on every screen, and smaller again on small ones, so it never takes a big share of a display.
    // Its width is a 1800th of the screen's width in device independent pixels, never above 85 per cent and never below 70 per cent of the design size.
    public const double ScreenWidthDivisor = 1800, LargestScale = 0.85, SmallestScale = 0.7;
    public static double ScaleFor(double screenWidth) => Math.Clamp(screenWidth / ScreenWidthDivisor, SmallestScale, LargestScale);
    private readonly Avalonia.Media.ScaleTransform pillScale = new(1, 1);
    public double Scale => pillScale.ScaleX;
    /// <summary>The window height of a closed pill at the size it is drawn on this screen.</summary>
    public double CollapsedWindowHeightNow => CollapsedWindowHeight * Scale;
    /// <summary>Sizes the pill for the screen it is on. The window keeps the pill's proportions, so the content only needs the scale.</summary>
    public void FitToScreen(Avalonia.Platform.Screen? screen)
    {
        var scale = screen is null ? 1 : ScaleFor(screen.WorkingArea.Width / screen.Scaling);
        if (Math.Abs(scale - pillScale.ScaleX) < 0.001 && Width > 0) { ApplyWidth(); return; }
        pillScale.ScaleX = scale; pillScale.ScaleY = scale;
        ApplyWidth(); if (IsLoaded || IsVisible) ApplyHeight();
    }
    private void ApplyWidth() => Width = (PillWidth + 2 * Ui.ShadowMargin) * pillScale.ScaleX;
    private readonly TextBlock credited = Ui.Text("", 13, "muted");
    private readonly Avalonia.Controls.Shapes.Ellipse dot = new() { Width = 16, Height = 16, StrokeThickness = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly Chevron chevron = new() { VerticalAlignment = VerticalAlignment.Center };
    private Border surfaceBorder = null!;
    private readonly Button pause;
    private readonly PauseGlyph pauseGlyph = new();
    private readonly BreakGlyph breakGlyph = new();
    private Button breakButton = null!; private bool pillHoverBreak;
    private readonly TextBlock saved = Ui.Text("", 11, "muted");
    private readonly Grid progressBar = new();
    private Border progressFill = null!, shadowHost = null!;
    private ScrollViewer pillScroll = null!;
    private (bool, string?, Avalonia.Styling.ThemeVariant)? lastShadow;
    public PillWindow(TimerModel model)
    {
        this.model = model;
        Title = "Basic’s Advanced Timer"; WindowChrome.Attach(this);
        ContextMenu = Ui.SettingsMenu(model);
        Width = PillWidth + 2 * Ui.ShadowMargin;
        Height = CollapsedHeight + 2 * Ui.ShadowMargin;
        CanResize = false;
        Topmost = true;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Avalonia.Media.Brushes.Transparent;
        var root = Ui.Grid("*", "82,*");
        var head = Ui.Grid("Auto,1,*,Auto,Auto", "*");
        head.Margin = new Thickness(24, 0, 20, 0);
        var timerButton = Ui.Button("", () => OpenDashboard?.Invoke(), "PillDashboard");
        // A small unallocated figure sits under the clock, only while the day is running and there is some.
        var timerColumn = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        pillUnallocated.HorizontalAlignment = HorizontalAlignment.Left; pillUnallocated.IsVisible = false;
        timerColumn.Children.Add(timer); timerColumn.Children.Add(pillUnallocated);
        timerButton.Content = timerColumn;
        timerButton.Classes.Add("pillAction");
        ToolTip.SetTip(timerButton, "Click for dashboard; drag to move");
        Ui.Put(head, timerButton).Margin = new Thickness(0, 0, 16, 0);
        var divider = new Border { Width = 1, Height = 42 }; divider.Classes.Add("rule");
        Ui.Put(head, divider, 1);
        // The account, with its colour dot and a chevron that opens the list, the line under it that says what is happening, and how far through the day it is.
        account.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis; account.FontWeight = Avalonia.Media.FontWeight.SemiBold;
        credited.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        nameRow.Children.Add(dot); nameRow.Children.Add(account); nameRow.Children.Add(chevron);
        // How far through the working day it is, as a thin bar under the account.
        progressBar.Height = 5; progressBar.Margin = new Thickness(0, 7, 0, 0);
        progressBar.ColumnDefinitions = new ColumnDefinitions("0*,1*");
        var fill = new Border { Name = "DayProgressFill", CornerRadius = new CornerRadius(3) };
        var rest = new Border { CornerRadius = new CornerRadius(3), Opacity = .9 }; rest.Classes.Add("rule");
        progressFill = fill; Grid.SetColumn(fill, 0); Grid.SetColumn(rest, 1);
        progressBar.Children.Add(rest); progressBar.Children.Add(fill);
        progressBar.Name = "DayProgress"; progressBar.IsHitTestVisible = false;
        var labels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(nameRow); labels.Children.Add(credited); labels.Children.Add(progressBar);
        var accountButton = Ui.Button("", ToggleExpansion, "PillAccounts");
        accountButton.Content = labels;
        accountButton.Classes.Add("pillAction");
        accountButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        accountButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(accountButton, "Click for accounts; drag to move");
        Ui.Put(head, accountButton, 2).Margin = new Thickness(16, 0, 16, 0);
        breakButton = Ui.Button("", model.ToggleBreak, "PillBreak");
        breakGlyph.Mono = true; // Plain text colour at rest, and the accent only under the pointer.
        breakButton.Content = new Viewbox { Width = 28, Height = 28, Child = breakGlyph }; breakButton.Classes.Add("pillRound"); breakButton.Classes.Add("bare");
        breakButton.Margin = new Thickness(0, 0, 10, 0);
        breakButton.PointerEntered += (_, _) => { pillHoverBreak = true; breakGlyph.Hot = true; breakGlyph.SetSteaming(true); };
        breakButton.PointerExited += (_, _) => { pillHoverBreak = false; breakGlyph.Hot = false; breakGlyph.SetSteaming(model.IsOnBreak); };
        Ui.Put(head, breakButton, 3);
        pause = Ui.Button("", model.TogglePause, "PillPause");
        pause.Content = new Viewbox { Width = 22, Height = 22, Child = pauseGlyph };
        pause.PointerEntered += (_, _) => pauseGlyph.Glow = 1;
        pause.PointerExited += (_, _) => pauseGlyph.Glow = 0;
        pause.Classes.Add("pillRound");
        Ui.Put(head, pause, 4);
        Ui.PillGestures(head, this);
        Ui.Put(root, head);
        expansion.Children.Add(Ui.Rule());
        expansion.Children.Add(new AccountList(model));
        day = Ui.Button("", model.ToggleDay, "PillDay"); day.Classes.Add("outlined"); day.Classes.Add("quiet");
        day.Content = Ui.Horizontal(pillDayFlag, pillDayText);
        day.PointerEntered += (_, _) => pillDayFlag.Waving = true; day.PointerExited += (_, _) => pillDayFlag.Waving = false; // The flag waves under the pointer.
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
        shadowHost = Ui.Shadowed(surface, CapsuleRadius); surfaceBorder = surface;
        Content = new Avalonia.Controls.LayoutTransformControl { LayoutTransform = pillScale, Child = shadowHost };
        Opened += (_, _) => FitToScreen(Screens.ScreenFromWindow(this));
        // The capsule changes colour with what is happening, and grows from a capsule into a rounded card as it opens, both eased.
        var ease = TimeSpan.FromMilliseconds(260);
        surface.Transitions = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.BrushTransition { Property = Border.BackgroundProperty, Duration = ease },
            new Avalonia.Animation.CornerRadiusTransition { Property = Border.CornerRadiusProperty, Duration = ease }
        };
        shadowHost.Transitions!.Add(new Avalonia.Animation.CornerRadiusTransition { Property = Border.CornerRadiusProperty, Duration = ease });
        model.PropertyChanged += Changed;
        ActualThemeVariantChanged += (_, _) => { lastShadow = null; Refresh(); };
        Closed += (_, _) => model.PropertyChanged -= Changed;
        Refresh();
    }
    private Avalonia.Media.Color ThemeColourOr(string key, string fallback) => Ui.ThemeColour(this, key, Avalonia.Media.Color.Parse(fallback));
    // The expanded pill grows by a line when a note such as a failed save is showing under the buttons.
    private double WantedHeight()
    {
        if (!expanded) return CollapsedHeight + 2 * Ui.ShadowMargin;
        // Measured, so there is no dead space whatever the number of accounts or notes showing.
        expansion.Measure(new Size(PillWidth, double.PositiveInfinity));
        return CollapsedHeight + expansion.DesiredSize.Height + 2 * Ui.ShadowMargin;
    }
    private (int, bool) expandedKey;
    /// <summary>Closes the list if it is open, so the pill is always a plain capsule when it comes back from the dashboard.</summary>
    public void Collapse() { if (expanded) ToggleExpansion(); }
    public void ToggleExpansion()
    {
        expanded = !expanded;
        expansion.IsVisible = expanded;
        chevron.Expanded = expanded; chevron.InvalidateVisual();
        var radius = new CornerRadius(expanded ? CardRadius : CapsuleRadius);
        surfaceBorder.CornerRadius = radius; shadowHost.CornerRadius = radius;
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
        Height = Math.Min(wanted * Scale, limit);
        pillScroll.VerticalScrollBarVisibility = wanted * Scale > Height + 1 ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
    private void Refresh()
    {
        // A break or overtime tints the pill alone, so the dashboard stays calm.
        var wanted = model.PillVariant;
        if (!ReferenceEquals(RequestedThemeVariant, wanted)) RequestedThemeVariant = wanted;
        // Only worth a line when something is unusual, so the bottom edge stays just the two buttons.
        saved.Text = model.LastError is not null ? "Save failed; retrying" : "";
        saved.IsVisible = saved.Text.Length > 0;
        // The expanded pill follows its content: it resizes when the rows or the note under the button change.
        var key = (model.Accounts.Count(a => !BreakAccount.Is(a.Name)), saved.IsVisible);
        if (expanded && key != expandedKey) { expandedKey = key; Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (expanded) ApplyHeight(); }); }
        ToolTip.SetTip(saved, model.LastError);
        timer.Text = model.ClockText;
        // Green while tracking, with the seconds a lighter green. The break and overtime capsules wear their own colour, so their text stays plain.
        var pillBreak = model.IsOnBreak; var pillLate = model.IsOvertime && !pillBreak; var pillLive = model.IsTracking && !pillBreak && !pillLate;
        timer.SetState(pillBreak, pillLate, pillLive);
        credited.Classes.Set("live", pillLive); // The line saying what is happening is green too while tracking.
        var open = model.Unallocated;
        pillUnallocated.IsVisible = model.DayStarted && open >= TimeSpan.FromMinutes(1);
        pillUnallocated.Text = pillUnallocated.IsVisible ? model.Format(open) + " unallocated" : "";
        var selected = model.Accounts.FirstOrDefault(a => a.Name == model.SelectedAccount);
        account.Text = model.IsOnBreak ? model.AccountLabel : model.ActiveAccount ?? (selected?.Name ?? model.AccountLabel);
        // What is happening, in one line. A break says where it will go back to, overtime adds how much there is, and an idle timer says what it is waiting for.
        var overtimeText = model.IsOvertime ? " · OT " + model.Format(model.Engine.Totals(model.Engine.Now).Overtime) : "";
        // On a break the first line already says so, so the second says something new: where it goes back to, or when the break began.
        var breakSince = model.Engine.Active is { } running && BreakAccount.Is(running.Account) ? running.Start : (DateTime?)null;
        credited.Text = model.IsOnBreak ? (model.ResumeAccount is { } returnTo ? "Returns to " + returnTo : breakSince is { } since ? "Since " + since.ToString("HH:mm") : "Nothing running") + overtimeText
            : model.IsTracking && selected is not null ? "Today " + model.Format(model.Today(selected)) + overtimeText : model.Status + overtimeText;
        // The dot is the account's own colour, with a ring in the pill's text colour so it shows on any capsule. A break is amber and an idle timer is grey.
        var shown = model.Accounts.FirstOrDefault(a => a.Name == (model.IsOnBreak ? null : model.ActiveAccount ?? selected?.Name));
        var dotColour = model.IsOnBreak ? ThemeColourOr("BudgetWarning", "#F2B84B") : !model.IsTracking ? ThemeColourOr("Muted", "#8A8F98") : shown?.Colour is { } own ? Avalonia.Media.Color.Parse(Ui.Visible(own)) : ThemeColourOr("Accent", "#F27A0D");
        dot.Fill = new Avalonia.Media.SolidColorBrush(dotColour); dot.Stroke = this.TryFindResource("Ink", ActualThemeVariant, out var ring) && ring is Avalonia.Media.IBrush ringBrush ? ringBrush : Avalonia.Media.Brushes.Gray;
        Ui.Pulse(dot, model.IsTracking);
        pauseGlyph.Playing = model.IsTracking && !model.IsOnBreak; pauseGlyph.InvalidateVisual();
        breakButton.Classes.Set("onBreak", model.IsOnBreak); breakGlyph.Filled = false; breakGlyph.HotKey = model.ThemeState == "Recording" ? "Accent" : "Break"; breakGlyph.SetSteaming(model.IsOnBreak || pillHoverBreak); breakGlyph.InvalidateVisual();
        ToolTip.SetTip(breakButton, model.IsOnBreak ? "Finish break" + (model.ResumeAccount is { } resumeTo ? " and resume " + resumeTo : "") : "Start break");
        pause.IsEnabled = model.IsTracking || selected is not null;
        ToolTip.SetTip(pause, model.IsOnBreak ? "End break; drag to move" : model.IsTracking ? "Pause account; drag to move" : "Resume account; drag to move");
        pillDayText.Text = model.DayStarted ? "Finish day" : "Start day";
        // The same quiet outline as the dashboard's button, with a flag that is green to start the day and red to finish it, so ending the day is never done by accident.
        pillDayFlag.TintKey = model.DayStarted ? "Finish" : "Start";
        day.Classes.Set("finishDay", model.DayStarted); day.Classes.Set("startDay", !model.DayStarted);
        var progress = model.DayProgress;
        // The line is always there, so the pill never changes height or leaves a gap: on a day off it is simply an empty track.
        progressBar.IsVisible = true;
        var done = progress ?? 0.0;
        progressBar.ColumnDefinitions[0].Width = new GridLength(done, GridUnitType.Star);
        progressBar.ColumnDefinitions[1].Width = new GridLength(1.0 - done, GridUnitType.Star);
        progressFill.Classes.Set("progressOvertime", model.IsOvertime && !model.IsOnBreak); progressFill.Classes.Set("progressBreak", model.IsOnBreak);
        ToolTip.SetTip(progressBar, progress is null ? "No working day today. The line fills as a working day goes by." : $"{(int)(done * 100)}% of the working day");
        // The capsule's own colour says what is happening, so the pill keeps the ordinary shadow in every state.
        var shadow = (model.Shadows, (string?)null, ActualThemeVariant);
        if (lastShadow != shadow) { lastShadow = shadow; Ui.SetShadow(shadowHost, model.Shadows, null); }
    }
}

public sealed class DashboardWindow : Window
{
    /// <summary>The dashboard's fixed size, before the room kept around it for its shadow.</summary>
    public const double DashboardWidth = 1080, DashboardHeight = 680;
    /// <summary>The height of the Start break and Finish day buttons, and of the bar under the timeline.</summary>
    public const double ControlBarHeight = 42;
    public Action? OpenPill { get; set; }
    private readonly TimerModel model;
    private readonly ClockText clock = new(46, 26) { Text = "00:00:00" };
    private readonly TextBlock workspaceTitle = new() { FontSize = 26, Classes = { "clock" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly Avalonia.Controls.Shapes.Ellipse accountDot = new() { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
    private Button todayButton = null!;
    private readonly TextBlock account = Ui.Text("", 16);
    private Border zoomControls = new(); private Control? identityBlock, accountsHeadBlock, toolsRow; private Button zoomOut = new(), zoomIn = new();
    private readonly TextBlock allocatedValue = new TextBlock { FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private readonly TextBlock unallocatedValue = new TextBlock { FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private readonly TextBlock dayText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private readonly IconGlyph dayFlag = new(IconKind.Flag);
    private bool hoverBreak;
    private readonly TextBlock date = Ui.Text("", 14);
    private readonly TextBlock today = Ui.Text("", 12, "muted");
    private readonly ContentControl workspace = new();
    private readonly Button day;
    private Button timelineRail = null!, breakButton = null!, previous = null!, next = null!, overviewTab = null!, hoursTab = null!, entriesTab = null!, addTime = null!;
    private readonly BreakGlyph dashboardCup = new();
    private readonly TextBlock dashboardBreakText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private readonly Button undo;
    private readonly Button merge;
    private EntryWindow? entryEditor;
    private bool weekMode, settingsMode, renderedSettings, settingZoom;
    private Slider zoomSlider = null!;
    private Button settingsRail = null!; private Control? headerRow;
    private readonly Grid layers = new(); private Control? dashboardBody; private OnboardingView? onboarding;
    private bool? renderedWeekMode;
    private DateTime? renderedDate;
    public DashboardWindow(TimerModel model)
    {
        this.model = model;
        Title = "Basic’s Advanced Timer"; WindowChrome.Attach(this);
        ContextMenu = Ui.SettingsMenu(model);
        Width = DashboardWidth + 2 * Ui.ShadowMargin;
        Height = DashboardHeight + 2 * Ui.ShadowMargin;
        Topmost = true; // The dashboard stays in front of other windows, as the pill does.
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Avalonia.Media.Brushes.Transparent;
        // A narrow rail of icons picks the view and holds settings, then the day lives in the left column (the Day elapsed card, the accounts,
        // and the break and day buttons at the foot). The right column is a titled workspace with the date to step through.
        var root = Ui.Grid("60,*", "*");
        var rail = new Border { Width = 60 }; rail.Classes.Add("panel");
        var railGrid = Ui.Grid("*", "*,Auto");
        var railTop = new StackPanel { Margin = new Thickness(0, 18, 0, 0), Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        Button RailButton(IconKind kind, string tip, string name, Action click)
        {
            var button = Ui.Button("", click, name); button.Classes.Add("rail"); button.Content = new IconGlyph(kind);
            ToolTip.SetTip(button, tip); Avalonia.Automation.AutomationProperties.SetName(button, tip); return button;
        }
        timelineRail = RailButton(IconKind.Timeline, "Timeline", "TimelineTab", () => SelectTab(false));
        overviewTab = RailButton(IconKind.Week, "Week overview", "WeekOverviewTab", () => SelectSection("overview"));
        hoursTab = RailButton(IconKind.Table, "Hours table", "WeekHoursTab", () => SelectSection("hours"));
        entriesTab = RailButton(IconKind.Entries, "Entries", "WeekEntriesTab", () => SelectSection("entries"));
        foreach (var button in new[] { timelineRail, overviewTab, hoursTab, entriesTab }) railTop.Children.Add(button);
        Ui.Put(railGrid, railTop);
        var railFoot = new StackPanel { Margin = new Thickness(0, 0, 0, 14), Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        railFoot.Children.Add(RailButton(IconKind.Minimise, "Back to pill", "BackToPill", () => OpenPill?.Invoke()));
        settingsRail = RailButton(IconKind.Gear, "Settings", "Settings", ShowSettings);
        railFoot.Children.Add(settingsRail);
        Ui.Put(railGrid, railFoot, row: 1);
        rail.Child = railGrid; Ui.Put(root, rail);
        Ui.DragHeader(railTop, this);
        var body = Ui.Grid("340,1,*", "*");
        var sidebar = Ui.Grid("*", "Auto,Auto,*,Auto");
        // The Day elapsed card, with a bar of the accent colour along its top edge.
        var dayCard = new Border { CornerRadius = new CornerRadius(18), ClipToBounds = true, BorderThickness = new Thickness(1) }; dayCard.Classes.Add("dayCard");
        var cardStack = new StackPanel();
        var accentEdge = new Border { Height = 4 }; accentEdge.Classes.Add("accentEdge");
        cardStack.Children.Add(accentEdge);
        var identity = new StackPanel { Margin = new Thickness(18, 14, 18, 16), Spacing = 2 };
        var topRow = Ui.Grid("*,Auto");
        Ui.Put(topRow, Ui.Text("Day elapsed", 12, "muted")); Ui.Put(topRow, today, 1);
        identity.Children.Add(topRow); identity.Children.Add(clock);
        var live = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 12) };
        live.Children.Add(Ui.Horizontal(accountDot, account)); live.Children.Add(new TrackingLabels(model));
        identity.Children.Add(live);
        // A hairline keeps the clock and who is running apart from the day's totals under it.
        var cardRule = Ui.Rule(); cardRule.Margin = new Thickness(0, 0, 0, 10); identity.Children.Add(cardRule);
        // Two figures side by side, a small label over each, with a hairline between them.
        var stats = Ui.Grid("*,1,*");
        var allocatedStat = new StackPanel { Spacing = 1 }; allocatedStat.Children.Add(Ui.Text("Allocated", 12, "muted")); allocatedStat.Children.Add(allocatedValue);
        var unallocatedStat = new StackPanel { Spacing = 1, Margin = new Thickness(16, 0, 0, 0) }; unallocatedStat.Children.Add(Ui.Text("Unallocated", 12, "muted")); unallocatedStat.Children.Add(unallocatedValue);
        var statDivider = new Border { Width = 1, Margin = new Thickness(0, 2) }; statDivider.Classes.Add("rule");
        Ui.Put(stats, allocatedStat); Ui.Put(stats, statDivider, 1); Ui.Put(stats, unallocatedStat, 2);
        identity.Children.Add(stats);
        cardStack.Children.Add(identity); dayCard.Child = cardStack;
        var identityHost = new Border { Padding = new Thickness(12, 18, 16, 6), Child = dayCard };
        Ui.DragHeader(identityHost, this, "SidebarDragHeader"); identityBlock = identityHost;
        Ui.Put(sidebar, identityHost);
        var title = Ui.Text("Client accounts", 13, "muted");
        title.Margin = new Thickness(20, 12, 20, 6);
        // A rule and a heading set the accounts apart from the card above them.
        var titleRule = Ui.Rule(); titleRule.Margin = new Thickness(16, 0);
        // "Today" sits over the bold daily totals, since each row also shows a weekly or monthly figure and the two could be mistaken for each other.
        var todayHead = Ui.Text("Today", 11, "muted"); todayHead.HorizontalAlignment = HorizontalAlignment.Right; todayHead.VerticalAlignment = VerticalAlignment.Bottom;
        todayHead.Margin = new Thickness(0, 12, 58, 6); todayHead.Name = "TodayHeading";
        var headLine = Ui.Grid("*,Auto"); Ui.Put(headLine, title); Ui.Put(headLine, todayHead, 1);
        var accountsHead = new StackPanel(); accountsHead.Children.Add(titleRule); accountsHead.Children.Add(headLine); accountsHeadBlock = accountsHead;
        Ui.Put(sidebar, accountsHead, row: 1);
        Ui.Put(sidebar, new ScrollViewer { Name = "AccountScroll", Content = new AccountList(model, reorderable: true, bars: true), AllowAutoHide = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, row: 2);
        var summary = new StackPanel { Margin = new Thickness(18, 12, 18, 18), Spacing = 10 };
        // The break is its own control, apart from the account rows above it, because it stops time going to any account.
        breakButton = Ui.Button("", model.ToggleBreak, "DashboardBreak");
        breakButton.Classes.Add("breakButton"); breakButton.Classes.Add("quiet"); breakButton.HorizontalAlignment = HorizontalAlignment.Stretch; breakButton.Height = ControlBarHeight;
        breakButton.Content = Ui.Horizontal(dashboardCup, dashboardBreakText);
        breakButton.PointerEntered += (_, _) => { hoverBreak = true; dashboardCup.SetSteaming(true); };
        breakButton.PointerExited += (_, _) => { hoverBreak = false; dashboardCup.SetSteaming(model.IsOnBreak || hoverBreak); }; // Steam rises under the pointer too.
        day = Ui.Button("", model.ToggleDay, "DashboardDay"); day.Classes.Add("outlined"); day.Classes.Add("quiet");
        day.HorizontalAlignment = HorizontalAlignment.Stretch; day.Height = ControlBarHeight; day.Content = Ui.Horizontal(dayFlag, dayText);
        day.PointerEntered += (_, _) => dayFlag.Waving = true; day.PointerExited += (_, _) => dayFlag.Waving = false; // The flag waves under the pointer.
        // Side by side, so the pair takes one row at the foot of the column.
        var pair = Ui.Grid("*,*"); breakButton.Margin = new Thickness(0, 0, 4, 0); day.Margin = new Thickness(4, 0, 0, 0);
        breakButton.Padding = new Thickness(6, 0); day.Padding = new Thickness(6, 0);
        Ui.Put(pair, breakButton); Ui.Put(pair, day, 1);
        summary.Children.Add(pair);
        Ui.Put(sidebar, summary, row: 3);
        var panel = new Border { Child = sidebar };
        panel.Classes.Add("panel");
        Ui.Put(body, panel);
        var divider = new Border(); divider.Classes.Add("rule"); Ui.Put(body, divider, 1);
        var review = Ui.Grid("*", "Auto,*,Auto");
        review.Margin = new Thickness(22, 16, 22, 18);
        // The workspace's name on the left; on the right the date to step through and a button back to today.
        // In Week views the arrows move a whole week, and forward stops at the current week. In Timeline they move a day.
        // The date has a fixed width, so the arrows stay put whatever its text says.
        var nav = Ui.Grid("*,Auto"); nav.VerticalAlignment = VerticalAlignment.Center;
        Ui.Put(nav, workspaceTitle); headerRow = nav;
        date.Width = 112; date.TextAlignment = Avalonia.Media.TextAlignment.Center;
        previous = Ui.Button("‹", () => { if (weekMode) model.ChangeWeek(-1); else model.ChangeDate(-1); }, "PreviousPeriod"); previous.Classes.Add("iconButton"); previous.Width = 30; previous.FontSize = 20;
        next = Ui.Button("›", () => { if (weekMode) model.ChangeWeek(1); else model.ChangeDate(1); }, "NextPeriod"); next.Classes.Add("iconButton"); next.Width = 30; next.FontSize = 20;
        todayButton = Ui.Button("Today", () => model.ChangeDate((model.Engine.Now.Date - model.SelectedDate.Date).Days), "GoToToday"); todayButton.Classes.Add("outlined"); todayButton.Height = 34; todayButton.Padding = new Thickness(14, 4); todayButton.Margin = new Thickness(10, 0, 0, 0);
        Ui.Put(nav, Ui.Horizontal(previous, date, next, todayButton), 1);
        Ui.Put(review, nav);
        undo = Ui.Button("", () => model.UndoEdit(), "UndoEdit");
        merge = Ui.Button("", () => model.MergeSelection(), "MergeSelected");
        // Editing buttons sit on the left and only where they apply; Tools stays on the right. Hours and minutes is in Settings.
        addTime = Ui.Button("", () => model.OpenGap(), "AddTime");
        // The same quiet outlined look as Start break and Finish day, each with a small icon: a plus in the accent colour, and plain ones for the rest.
        (Button Button, IconKind Icon, string Words, string? Tint)[] toolSpecs = { (addTime, IconKind.Plus, "Add time", "Accent"), (undo, IconKind.Undo, "Undo", null), (merge, IconKind.Merge, "Merge", null) };
        foreach (var (toolButton, icon, words, tint) in toolSpecs)
        {
            toolButton.Classes.Add("toolButton"); toolButton.Classes.Add("quiet");
            toolButton.Content = Ui.Horizontal(new IconGlyph(icon) { TintKey = tint }, new TextBlock { Text = words, VerticalAlignment = VerticalAlignment.Center, FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold });
            Avalonia.Automation.AutomationProperties.SetName(toolButton, words);
        }
        addTime.Classes.Add("addButton");
        var edits = Ui.Horizontal(addTime, undo, merge);
        // Zoom and fit sit beside Tools in this row, applying to the timeline underneath. They show only on the Timeline tab.
        ScrollViewer? TimelineScroll() => workspace.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Name == "TimelineScroll");
        zoomOut = Ui.IconButton(IconKind.ZoomOut, "Zoom out", "TimelineZoomOut", () => { if (TimelineScroll() is { } s) model.ZoomTimeline(-1, s.Viewport.Height / 2, s.Offset.Y); });
        zoomIn = Ui.IconButton(IconKind.ZoomIn, "Zoom in (or hold Ctrl and use the mouse wheel)", "TimelineZoomIn", () => { if (TimelineScroll() is { } s) model.ZoomTimeline(1, s.Viewport.Height / 2, s.Offset.Y); });
        var fit = Ui.IconButton(IconKind.FitDay, "Fit the day to the window", "TimelineFit", () => { if (TimelineScroll() is { } s) model.FitTimeline(s.Viewport.Height); });
        // The zoom controls sit in a bar under the timeline: fit the day, zoom out, a slider, zoom in. They show only on the Timeline view.
        zoomSlider = new Slider { Name = "TimelineZoom", Minimum = 0, Maximum = 1, Width = 130, VerticalAlignment = VerticalAlignment.Center, Value = model.TimelineZoomFraction,
            // The standard slider draws its track a little below the middle of its own box, so the box is lifted by the same amount (drawing only, no layout change) to put the track on the bar's centre line.
            RenderTransform = new Avalonia.Media.TranslateTransform(0, -5) };
        Avalonia.Automation.AutomationProperties.SetName(zoomSlider, "Zoom"); ToolTip.SetTip(zoomSlider, "Zoom the timeline");
        zoomSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty || settingZoom) return;
            if (TimelineScroll() is { } s) model.SetTimelineScale(TimerModel.ScaleAt(zoomSlider.Value), s.Viewport.Height / 2, s.Offset.Y);
        };
        var zoomBar = new Border { Name = "TimelineControls", Child = Ui.Horizontal(fit, zoomOut, zoomSlider, zoomIn), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 0), Height = ControlBarHeight, BorderThickness = new Thickness(1), };
        ((StackPanel)zoomBar.Child).Spacing = 4; zoomBar.Classes.Add("chip"); zoomControls = zoomBar;
        // Add time, Undo and Merge sit on the left of a bar under the workspace, and the zoom controls on its right, so the working area has the whole height above.
        // The bar is as tall as the Start break and Finish day buttons beside it, so the two bottom rows line up. Its buttons fill that height.
        addTime.Height = ControlBarHeight; undo.Height = ControlBarHeight; merge.Height = ControlBarHeight;
        var tools = Ui.Grid("*,Auto"); toolsRow = tools; Ui.Put(tools, edits); Ui.Put(tools, zoomBar, 1);
        tools.Margin = new Thickness(0, 12, 0, 0); zoomBar.Margin = new Thickness(12, 0, 0, 0); // Clear space between Merge and the zoom bar.
        workspace.Margin = new Thickness(0, 14, 0, 0); // Room under the title and date, so content never touches them.
        Ui.Put(review, workspace, row: 1);
        Ui.Put(review, tools, row: 2);
        // The title row's empty middle still drags the window.
        var rightColumn = Ui.Grid("*", "*,Auto");
        Ui.Put(rightColumn, review); Ui.DragHeader(nav, this);
        Ui.Put(body, rightColumn, 2);
        Ui.Put(root, body, 1);
        // The bottom line appears only when there is something to say: a failed save, an edit that was refused, or the one-off note
        // about old files. Otherwise it takes no room.
        string? FooterText() => model.LastError ?? model.EditError ?? model.Notice;
        var footer = Ui.Text(FooterText() ?? "", 11, "muted"); footer.IsVisible = FooterText() is not null;
        model.PropertyChanged += (_, _) => { var text = FooterText(); footer.Text = text ?? ""; footer.IsVisible = text is not null; };
        footer.Margin = new Thickness(24, 10);
        // Messages sit under the review only, so they never change the height of the left column.
        footer.Margin = new Thickness(22, 4, 22, 10); Ui.Put(rightColumn, footer, row: 1);
        // The setup overlay, when it is wanted, sits over everything in the card, which is blurred and cannot be clicked while it is up.
        layers.Children.Add(root); dashboardBody = root;
        var surface = new Border { Name = "DashboardSurface", Child = layers, ClipToBounds = true };
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
        // The window can be picked up by any bare part of its top: the whole identity block and the accounts heading on the left, and the tab
        // and action rows on the right, not only the thin strips between the controls. Buttons, boxes and the timeline itself are left alone.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !Ui.IsPlainSurface(e.Source) || !InDragZone(e.GetPosition(this))) return;
            Ui.CloseMenus(this); BeginMoveDrag(e); e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Left and right arrow keys step by a week or a day, like the buttons, unless a box is being typed in.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (settingsMode || e.KeyModifiers != Avalonia.Input.KeyModifiers.None || e.Key is not (Avalonia.Input.Key.Left or Avalonia.Input.Key.Right)) return;
            if (FocusManager?.GetFocusedElement() is TextBox or ComboBox) return;
            var step = e.Key == Avalonia.Input.Key.Left ? -1 : 1;
            if (weekMode) { if (step < 0 || model.CanStepForwardAWeek) model.ChangeWeek(step); } else model.ChangeDate(step);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) { model.ClearSelection(); e.Handled = true; }
            // Ctrl+C copies the selected blocks and Ctrl+V pastes them on the timeline, but never while typing in a text box.
            else if (e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && e.Key is Avalonia.Input.Key.C or Avalonia.Input.Key.V && !settingsMode && FocusManager?.GetFocusedElement() is not TextBox)
            {
                if (e.Key == Avalonia.Input.Key.C) model.CopySelection(); else if (!weekMode) model.PasteBlocks(model.DefaultPasteTime());
                e.Handled = true;
            }
            // Backspace and Delete remove the selected blocks, but never while typing in a text box.
            else if (e.Key is Avalonia.Input.Key.Back or Avalonia.Input.Key.Delete && FocusManager?.GetFocusedElement() is not TextBox)
            { if (model.SelectedRows.Count > 0) { model.DeleteSelection(); e.Handled = true; } }
        };
        model.PropertyChanged += Changed;
        model.PropertyChanged += (_, _) => UpdateOnboarding();
        model.EntryRequested += ShowEntry;
        model.GroupRequested += ShowGroup; model.TidyRequested += ShowTidy;
        Closed += (_, _) => { model.PropertyChanged -= Changed; model.EntryRequested -= ShowEntry; model.GroupRequested -= ShowGroup; model.TidyRequested -= ShowTidy; };
        Refresh(true);
        UpdateOnboarding();
    }
    /// <summary>Whether the getting started setup is showing over the dashboard.</summary>
    public bool SetupShowing => onboarding is not null;
    // The setup shows for as long as the model says it is not done, over a blurred dashboard that cannot be clicked, and goes when it is finished or skipped.
    private void UpdateOnboarding()
    {
        var wanted = !model.SetupDone;
        if (wanted && onboarding is null && dashboardBody is not null)
        {
            onboarding = new OnboardingView(model);
            onboarding.Finished += () => { model.CompleteSetup(); UpdateOnboarding(); };
            dashboardBody.Effect = new Avalonia.Media.BlurEffect { Radius = 7 }; dashboardBody.IsHitTestVisible = false;
            layers.Children.Add(onboarding);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => onboarding?.Focus());
        }
        else if (!wanted && onboarding is not null && dashboardBody is not null)
        {
            layers.Children.Remove(onboarding); onboarding = null;
            dashboardBody.Effect = null; dashboardBody.IsHitTestVisible = true;
        }
    }
    /// <summary>Whether a point (in window coordinates) is in the part of the dashboard that drags the window.</summary>
    public bool InDragZone(Point point)
    {
        if (identityBlock is null || accountsHeadBlock is null || toolsRow is null) return false;
        double Bottom(Control c) => c.TranslatePoint(new Point(0, c.Bounds.Height), this)?.Y ?? 0;
        var sidebarRight = identityBlock.TranslatePoint(new Point(identityBlock.Bounds.Width, 0), this)?.X ?? 0;
        return point.X <= sidebarRight + 18 ? point.Y <= Bottom(accountsHeadBlock) : point.Y <= Bottom(headerRow ?? toolsRow);
    }
    private void ShowEntry(Timer.Core.SessionRow? row, DateTime start, DateTime end)
    {
        if (!IsVisible) return;
        if (entryEditor is { IsVisible: true }) { entryEditor.Activate(); return; }
        entryEditor = new EntryWindow(model, row, start, end);
        entryEditor.Closed += (_, _) => entryEditor = null;
        entryEditor.Show(this);
    }
    private void ShowTidy() { if (IsVisible) new TidyWindow(model).Show(this); }
    private void ShowGroup(string id) { if (IsVisible) new GroupWindow(model, id).Show(this); }
    public void SelectTab(bool showWeek) { settingsMode = false; weekMode = showWeek; Refresh(true); }
    // The gear opens the settings page and the same gear closes it, going back to the view that was showing.
    private void ShowSettings() { settingsMode = !settingsMode; Refresh(true); }
    private void SelectSection(string section)
    {
        settingsMode = false; weekMode = true;
        if (model.WeekSection != section) model.SetWeekSection(section); else Refresh(true);
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh(e.PropertyName != nameof(TimerModel.Elapsed));
    private void Refresh(bool refreshWorkspace)
    {
        clock.Text = model.ClockText;
        // Amber on a break, red in overtime, green while tracking, full-strength text otherwise. A break outranks overtime.
        var onBreak = model.IsOnBreak; var late = model.IsOvertime && !onBreak;
        clock.SetState(onBreak, late, model.IsTracking && !onBreak && !late);
        var shownAccount = onBreak ? null : model.Accounts.FirstOrDefault(a => a.Name == (model.ActiveAccount ?? model.SelectedAccount));
        accountDot.IsVisible = shownAccount is not null; if (shownAccount is not null) accountDot.Fill = Avalonia.Media.Brush.Parse(Ui.Visible(shownAccount.Colour));
        account.Text = model.IsOnBreak ? (model.ResumeAccount is { } backTo ? "Then back to " + backTo : "No account running") : model.AccountLabel;
        today.Text = model.Engine.Now.ToString("dddd d MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        var british = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        date.Text = weekMode ? "Week of " + model.WeekStart(model.SelectedDate).ToString("d MMM", british) : model.SelectedDate.ToString("ddd d MMM", british);
        next.IsEnabled = !weekMode || model.CanStepForwardAWeek;
        ToolTip.SetTip(previous, weekMode ? "Previous week" : "Previous day"); ToolTip.SetTip(next, weekMode ? "Next week" : "Next day");
        allocatedValue.Text = model.Format(model.Allocated);
        unallocatedValue.Text = model.Format(model.Unallocated);
        dayText.Text = model.DayStarted ? "Finish day" : "Start day";
        dayFlag.TintKey = model.DayStarted ? "Finish" : "Start";
        day.Classes.Set("startDay", !model.DayStarted); // Green to start the day, red to finish it.
        day.Classes.Set("finishDay", model.DayStarted); // Red when it would finish the day; the dashboard keeps it as an outline.
        // Editing buttons show only where they apply: Merge when blocks are selected, and neither on the overview or the hours table.
        var editing = !weekMode || model.WeekSection == "entries";
        zoomControls.IsVisible = !weekMode && !settingsMode;
        settingZoom = true; zoomSlider.Value = model.TimelineZoomFraction; settingZoom = false; // Follows the wheel and the buttons too.
        zoomOut.IsEnabled = model.TimelinePixelsPerHour > 60.5; zoomIn.IsEnabled = model.TimelinePixelsPerHour < 639.5;
        addTime.IsVisible = editing; undo.IsVisible = editing; merge.IsVisible = editing && model.SelectedBlockCount > 0;
        merge.IsEnabled = model.CanMergeSelection; ToolTip.SetTip(merge, model.MergeHint);
        dashboardCup.Filled = model.IsOnBreak; dashboardCup.SetSteaming(model.IsOnBreak); dashboardCup.InvalidateVisual();
        dashboardBreakText.Text = model.IsOnBreak ? "Finish break" : "Start break";
        breakButton.Classes.Set("onBreak", model.IsOnBreak);
        ToolTip.SetTip(breakButton, model.IsOnBreak ? "End the break" + (model.ResumeAccount is { } resumeTo ? " and go back to " + resumeTo : "") : "Stop timing any account while you take a break");
        undo.IsEnabled = model.Editor.CanUndo;
        ToolTip.SetTip(undo, model.Editor.UndoLabel is { } label ? $"Undo: {label} ({model.Editor.UndoCount} available)" : "No edit to undo");
        void Rail(Button button, bool selected) { button.Classes.Set("selected", selected); if (button.Content is IconGlyph glyph) glyph.TintKey = selected ? "Ink" : "Muted"; }
        Rail(timelineRail, !weekMode && !settingsMode);
        Rail(overviewTab, !settingsMode && weekMode && model.WeekSection == "overview");
        Rail(hoursTab, !settingsMode && weekMode && model.WeekSection == "hours");
        Rail(entriesTab, !settingsMode && weekMode && model.WeekSection == "entries");
        Rail(settingsRail, settingsMode);
        // The settings page has no date to step through and no editing buttons, so the header shows only its name.
        foreach (var control in new Control[] { previous, date, next, todayButton }) control.IsVisible = !settingsMode;
        if (toolsRow is not null) toolsRow.IsVisible = !settingsMode && (!weekMode || model.WeekSection == "entries"); // Nothing to put in it on the overview or the hours table.
        workspaceTitle.Text = settingsMode ? "Settings" : !weekMode ? "Timeline" : model.WeekSection switch { "hours" => "Hours table", "entries" => "Entries", _ => "Week overview" };
        todayButton.IsEnabled = model.SelectedDate.Date != model.Engine.Now.Date;
        // The settings page is built once when it opens, so a model change does not rebuild it under the pointer and lose what is being typed.
        if (settingsMode ? !renderedSettings : refreshWorkspace)
        {
            var oldScroll = workspace.Content is ScrollViewer directOld ? directOld
                : (workspace.Content as Control)?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            var previous = renderedSettings == settingsMode && !settingsMode && renderedWeekMode == weekMode && renderedDate == model.SelectedDate
                ? !weekMode && model.TimelineOffset is double timelineOffset ? new Vector(0, timelineOffset)
                    : oldScroll?.Tag is Vector pending ? pending : oldScroll?.Offset : null;
            workspace.Content = settingsMode ? new SettingsView(model) : weekMode ? ReviewViews.Week(model) : ReviewViews.Timeline(model);
            if (previous is { } offset && workspace.Content is Control control)
            {
                var scroll = control is ScrollViewer direct ? direct : control.GetLogicalDescendants().OfType<ScrollViewer>().FirstOrDefault();
                if (scroll is not null) scroll.Tag = offset;
            }
            renderedWeekMode = weekMode; renderedSettings = settingsMode;
            renderedDate = model.SelectedDate;
        }
    }
}
