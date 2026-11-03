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
/// The pill: a small card that stays on top. It shows what the timer is doing with a state light (and a second, blue one only while a reminder is due), the
/// day clock, a tile that carries the account's initials and pauses or resumes, the account's box and the amber tile that starts a break. Clicking the time
/// opens the dashboard. The account's box hangs a list of accounts below the pill; it stays open while accounts are chosen, so a wrong choice can be put right
/// at once, and the box, Escape or a click elsewhere folds it away.
/// </summary>
public sealed class PillWindow : Window
{
    public Action? OpenDashboard { get; set; }
    private readonly TimerModel model;
    /// <summary>The pill's own size before it is scaled for the screen: 336 wide and 72 tall, a card with the dashboard's 20 pixel corner. Its contents start 20 pixels from the left and end 14 from the right.</summary>
    public const double PillWidth = 406, PillHeight = 72, PillRadius = 20, InsetLeft = 10, InsetRight = 14;
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
    public double WindowHeightNow => (PillHeight + 2 * Ui.ShadowMargin) * Scale;
    /// <summary>The card's own size as drawn (the window less the room round it), and its corner radius, for the dashboard to grow from.</summary>
    public Size CardSizeNow => new(PillWidth * Scale, PillHeight * Scale);
    public double CardRadiusNow => PillRadius * Scale;
    /// <summary>The window width at the size the pill is drawn on this screen.</summary>
    public double WindowWidthNow => (PillWidth + 2 * Ui.ShadowMargin) * Scale;

    private StatusFace face = null!;
    private Border surface = null!, shadowHost = null!;
    private Popup panel = new();
    private Popup alertsPanel = new();
    private PillAlertsCard? alertsCard; private Grid? alertsHung;
    private readonly HashSet<string> peeked = new();
    private DispatcherTimer? foldTimer;
    private long alertsDismissedAt;
    private Grid rootGrid = new();
    private ScrollViewer accountScroll = null!;
    private bool overList, closedWindow, ensuring, closingByCode;
    private long dismissedAt;
    private DispatcherTimer? settleTimer;
    private int? appliedChoice;
    private (PixelRect Bounds, double Scaling)? lastScreen;
    private (bool, string, ThemeVariant)? lastShadow;

    /// <summary>Whether the account list is open below the pill.</summary>
    public bool PanelOpen => panel.IsOpen;
    /// <summary>Whether the alerts card (a card, the list or one opened from it) is hanging below the pill.</summary>
    public bool AlertsOpen => alertsPanel.IsOpen;
    /// <summary>The alerts card while it is open, so tests and the keyboard can reach it.</summary>
    public PillAlertsCard? AlertsCard => alertsCard;
    /// <summary>The light button that opens what is waiting.</summary>
    public Button AlertsButton => face.AlertsButton!;
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
        Topmost = model.PillOnTop;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;

        Build();
        Width = WindowWidthNow; Height = WindowHeightNow;
        Opened += (_, _) => FitToScreen(Screens.ScreenFromWindow(this));
        PositionChanged += (_, _) => { FitIfScreenChanged(); SettleSoon(); };
        ScalingChanged += (_, _) => FitIfScreenChanged();
        Closed += (_, _) => { closedWindow = true; settleTimer?.Stop(); foldTimer?.Stop(); };
        // Whenever the real size is not the wanted one, say after a drag or a change made while hidden, it is put right.
        SizeChanged += (_, _) => { if (IsVisible && !ensuring && !closedWindow && (Math.Abs(ClientSize.Width - WindowWidthNow) > 1.5 || Math.Abs(ClientSize.Height - WindowHeightNow) > 1.5)) EnsureSize(); };
        Activated += (_, _) => EnsureSize();
        Deactivated += (_, _) => { if (!overList) CloseFlyouts(); };
        KeyDown += (_, e) =>
        {
            if (AlertsOpen && alertsCard is not null && alertsCard.HandleKey(e.Key)) e.Handled = true;
            else if (e.Key == Avalonia.Input.Key.Escape && PanelOpen) { CloseFlyouts(); e.Handled = true; }
        };
        peeked.UnionWith(model.Alerts.Select(a => a.Key)); // What was already waiting when the pill came up is not shown again by itself.
        model.PropertyChanged += Changed; PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) ShowToast(); };
        ActualThemeVariantChanged += (_, _) => { lastShadow = null; Refresh(); };
        Closed += (_, _) => model.PropertyChanged -= Changed;
        Refresh();
    }

    // ---- the card
    /// <summary>Builds the pill's card and the account list that hangs below it.</summary>
    private void Build()
    {
        // The top of the pill is shared with the dashboard's sidebar.
        face = new StatusFace(model, "Pill", "PillBreak", "Open dashboard", () => OpenDashboard?.Invoke(), OnCircle, ToggleAccounts, alertsClick: ToggleAlerts);
        var root = rootGrid = new Grid { Background = Brushes.Transparent };
        // Along the card, centred on its height, with the room kept clear at each end (the card's edge is part of it).
        face.Margin = new Thickness(InsetLeft - EdgeThickness, 0, InsetRight - EdgeThickness, 0); face.HorizontalAlignment = HorizontalAlignment.Left; face.VerticalAlignment = VerticalAlignment.Center;
        root.Children.Add(face);
        Ui.PillGestures(root, this);
        surface = new Border { Name = "PillSurface", Child = root, ClipToBounds = true, Width = PillWidth, Height = PillHeight, BorderThickness = new Thickness(EdgeThickness) };
        surface.Classes.Add("pillBody");
        shadowHost = Ui.Shadowed(surface, PillRadius);
        Content = new LayoutTransformControl { LayoutTransform = pillScale, Child = shadowHost };
        panel = new Popup();
        BuildDropdown();
        BuildAlerts();
        BuildToast();
        lastShadow = null;
    }
    /// <summary>The account list, a card that hangs below the account's box and scrolls inside.</summary>
    public const double DropdownWidth = 224, DropdownMaxHeight = 380, DropdownGap = 11;
    /// <summary>The list's card, built apart from the pop-up so it can also be drawn on its own.</summary>
    public Border MakeDropdownCard()
    {
        var content = new AccountsPanel(model, "Pill", compact: true, reorderable: false, () => { CloseFlyouts(); model.OpenAccounts(); });
        accountScroll = content.Scroll;
        // The pop-up's window clips what is drawn outside it, so the card has room round it for its whole shadow.
        var card = new Border { Name = "PillDropdown", Width = DropdownWidth, MaxHeight = DropdownMaxHeight, Padding = new Thickness(12, 12, 12, 10), CornerRadius = new CornerRadius(PillRadius), BorderThickness = new Thickness(1), Child = content, BoxShadow = HangingCard.Shadow, Margin = HangingCard.Room };
        card.Classes.Add("pillBody"); card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("Line"));
        card.PointerEntered += (_, _) => overList = true;
        card.PointerExited += (_, _) => overList = false;
        // A list long enough to scroll keeps a 10 pixel track on its right, which with the card's own padding left a wide empty strip beside the play buttons. The track
        // takes that padding's place while it shows; a short list has no track and keeps the even padding.
        void FitTrack() => card.Padding = content.Scroll.Extent.Height > content.Scroll.Viewport.Height + .5 ? new Thickness(12, 12, 2, 10) : new Thickness(12, 12, 12, 10);
        content.Scroll.PropertyChanged += (_, e) => { if (e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.ViewportProperty) FitTrack(); };
        return card;
    }
    private Grid? dropdownHung; private const string DropdownPoint = "PillDropdownPoint";
    private void BuildDropdown()
    {
        var card = MakeDropdownCard();
        panel.PlacementTarget = surface; panel.Placement = PlacementMode.BottomEdgeAlignedLeft;
        panel.PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipY | PopupPositionerConstraintAdjustment.SlideX;
        // A point under the account's box joins the card to it, as the bell's card hangs from the bell. It sits on the pill's lower edge, and is moved under the box each time the list opens.
        dropdownHung = HangingCard.Hang(card, -1, DropdownPoint, "PillBody");
        panel.IsLightDismissEnabled = true; panel.Child = new LayoutTransformControl { LayoutTransform = pillScale, Child = dropdownHung };
        panel.Closed += (_, _) => { overList = false; if (!closingByCode) dismissedAt = Environment.TickCount64; SetOpenLook(false); };
        rootGrid.Children.Add(panel);
    }

    // ---- a failed save
    private Popup toastPanel = new(); private MessageBar? toast;
    /// <summary>The pill has no page to show a message on, so a save that failed (an account started or paused here that could not be written) is shown by the same toast the dashboard
    /// uses, hung above the pill (below it when there is no room above). It stays until a save works or the cross is pressed, and says nothing else.</summary>
    private void BuildToast()
    {
        toast = new MessageBar(model, failuresOnly: true);
        toastPanel = new Popup { PlacementTarget = surface, Placement = PlacementMode.TopEdgeAlignedLeft, IsLightDismissEnabled = false, Name = "PillToastPopup" };
        toastPanel.PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipY | PopupPositionerConstraintAdjustment.SlideX;
        toastPanel.Child = new LayoutTransformControl { LayoutTransform = pillScale, Child = new Border { Padding = new Thickness(18), Child = toast } };
        rootGrid.Children.Add(toastPanel);
        toast.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) ShowToast(); };
        ShowToast();
    }
    /// <summary>Opens the toast while a failure is showing and the pill is, and puts it away otherwise.</summary>
    public void ShowToast()
    {
        if (toast is null) return;
        var show = toast.IsVisible && IsVisible && !closedWindow;
        if (show) toastPanel.VerticalOffset = -DropdownGap * Scale;
        if (toastPanel.IsOpen != show) toastPanel.IsOpen = show;
    }
    /// <summary>Whether the pill's toast is open (for tests, which have no pop-up window).</summary>
    public bool ToastOpen => toastPanel.IsOpen;
    public MessageBar? PillToast => toast;

    // ---- alerts: what is waiting
    /// <summary>The alerts card's width and the gap under the pill match the account list's, so the two hang the same way.</summary>
    private void BuildAlerts()
    {
        alertsPanel = new Popup { PlacementTarget = surface, Placement = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = true };
        alertsPanel.PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipY | PopupPositionerConstraintAdjustment.SlideX;
        alertsPanel.Closed += (_, _) => { foldTimer?.Stop(); if (!closingByCode) alertsDismissedAt = Environment.TickCount64; overList = false; };
        rootGrid.Children.Add(alertsPanel);
    }
    /// <summary>Opens the card under the pill: the most urgent alert, or the whole list (with <paramref name="list"/>, which is also what an empty pill shows).</summary>
    public void ShowAlerts(bool list = false)
    {
        if (panel.IsOpen) { closingByCode = true; try { panel.IsOpen = false; } finally { closingByCode = false; } SetOpenLook(false); }
        var wasOpen = alertsPanel.IsOpen;
        if (alertsCard is null)
        {
            alertsCard = new PillAlertsCard(model, FoldAlerts, () => OpenDashboard?.Invoke());
            alertsCard.PointerEntered += (_, _) => overList = true; alertsCard.PointerExited += (_, _) => overList = false;
            alertsCard.Emptied += () => FoldAlerts();
            alertsHung = HangingCard.Hang(alertsCard, -1, AlertsPoint, "PillBody");
            alertsPanel.Child = new LayoutTransformControl { LayoutTransform = pillScale, Child = alertsHung };
        }
        alertsCard.Open(list || model.Alerts.Count == 0);
        // The card lines up with the pill's left edge, under the lights, and its point sits under them.
        alertsPanel.HorizontalOffset = -HangingCard.Room.Left * Scale; alertsPanel.VerticalOffset = (DropdownGap - HangingCard.Room.Top) * Scale;
        // Its point sits under the count, which is what opens it.
        if (alertsHung is not null && face.AlertsButton is { } countButton && countButton.TranslatePoint(new Point(countButton.Bounds.Width / 2, 0), surface) is { } under)
            HangingCard.MovePoint(alertsHung, AlertsPoint, HangingCard.ClampFromRight(PillAlertsCard.CardWidth - under.X, PillAlertsCard.CardWidth, PillRadius));
        alertsPanel.IsOpen = true;
        if (!wasOpen) StartFoldTimer();
    }
    private const string AlertsPoint = "PillAlertsPoint";
    /// <summary>Puts the alerts card away. The light keeps showing what is waiting.</summary>
    public void FoldAlerts() { foldTimer?.Stop(); closingByCode = true; try { alertsPanel.IsOpen = false; } finally { closingByCode = false; } overList = false; }
    private void ToggleAlerts()
    {
        if (alertsPanel.IsOpen) { FoldAlerts(); return; }
        if (Environment.TickCount64 - alertsDismissedAt < 300) return;
        // Pressing the light is asking: under Light only it opens the list at once, as it does when nothing is waiting.
        peekOpened = false; ShowAlerts(model.PillAlertsMode != "peek");
    }
    // A card that opened by itself folds after twelve seconds, except a spent budget, which stays until it is dealt with.
    public static readonly TimeSpan FoldAfter = TimeSpan.FromSeconds(12);
    private void StartFoldTimer()
    {
        foldTimer ??= new DispatcherTimer { Interval = FoldAfter };
        foldTimer.Stop(); foldTimer.Tick -= OnFold; foldTimer.Tick += OnFold;
        if (!peekOpened) return; foldTimer.Start();
    }
    private bool peekOpened;
    private void OnFold(object? sender, EventArgs e) { foldTimer?.Stop(); if (alertsCard is { View: "card" } card && card.Items.Any(i => i.Kind == AlertKind.BudgetSpent)) return; if (!overList) FoldAlerts(); else foldTimer?.Start(); }
    /// <summary>
    /// Opens the card by itself when something new is waiting (under Peek, with the pill showing and no other list open). What was already waiting, or came while the
    /// dashboard was up, is not shown again; an alert that goes away and comes back (a break stayed on, then asked again) is new.
    /// </summary>
    public void PeekAtNewAlerts()
    {
        var current = model.Alerts; var keys = current.Select(a => a.Key).ToHashSet();
        peeked.RemoveWhere(k => !keys.Contains(k));
        var fresh = current.Where(a => !peeked.Contains(a.Key)).ToList();
        foreach (var a in fresh) peeked.Add(a.Key);
        if (fresh.Count == 0 || model.PillAlertsMode != "peek" || !IsVisible || closedWindow || alertsPanel.IsOpen || panel.IsOpen) return;
        peekOpened = true; ShowAlerts(false);
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

    // ---- the account list
        /// <summary>The card's edge is this thick in every state, so the contents never move when a ring comes and goes; the face starts inside it.</summary>
    public const double EdgeThickness = 1.5;
    // While the list is open the Accounts button shows pressed; the pill itself keeps its shape and glow.
    private void SetOpenLook(bool open) => face.SetAccountsOpen(open);
    /// <summary>Opens the account list below the pill. It closes with Escape, a click elsewhere, or by pressing the button again.</summary>
    public void ShowPanel()
    {
        FoldAlerts(); SetOpenLook(true);
        // The list hangs below the pill with its right edge level with the pill's, since the card is nearly as wide as the pill and the account's box is near that end.
        var wellLeft = Math.Max(0, PillWidth - DropdownWidth);
        panel.HorizontalOffset = (wellLeft - HangingCard.Room.Left) * Scale; panel.VerticalOffset = (DropdownGap - HangingCard.Room.Top) * Scale;
        if (dropdownHung is not null && face.AccountsButton.TranslatePoint(new Point(face.AccountsButton.Bounds.Width / 2, 0), surface) is { } centre)
            HangingCard.MovePoint(dropdownHung, DropdownPoint, HangingCard.ClampFromRight(DropdownWidth - (centre.X - wellLeft), DropdownWidth, PillRadius));
        panel.IsOpen = true;
    }
    // Pressing the button while the list is open closes it. Light dismiss has already closed it by the time the click arrives, so a close that
    // just happened from the same press is not undone by opening it again.
    private void ToggleAccounts()
    {
        if (panel.IsOpen) { CloseFlyouts(); return; }
        if (Environment.TickCount64 - dismissedAt < 300) return;
        ShowPanel();
    }
    /// <summary>Puts the account list away, so the pill is a plain capsule again when it comes back from the dashboard.</summary>
    public void CloseFlyouts() { closingByCode = true; try { panel.IsOpen = false; } finally { closingByCode = false; } FoldAlerts(); overList = false; SetOpenLook(false); }

    // ---- behaviour
    private void OnCircle()
    {
        var chosen = model.Accounts.Any(a => a.Name == model.SelectedAccount);
        // With nothing to run or resume, the list opens so an account can be picked.
        if (model.IsTracking || chosen) model.TogglePause(); else ShowPanel();
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Topmost != model.PillOnTop) Topmost = model.PillOnTop;
        // A new pill size from settings takes effect straight away, even while the pill is hidden behind the dashboard.
        if (model.PillScalePercent != appliedChoice) FitToScreen(Screens.ScreenFromWindow(this));
        Refresh(); PeekAtNewAlerts();
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
        var shadow = (model.Shadows, key, ActualThemeVariant);
        if (lastShadow != shadow) { lastShadow = shadow; Ui.SetPillShadow(shadowHost, model.Shadows, key == "Idle" ? null : key); }
    }
}
