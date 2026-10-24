using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>The clock stacked as hours, minutes and seconds, the seconds smaller and quieter, for the narrow vertical pill.</summary>
public sealed class StackedClock : StackPanel
{
    private readonly TextBlock hours, minutes, seconds;
    public StackedClock()
    {
        Spacing = 2; HorizontalAlignment = HorizontalAlignment.Center;
        // The lines are as tall as their type, so the three stack tightly and the pill's height holds.
        hours = new TextBlock { FontSize = 30, LineHeight = 30, Classes = { "clock" }, HorizontalAlignment = HorizontalAlignment.Center };
        minutes = new TextBlock { FontSize = 30, LineHeight = 30, Classes = { "clock" }, HorizontalAlignment = HorizontalAlignment.Center };
        seconds = new TextBlock { FontSize = 15, LineHeight = 15, Classes = { "clockLight", "muted" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        Children.Add(hours); Children.Add(minutes); Children.Add(seconds);
    }
    /// <summary>The whole clock, as hours:minutes:seconds. Setting it splits it over the three lines.</summary>
    public string Text
    {
        get => hours.Text + ":" + minutes.Text + ":" + seconds.Text;
        set { var parts = value.Split(':'); hours.Text = parts.Length > 0 ? parts[0] : ""; minutes.Text = parts.Length > 1 ? parts[1] : ""; seconds.Text = parts.Length > 2 ? parts[2] : ""; }
    }
}

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
/// The pill's face, drawn once for the pill and for the dashboard's rail: the state light (and the blue reminder light while one is due), the stacked day
/// clock that moves between the two windows, the primary control that carries the account's initials and pauses or resumes, the break, and the Accounts
/// button at the foot. Every control is a soft tint of its colour with a stronger tint under the pointer. The owner calls <see cref="Refresh"/> when
/// the model changes, and the names of the controls start with the prefix it is given (Pill or Rail).
/// </summary>
public sealed class StatusFace : StackPanel
{
    private readonly TimerModel model;
    private readonly StackedClock clock = new() { Text = "00:00:00" };
    private readonly Border stateLight, reminderLight;
    private readonly TextBlock initials = new() { FontSize = 17, Classes = { "clock" }, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly PauseGlyph glyph = new();
    /// <summary>The primary control, the break and the Accounts button are all this square, so they read as one set with the same room either side in the pill.</summary>
    public const double TileSize = 44, TileRadius = 14;
    /// <summary>
    /// Each tile's button is this much larger on every side and pulled back by the same amount, so the ring that marks keyboard focus or a break is drawn
    /// inside the button (a button clips what falls outside it) without moving the tile or its neighbours.
    /// </summary>
    public const double RingRoom = 4;
    public static void MakeRoomForRing(Button button, double top = 0)
    {
        button.Width = button.Height = TileSize + 2 * RingRoom; button.Padding = new Thickness(RingRoom);
        button.Margin = new Thickness(-RingRoom, top - RingRoom, -RingRoom, -RingRoom);
        button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center;
    }
    private readonly Border clockTile;
    // The primary control is a rounded square, to match the accounts icon beside it.
    private readonly Border primaryTile = new() { Width = StatusFace.TileSize, Height = StatusFace.TileSize, CornerRadius = new CornerRadius(StatusFace.TileRadius) };
    private readonly Viewbox glyphBox = new() { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly string clockTip;
    private bool hoverCircle, circleFocus, accountsFocus;
    public StackPanel Lights { get; }
    public Button ClockButton { get; }
    public Button Circle { get; }
    public Button AccountsButton { get; }
    public BreakButton Break { get; }
    private readonly Border accountsTile = new() { Width = StatusFace.TileSize, Height = StatusFace.TileSize, CornerRadius = new CornerRadius(StatusFace.TileRadius), Background = Brushes.Transparent, Child = new AccountsGlyph { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
    private bool accountsOpen, hoverAccounts;

    public StatusFace(TimerModel model, string prefix, string breakName, string clockTip, Action clockClick, Action circleClick, Action accountsClick)
    {
        this.model = model; this.clockTip = clockTip;
        // As wide as the pill inside its edge, so the tiles sit centred on whole pixels (the clock tile alone is an odd width).
        Spacing = 10; HorizontalAlignment = HorizontalAlignment.Center; Width = PillWindow.PillWidth - 2;
        stateLight = new Border { Name = prefix + "StateLight", Width = 6, Height = 14, CornerRadius = new CornerRadius(3) };
        reminderLight = new Border { Name = prefix + "ReminderLight", Width = 6, Height = 14, CornerRadius = new CornerRadius(3) };
        Lights = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, Name = prefix + "Lights" };
        Lights.Children.Add(stateLight); Lights.Children.Add(reminderLight);
        // The clock moves between the pill and the dashboard.
        clockTile = new Border { Name = prefix + "ClockTile", CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 4), Background = Brushes.Transparent, Child = clock };
        ClockButton = Ui.Button("", clockClick, prefix + "Dashboard");
        ClockButton.Classes.Add("pillBare"); ClockButton.Padding = new Thickness(0); ClockButton.Content = clockTile; ClockButton.HorizontalAlignment = HorizontalAlignment.Center;
        ClockButton.PointerEntered += (_, _) => SetTileHover(clockTile, true);
        ClockButton.PointerExited += (_, _) => SetTileHover(clockTile, false);
        ToolTip.SetTip(ClockButton, clockTip); Avalonia.Automation.AutomationProperties.SetName(ClockButton, clockTip);
        // The primary control carries the running account's initials, and pauses or resumes. The initials give way to the symbol under the pointer.
        glyphBox.Child = glyph; glyph.Fixed = Brushes.Black;
        primaryTile.Name = prefix + "PrimaryTile";
        var host = new Grid(); host.Children.Add(initials); host.Children.Add(glyphBox); primaryTile.Child = host;
        Circle = Ui.Button("", circleClick, prefix + "Pause");
        Circle.Classes.Add("pillBare"); Circle.Content = primaryTile; MakeRoomForRing(Circle, 6); Circle.HorizontalAlignment = HorizontalAlignment.Center;
        // Under the pointer the tint deepens instead of the button growing, which its edges would clip.
        Circle.PointerEntered += (_, _) => { hoverCircle = true; Refresh(); };
        Circle.PointerExited += (_, _) => { hoverCircle = false; Refresh(); };
        SoftTint.WatchKeyboardFocus(Circle, f => { circleFocus = f; Refresh(); });
        Break = new BreakButton(model, breakName);
        // The accounts button is the same on the pill and the rail: it opens the account list beside the window, and closes it again.
        AccountsButton = Ui.Button("", accountsClick, prefix + "Accounts");
        AccountsButton.Classes.Add("pillBare"); AccountsButton.Content = accountsTile; MakeRoomForRing(AccountsButton); AccountsButton.HorizontalAlignment = HorizontalAlignment.Center;
        AccountsButton.PointerEntered += (_, _) => { hoverAccounts = true; PaintAccountsTile(); };
        AccountsButton.PointerExited += (_, _) => { hoverAccounts = false; PaintAccountsTile(); };
        SoftTint.WatchKeyboardFocus(AccountsButton, f => { accountsFocus = f; PaintAccountsTile(); });
        SetAccountsOpen(false);
        Children.Add(Lights); ClockButton.Margin = new Thickness(0, 4, 0, 0); Children.Add(ClockButton);
        Children.Add(Circle); Children.Add(Break); Children.Add(AccountsButton);
        ActualThemeVariantChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Marks the accounts button as pressed while the list is open, and words its tooltip for what pressing it will do.</summary>
    public void SetAccountsOpen(bool open)
    {
        accountsOpen = open; PaintAccountsTile();
        var tip = open ? "Close the accounts" : "Accounts";
        ToolTip.SetTip(AccountsButton, tip); Avalonia.Automation.AutomationProperties.SetName(AccountsButton, tip);
    }
    // A faint wash at rest, a blue tint under the pointer and a stronger one while the list is open.
    private void PaintAccountsTile()
    {
        var dark = SoftTint.IsDark(this);
        var accent = ThemeColourOr("Accent", "#3558C8"); var ink = ThemeColourOr("Ink", "#18233A");
        var tint = accountsOpen ? SoftTint.WithAlpha(accent, dark ? .30 : .22) : hoverAccounts ? SoftTint.WithAlpha(accent, dark ? .18 : .12) : SoftTint.WithAlpha(ink, .05);
        accountsTile.Background = new SolidColorBrush(tint);
        accountsTile.BoxShadow = accountsFocus ? SoftTint.FocusRing(accountsTile) : default;
    }
    private static void SetTileHover(Border tile, bool on) => tile.Background = on ? (tile.TryFindResource("Hover", tile.ActualThemeVariant, out var brush) && brush is IBrush hover ? hover : Brushes.Transparent) : Brushes.Transparent;
    private Color ThemeColourOr(string key, string fallback) => Ui.ThemeColour(this, key, Color.Parse(fallback));
    /// <summary>The state's key (Break outranks Overtime outranks Live, else Idle) and its words, shared with the owner's ring and glow.</summary>
    public static (string Key, string Name) State(TimerModel model)
    {
        var onBreak = model.IsOnBreak; var late = model.IsOvertime && !onBreak; var live = model.IsTracking && !onBreak && !late;
        return (onBreak ? "Break" : late ? "Overtime" : live ? "Live" : "Idle", onBreak ? "On break" : late ? "Overtime" : live ? "Recording" : "Not recording");
    }
    public void Refresh()
    {
        var onBreak = model.IsOnBreak;
        var (key, stateName) = State(model);
        var dark = SoftTint.IsDark(this);
        // The light's colour is the state, and the ring and halo repeat it, so the light never carries it alone.
        var stateColour = ThemeColourOr(key, "#8A8F98");
        stateLight.Background = new SolidColorBrush(stateColour);
        ToolTip.SetTip(Lights, stateName); Avalonia.Automation.AutomationProperties.SetName(stateLight, stateName);
        // The reminder light is always there: blue and pulsing while a reminder is due, and a quiet grey when nothing is waiting. It only notifies.
        var grey = ThemeColourOr("Idle", "#8A8F98");
        reminderLight.Background = new SolidColorBrush(model.ReminderDue ? ThemeColourOr("Accent", "#3558C8") : Color.FromArgb(150, grey.R, grey.G, grey.B));
        Ui.Pulse(reminderLight, model.ReminderDue);
        var reminderWords = model.ReminderDue ? "A reminder is due" : "No reminders waiting";
        ToolTip.SetTip(reminderLight, reminderWords); Avalonia.Automation.AutomationProperties.SetName(reminderLight, reminderWords);
        clock.Text = model.ClockText;

        var selected = model.Accounts.FirstOrDefault(a => a.Name == model.SelectedAccount);
        var who = onBreak ? model.ResumeAccount : model.ActiveAccount ?? selected?.Name;
        // The primary control is a soft green (soft red in overtime) with its symbol in the same colour. On a break it stays green and keeps the
        // account's initials, since the break itself is lit beside it.
        var late = key == "Overtime";
        var tone = ThemeColourOr(late ? "Overtime" : "Live", "#34D399");
        primaryTile.Background = new SolidColorBrush(SoftTint.WithAlpha(tone, hoverCircle ? (dark ? .30 : .32) : (dark ? .18 : .20)));
        primaryTile.BoxShadow = circleFocus ? SoftTint.FocusRing(primaryTile) : default;
        initials.Foreground = new SolidColorBrush(tone); glyph.Fixed = new SolidColorBrush(tone);
        initials.Text = who is null ? "" : PillWindow.Initials(who);
        // Initials while an account runs, and the symbol at rest, under the pointer, or when there is no account to name.
        var showGlyph = hoverCircle || (!model.IsTracking && !onBreak) || initials.Text.Length == 0;
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
    public BreakGlyph Cup => cup;
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
        var dark = SoftTint.IsDark(this);
        var orange = Ui.ThemeColour(this, "Break", Color.Parse("#F5A524"));
        var hex = $"#{orange.R:X2}{orange.G:X2}{orange.B:X2}";
        cup.Filled = false;
        if (onBreak)
        {
            tile.Background = new SolidColorBrush(orange);
            cup.Fixed = ThemeColours.Contrast(hex, "#FFFFFF") >= 2.9 ? Brushes.White : new SolidColorBrush(Color.Parse("#2A1A05"));
            tile.BoxShadow = BoxShadows.Parse($"0 0 0 3 #52{orange.R:X2}{orange.G:X2}{orange.B:X2}");
        }
        else
        {
            tile.Background = new SolidColorBrush(SoftTint.WithAlpha(orange, hover ? .28 : .16));
            cup.Fixed = null; // The theme's break orange.
            tile.BoxShadow = default;
        }
        if (focus) tile.BoxShadow = SoftTint.FocusRing(tile);
        cup.SetSteaming(onBreak || hover); cup.InvalidateVisual();
        var tip = onBreak ? "End break" + (model.ResumeAccount is { } returnTo ? " and resume " + returnTo : "") : "Start break";
        ToolTip.SetTip(this, tip); Avalonia.Automation.AutomationProperties.SetName(this, tip);
    }
}
