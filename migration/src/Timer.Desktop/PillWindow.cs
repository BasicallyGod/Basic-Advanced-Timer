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
    /// <summary>The horizontal pill's own size: 336 wide and 72 tall, a capsule of the same 36 pixel radius. Its contents start 20 pixels from the left and end 14 from the right.</summary>
    public const double HorizontalWidth = 336, HorizontalHeight = 72, HorizontalInsetLeft = 20, HorizontalInsetRight = 14;
    /// <summary>Which shape the pill is now, from the setting. It is rebuilt in place when the setting changes.</summary>
    public bool Horizontal => horizontal;
    private bool horizontal;
    private double ShapeWidth => horizontal ? HorizontalWidth : PillWidth;
    private double ShapeHeight => horizontal ? HorizontalHeight : PillHeight;
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
    public double WindowHeightNow => (ShapeHeight + 2 * Ui.ShadowMargin) * Scale;
    /// <summary>The window width at the size the pill is drawn on this screen.</summary>
    public double WindowWidthNow => (ShapeWidth + 2 * Ui.ShadowMargin) * Scale;

    private StatusFace face = null!;
    private Border surface = null!, shadowHost = null!;
    private Border? drawerFiller, drawer;
    private Popup panel = new();
    private Grid rootGrid = new();
    private ScrollViewer accountScroll = null!;
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
        CanResize = false;
        Topmost = model.PillOnTop; model.PropertyChanged += (_, _) => { if (Topmost != model.PillOnTop) Topmost = model.PillOnTop; };
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;

        BuildShape();
        Width = WindowWidthNow; Height = WindowHeightNow;
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

    // ---- the shape: vertical or horizontal
    /// <summary>
    /// Builds the pill's card for the shape the setting calls for, and its account list (the drawer that slides out beside the vertical pill, or the list that hangs
    /// below the horizontal one). It is built again when the setting changes, so the pill turns into the other shape where it stands.
    /// </summary>
    private void BuildShape()
    {
        if (panel.IsOpen) { closingByCode = true; try { panel.IsOpen = false; } finally { closingByCode = false; } }
        horizontal = model.PillHorizontal;
        // The top of the pill is shared with the dashboard's rail.
        face = new StatusFace(model, "Pill", "PillBreak", "Open dashboard", () => OpenDashboard?.Invoke(), OnCircle, ToggleAccounts, horizontal);
        var root = rootGrid = new Grid { Background = Brushes.Transparent };
        if (horizontal)
        {
            // Along the card, centred on its height, with the room kept clear at each end (the card's edge is part of it).
            face.Margin = new Thickness(HorizontalInsetLeft - EdgeThickness, 0, HorizontalInsetRight - EdgeThickness, 0); face.HorizontalAlignment = HorizontalAlignment.Left; face.VerticalAlignment = VerticalAlignment.Center;
            root.Children.Add(face);
        }
        else
        {
            // From the top, so the rail's top can start at the same distance from its own edge and nothing moves when one becomes the other.
            var column = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, TopInset - EdgeThickness, 0, 0) };
            column.Children.Add(face); root.Children.Add(column);
        }
        Ui.PillGestures(root, this);
        surface = new Border { Name = "PillSurface", Child = root, ClipToBounds = true, Width = ShapeWidth, Height = ShapeHeight, BorderThickness = new Thickness(EdgeThickness) };
        surface.Classes.Add("pillBody");
        shadowHost = Ui.Shadowed(surface, PillRadius);
        drawerFiller = null; drawer = null;
        if (!horizontal)
        {
            // While the drawer is open, a square of the drawer's own surface sits behind the pill's right half, so the corners the capsule cuts away show the drawer
            // tucked behind them. It lives in the pill's window, not the drawer's, so the drawer's window never lies over the pill's controls.
            drawerFiller = new Border { Name = "PillDrawerFiller", Width = PillRadius, Height = PillHeight, HorizontalAlignment = HorizontalAlignment.Right, IsVisible = false, IsHitTestVisible = false };
            drawerFiller.Classes.Add("pillBody");
            shadowHost.Child = null; shadowHost.Child = new Grid { Children = { drawerFiller, surface } };
        }
        Content = new LayoutTransformControl { LayoutTransform = pillScale, Child = shadowHost };
        panel = new Popup();
        if (horizontal) BuildDropdown(); else BuildDrawer();
        lastShadow = null;
    }
    /// <summary>Turns the pill into the other shape when the setting has changed, keeping its top left corner, and brings it back inside the screen if it now runs off it.</summary>
    private void SwitchShape()
    {
        BuildShape(); EnsureSize(); Refresh();
        if (!IsVisible || Screens.ScreenFromWindow(this) is not { } screen) return;
        var area = screen.WorkingArea; var box = ScreenGuard.SlideInside(new ScreenGuard.Box(area.X, area.Y, area.Width, area.Height), new ScreenGuard.Box(Position.X, Position.Y, (int)Math.Round(WindowWidthNow * screen.Scaling), (int)Math.Round(WindowHeightNow * screen.Scaling)));
        if (box.X != Position.X || box.Y != Position.Y) Position = new PixelPoint(box.X, box.Y);
    }
    /// <summary>The horizontal pill's account list, a card that hangs below the account's box. It is as wide as the vertical pill's drawer, and scrolls inside.</summary>
    public const double DropdownWidth = 208, DropdownMaxHeight = 380, DropdownGap = 11;
    /// <summary>The list's card, built apart from the pop-up so it can also be drawn on its own.</summary>
    public Border MakeDropdownCard()
    {
        var content = new AccountsPanel(model, "Pill", compact: true, reorderable: false, () => { CloseFlyouts(); model.OpenAccounts(); });
        accountScroll = content.Scroll;
        // The pop-up's window clips what is drawn outside it, so the card has room round it for its whole shadow.
        var card = new Border { Name = "PillDropdown", Width = DropdownWidth, MaxHeight = DropdownMaxHeight, Padding = new Thickness(12, 12, 12, 10), CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(1), Child = content, BoxShadow = HangingCard.Shadow, Margin = HangingCard.Room };
        card.Classes.Add("pillBody"); card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("Line"));
        card.PointerEntered += (_, _) => overDrawer = true;
        card.PointerExited += (_, _) => overDrawer = false;
        return card;
    }
    private Grid? dropdownHung; private const string DropdownPoint = "PillDropdownPoint";
    private void BuildDropdown()
    {
        var card = MakeDropdownCard();
        drawer = card;
        panel.PlacementTarget = surface; panel.Placement = PlacementMode.BottomEdgeAlignedLeft;
        panel.PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipY | PopupPositionerConstraintAdjustment.SlideX;
        // A point under the account's box joins the card to it, as the bell's card hangs from the bell. It sits on the pill's lower edge, and is moved under the box each time the list opens.
        dropdownHung = HangingCard.Hang(card, -1, DropdownPoint, "PillBody");
        panel.IsLightDismissEnabled = true; panel.Child = new LayoutTransformControl { LayoutTransform = pillScale, Child = dropdownHung };
        panel.Closed += (_, _) => { overDrawer = false; if (!closingByCode) dismissedAt = Environment.TickCount64; SetOpenLook(false); };
        rootGrid.Children.Add(panel);
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
        // The list slides out from behind the pill, which keeps its own shape, ring and glow: the drawer starts Behind pixels under the pill's right edge and is
        // cut away where the pill's capsule is, so what shows is the part beside it, with its own rounded corners tucked behind the pill's.
        var content = new AccountsPanel(model, "Pill", compact: true, reorderable: false, () => { CloseFlyouts(); model.OpenAccounts(); });
        accountScroll = content.Scroll;
        drawer = new Border { Name = "PillDrawer", Width = DrawerWidth, Height = PillHeight, Padding = new Thickness(Behind + 14, 14, 14, 12), CornerRadius = new CornerRadius(0, PillRadius, PillRadius, 0), Child = content, RenderTransform = drawerSlide };
        drawer.Classes.Add("pillBody");
        drawer.PointerEntered += (_, _) => overDrawer = true;
        drawer.PointerExited += (_, _) => overDrawer = false;
        // The cut-out stays put while the drawer slides, so the pill is never covered on the way.
        var holder = new Grid { Name = "PillDrawerHolder", Width = DrawerWidth, Height = PillHeight, Clip = BehindPill() }; holder.Children.Add(drawer);
        panel.PlacementTarget = surface; panel.Placement = PlacementMode.Right; panel.HorizontalOffset = -Behind * Scale; panel.VerticalOffset = 0;
        panel.PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipX | PopupPositionerConstraintAdjustment.SlideY;
        panel.IsLightDismissEnabled = true; panel.Child = new LayoutTransformControl { LayoutTransform = pillScale, Child = holder };
        panel.Closed += (_, _) => { overDrawer = false; if (!closingByCode) dismissedAt = Environment.TickCount64; SetOpenLook(false); };
        // The popup belongs to the pill's tree, so it inherits its theme.
        rootGrid.Children.Add(panel);
    }
    /// <summary>The drawer's width, beside the pill's own 72.</summary>
    public const double DrawerWidth = 211;
    /// <summary>How far the drawer reaches under the pill's right edge. It is the room the pill keeps clear either side of its controls, so nothing of the pill is under the drawer's window.</summary>
    public const double Behind = 3;
    // The drawer's own area less the pill's capsule, which is where the pill shows through. Drawn a hair inside the pill's edge, so no sliver of desktop shows between.
    public static Geometry BehindPill()
    {
        const double inset = .5; var left = Behind - PillWidth + inset; var right = Behind - inset; var top = inset; var bottom = PillHeight - inset; var r = PillRadius - inset;
        var capsule = new StreamGeometry();
        using (var c = capsule.Open())
        {
            c.BeginFigure(new Point(left + r, top), true);
            c.LineTo(new Point(right - r, top)); c.ArcTo(new Point(right, top + r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(right, bottom - r)); c.ArcTo(new Point(right - r, bottom), new Size(r, r), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(left + r, bottom)); c.ArcTo(new Point(left, bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            c.LineTo(new Point(left, top + r)); c.ArcTo(new Point(left + r, top), new Size(r, r), 0, false, SweepDirection.Clockwise);
            c.EndFigure(true);
        }
        return new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, DrawerWidth, PillHeight)), capsule);
    }
    private readonly TranslateTransform drawerSlide = new();
    private DispatcherTimer? slideTimer;
    // Slides the drawer out from behind the pill, easing out over SlideMilliseconds. The first step is taken on a tick, so where nothing ticks (headless) it simply stands at its place.
    /// <summary>Whether the drawer eases out. The tests switch it off: nothing ticks there, so the drawer would stay where it starts.</summary>
    public static bool SlideEnabled { get; set; } = true;
    /// <summary>How long the drawer takes to ease out; a fifth of a second read as a snap.</summary>
    public const double SlideMilliseconds = 420;
    private void SlideOut()
    {
        slideTimer?.Stop(); var from = -(DrawerWidth - 24); var started = Environment.TickCount64;
        // It starts hidden behind the pill, before it is first drawn, or its first frame would show it fully open.
        if (!SlideEnabled || Motion.Reduced) { drawerSlide.X = 0; return; }
        drawerSlide.X = from;
        slideTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
        slideTimer.Tick += (_, _) =>
        {
            var t = Math.Min(1, (Environment.TickCount64 - started) / SlideMilliseconds); var eased = 1 - Math.Pow(1 - t, 3);
            drawerSlide.X = from * (1 - eased); if (t >= 1) slideTimer?.Stop();
        };
        slideTimer.Start();
    }
    /// <summary>How far the lights sit below the card's top edge, on the pill and on the rail.</summary>
    public const double TopInset = 14;
    /// <summary>The card's edge is this thick in every state, so the contents never move when a ring comes and goes; the column starts under it so the lights sit TopInset from the edge.</summary>
    public const double EdgeThickness = 1.5;
    // While the drawer is open the Accounts button shows pressed; the pill itself keeps its shape and glow.
    private void SetOpenLook(bool open) { face.SetAccountsOpen(open); if (drawerFiller is not null) drawerFiller.IsVisible = open; SyncDrawerRing(); }
    private void SyncDrawerRing()
    {
        if (horizontal || drawer is null || drawerFiller is null) return; // The horizontal pill's list has its own hairline.
        var thickness = surface.BorderThickness.Left;
        // No ring on the drawer's pill side: there it is square and meets the pill (and the square behind it), so the top and bottom rings run on from them.
        drawer.BorderThickness = new Thickness(0, thickness, thickness, thickness);
        drawer.BorderBrush = surface.BorderBrush;
        drawerFiller.BorderThickness = new Thickness(0, thickness, 0, thickness); drawerFiller.BorderBrush = surface.BorderBrush;
    }
    /// <summary>Opens the account drawer beside the pill. It closes when an account is chosen, with Escape, or by pressing the button again.</summary>
    public void ShowPanel()
    {
        SetOpenLook(true);
        if (horizontal)
        {
            // The list hangs below the pill with its right edge level with the pill's, since the card is nearly as wide as the pill and the account's box is near that end.
            var wellLeft = Math.Max(0, HorizontalWidth - DropdownWidth);
            panel.HorizontalOffset = (wellLeft - HangingCard.Room.Left) * Scale; panel.VerticalOffset = (DropdownGap - HangingCard.Room.Top) * Scale;
            if (dropdownHung is not null && face.AccountsButton.TranslatePoint(new Point(face.AccountsButton.Bounds.Width / 2, 0), surface) is { } centre)
                HangingCard.MovePoint(dropdownHung, DropdownPoint, HangingCard.ClampFromRight(DropdownWidth - (centre.X - wellLeft), DropdownWidth, 24));
        }
        else { panel.HorizontalOffset = -Behind * Scale; SlideOut(); }
        panel.IsOpen = true;
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
        // The shape chosen in settings takes effect straight away too, where the pill stands.
        if (!closedWindow && model.PillHorizontal != horizontal) { SwitchShape(); return; }
        // A new pill size from settings takes effect straight away, even while the pill is hidden behind the dashboard.
        if (model.PillScalePercent != appliedChoice) FitToScreen(Screens.ScreenFromWindow(this));
        Refresh();
    }
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
        var (key, _) = StatusFace.State(model);
        var stateColour = key == "Idle" ? Colors.Gray : Ui.PillStateColour(this, key);

        // A ring and a glow in the state's colour round the pill while it is doing something, and the plain edge and shadow when it is idle.
        // The same thickness in every state: a thicker edge only while something is happening moved the contents by half a pixel, which showed as a jump.
        surface.BorderThickness = new Thickness(EdgeThickness);
        surface.BorderBrush = key == "Idle" ? (this.TryFindResource("Line", ActualThemeVariant, out var line) && line is IBrush lineBrush ? lineBrush : Brushes.Gray) : new SolidColorBrush(Color.FromArgb(179, stateColour.R, stateColour.G, stateColour.B));
        if (drawer is not null) SyncDrawerRing();
        var shadow = (model.Shadows, key, ActualThemeVariant);
        if (lastShadow != shadow) { lastShadow = shadow; Ui.SetPillShadow(shadowHost, model.Shadows, key == "Idle" ? null : key); }
    }
}
