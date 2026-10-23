using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
/// The top of the pill and of the dashboard's rail, drawn once for both: the state light (and the blue reminder light while one is due), the stacked
/// day clock that moves between the two windows, and the circle that carries the account's initials and pauses or resumes. The owner calls
/// <see cref="Refresh"/> when the model changes, and the names of the controls start with the prefix it is given (Pill or Rail).
/// </summary>
public sealed class StatusFace : StackPanel
{
    private readonly TimerModel model;
    private readonly StackedClock clock = new() { Text = "00:00:00" };
    private readonly Border stateLight, reminderLight;
    private readonly Avalonia.Controls.Shapes.Ellipse circleFill = new() { Width = 46, Height = 46 };
    private readonly TextBlock initials = new() { FontSize = 17, Classes = { "clock" }, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly PauseGlyph glyph = new();
    private readonly Border clockTile;
    private readonly Grid circleHost = new() { Width = 46, Height = 46, RenderTransform = new ScaleTransform(1, 1), RenderTransformOrigin = RelativePoint.Center };
    private readonly Viewbox glyphBox = new() { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly string clockTip;
    private bool hoverCircle;
    public StackPanel Lights { get; }
    public Button ClockButton { get; }
    public Button Circle { get; }

    public StatusFace(TimerModel model, string prefix, string clockTip, Action clockClick, Action circleClick)
    {
        this.model = model; this.clockTip = clockTip;
        Spacing = 10; HorizontalAlignment = HorizontalAlignment.Center;
        stateLight = new Border { Name = prefix + "StateLight", Width = 6, Height = 14, CornerRadius = new CornerRadius(3) };
        reminderLight = new Border { Name = prefix + "ReminderLight", Width = 6, Height = 14, CornerRadius = new CornerRadius(3), IsVisible = false };
        Lights = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, Name = prefix + "Lights" };
        Lights.Children.Add(stateLight); Lights.Children.Add(reminderLight);
        // The clock moves between the pill and the dashboard.
        clockTile = new Border { Name = prefix + "ClockTile", CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 6), Background = Brushes.Transparent, Child = clock };
        ClockButton = Ui.Button("", clockClick, prefix + "Dashboard");
        ClockButton.Classes.Add("pillBare"); ClockButton.Padding = new Thickness(0); ClockButton.Content = clockTile; ClockButton.HorizontalAlignment = HorizontalAlignment.Center;
        ClockButton.PointerEntered += (_, _) => SetTileHover(clockTile, true);
        ClockButton.PointerExited += (_, _) => SetTileHover(clockTile, false);
        ToolTip.SetTip(ClockButton, clockTip); Avalonia.Automation.AutomationProperties.SetName(ClockButton, clockTip);
        // The circle carries the running account's initials, and pauses or resumes. The initials give way to the symbol under the pointer.
        glyphBox.Child = glyph; glyph.Fixed = Brushes.Black;
        circleHost.Children.Add(circleFill); circleHost.Children.Add(initials); circleHost.Children.Add(glyphBox);
        Circle = Ui.Button("", circleClick, prefix + "Pause");
        Circle.Classes.Add("pillBare"); Circle.Padding = new Thickness(0); Circle.Width = 46; Circle.Height = 46; Circle.Content = circleHost; Circle.HorizontalAlignment = HorizontalAlignment.Center;
        Circle.PointerEntered += (_, _) => { hoverCircle = true; glyph.Glow = 1; circleHost.RenderTransform = new ScaleTransform(1.06, 1.06); Refresh(); };
        Circle.PointerExited += (_, _) => { hoverCircle = false; glyph.Glow = 0; circleHost.RenderTransform = new ScaleTransform(1, 1); Refresh(); };
        Children.Add(Lights); ClockButton.Margin = new Thickness(0, 4, 0, 0); Children.Add(ClockButton);
        Circle.Margin = new Thickness(0, 6, 0, 0); Children.Add(Circle);
        ActualThemeVariantChanged += (_, _) => Refresh();
        Refresh();
    }

    private static void SetTileHover(Border tile, bool on) => tile.Background = on ? (tile.TryFindResource("Hover", tile.ActualThemeVariant, out var brush) && brush is IBrush hover ? hover : Brushes.Transparent) : Brushes.Transparent;
    private Color ThemeColourOr(string key, string fallback) => Ui.ThemeColour(this, key, Color.Parse(fallback));
    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
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
        // The light's colour is the state, and the circle and the ring repeat it, so the light never carries it alone.
        var stateColour = ThemeColourOr(key, "#8A8F98");
        stateLight.Background = new SolidColorBrush(stateColour);
        ToolTip.SetTip(Lights, stateName); Avalonia.Automation.AutomationProperties.SetName(stateLight, stateName);
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
    }
}

