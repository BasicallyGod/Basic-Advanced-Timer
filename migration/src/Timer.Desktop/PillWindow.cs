using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>The clock stacked as hours, minutes and seconds, the seconds smaller and quieter, for the narrow vertical pill.</summary>
public sealed class StackedClock : StackPanel
{
    private readonly TextBlock hours, minutes, seconds;
    public StackedClock()
    {
        Spacing = 2; HorizontalAlignment = HorizontalAlignment.Center;
        hours = new TextBlock { FontSize = 30, Classes = { "clock" }, HorizontalAlignment = HorizontalAlignment.Center };
        minutes = new TextBlock { FontSize = 30, Classes = { "clock" }, HorizontalAlignment = HorizontalAlignment.Center };
        seconds = new TextBlock { FontSize = 15, Classes = { "clockLight", "muted" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        Children.Add(hours); Children.Add(minutes); Children.Add(seconds);
    }
    /// <summary>The whole clock, as hours:minutes:seconds. Setting it splits it over the three lines.</summary>
    public string Text
    {
        get => hours.Text + ":" + minutes.Text + ":" + seconds.Text;
        set { var parts = value.Split(':'); hours.Text = parts.Length > 0 ? parts[0] : ""; minutes.Text = parts.Length > 1 ? parts[1] : ""; seconds.Text = parts.Length > 2 ? parts[2] : ""; }
    }
}

/// <summary>
/// The pill: a small vertical card that stays on top. It shows what the timer is doing with one capsule (and a second, blue one only while a
/// reminder is due), the day clock, a circle that carries the account's initials and pauses or resumes, and the mug that starts a break. Clicking the
/// time opens the dashboard. Hovering shows a few accounts beside it, and clicking that opens the full list.
/// </summary>
public sealed class PillWindow : Window
{
    public Action? OpenDashboard { get; set; }
    private readonly TimerModel model;
    /// <summary>The pill's own size before it is scaled for the screen, and the room kept around it for its shadow or glow.</summary>
    public const double PillWidth = 72, PillHeight = 262, PillRadius = 36;
    public static double BaseWindowHeight => PillHeight + 2 * Ui.ShadowMargin;
    // The whole pill is drawn smaller than its design size on every screen, so it never takes a big share of a display. Two things shrink it further.
    // A narrow screen (its width in device independent pixels over 1800, never above 85 per cent or below 70 per cent), and a screen with a low
    // Windows scale, because it has fewer pixels to the inch, so the same pill looks bigger on it. Windows scale 125 per cent or more gives the
    // largest pill, and 100 per cent the smallest, with the steps between spread evenly. The smaller of the two wins.
    public const double ScreenWidthDivisor = 1800, LargestScale = 0.85, SmallestScale = 0.7, LargestScaleFromDisplayScale = 1.25;
    public static double ScaleFor(double screenWidth, double displayScale = LargestScaleFromDisplayScale)
    {
        var byWidth = Math.Clamp(screenWidth / ScreenWidthDivisor, SmallestScale, LargestScale);
        var byDensity = SmallestScale + Math.Clamp((displayScale - 1.0) / (LargestScaleFromDisplayScale - 1.0), 0, 1) * (LargestScale - SmallestScale);
        return Math.Min(byWidth, byDensity);
    }
    private readonly ScaleTransform pillScale = new(1, 1);
    public double Scale => pillScale.ScaleX;
    /// <summary>The window height at the size the pill is drawn on this screen.</summary>
    public double WindowHeightNow => BaseWindowHeight * Scale;
    /// <summary>The window width at the size the pill is drawn on this screen.</summary>
    public double WindowWidthNow => (PillWidth + 2 * Ui.ShadowMargin) * Scale;

    private readonly StackedClock clock = new() { Text = "00:00:00" };
    private readonly Border stateLight = new() { Name = "PillStateLight", Width = 6, Height = 14, CornerRadius = new CornerRadius(3) };
    private readonly Border reminderLight = new() { Name = "PillReminderLight", Width = 6, Height = 14, CornerRadius = new CornerRadius(3), IsVisible = false };
    private readonly Avalonia.Controls.Shapes.Ellipse circleFill = new() { Width = 46, Height = 46 };
    private readonly TextBlock initials = new() { FontSize = 17, Classes = { "clock" }, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly PauseGlyph glyph = new();
    private readonly BreakGlyph cup = new();
    private readonly Border clockTile = new() { Name = "PillClockTile", CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 6), Background = Brushes.Transparent };
    private readonly Border mugTile = new() { Name = "PillMugTile", Width = 40, Height = 40, CornerRadius = new CornerRadius(13), Background = Brushes.Transparent };
    private readonly Grid circleHost = new() { Width = 46, Height = 46, RenderTransform = new ScaleTransform(1, 1), RenderTransformOrigin = RelativePoint.Center };
    private readonly Viewbox glyphBox = new() { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private Button clockButton = null!, circle = null!, mug = null!;
    private Border surface = null!, shadowHost = null!;
    private readonly Popup peek = new(), panel = new();
    private readonly Grid rootGrid = new();
    private StackPanel lightsRow = null!;
    private ScrollViewer accountScroll = null!;
    private StackPanel peekRows = null!;
    private bool hoverCircle, hoverMug, overPill, overFlyout, closedWindow, ensuring;
    private string? activeWhenOpened;
    private DispatcherTimer? openTimer, closeTimer, settleTimer;
    private int? appliedChoice;
    private (PixelRect Bounds, double Scaling)? lastScreen;
    private (bool, string, ThemeVariant)? lastShadow;

    /// <summary>The pill's own state for tests and for the coordinator: whether the hover card or the account list is open.</summary>
    public bool PeekOpen => peek.IsOpen;
    public bool PanelOpen => panel.IsOpen;
    /// <summary>The account list's scroll box, so tests can read its limit.</summary>
    public ScrollViewer AccountScroll => accountScroll;

    public PillWindow(TimerModel model)
    {
        this.model = model;
        Title = "Basic’s Advanced Timer"; WindowChrome.Attach(this);
        // One item for now: the menu is kept to what the pill itself cannot do. Starting a break is the mug.
        ContextMenu = Ui.SettingsMenu(model);
        Width = PillWidth + 2 * Ui.ShadowMargin; Height = BaseWindowHeight;
        CanResize = false;
        Topmost = model.PillOnTop; model.PropertyChanged += (_, _) => { if (Topmost != model.PillOnTop) Topmost = model.PillOnTop; };
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;

        // Two lights: the state, and a reminder that is due.
        lightsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, Name = "PillLights" };
        lightsRow.Children.Add(stateLight); lightsRow.Children.Add(reminderLight);
        // The clock opens the dashboard.
        clockButton = Ui.Button("", () => OpenDashboard?.Invoke(), "PillDashboard");
        clockButton.Classes.Add("pillBare"); clockButton.Padding = new Thickness(0);
        clockTile.Child = clock; clockButton.Content = clockTile; clockButton.HorizontalAlignment = HorizontalAlignment.Center;
        clockButton.PointerEntered += (_, _) => SetTileHover(clockTile, true);
        clockButton.PointerExited += (_, _) => SetTileHover(clockTile, false);
        ToolTip.SetTip(clockButton, "Open dashboard");
        Avalonia.Automation.AutomationProperties.SetName(clockButton, "Open dashboard");
        // The circle carries the running account's initials, and pauses or resumes. The initials give way to the symbol under the pointer.
        glyphBox.Child = glyph; glyph.Fixed = Brushes.Black;
        circleHost.Children.Add(circleFill); circleHost.Children.Add(initials); circleHost.Children.Add(glyphBox);
        circle = Ui.Button("", OnCircle, "PillPause");
        circle.Classes.Add("pillBare"); circle.Padding = new Thickness(0); circle.Width = 46; circle.Height = 46; circle.Content = circleHost; circle.HorizontalAlignment = HorizontalAlignment.Center;
        circle.PointerEntered += (_, _) => { hoverCircle = true; glyph.Glow = 1; circleHost.RenderTransform = new ScaleTransform(1.06, 1.06); Refresh(); };
        circle.PointerExited += (_, _) => { hoverCircle = false; glyph.Glow = 0; circleHost.RenderTransform = new ScaleTransform(1, 1); Refresh(); };
        // The mug starts a break and shows it is on while one runs. It is amber, with no circle round it.
        cup.Mono = false;
        mugTile.Child = new Viewbox { Width = 26, Height = 26, Child = cup, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        mug = Ui.Button("", model.ToggleBreak, "PillBreak");
        mug.Classes.Add("pillBare"); mug.Padding = new Thickness(0); mug.Content = mugTile; mug.HorizontalAlignment = HorizontalAlignment.Center;
        mug.PointerEntered += (_, _) => { hoverMug = true; cup.SetSteaming(true); SetTileHover(mugTile, true); };
        mug.PointerExited += (_, _) => { hoverMug = false; cup.SetSteaming(model.IsOnBreak); SetTileHover(mugTile, false); };

        var column = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        column.Children.Add(lightsRow); clockButton.Margin = new Thickness(0, 4, 0, 0); column.Children.Add(clockButton);
        circle.Margin = new Thickness(0, 6, 0, 0); column.Children.Add(circle); column.Children.Add(mug);
        var root = rootGrid; root.Background = Brushes.Transparent; root.Children.Add(column);
        Ui.PillGestures(root, this);
        surface = new Border { Name = "PillSurface", Child = root, ClipToBounds = true, Width = PillWidth, Height = PillHeight, BorderThickness = new Thickness(1) };
        surface.Classes.Add("surface");
        shadowHost = Ui.Shadowed(surface, PillRadius);
        Content = new LayoutTransformControl { LayoutTransform = pillScale, Child = shadowHost };
        surface.PointerEntered += (_, _) => { overPill = true; ScheduleOpen(); };
        surface.PointerExited += (_, _) => { overPill = false; ScheduleClose(); };

        BuildFlyouts();
        Opened += (_, _) => FitToScreen(Screens.ScreenFromWindow(this));
        PositionChanged += (_, _) => { FitIfScreenChanged(); SettleSoon(); };
        ScalingChanged += (_, _) => FitIfScreenChanged();
        Closed += (_, _) => { closedWindow = true; settleTimer?.Stop(); openTimer?.Stop(); closeTimer?.Stop(); };
        // Whenever the real size is not the wanted one, say after a drag or a change made while hidden, it is put right.
        SizeChanged += (_, _) => { if (IsVisible && !ensuring && !closedWindow && (Math.Abs(ClientSize.Width - WindowWidthNow) > 1.5 || Math.Abs(ClientSize.Height - WindowHeightNow) > 1.5)) EnsureSize(); };
        Activated += (_, _) => EnsureSize();
        Deactivated += (_, _) => { if (!overFlyout) CloseFlyouts(); };
        model.PropertyChanged += Changed;
        ActualThemeVariantChanged += (_, _) => { lastShadow = null; Refresh(); };
        Closed += (_, _) => model.PropertyChanged -= Changed;
        Refresh();
    }

    // ---- size
    /// <summary>Sizes the pill for the screen it is on. The window keeps the pill's proportions, so the content only needs the scale.</summary>
    public void FitToScreen(Avalonia.Platform.Screen? screen)
    {
        // A size chosen in settings wins. Otherwise it is worked out from the screen, and with no screen to measure (a hidden window) it stays as it is.
        var chosen = model.PillScalePercent;
        var scale = chosen is { } percent ? percent / 100.0 : screen is null ? pillScale.ScaleX : ScaleFor(screen.WorkingArea.Width / screen.Scaling, screen.Scaling);
        appliedChoice = chosen; if (screen is not null) lastScreen = (screen.Bounds, screen.Scaling);
        pillScale.ScaleX = scale; pillScale.ScaleY = scale;
        // Whether or not the scale moved, the window is brought to the size it calls for. Skipping this when the scale was already right left the
        // height at its old unscaled value whenever the scale had been set before the window was showing.
        EnsureSize();
    }
    // After the last move of a drag (nothing for a moment) the size is checked once more, since Windows does not take size changes during the drag.
    private void SettleSoon()
    {
        settleTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        settleTimer.Tick -= OnSettled; settleTimer.Tick += OnSettled; settleTimer.Stop(); settleTimer.Start();
    }
    private void OnSettled(object? sender, EventArgs e) { settleTimer?.Stop(); if (IsVisible && !closedWindow) { FitIfScreenChanged(); EnsureSize(); } }
    // Dragging the pill onto another screen, or Windows changing a screen's scale, sizes it for that screen at once, without waiting for it to be shown again.
    public void FitIfScreenChanged()
    {
        if (closedWindow || Screens.ScreenFromWindow(this) is not { } screen) return;
        if (lastScreen is { } known && known.Bounds == screen.Bounds && known.Scaling == screen.Scaling) return;
        FitToScreen(screen);
    }
    /// <summary>
    /// Gives the window the size the pill's scale calls for, and does it even when the window already has that size asked of it but not the real
    /// one. Windows ignores a size change while the window is being dragged, which left a pill dragged onto another screen with big contents in a
    /// window of the old size, so when the real size is wrong the request is nudged and made again.
    /// </summary>
    public void EnsureSize()
    {
        if (closedWindow) return;
        var wantWidth = WindowWidthNow; var wantHeight = WindowHeightNow;
        Width = wantWidth; Height = wantHeight;
        // A correction is already on its way. It will use the size wanted when it runs, so this change is not lost.
        if (ensuring || !IsVisible || (Math.Abs(ClientSize.Width - wantWidth) <= 1.5 && Math.Abs(ClientSize.Height - wantHeight) <= 1.5)) return;
        ensuring = true;
        Width = wantWidth + 1; Height = wantHeight + 1;
        Dispatcher.UIThread.Post(() =>
        {
            ensuring = false;
            if (!closedWindow) { Width = WindowWidthNow; Height = WindowHeightNow; }
        }, DispatcherPriority.Background);
    }

    // ---- the hover card and the account list
    private void BuildFlyouts()
    {
        // The hover card: a few accounts with today's time, and a way to the full list.
        peekRows = new StackPanel { Spacing = 8 };
        var peekCard = new Border { Name = "PillPeek", Width = 168, Padding = new Thickness(10), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Child = peekRows };
        peekCard.Classes.Add("surface"); peekCard.Classes.Add("flyoutCard");
        peekCard.PointerEntered += (_, _) => { overFlyout = true; closeTimer?.Stop(); };
        peekCard.PointerExited += (_, _) => { overFlyout = false; ScheduleClose(); };
        ConfigurePopup(peek, new LayoutTransformControl { LayoutTransform = pillScale, Child = peekCard }, false);
        // The account list: the dashboard's account rows with their budget bars, then Manage accounts.
        var heading = Ui.Grid("*,Auto"); heading.Margin = new Thickness(6, 0, 6, 6);
        var title = Ui.Text("Accounts", 15); title.FontWeight = FontWeight.SemiBold; Ui.Put(heading, title);
        Ui.Put(heading, Ui.Text("Today", 12, "muted"), 1);
        accountScroll = new ScrollViewer { Name = "PillAccountScroll", Content = new AccountList(model, bars: true), AllowAutoHide = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var manage = Ui.DialogButton("Manage accounts", () => { CloseFlyouts(); model.OpenAccounts(); }, "PillManageAccounts", false);
        manage.HorizontalAlignment = HorizontalAlignment.Stretch; manage.HorizontalContentAlignment = HorizontalAlignment.Center; manage.Margin = new Thickness(6, 8, 6, 0);
        var list = new StackPanel { Spacing = 2 }; list.Children.Add(heading); list.Children.Add(Ui.WithMoreBelow(accountScroll)); list.Children.Add(manage);
        var panelCard = new Border { Name = "PillPanel", Width = 320, Padding = new Thickness(8, 12, 8, 12), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Child = list };
        panelCard.Classes.Add("surface"); panelCard.Classes.Add("flyoutCard");
        panelCard.PointerEntered += (_, _) => { overFlyout = true; closeTimer?.Stop(); };
        panelCard.PointerExited += (_, _) => overFlyout = false;
        ConfigurePopup(panel, new LayoutTransformControl { LayoutTransform = pillScale, Child = panelCard }, true);
        panel.Closed += (_, _) => overFlyout = false;
        // The popups belong to the pill's tree, so they inherit its theme.
        rootGrid.Children.Add(peek); rootGrid.Children.Add(panel);
    }
    private void ConfigurePopup(Popup popup, Control child, bool dismissOnOutsideClick)
    {
        popup.PlacementTarget = surface; popup.Placement = PlacementMode.Right; popup.HorizontalOffset = 8;
        popup.PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipX | PopupPositionerConstraintAdjustment.SlideY;
        popup.IsLightDismissEnabled = dismissOnOutsideClick; popup.Child = child;
    }
    private void ScheduleOpen()
    {
        closeTimer?.Stop();
        if (PanelOpen || PeekOpen) return;
        openTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        openTimer.Tick -= OnOpenTick; openTimer.Tick += OnOpenTick; openTimer.Stop(); openTimer.Start();
    }
    private void OnOpenTick(object? sender, EventArgs e) { openTimer?.Stop(); if (overPill && !closedWindow && IsVisible) ShowPeek(); }
    private void ScheduleClose()
    {
        openTimer?.Stop();
        closeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        closeTimer.Tick -= OnCloseTick; closeTimer.Tick += OnCloseTick; closeTimer.Stop(); closeTimer.Start();
    }
    private void OnCloseTick(object? sender, EventArgs e) { closeTimer?.Stop(); if (!overPill && !overFlyout && peek.IsOpen) peek.IsOpen = false; }
    /// <summary>Shows a few accounts with today's time beside the pill. The one running comes first, then the ones with the most time today.</summary>
    public void ShowPeek()
    {
        if (PanelOpen) return;
        peekRows.Children.Clear();
        var accounts = model.Accounts.Where(a => !BreakAccount.Is(a.Name)).ToList();
        var shown = accounts.OrderByDescending(a => a.Name == model.ActiveAccount).ThenByDescending(a => model.Today(a)).Take(3).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            var account = shown[i];
            var swatch = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(11), Background = Brush.Parse(Ui.Visible(account.Colour)), VerticalAlignment = VerticalAlignment.Center };
            var name = Ui.Text(account.Display, 13); name.FontWeight = FontWeight.SemiBold; name.TextTrimming = TextTrimming.CharacterEllipsis;
            var time = Ui.Text(model.Format(model.Today(account)), 12, "muted");
            var texts = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center }; texts.Children.Add(name); texts.Children.Add(time);
            var row = Ui.Grid("Auto,*"); swatch.Margin = new Thickness(0, 0, 10, 0); Ui.Put(row, swatch); Ui.Put(row, texts, 1);
            var button = Ui.Button("", () => { CloseFlyouts(); model.ToggleAccount(account.Name); }, "PeekAccount" + i);
            button.Classes.Add("pillBare"); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Content = row;
            peekRows.Children.Add(button);
        }
        var all = Ui.Button("All accounts ›", ShowPanel, "PillAllAccounts"); all.Classes.Add("peekAll");
        all.HorizontalAlignment = HorizontalAlignment.Stretch; all.HorizontalContentAlignment = HorizontalAlignment.Left;
        peekRows.Children.Add(all);
        peek.IsOpen = true;
    }
    /// <summary>Opens the list of accounts beside the pill, with their budgets. It closes when an account is chosen or anywhere else is clicked.</summary>
    public void ShowPanel()
    {
        peek.IsOpen = false; openTimer?.Stop();
        accountScroll.MaxHeight = ListMaxHeight();
        activeWhenOpened = model.ActiveAccount;
        panel.IsOpen = true;
    }
    /// <summary>The most of a screen's height the list takes: three fifths of it, in the pill's own units, never less than a few rows.</summary>
    public const double OpenShareOfScreen = 0.6, FewestRows = 3;
    private double ListMaxHeight()
    {
        var screenHeight = 800.0;
        if (!closedWindow && Screens.ScreenFromWindow(this) is { } screen) screenHeight = screen.WorkingArea.Height / screen.Scaling;
        var room = screenHeight * OpenShareOfScreen / Scale - 130;
        return Math.Max(FewestRows * 70, room);
    }
    /// <summary>Puts the hover card and the account list away, so the pill is a plain capsule again when it comes back from the dashboard.</summary>
    public void CloseFlyouts() { openTimer?.Stop(); closeTimer?.Stop(); overFlyout = false; peek.IsOpen = false; panel.IsOpen = false; }

    // ---- behaviour
    private void OnCircle()
    {
        var chosen = model.Accounts.Any(a => a.Name == model.SelectedAccount);
        // With nothing to run or resume, the list opens so an account can be picked.
        if (model.IsTracking || chosen) model.TogglePause(); else ShowPanel();
    }
    private static void SetTileHover(Border tile, bool on) => tile.Background = on ? (tile.TryFindResource("Hover", tile.ActualThemeVariant, out var brush) && brush is IBrush hover ? hover : Brushes.Transparent) : Brushes.Transparent;
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A new pill size from settings takes effect straight away, even while the pill is hidden behind the dashboard.
        if (model.PillScalePercent != appliedChoice) FitToScreen(Screens.ScreenFromWindow(this));
        if (PanelOpen && model.ActiveAccount != activeWhenOpened) CloseFlyouts();
        Refresh();
    }
    private Color ThemeColourOr(string key, string fallback) => Ui.ThemeColour(this, key, Color.Parse(fallback));
    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    /// <summary>The first letters of up to two words, so Account three reads AT, Northwind Traders reads NT and Client 7 reads C7.</summary>
    public static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "";
        if (words.Length == 1) return words[0].Length >= 2 && char.IsDigit(words[0][^1]) ? char.ToUpperInvariant(words[0][0]).ToString() + words[0][^1] : char.ToUpperInvariant(words[0][0]).ToString();
        return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }
    private void Refresh()
    {
        var onBreak = model.IsOnBreak; var late = model.IsOvertime && !onBreak; var live = model.IsTracking && !onBreak && !late;
        var key = onBreak ? "Break" : late ? "Overtime" : live ? "Live" : "Idle";
        var stateName = onBreak ? "On break" : late ? "Overtime" : live ? "Recording" : "Not recording";
        // The light's colour is the state, and the circle and the ring repeat it, so the light never carries it alone.
        var stateColour = ThemeColourOr(key, "#8A8F98");
        stateLight.Background = new SolidColorBrush(stateColour);
        ToolTip.SetTip(lightsRow, stateName); Avalonia.Automation.AutomationProperties.SetName(stateLight, stateName);
        // The reminder light is there only while a reminder is due, and pulses. It only notifies.
        reminderLight.IsVisible = model.ReminderDue; reminderLight.Background = new SolidColorBrush(ThemeColourOr("Accent", "#3558C8"));
        Ui.Pulse(reminderLight, model.ReminderDue);
        Avalonia.Automation.AutomationProperties.SetName(reminderLight, "A reminder is due");
        clock.Text = model.ClockText;

        var selected = model.Accounts.FirstOrDefault(a => a.Name == model.SelectedAccount);
        var who = onBreak ? model.ResumeAccount : model.ActiveAccount ?? selected?.Name;
        var circleColour = ThemeColourOr(key == "Idle" ? "Live" : key, "#34D399");
        circleFill.Fill = new SolidColorBrush(circleColour);
        var ink = Color.Parse(Ui.InkOn(Hex(circleColour)));
        initials.Foreground = new SolidColorBrush(ink); glyph.Fixed = new SolidColorBrush(ink);
        initials.Text = who is null ? "" : Initials(who);
        // Initials while an account runs, and the symbol at rest, under the pointer, or when there is no account to name.
        var showGlyph = hoverCircle || (!model.IsTracking && !onBreak) || initials.Text.Length == 0;
        initials.IsVisible = !showGlyph; glyphBox.IsVisible = showGlyph;
        glyph.Playing = model.IsTracking && !onBreak; glyph.InvalidateVisual();
        var circleTip = onBreak ? "End break" + (model.ResumeAccount is { } back ? " and resume " + back : "")
            : model.IsTracking ? "Pause. Tracking " + (model.ActiveAccount is { } active ? model.Accounts.FirstOrDefault(a => a.Name == active)?.Display ?? active : "")
            : selected is not null ? "Start " + selected.Display : "Choose an account";
        ToolTip.SetTip(circle, circleTip); Avalonia.Automation.AutomationProperties.SetName(circle, circleTip);

        // The mug is lit while a break runs, and its steam moves.
        mugTile.Background = onBreak ? new SolidColorBrush(Color.FromArgb(0x33, ThemeColourOr("Break", "#F5A524").R, ThemeColourOr("Break", "#F5A524").G, ThemeColourOr("Break", "#F5A524").B)) : hoverMug ? mugTile.Background : Brushes.Transparent;
        cup.SetSteaming(onBreak || hoverMug); cup.InvalidateVisual();
        var mugTip = onBreak ? "End break" + (model.ResumeAccount is { } returnTo ? " and resume " + returnTo : "") : "Start break";
        ToolTip.SetTip(mug, mugTip); Avalonia.Automation.AutomationProperties.SetName(mug, mugTip);
        Avalonia.Automation.AutomationProperties.SetName(clockButton, "Open dashboard. Working time " + model.ClockText);

        // A ring and a glow in the state's colour round the pill while it is doing something, and the plain edge and shadow when it is idle.
        surface.BorderThickness = new Thickness(key == "Idle" ? 1 : 2);
        surface.BorderBrush = key == "Idle" ? (this.TryFindResource("Line", ActualThemeVariant, out var line) && line is IBrush lineBrush ? lineBrush : Brushes.Gray) : new SolidColorBrush(stateColour);
        var shadow = (model.Shadows, key, ActualThemeVariant);
        if (lastShadow != shadow) { lastShadow = shadow; Ui.SetShadow(shadowHost, model.Shadows, key == "Idle" ? null : key); }
    }
}
