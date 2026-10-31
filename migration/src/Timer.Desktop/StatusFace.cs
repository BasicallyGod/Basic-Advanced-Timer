using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>A clock that can be set from one hours:minutes:seconds string, whichever way it is drawn.</summary>
public interface IClockText { string Text { get; set; } }

/// <summary>Small helpers shared by the pill's controls, which draw soft tints of their colours and a ring for keyboard focus.</summary>
public static class SoftTint
{
    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);
    public static bool IsDark(Control c) => c.ActualThemeVariant == ThemeVariant.Dark;
    /// <summary>The ring round a control that has keyboard focus (not mouse focus): a gap in the surface colour, then the accent.</summary>
    public static BoxShadows FocusRing(Control c)
    {
        var gap = Ui.ThemeColour(c, "Surface", Colors.White); var accent = Ui.ThemeColour(c, "Accent", Colors.Blue);
        return new BoxShadows(new BoxShadow { Spread = 2, Color = gap }, new[] { new BoxShadow { Spread = 4, Color = accent } });
    }
    /// <summary>Tracks whether the button has keyboard focus, and calls <paramref name="changed"/> when that changes.</summary>
    public static void WatchKeyboardFocus(Button button, Action<bool> changed)
    {
        button.GotFocus += (_, e) => changed(e.NavigationMethod is Avalonia.Input.NavigationMethod.Tab or Avalonia.Input.NavigationMethod.Directional);
        button.LostFocus += (_, _) => changed(false);
    }
}

/// <summary>
/// The pill's face, drawn once for the pill and for the dashboard's sidebar: the state light (and the blue reminder light while one is due), the day clock
/// that moves between the two windows, the primary control that carries the account's initials and pauses or resumes, the Accounts button and the break.
/// Every control is a soft tint of its colour with a stronger tint under the pointer. The owner calls <see cref="Refresh"/> when the model changes, and the
/// names of the controls start with the prefix it is given (Pill, Setup or Sidebar).
/// </summary>
public sealed class StatusFace : StackPanel
{
    private readonly TimerModel model;
    private readonly IClockText clock;
    private readonly Border stateLight;
    private readonly SegmentRun alertCountRun = new() { DigitHeight = CountDigitHeight, Text = "  " };
    private readonly IconGlyph bell = new(IconKind.Bell);
    private readonly Border displayBox;
    private int shownCount = -1;
    /// <summary>How many alerts are waiting (none while Pill alerts is off), as of the last refresh.</summary>
    public int AlertCount { get; private set; }
    private readonly TextBlock initials = new() { FontSize = 17, Classes = { "clock" }, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly PauseGlyph glyph = new();
    /// <summary>The primary control, the break and the Accounts button are all this square, so they read as one set with the same room either side in the pill.</summary>
    public const double TileSize = 44, TileRadius = 22;
    /// <summary>
    /// The primary control and the Accounts button sit together on one soft well (they are about what you are tracking: one pauses it, the other changes it), so each is a
    /// little wider than the break's tile, 46 by 44, with 4 pixels of well round them and 2 between them. The well is 54 wide and 98 tall, the same height as the two tiles
    /// and the gap they took before, so the pill and the rail keep their size.
    /// </summary>
    public const double WellTileWidth = 44, WellWidth = 54, WellPadding = 4, WellGap = 2, WellRadius = 26;
    /// <summary>
    /// Each tile's button is this much larger on every side and pulled back by the same amount, so the ring that marks keyboard focus or a break is drawn
    /// inside the button (a button clips what falls outside it) without moving the tile or its neighbours.
    /// </summary>
    public const double RingRoom = 4;
    public static void MakeRoomForRing(Button button, double top = 0, double width = TileSize)
    {
        button.Width = width + 2 * RingRoom; button.Height = TileSize + 2 * RingRoom; button.Padding = new Thickness(RingRoom);
        button.Margin = new Thickness(-RingRoom, top - RingRoom, -RingRoom, -RingRoom);
        button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center;
    }
    /// <summary>Whether this is the dashboard's sidebar face: the lights and the clock centred, then one tracking card (play or pause, the account and how long it has run, the break).</summary>
    public bool Sidebar { get; }
    private readonly TextBlock whoName = new() { FontSize = 13.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock whoStatus = new() { FontSize = 11.5, Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Ellipse whoDot = new() { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center };
    /// <summary>The pill's account box (only a dot and a chevron: the name is its tooltip and the list) and the day clock's box are these widths, so the pill keeps one size whatever the time.</summary>
    public const double SelectorWidth = 50;
    /// <summary>The display window is as tall as the well beside it, with its digits 28 tall and the count's 15; the count's column is 24 wide.</summary>
    public const double DisplayHeight = 52, CountDigitHeight = 15, CountColumn = 24, DisplayPadding = 10, DividerGap = 9;
    /// <summary>The window's width, which is one size whatever the time or the count: padding, the LED, a divider, the clock, a divider and the count's column.</summary>
    public static double DisplayWidth => 2 * DisplayPadding + 6 + 2 * (2 * DividerGap + 1) + SegmentClock.Width + CountColumn;
    private readonly Ellipse selectorDot = new() { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly Chevron selectorChevron = new() { VerticalAlignment = VerticalAlignment.Center };
    private Border selectorTile = null!;
    private string selectorWords = "";
    // The primary control is a round key: raised on the well it sits in, the way the break is a round key outside it.
    private readonly Border primaryTile = new() { Width = StatusFace.WellTileWidth, Height = StatusFace.TileSize, CornerRadius = new CornerRadius(StatusFace.TileRadius) };
    private readonly Viewbox glyphBox = new() { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly string clockTip;
    private bool hoverCircle, circleFocus, accountsFocus;
    /// <summary>The display window the state LED, the clock and the alert count sit in.</summary>
    public Border Display => displayBox;
    /// <summary>The state LED, a capsule in the window: lit in the state's colour, an empty groove when idle.</summary>
    public Border StateLight => stateLight;
    /// <summary>The alert count's two digits.</summary>
    public SegmentRun AlertCountDigits => alertCountRun;
    public Button ClockButton { get; }
    /// <summary>The lights as a button that opens the pill's alerts; null on the sidebar.</summary>
    public Button? AlertsButton { get; }
    public Button Circle { get; }
    public Button AccountsButton { get; }
    public BreakButton Break { get; }
    private bool accountsOpen, hoverAccounts; private double accountsAmount;
    private readonly Border well = new() { Width = StatusFace.WellWidth, CornerRadius = new CornerRadius(StatusFace.WellRadius), Padding = new Thickness(StatusFace.WellPadding), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
    /// <summary>The soft background the primary control and the Accounts button share.</summary>
    public Border Well => well;

    public StatusFace(TimerModel model, string prefix, string breakName, string clockTip, Action clockClick, Action circleClick, Action accountsClick, bool sidebar = false, Action? alertsClick = null)
    {
        this.model = model; this.clockTip = clockTip; Sidebar = sidebar;
        clock = new SegmentClock();
        Spacing = 12; HorizontalAlignment = HorizontalAlignment.Center; if (sidebar) { Spacing = 6; HorizontalAlignment = HorizontalAlignment.Stretch; } else { Orientation = Orientation.Horizontal; VerticalAlignment = VerticalAlignment.Center; }
        // The state LED is a capsule set into the display, like a segment: an empty groove when idle, inlaid in the state's colour when not.
        stateLight = new Border { Name = prefix + "StateLight", Width = 6, Height = 14, CornerRadius = new CornerRadius(3.5), VerticalAlignment = VerticalAlignment.Center };
        // The clock opens the dashboard (and collapses it again), as it always has; it is one width whatever the time.
        ClockButton = Ui.Button("", clockClick, prefix + "Dashboard");
        ClockButton.Classes.Add("pillBare"); ClockButton.Padding = new Thickness(0); ClockButton.Content = (Control)clock; ClockButton.HorizontalAlignment = HorizontalAlignment.Center; ClockButton.VerticalAlignment = VerticalAlignment.Center;
        ClockButton.PointerEntered += (_, _) => ((Control)clock).Opacity = .8;
        ClockButton.PointerExited += (_, _) => ((Control)clock).Opacity = 1;
        ToolTip.SetTip(ClockButton, clockTip); Avalonia.Automation.AutomationProperties.SetName(ClockButton, clockTip);
        // The alert count: a small bell over two segment digits, in the colour of the most urgent thing waiting. Pressing it opens what is waiting (on the pill).
        alertCountRun.Name = prefix + "AlertCount";
        var countStack = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = CountColumn, Name = prefix + "AlertCountStack" };
        countStack.Children.Add(new Viewbox { Width = 9, Height = 9, HorizontalAlignment = HorizontalAlignment.Center, Child = bell }); countStack.Children.Add(new Border { Child = alertCountRun, HorizontalAlignment = HorizontalAlignment.Center });
        Control countHost;
        if (alertsClick is not null)
        {
            AlertsButton = Ui.Button("", alertsClick, prefix + "AlertsButton"); AlertsButton.Classes.Add("pillBare"); AlertsButton.Padding = new Thickness(0, 6); AlertsButton.Margin = new Thickness(0, -6);
            AlertsButton.Content = countStack; AlertsButton.VerticalAlignment = VerticalAlignment.Center; AlertsButton.HorizontalContentAlignment = HorizontalAlignment.Center; countHost = AlertsButton;
        }
        else countHost = countStack;
        Border Divider() { var d = new Border { Width = 1, Height = 28, Margin = new Thickness(DividerGap, 0), VerticalAlignment = VerticalAlignment.Center }; d.Bind(Border.BackgroundProperty, d.GetResourceObservable("DisplayEdge")); return d; }
        var inside = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 0 };
        inside.Children.Add(stateLight); inside.Children.Add(Divider()); inside.Children.Add(ClockButton); inside.Children.Add(Divider()); inside.Children.Add(countHost);
        displayBox = new Border { Name = prefix + "Display", Height = DisplayHeight, CornerRadius = new CornerRadius(14), Padding = new Thickness(DisplayPadding, 0), Child = inside, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        displayBox.Bind(Border.BackgroundProperty, displayBox.GetResourceObservable("DisplayWell"));
        // The primary control carries the running account's initials, and pauses or resumes. The initials give way to the symbol under the pointer.
        glyphBox.Child = glyph; glyph.Fixed = Brushes.Black;
        primaryTile.Name = prefix + "PrimaryTile";
        var host = new Grid(); host.Children.Add(initials); host.Children.Add(glyphBox); primaryTile.Child = host;
        Circle = Ui.Button("", circleClick, prefix + "Pause");
        Circle.Classes.Add("pillBare"); Circle.Content = primaryTile; MakeRoomForRing(Circle, 0, WellTileWidth); Circle.HorizontalAlignment = HorizontalAlignment.Center;
        // Under the pointer the tint deepens instead of the button growing, which its edges would clip.
        Circle.PointerEntered += (_, _) => { hoverCircle = true; Refresh(); };
        Circle.PointerExited += (_, _) => { hoverCircle = false; Refresh(); };
        SoftTint.WatchKeyboardFocus(Circle, f => { circleFocus = f; Refresh(); });
        Break = new BreakButton(model, breakName);
        // The accounts button is the same on the pill and the rail: it opens the account list beside the window, and closes it again.
        AccountsButton = Ui.Button("", accountsClick, prefix + "Accounts");
        AccountsButton.Classes.Add("pillBare");
        // The account box: the state dot and a chevron in one tile on the well. The account's name is its tooltip and the list it opens, since the tile beside it
        // already carries the initials while one runs.
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { selectorDot, selectorChevron } };
        selectorTile = new Border { Name = prefix + "AccountSelector", Width = SelectorWidth, Height = TileSize, CornerRadius = new CornerRadius(TileRadius), Background = Brushes.Transparent, Child = line };
        AccountsButton.Content = selectorTile; MakeRoomForRing(AccountsButton, 0, SelectorWidth);
        AccountsButton.HorizontalAlignment = HorizontalAlignment.Center;
        AccountsButton.PointerEntered += (_, _) => { hoverAccounts = true; PaintAccountsTile(); };
        AccountsButton.PointerExited += (_, _) => { hoverAccounts = false; PaintAccountsTile(); };
        SoftTint.WatchKeyboardFocus(AccountsButton, f => { accountsFocus = f; PaintAccountsTile(); });
        SetAccountsOpen(false);
        // The primary control and the Accounts button share one well.
        well.Name = prefix + "Well";
        if (!sidebar)
        {
            // Along the pill: the display (LED, clock and count), the well (the play tile, then the account), and the break at the end.
            well.Width = double.NaN; well.Height = TileSize + 2 * WellPadding; well.Margin = default; well.VerticalAlignment = VerticalAlignment.Center;
            well.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = WellGap, Children = { Circle, AccountsButton } };
            Break.VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            // One tracking card: pause or play at its left end, the break at its right, and between them the account's colour and name with how long it has run.
            well.Width = double.NaN; well.Height = double.NaN; well.Margin = new Thickness(0, 6, 0, 4); well.Padding = new Thickness(6); well.HorizontalAlignment = HorizontalAlignment.Stretch;
            // The words are left aligned in the room between the two tiles, so the dot stays at one left edge whatever the name's length (centred, it moved with every
            // name, which read as a bounce when the account changed). The name is in a grid so a long one is cut with an ellipsis instead of widening the card.
            var who = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(8, 0, 0, 0) };
            var nameLine = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 7 };
            whoName.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(whoName, 1); nameLine.Children.Add(whoDot); nameLine.Children.Add(whoName);
            // The status sits under the name, not the dot.
            whoStatus.HorizontalAlignment = HorizontalAlignment.Left; whoStatus.TextAlignment = TextAlignment.Left; whoStatus.Margin = new Thickness(16, 0, 0, 0); whoStatus.TextTrimming = TextTrimming.CharacterEllipsis;
            who.Children.Add(nameLine); who.Children.Add(whoStatus); whoName.Name = prefix + "WhoName"; whoStatus.Name = prefix + "WhoStatus";
            // The well holds the play key and the account's name; the break is its own round key outside it, as on the pill (the mock draws the dashboard the same way).
            var card = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 4 };
            Grid.SetColumn(who, 1);
            card.Children.Add(Circle); card.Children.Add(who);
            well.Child = card; well.CornerRadius = new CornerRadius(WellRadius); well.Padding = new Thickness(WellPadding, WellPadding, 8, WellPadding);
            Break.VerticalAlignment = VerticalAlignment.Center;
        }
        Children.Add(displayBox);
        if (sidebar)
        {
            var track = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 5, Margin = well.Margin } /* the break key clear of the well */; well.Margin = default;
            Grid.SetColumn(Break, 1); track.Children.Add(well); track.Children.Add(Break); Children.Add(track);
        }
        else { Children.Add(well); Children.Add(Break); }
        ActualThemeVariantChanged += (_, _) => Refresh();
        Refresh();
    }

    // The sidebar's tracking card: the account being tracked (or the one a press would start) by its colour and name, and what it is doing.
    private void RefreshWho(bool onBreak, TimerAccount? selected, string? who, Color stateColour)
    {
        var account = model.Accounts.FirstOrDefault(a => a.Name == who);
        var name = onBreak ? "Break" : who is null ? "Choose an account" : account?.Display ?? who;
        whoName.Text = name;
        whoDot.Fill = onBreak ? new SolidColorBrush(ThemeColourOr("Break", "#F5A524")) : account is null ? new SolidColorBrush(ThemeColourOr("Idle", "#8A8F98")) : Brush.Parse(Ui.Visible(account.Colour));
        whoStatus.Text = onBreak ? "On break" + (model.ResumeAccount is { } back ? " · back to " + (model.Accounts.FirstOrDefault(a => a.Name == back)?.Display ?? back) : "")
            : model.IsTracking ? "" : who is null ? "Pick one below" : "Ready to track";
        // While tracking, the play or pause tile and the state light already say so, so the line is left out and the name stands alone, centred in the card.
        whoStatus.IsVisible = whoStatus.Text.Length > 0;
    }

    /// <summary>Marks the accounts button as pressed while the list is open, and words its tooltip for what pressing it will do.</summary>
    public void SetAccountsOpen(bool open, bool wash = true)
    {
        accountsOpen = open; if (wash) accountsAmount = open ? 1 : 0; PaintAccountsTile();
        var tip = open ? "Close the accounts" : "Accounts";
        ToolTip.SetTip(AccountsButton, tip);
        // The button also says which account it shows, since the pill has no room for the name.
        if (!open && selectorWords.Length > 0) tip = selectorWords;
        ToolTip.SetTip(AccountsButton, tip);
        Avalonia.Automation.AutomationProperties.SetName(AccountsButton, selectorWords.Length > 0 ? (open ? "Close the accounts" : "Accounts") + ", " + selectorWords : tip);
        selectorChevron.Expanded = open; selectorChevron.InvalidateVisual();
    }
    /// <summary>How much of the blue wash the accounts button wears, 0 to 1, while the dashboard's panel eases out or away, so the button and the panel move as one.</summary>
    public void SetAccountsAmount(double amount) { accountsAmount = Math.Clamp(amount, 0, 1); PaintAccountsTile(); }
    // Bare on the well at rest, the theme's hover under the pointer, and a blue wash while the list is open.
    private void PaintAccountsTile()
    {
        var dark = SoftTint.IsDark(this);
        var accent = ThemeColourOr("Accent", "#3558C8"); var ink = ThemeColourOr("Ink", "#18233A");
        well.Background = well.TryFindResource("Well", well.ActualThemeVariant, out var wellBrush) && wellBrush is IBrush wb ? wb : new SolidColorBrush(SoftTint.WithAlpha(ink, dark ? .07 : .055));
        well.BoxShadow = Res(well, "ShadowSunk");
        // The selector is bare on the well until the pointer is over it.
        IBrush rest = Brushes.Transparent;
        var tile = selectorTile;
        tile.Background = accountsAmount > 0 ? new SolidColorBrush(SoftTint.WithAlpha(accent, (dark ? .22 : .14) * accountsAmount))
            : hoverAccounts ? (tile.TryFindResource("Hover", tile.ActualThemeVariant, out var hover) && hover is IBrush hoverBrush ? hoverBrush : rest) : rest;
        tile.BoxShadow = accountsFocus ? SoftTint.FocusRing(tile) : default;
    }
    /// <summary>A shadow set the theme writes (raised, sunk and so on), or none before the window has a theme.</summary>
    public static BoxShadows Res(Control control, string key) => control.TryFindResource(key, control.ActualThemeVariant, out var found) && found is BoxShadows shadows ? shadows : default;
    private Color ThemeColourOr(string key, string fallback) => Ui.ThemeColour(this, key, Color.Parse(fallback));
    /// <summary>The state's key (Break outranks Live, else Idle) and its words, shared with the owner's ring and glow.</summary>
    public static (string Key, string Name) State(TimerModel model)
    {
        var onBreak = model.IsOnBreak; var live = model.IsTracking && !onBreak;
        return (onBreak ? "Break" : live ? "Live" : "Idle", onBreak ? "On break" : live ? "Recording" : "Not recording");
    }
    // A new arrival makes the count flash twice, and nothing flashes when Windows animations are off.
    private void FlashCount()
    {
        var host = (Control)alertCountRun; var steps = 0;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(170) };
        timer.Tick += (_, _) => { steps++; host.Opacity = steps % 2 == 1 ? .25 : 1; if (steps >= 4) { host.Opacity = 1; timer.Stop(); } };
        timer.Start();
    }
    public void Refresh()
    {
        var onBreak = model.IsOnBreak;
        var (key, stateName) = State(model);
        var dark = SoftTint.IsDark(this);
        // The light's colour is the state, and the ring and halo repeat it, so the light never carries it alone.
        var stateColour = ThemeColourOr(key, "#8A8F98");
        stateLight.Background = new SolidColorBrush(stateColour);
        // The state LED is a capsule set into the display: inlaid in the state's colour while recording or on a break, an empty groove when idle. The window's ring and halo
        // repeat the state, so the LED never carries it alone.
        var groove = ThemeColourOr("DisplayGroove", "#101317"); var shade = ThemeColourOr("DisplayShade", "#A0000000");
        var idle = key == "Idle";
        stateLight.Background = new SolidColorBrush(idle ? groove : stateColour);
        stateLight.BoxShadow = new BoxShadows(new BoxShadow { IsInset = true, OffsetX = .6, OffsetY = 1, Blur = idle ? 1.6 : 1, Color = idle ? shade : SoftTint.WithAlpha(shade, .55) });
        Avalonia.Automation.AutomationProperties.SetName(stateLight, stateName); ToolTip.SetTip(stateLight, stateName);
        var well0 = ThemeColourOr("DisplayWell", "#101317"); var edge = ThemeColourOr("DisplayEdge", "#12FFFFFF");
        displayBox.BoxShadow = new BoxShadows(new BoxShadow { IsInset = true, OffsetY = 2, Blur = 5, Color = shade }, new[] { new BoxShadow { OffsetY = 1, Color = edge } });
        // The count takes the colour of the most urgent thing waiting: blue a reminder, amber a budget at 80 per cent or a long break, red a spent budget. It is two digits
        // always (the leading one blank under ten, both blank with nothing waiting), so the display never changes width.
        var alerts = model.PillAlertsMode == "off" ? Array.Empty<AlertItem>() : model.Alerts; var waiting = alerts.Count > 0;
        var lit = waiting ? ThemeColourOr(alerts[0].LightToken, "#3558C8") : ThemeColourOr("Muted", "#8A8F98");
        var count = Math.Min(alerts.Count, 99);
        alertCountRun.Text = waiting ? (count < 10 ? " " + count : count.ToString()) : "  ";
        alertCountRun.Colour = lit; bell.TintKey = waiting ? alerts[0].LightToken : "Muted"; bell.Opacity = waiting ? 1 : .55;
        var reminderWords = !waiting ? "No reminders waiting" : alerts.All(a => a.Kind == AlertKind.Reminder) ? "A reminder is due" : $"{alerts.Count} waiting, most urgent first";
        if (shownCount >= 0 && count > shownCount && !Motion.Reduced) FlashCount(); shownCount = count; AlertCount = alerts.Count;
        var countTarget = (Control?)AlertsButton ?? (Control)alertCountRun;
        ToolTip.SetTip(countTarget, reminderWords); Avalonia.Automation.AutomationProperties.SetName(countTarget, waiting ? (alerts.Count == 1 ? reminderWords : $"{alerts.Count} waiting") : "Nothing waiting");
        clock.Text = model.ClockText;

        var selected = model.Accounts.FirstOrDefault(a => a.Name == model.SelectedAccount);
        var who = onBreak ? model.ResumeAccount : model.ActiveAccount ?? selected?.Name;
        // The primary control is a soft green with its symbol in the same colour. On a break it stays green and keeps the
        // account's initials, since the break itself is lit beside it.
        var tone = ThemeColourOr(dark ? "Live" : "LiveText", "#34D399");
        // A raised key (a step lighter under the pointer) with the symbol in the tracking colour, which is what the key does.
        // While it is recording the key is on, so it sits sunk with the symbol in green, as the mock draws it.
        var recording = model.IsTracking && !onBreak;
        primaryTile.Background = primaryTile.TryFindResource(recording ? "Well" : hoverCircle ? "KeyHover" : "Panel", primaryTile.ActualThemeVariant, out var keyFace) && keyFace is IBrush kf ? kf : Brushes.Transparent;
        primaryTile.BoxShadow = circleFocus ? SoftTint.FocusRing(primaryTile) : Res(primaryTile, recording ? "ShadowSunk" : hoverCircle ? "ShadowKeyHover" : "ShadowKey");
        initials.Foreground = new SolidColorBrush(tone); glyph.Fixed = new SolidColorBrush(tone);
        initials.Text = who is null ? "" : PillWindow.Initials(who);
        // The account the pill is on (or would start). With nothing chosen the button says so.
        var shown = who ?? selected?.Name; var shownAccount = shown is null ? null : model.Accounts.FirstOrDefault(a => a.Name == shown);
        var display = shown is null ? "Choose an account" : shownAccount?.Display ?? shown;
        if (selectorWords != display) { selectorWords = display; SetAccountsOpen(accountsOpen, wash: false); }
        selectorDot.Fill = new SolidColorBrush(stateColour); // The dot is the state.
        // The pill's tile carries the initials while an account runs, and gives way to the symbol at rest, under the pointer, or with no account to name.
        if (Sidebar) RefreshWho(onBreak, selected, who, stateColour);
        var showGlyph = Sidebar || hoverCircle || (!model.IsTracking && !onBreak) || initials.Text.Length == 0;
        initials.IsVisible = !showGlyph; glyphBox.IsVisible = showGlyph;
        glyph.Playing = model.IsTracking && !onBreak; glyph.InvalidateVisual();
        var circleTip = onBreak ? "End break" + (model.ResumeAccount is { } back ? " and resume " + back : "")
            : model.IsTracking ? "Pause. Tracking " + (model.ActiveAccount is { } active ? model.Accounts.FirstOrDefault(a => a.Name == active)?.Display ?? active : "")
            : selected is not null ? "Start " + selected.Display : "Choose an account";
        ToolTip.SetTip(Circle, circleTip); Avalonia.Automation.AutomationProperties.SetName(Circle, circleTip);
        Avalonia.Automation.AutomationProperties.SetName(ClockButton, clockTip + ". Working time " + model.ClockText);
        Break.Refresh();
        PaintAccountsTile();
    }
}

/// <summary>
/// The break control: a soft orange tile with an orange cup at rest, a stronger tint under the pointer, and while a break is on a solid orange tile with
/// a white cup (or a dark one where white would not read on the theme's orange), a soft ring, and moving steam.
/// </summary>
public sealed class BreakButton : Button
{
    private readonly TimerModel model;
    private readonly BreakGlyph cup = new() { Mono = false };
    private readonly Border tile = new() { Width = StatusFace.TileSize, Height = StatusFace.TileSize, CornerRadius = new CornerRadius(StatusFace.TileRadius), Background = Brushes.Transparent };
    private bool hover, focus;
    public BreakButton(TimerModel model, string name)
    {
        this.model = model; Name = name;
        Click += (_, _) => model.ToggleBreak();
        Classes.Add("pillBare"); StatusFace.MakeRoomForRing(this); HorizontalAlignment = HorizontalAlignment.Center;
        // Drawn at its own size, so its line is as thin as the other icons'.
        tile.Child = cup; cup.HorizontalAlignment = HorizontalAlignment.Center; cup.VerticalAlignment = VerticalAlignment.Center;
        Content = tile;
        PointerEntered += (_, _) => { hover = true; cup.SetSteaming(true); Refresh(); };
        PointerExited += (_, _) => { hover = false; cup.SetSteaming(model.IsOnBreak); Refresh(); };
        SoftTint.WatchKeyboardFocus(this, f => { focus = f; Refresh(); });
        ActualThemeVariantChanged += (_, _) => Refresh();
        Refresh();
    }
    public void Refresh()
    {
        var onBreak = model.IsOnBreak;
        var orange = Ui.ThemeColour(this, "Break", Color.Parse("#F5A524"));
        cup.Filled = false;
        if (onBreak)
        {
            // Pressed in and lit, as the mock draws a key that is on: sunk into its place, the cup in the break colour.
            tile.Background = tile.TryFindResource("Well", tile.ActualThemeVariant, out var well) && well is IBrush wellBrush ? wellBrush : Brushes.Transparent;
            cup.Fixed = null; // The theme's break orange.
            tile.BoxShadow = StatusFace.Res(tile, "ShadowSunk");
        }
        else
        {
            // A raised round key outside the well, with the cup in the break colour.
            tile.Background = tile.TryFindResource(hover ? "KeyHover" : "Panel", tile.ActualThemeVariant, out var face) && face is IBrush faceBrush ? faceBrush : Brushes.Transparent;
            cup.Fixed = null; // The theme's break orange.
            tile.BoxShadow = StatusFace.Res(tile, hover ? "ShadowKeyHover" : "ShadowKey");
        }
        if (focus) tile.BoxShadow = SoftTint.FocusRing(tile);
        cup.SetSteaming(onBreak || hover); cup.InvalidateVisual();
        var tip = onBreak ? "End break" + (model.ResumeAccount is { } returnTo ? " and resume " + returnTo : "") : "Start break";
        ToolTip.SetTip(this, tip); Avalonia.Automation.AutomationProperties.SetName(this, tip);
    }
}
