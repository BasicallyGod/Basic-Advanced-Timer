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

/// <summary>
/// The pill: a small vertical card that stays on top. It shows what the timer is doing with one capsule (and a second, blue one only while a
/// reminder is due), the day clock, a circle that carries the account's initials and pauses or resumes, the Accounts button, and the amber circle that
/// starts a break. Clicking the time opens the dashboard. The Accounts button unfolds the list sideways from the pill as one capsule with it; it stays open
/// while accounts are chosen, so a wrong choice can be put right at once, and the button, Escape or a click elsewhere folds it away.
/// </summary>
public sealed class PillWindow : Window
{
    public Action? OpenDashboard { get; set; }
    private readonly TimerModel model;
    /// <summary>The pill's own size before it is scaled for the screen, and the room kept around it for its shadow or glow.</summary>
    public const double PillWidth = 72, PillHeight = 316, PillRadius = 36;
    public static double BaseWindowHeight => PillHeight + 2 * Ui.ShadowMargin;
    // The whole pill is drawn smaller than its design size on every screen, so it never takes a big share of a display. Two things shrink it further.
    // A narrow screen (its width in device independent pixels over 1800, never above 95 per cent or below 80 per cent), and a screen with a low
    // Windows scale, because it has fewer pixels to the inch, so the same pill looks bigger on it. Windows scale 125 per cent or more gives the
    // largest pill, and 100 per cent the smallest, with the steps between spread evenly. The smaller of the two wins.
    public const double ScreenWidthDivisor = 1800, LargestScale = 0.95, SmallestScale = 0.8, LargestScaleFromDisplayScale = 1.25;
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

    private StatusFace face = null!;
    private Border surface = null!, shadowHost = null!;
    private readonly Popup panel = new();
    private readonly Grid rootGrid = new();
    private ScrollViewer accountScroll = null!;
    private Border drawer = null!;
    private bool overDrawer, closedWindow, ensuring, closingByCode;
    private long dismissedAt;
    private DispatcherTimer? settleTimer;
    private int? appliedChoice;
    private (PixelRect Bounds, double Scaling)? lastScreen;
    private (bool, string, ThemeVariant)? lastShadow;

    /// <summary>Whether the account drawer is open beside the pill.</summary>
    public bool PanelOpen => panel.IsOpen;
    /// <summary>The account list's scroll box, so tests can read its limit.</summary>
    public ScrollViewer AccountScroll => accountScroll;
    /// <summary>The Accounts button on the pill's face.</summary>
    public Button AccountsButton => face.AccountsButton;

    public PillWindow(TimerModel model)
    {
        this.model = model;
        Title = "Basic’s Advanced Timer"; WindowChrome.Attach(this);
        // One item for now: the menu is kept to what the pill itself cannot do. Starting a break is the amber circle.
        ContextMenu = Ui.SettingsMenu(model);
        Width = PillWidth + 2 * Ui.ShadowMargin; Height = BaseWindowHeight;
        CanResize = false;
        Topmost = model.PillOnTop; model.PropertyChanged += (_, _) => { if (Topmost != model.PillOnTop) Topmost = model.PillOnTop; };
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;

        // The top of the pill is shared with the dashboard's rail.
        face = new StatusFace(model, "Pill", "PillBreak", "Open dashboard", () => OpenDashboard?.Invoke(), OnCircle, ToggleAccounts);
        // From the top, so the rail's top can start at the same distance from its own edge and nothing moves when one becomes the other.
        var column = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, TopInset, 0, 0) };
        column.Children.Add(face);
        var root = rootGrid; root.Background = Brushes.Transparent; root.Children.Add(column);
        Ui.PillGestures(root, this);
        surface = new Border { Name = "PillSurface", Child = root, ClipToBounds = true, Width = PillWidth, Height = PillHeight, BorderThickness = new Thickness(1) };
        surface.Classes.Add("surface");
        shadowHost = Ui.Shadowed(surface, PillRadius);
        Content = new LayoutTransformControl { LayoutTransform = pillScale, Child = shadowHost };

        BuildDrawer();
        Opened += (_, _) => FitToScreen(Screens.ScreenFromWindow(this));
        PositionChanged += (_, _) => { FitIfScreenChanged(); SettleSoon(); };
        ScalingChanged += (_, _) => FitIfScreenChanged();
        Closed += (_, _) => { closedWindow = true; settleTimer?.Stop(); };
        // Whenever the real size is not the wanted one, say after a drag or a change made while hidden, it is put right.
        SizeChanged += (_, _) => { if (IsVisible && !ensuring && !closedWindow && (Math.Abs(ClientSize.Width - WindowWidthNow) > 1.5 || Math.Abs(ClientSize.Height - WindowHeightNow) > 1.5)) EnsureSize(); };
        Activated += (_, _) => EnsureSize();
        Deactivated += (_, _) => { if (!overDrawer) CloseFlyouts(); };
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape && PanelOpen) { CloseFlyouts(); e.Handled = true; } };
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

    // ---- the account drawer
    private void BuildDrawer()
    {
        // The list unfolds from the pill's right edge as one capsule with it: the same surface, the same ring, no gap, and rounded only on the outside.
        var content = new AccountsPanel(model, "Pill", compact: true, reorderable: false, () => { CloseFlyouts(); model.OpenAccounts(); });
        accountScroll = content.Scroll;
        drawer = new Border { Name = "PillDrawer", Width = DrawerWidth, Height = PillHeight, Padding = new Thickness(14, 14, 14, 12), CornerRadius = new CornerRadius(0, 28, 28, 0), Child = content };
        drawer.Classes.Add("surface");
        drawer.PointerEntered += (_, _) => overDrawer = true;
        drawer.PointerExited += (_, _) => overDrawer = false;
        panel.PlacementTarget = surface; panel.Placement = PlacementMode.Right; panel.HorizontalOffset = 0; panel.VerticalOffset = 0;
        panel.PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipX | PopupPositionerConstraintAdjustment.SlideY;
        panel.IsLightDismissEnabled = true; panel.Child = new LayoutTransformControl { LayoutTransform = pillScale, Child = drawer };
        panel.Closed += (_, _) => { overDrawer = false; if (!closingByCode) dismissedAt = Environment.TickCount64; SetOpenLook(false); };
        // The popup belongs to the pill's tree, so it inherits its theme.
        rootGrid.Children.Add(panel);
    }
    /// <summary>The drawer's width, beside the pill's own 72.</summary>
    public const double DrawerWidth = 262;
    /// <summary>How far the lights sit below the card's top edge, on the pill and on the rail.</summary>
    public const double TopInset = 14;
    /// <summary>The most of a screen's height the drawer's list may take is its own height, so it never grows past the pill; it scrolls inside.</summary>
    public const double FewestRows = 3;
    // While the drawer is open the pill's right side is square, so the two read as one capsule, and the button shows pressed.
    private void SetOpenLook(bool open)
    {
        surface.CornerRadius = open ? new CornerRadius(PillRadius, 0, 0, PillRadius) : new CornerRadius(PillRadius);
        face.SetAccountsOpen(open); SyncDrawerRing();
    }
    private void SyncDrawerRing()
    {
        var thickness = surface.BorderThickness.Left;
        drawer.BorderThickness = new Thickness(0, thickness, thickness, thickness);
        drawer.BorderBrush = surface.BorderBrush;
    }
    /// <summary>Opens the account drawer beside the pill. It closes when an account is chosen, with Escape, or by pressing the button again.</summary>
    public void ShowPanel()
    {
        SetOpenLook(true); panel.IsOpen = true;
    }
    // Pressing the button while the drawer is open closes it. Light dismiss has already closed it by the time the click arrives, so a close that
    // just happened from the same press is not undone by opening it again.
    private void ToggleAccounts()
    {
        if (panel.IsOpen) { CloseFlyouts(); return; }
        if (Environment.TickCount64 - dismissedAt < 300) return;
        ShowPanel();
    }
    /// <summary>Puts the account drawer away, so the pill is a plain capsule again when it comes back from the dashboard.</summary>
    public void CloseFlyouts() { closingByCode = true; try { panel.IsOpen = false; } finally { closingByCode = false; } overDrawer = false; SetOpenLook(false); }

    // ---- behaviour
    private void OnCircle()
    {
        var chosen = model.Accounts.Any(a => a.Name == model.SelectedAccount);
        // With nothing to run or resume, the list opens so an account can be picked.
        if (model.IsTracking || chosen) model.TogglePause(); else ShowPanel();
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A new pill size from settings takes effect straight away, even while the pill is hidden behind the dashboard.
        if (model.PillScalePercent != appliedChoice) FitToScreen(Screens.ScreenFromWindow(this));
        Refresh();
    }
    private Color ThemeColourOr(string key, string fallback) => Ui.ThemeColour(this, key, Color.Parse(fallback));
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
        face.Refresh();
        var onBreak = model.IsOnBreak;
        var (key, _) = StatusFace.State(model);
        var stateColour = ThemeColourOr(key, "#8A8F98");

        // A ring and a glow in the state's colour round the pill while it is doing something, and the plain edge and shadow when it is idle.
        surface.BorderThickness = new Thickness(key == "Idle" ? 1 : 1.5);
        surface.BorderBrush = key == "Idle" ? (this.TryFindResource("Line", ActualThemeVariant, out var line) && line is IBrush lineBrush ? lineBrush : Brushes.Gray) : new SolidColorBrush(Color.FromArgb(200, stateColour.R, stateColour.G, stateColour.B));
        if (drawer is not null) SyncDrawerRing();
        var shadow = (model.Shadows, key, ActualThemeVariant);
        if (lastShadow != shadow) { lastShadow = shadow; Ui.SetShadow(shadowHost, model.Shadows, key == "Idle" ? null : key); }
    }
}
