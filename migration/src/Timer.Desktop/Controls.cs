using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Timer.Core;

namespace Timer.Desktop;

public static class Ui
{
    /// <summary>
    /// The colour to draw a block, dot or bar in. A very dark colour would vanish on a dark background, so it is lifted a little
    /// towards white for display only (the saved colour is untouched). Ordinary and palette colours are returned as they are.
    /// </summary>
    public static string Visible(string colour)
    {
        if (!IsColour(colour)) return colour;
        double Channel(int at) { var v = Convert.ToInt32(colour.Substring(at, 2), 16) / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        if (0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5) >= 0.08) return colour;
        string Lift(int at) { var v = Convert.ToInt32(colour.Substring(at, 2), 16); return ((int)Math.Round(v + (255 - v) * 0.3)).ToString("X2"); }
        return "#" + Lift(1) + Lift(3) + Lift(5);
    }
    public static bool IsColour(string? text) => text is not null && System.Text.RegularExpressions.Regex.IsMatch(text, "^#[0-9A-Fa-f]{6}$");
    /// <summary>The text colour that reads best on a block of this colour: the usual dark ink on light colours, white on dark ones.</summary>
    public static string InkOn(string colour) => IsColour(colour) ? ThemeColours.ReadableOn(colour) : "#24312B";
    // Room around a borderless window for its soft shadow or glow, which are drawn outside the card.
    public const double ShadowMargin = 30;
    /// <summary>Wraps a window's card so it can cast a shadow or glow. The card clips its own content, so the shadow lives on a parent.</summary>
    /// <summary>
    /// The slim scroll bar lies over the content, so while a list scrolls its rows are given room on the right for the 10 pixel track (and a gap), and when it fits they
    /// use the whole width. Without this the bar ran over the buttons at the right end of each row.
    /// </summary>
    public static void LeaveRoomForTrack(ScrollViewer scroll, Control content, double room = 14)
    {
        void Fit() => content.Margin = scroll.Extent.Height > scroll.Viewport.Height + .5 ? new Thickness(content.Margin.Left, content.Margin.Top, room, content.Margin.Bottom) : new Thickness(content.Margin.Left, content.Margin.Top, 0, content.Margin.Bottom);
        scroll.PropertyChanged += (_, e) => { if (e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.ViewportProperty) Fit(); };
    }
    public static Border Shadowed(Border card, double radius)
    {
        card.CornerRadius = new CornerRadius(radius);
        var host = new Border { Margin = new Thickness(ShadowMargin), CornerRadius = new CornerRadius(radius), Child = card, Name = "ShadowHost" };
        // The shadow and the glow fade into each other, so pressing play or pause does not make the edge jump.
        host.Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.BoxShadowsTransition { Property = Border.BoxShadowProperty, Duration = Motion.Ease(350) } };
        return host;
    }
    public static Color ThemeColour(Control control, string key, Color fallback)
    {
        if (control.TryFindResource(key, control.ActualThemeVariant, out var value))
            return value switch { Color c => c, ISolidColorBrush b => b.Color, _ => fallback };
        return fallback;
    }
    /// <summary>The window's shadow, or a coloured glow in its place when <paramref name="glow"/> names a theme brush.</summary>
    public static void SetShadow(Border host, bool shadows, string? glow)
    {
        if (glow is not null)
        {
            var colour = ThemeColour(host, glow, Colors.Gray);
            // A soft halo, not a strong glow: it only has to say the state without pulling the eye.
            host.BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, Spread = 0, Color = Color.FromArgb(48, colour.R, colour.G, colour.B) });
        }
        else if (shadows)
            host.BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 22, Spread = 0, Color = ThemeColour(host, "ShadowColor", Color.FromArgb(90, 0, 0, 0)) });
        else host.BoxShadow = default;
    }
    /// <summary>
    /// Avalonia turns a shadow's blur into a Gaussian with sigma 0.2887 x blur + 0.5, where a CSS blur has sigma of half the blur, so the same number
    /// spreads about a third less. This gives the Avalonia blur that spreads like the CSS one the mock is drawn with.
    /// </summary>
    public static double CssBlur(double css) => Math.Round((css / 2 - 0.5) / 0.288675, 1);
    /// <summary>
    /// The colour of the pill's ring and halo for a state. On a Light theme it is the mock's bright mint, amber or coral, because the theme's own state
    /// colours are deeper there and the same glow read about one and a half times as strong; a dark theme uses its own state colours, which are close to them.
    /// </summary>
    public static Color PillStateColour(Control host, string state)
    {
        if (!SoftTint.IsDark(host))
            switch (state)
            {
                case "Live": return Color.FromRgb(52, 211, 153);
                case "Break": return Color.FromRgb(245, 165, 36);
            }
        return ThemeColour(host, state, Colors.Gray);
    }
    /// <summary>
    /// The pill's own halo, drawn as the mock draws it: a glow of a 12 pixel CSS blur in the state's colour at 16 per cent (22 on a dark theme) while it is doing something,
    /// and at rest a light shadow dropped 6 pixels with an 18 pixel blur (10 per cent warm black, or 40 per cent black on a dark theme) rather than the heavier shadow cards use.
    /// </summary>
    public static void SetPillShadow(Border host, bool shadows, string? glow)
    {
        var dark = SoftTint.IsDark(host);
        if (glow is not null)
        {
            var colour = PillStateColour(host, glow);
            host.BoxShadow = new BoxShadows(new BoxShadow { Blur = CssBlur(12), Spread = 0, Color = Color.FromArgb((byte)Math.Round((dark ? .22 : .16) * 255), colour.R, colour.G, colour.B) });
        }
        else if (shadows)
            host.BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = CssBlur(18), Spread = 0, Color = dark ? Color.FromArgb(102, 0, 0, 0) : Color.FromArgb(26, 30, 30, 20) });
        else host.BoxShadow = default;
    }
    // An animation that has stopped (its window was hidden or the control left the tree, which ends a repeating one) must not still count as running,
    // or asking for it again does nothing and the dot stays still for good. So the task is kept, and a finished one is started afresh.
    private sealed record PulseRun(CancellationTokenSource Source, RunningAnimation Run);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, PulseRun> pulses = new();
    /// <summary>Fades a control in and out gently while <paramref name="on"/> is true, as the live dot does while an account is recording.</summary>
    public static void Pulse(Control control, bool on)
    {
        // With less motion asked for, the light just stays on, steadily.
        if (Motion.Reduced)
        {
            if (pulses.TryGetValue(control, out var still)) { still.Source.Cancel(); pulses.Remove(control); }
            control.Opacity = 1; return;
        }
        if (pulses.TryGetValue(control, out var running))
        {
            if (on && running.Run.Alive) return;
            running.Source.Cancel(); pulses.Remove(control); control.Opacity = 1;
            if (!on) return;
        }
        if (!on) return;
        var source = new CancellationTokenSource();
        var animation = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromSeconds(1.6), IterationCount = Avalonia.Animation.IterationCount.Infinite,
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(.5), Setters = { new Setter(Visual.OpacityProperty, .3) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
            }
        };
        pulses.Add(control, new PulseRun(source, new RunningAnimation(animation.RunAsync(control, source.Token))));
    }
    public static bool IsPulsing(Control control) => pulses.TryGetValue(control, out var run) && run.Run.Alive;
    /// <summary>A heading in the app's display face, the same as the dashboard's workspace title, so every page and pop-up names itself alike.</summary>
    public static TextBlock Heading(string text, double size = 20)
    {
        var heading = Text(text, size, "clock"); return heading;
    }
    public static TextBlock Text(string text, double size = 14, string? style = null)
    {
        var control = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        if (style is not null) control.Classes.Add(style);
        return control;
    }
    /// <summary>The dash between a start and an end box, centred in the gap between them.</summary>
    public static TextBlock Dash()
    {
        var dash = Text("–");
        dash.HorizontalAlignment = HorizontalAlignment.Center; dash.TextAlignment = TextAlignment.Center;
        return dash;
    }
    public static Button Button(string text, Action click, string? name = null)
    {
        var button = new Button { Content = text, Name = name, VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => click();
        return button;
    }
    // The buttons in dialogs wear the same quiet outlined look as the dashboard's (Add time, Undo, Start break). The main action of a dialog,
    // such as Save or Export, has an accent outline so it reads as the one to press.
    private static readonly string[] MainActions = { "Save", "Export CSV files" };
    public static Button DialogButton(string text, Action click, string? name = null, bool? main = null)
    {
        var button = Button(text, click, name);
        button.Classes.Add("toolButton"); button.Classes.Add("quiet"); button.Classes.Add("dialogButton");
        if (main ?? MainActions.Contains(text)) button.Classes.Add("mainAction");
        return button;
    }
    /// <summary>
    /// Draws a hairline in the theme's line colour along the edge of a scroll viewer that has more content past it, at the bottom while there is more
    /// below and at the top once it has scrolled down, so rows cut off by the buttons underneath read as scrollable. A plain line suits every theme,
    /// where a fade or shadow looked muddy on light ones. The lines ignore the pointer and go once the end is reached.
    /// </summary>
    public static Grid WithMoreBelow(ScrollViewer scroll)
    {
        Border Edge(string name, VerticalAlignment edge) { var line = new Border { Name = name, Height = 1, VerticalAlignment = edge, IsHitTestVisible = false, IsVisible = false }; line.Classes.Add("scrollEdge"); return line; }
        var below = Edge("MoreBelow", VerticalAlignment.Bottom); var above = Edge("MoreAbove", VerticalAlignment.Top);
        void Update()
        {
            var scrolls = scroll.Extent.Height > scroll.Viewport.Height + 1;
            below.IsVisible = scrolls && scroll.Offset.Y + scroll.Viewport.Height < scroll.Extent.Height - 1;
            above.IsVisible = scrolls && scroll.Offset.Y > 1;
        }
        scroll.ScrollChanged += (_, _) => Update();
        scroll.PropertyChanged += (_, e) => { if (e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.ViewportProperty) Update(); };
        var host = new Grid(); host.Children.Add(scroll); host.Children.Add(above); host.Children.Add(below);
        return host;
    }
    public static Grid Grid(string columns, string rows = "Auto") => new()
    {
        ColumnDefinitions = new ColumnDefinitions(columns), RowDefinitions = new RowDefinitions(rows)
    };
    public static T Put<T>(Grid grid, T child, int column = 0, int row = 0) where T : Control
    {
        Avalonia.Controls.Grid.SetColumn(child, column);
        Avalonia.Controls.Grid.SetRow(child, row);
        grid.Children.Add(child);
        return child;
    }
    public static Border Rule() { var b = new Border { Height = 1 }; b.Classes.Add("rule"); return b; }
    public static StackPanel Horizontal(params Control[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }
    /// <summary>True for anything that is not a control you use: the bare panel and plain text, not buttons, boxes, lists, sliders or scroll bars.</summary>
    public static bool IsPlainSurface(object? source) => source is Visual visual
        && visual is not (Avalonia.Controls.Button or TextBox or ComboBox or Avalonia.Controls.Slider or Avalonia.Controls.Primitives.ScrollBar or Avalonia.Controls.Primitives.Thumb or ListBox)
        && !visual.GetVisualAncestors().Any(a => a is Avalonia.Controls.Button or TextBox or ComboBox or Avalonia.Controls.Slider or Avalonia.Controls.Primitives.ScrollBar or Avalonia.Controls.Primitives.Thumb or ListBox);
    /// <summary>Closes any open right-click menu in the window. A drag handler marks the press as handled, so the menu never sees the click that should dismiss it.</summary>
    public static void CloseMenus(Window window)
    {
        if (window.ContextMenu is { IsOpen: true } own) own.Close();
        foreach (var control in window.GetVisualDescendants().OfType<Control>())
            if (control.ContextMenu is { IsOpen: true } menu) menu.Close();
    }
    public static void DragHeader(Control header, Window window, string name = "DragHeader")
    {
        if (header is Panel panel) panel.Background = Brushes.Transparent;
        header.Name = name;
        header.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            // Boxes count as controls too, or a press on the Notes search box would start dragging the window and the box could never be clicked into.
            if (IsPlainSurface(e.Source) && e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
            { CloseMenus(window); window.BeginMoveDrag(e); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }
    public static void PillGestures(Control header, Window window)
    {
        if (header is Panel panel) panel.Background = Brushes.Transparent;
        header.Name = "DragHeader";
        PointerPressedEventArgs? press = null;
        Button? target = null;
        Point origin = default;
        header.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(window).Properties.IsLeftButtonPressed) return;
            CloseMenus(window); press = e; origin = e.GetPosition(header);
            target = e.Source is Visual visual ? visual as Button ?? visual.GetVisualAncestors().OfType<Button>().FirstOrDefault() : null;
            // Stop the button from capturing/activating before intent is known.
            e.Handled = true; e.Pointer.Capture(header);
        }, RoutingStrategies.Tunnel);
        header.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (press is null) return;
            var delta = e.GetPosition(header) - origin;
            if (Math.Abs(delta.X) < 5 && Math.Abs(delta.Y) < 5) return;
            var start = press;
            press = null; target = null;
            e.Handled = true; e.Pointer.Capture(null);
            CloseMenus(window); window.BeginMoveDrag(start);
        }, RoutingStrategies.Tunnel);
        header.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            if (press is null) return;
            var click = target;
            press = null; target = null;
            e.Handled = true; e.Pointer.Capture(null);
            if (click is not null && click.IsEffectivelyEnabled && new Rect(click.Bounds.Size).Contains(e.GetPosition(click)))
                click.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        }, RoutingStrategies.Tunnel);
        header.PointerCaptureLost += (_, _) => { press = null; target = null; };
    }
    /// <summary>
    /// Whether tooltips show. A tooltip is cancelled as it opens when this is off, which is the one switch that covers every control,
    /// including ones made later. The words stay as the controls' accessible names either way.
    /// </summary>
    public static bool TooltipsOn { get; private set; } = true;
    private static bool tooltipHandlerAdded;
    public static void ApplyTooltips(bool on)
    {
        TooltipsOn = on;
        // The opening event is direct, so it is not seen by a window above the control. A class handler is the way to watch every control at once.
        if (tooltipHandlerAdded) return;
        tooltipHandlerAdded = true;
        ToolTip.ToolTipOpeningEvent.AddClassHandler<Control>((_, e) => { if (!TooltipsOn) e.Cancel = true; });
    }
    /// <summary>Starts a second copy of the app on the sample-data demo, which saves nothing real.</summary>
    public static void StartDemo()
    {
        if (Environment.ProcessPath is not { } executable) return;
        var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(App).Assembly.Location);
        start.ArgumentList.Add("--demo");
        System.Diagnostics.Process.Start(start);
    }
    /// <summary>
    /// The right-click menu is kept short on purpose: which build this is (once developer features are unlocked) and Collapse to tray.
    /// Everything else lives on the settings page.
    /// </summary>
    public static ContextMenu SettingsMenu(TimerModel model)
    {
        // Which build this is, greyed out because it is information, not an action.
        var version = new MenuItem { Header = BuildInfo.Current, IsEnabled = false, IsVisible = false, Name = "BuildVersion" };
        // Tucks the timer away without stopping it. Closing it for good is the cross at the foot of the dashboard's rail.
        var collapse = new MenuItem { Header = "Collapse to tray", Name = "CollapseToTray" };
        collapse.Click += (_, _) => model.RequestHide();
        var collapseRule = new Separator { IsVisible = false };
        var menu = new ContextMenu { ItemsSource = new Control[] { version, collapseRule, collapse } };
        void Sync()
        {
            var developer = model.DeveloperUnlocked;
            version.IsVisible = developer;
            // A divider only goes between two things that are showing, so it never sits at the top of the menu or above the only item.
            collapseRule.IsVisible = version.IsVisible;
        }
        menu.Opened += (_, _) => Sync(); Sync();
        return menu;
    }
    /// <summary>
    /// A button in the dashboard's outlined card look: 38 tall (30 when small), with an icon, words or both. With no words it is a 38 pixel square and
    /// <paramref name="tip"/> is its tooltip and accessible name. <paramref name="tint"/> names a theme colour for the icon.
    /// </summary>
    /// <summary>
    /// What a tooltip holds: the action in a word or two and, when there is one, its shortcut as a small key, as the Round 7 board draws it. A button that already
    /// has words gets no tooltip, except to show its shortcut.
    /// </summary>
    public static object Tip(string text, string? key = null)
    {
        if (key is null) return text;
        var chip = new Border { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Padding = new Thickness(5, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = key, FontSize = 11, FontWeight = FontWeight.Medium } };
        chip.Classes.Add("tipKey");
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = text }, chip } };
    }
    public static Button Ctl(string name, string? words, IconKind? icon, string tip, Action click, bool small = false, string? tint = null, string? key = null)
    {
        var button = Button(words ?? tip, click, name); button.Classes.Add("ctl"); if (small) button.Classes.Add("small");
        button.Height = small ? 30 : 38;
        var glyph = icon is { } kind ? new IconGlyph(kind) { TintKey = tint } : null;
        if (words is null) { button.Width = small ? 30 : 38; button.Padding = new Thickness(0); button.Content = glyph; }
        else button.Content = glyph is null ? new TextBlock { Text = words, VerticalAlignment = VerticalAlignment.Center }
            : Horizontal(glyph, new TextBlock { Text = words, VerticalAlignment = VerticalAlignment.Center });
        if (words is null || key is not null) ToolTip.SetTip(button, Tip(words is null ? tip : words, key));
        Avalonia.Automation.AutomationProperties.SetName(button, words ?? tip);
        return button;
    }
    /// <summary>A button that shows only an icon, with its words as the tooltip and the accessible name.</summary>
    public static Button IconButton(IconKind kind, string tip, string name, Action click)
    {
        var button = Button(tip, click, name); button.Content = new IconGlyph(kind); button.Classes.Add("iconButton");
        ToolTip.SetTip(button, tip); Avalonia.Automation.AutomationProperties.SetName(button, tip); return button;
    }
}

// Vector geometry keeps the budget ring and cutlery crisp at any display scale.
/// <summary>
/// A repeating animation that was started, with a way to tell that it has since stopped by itself (its window was hidden, or its control left the tree).
/// One that finished the moment it started, as in the headless tests where nothing ticks, is not treated as stopped.
/// </summary>
public sealed class RunningAnimation
{
    private readonly Task task; private readonly bool pendingAtStart;
    public RunningAnimation(Task task) { this.task = task; pendingAtStart = !task.IsCompleted; }
    public bool Alive => !pendingAtStart || !task.IsCompleted;
}

public sealed class AccountIndicator : Control
{
    // 0 at rest, 1 while the pointer is over the row. It eases in and out, so the button visibly answers the pointer and reads as pressable.
    public static readonly StyledProperty<double> GlowProperty = AvaloniaProperty.Register<AccountIndicator, double>(nameof(Glow));
    static AccountIndicator() => AffectsRender<AccountIndicator>(GlowProperty);
    public double Glow { get => GetValue(GlowProperty); set => SetValue(GlowProperty, value); }
    public TimerAccount Account { get; }
    /// <summary>Whether the budget ring is drawn. The dashboard shows the budget as a bar instead, so its button is a plain circle.</summary>
    public bool Ring { get; init; } = true;
    private readonly TimerModel model;
    public AccountIndicator(TimerAccount account, TimerModel model)
    {
        Account = account;
        this.model = model;
        Width = Height = Size;
        Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = GlowProperty, Duration = IconGlyph.HoverDuration } };
    }
    // Drawn on a 42 unit canvas and scaled down, so the ring, bars and play triangle stay in proportion at the smaller size.
    public const double Size = 36;
    /// <summary>The account rows' play tile is smaller than the old circle, 26 square, so a narrow panel leaves the name and time their room.</summary>
    public const double RowSize = 26;
    /// <summary>Redraws at <paramref name="side"/> square, the whole drawing scaled with it.</summary>
    public AccountIndicator Sized(double side) { Width = Height = side; Scale = side / Size; return this; }
    private double Scale = 1;
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var scaled = context.PushTransform(Matrix.CreateScale(Size * Scale / 42, Size * Scale / 42));
        var centre = new Point(21, 21);
        var ink = Resource("Ink");
        var running = model.ActiveAccount == Account.Name;
        // Without the ring the button is a rounded square, like the pill's circle and the accounts icon, not a circle.
        // The play button is a soft green tile with a green symbol, deeper while it is running or under the pointer, like the pill's primary control.
        var liveColour = Resource("Live") is SolidColorBrush liveBrush ? liveBrush.Color : Colors.SeaGreen;
        if (!Ring)
        {
            var tint = running ? .30 : .14 + .16 * Math.Clamp(Glow, 0, 1);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(tint * 255), liveColour.R, liveColour.G, liveColour.B)), null, new Rect(2, 2, 38, 38), 12, 12);
            ink = new SolidColorBrush(liveColour);
        }
        if (Ring && Account.Budget is { } budget)
        {
            context.DrawEllipse(null, new Pen(Resource("Line"), 3), centre, 19, 19);
            var ratio = Math.Clamp(model.BudgetUsed(Account).TotalSeconds / budget.TotalSeconds, 0, 1);
            var colour = ratio >= 1 ? Resource("Overtime") : ratio >= .8 ? Resource("BudgetWarning") : Resource("Muted");
            if (ratio >= 1) context.DrawEllipse(null, new Pen(colour, 3), centre, 19, 19);
            else if (ratio > 0)
            {
                var angle = ratio * 2 * Math.PI - Math.PI / 2;
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(21, 2), false);
                    g.ArcTo(new Point(21 + 19 * Math.Cos(angle), 21 + 19 * Math.Sin(angle)),
                        new Size(19, 19), 0, ratio > .5, SweepDirection.Clockwise);
                    g.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(colour, 3), geometry);
            }
        }
        // Under the hand, the button fills with the accent colour and its symbol grows a little and turns to the accent's own text colour.
        var glow = Math.Clamp(Glow, 0, 1);
        var accent = Resource("Accent"); var accentInk = Resource("AccentInk");
        if (Ring && glow > 0 && !BreakAccount.Is(Account.Name))
        {
            using (context.PushOpacity(glow))
            {
                context.DrawEllipse(accent, null, centre, 15, 15);
            }
        }
        // Only the ring version grows under the pointer; the plain tile shifts its tint instead, which its own edges would otherwise clip.
        var grown = Ring ? 1 + .08 * glow : 1;
        using var grow = context.PushTransform(Matrix.CreateTranslation(-21, -21) * Matrix.CreateScale(grown, grown) * Matrix.CreateTranslation(21, 21));
        if (model.ActiveAccount == Account.Name)
        {
            context.DrawRectangle(ink, null, new Rect(15, 14, 4, 14), 1, 1);
            context.DrawRectangle(ink, null, new Rect(23, 14, 4, 14), 1, 1);
            if (Ring && glow > 0)
                using (context.PushOpacity(glow)) { context.DrawRectangle(accentInk, null, new Rect(15, 14, 4, 14), 1, 1); context.DrawRectangle(accentInk, null, new Rect(23, 14, 4, 14), 1, 1); }
        }
        else if (BreakAccount.Is(Account.Name))
        {
            var pen = new Pen(ink, 1.8);
            context.DrawLine(pen, new Point(15, 12), new Point(15, 29));
            context.DrawLine(pen, new Point(12, 12), new Point(12, 19));
            context.DrawLine(pen, new Point(18, 12), new Point(18, 19));
            context.DrawLine(pen, new Point(12, 19), new Point(18, 19));
            context.DrawLine(pen, new Point(26, 12), new Point(26, 29));
            context.DrawLine(pen, new Point(23, 12), new Point(23, 21));
            context.DrawLine(pen, new Point(23, 21), new Point(26, 21));
        }
        else
        {
            var triangle = Geometry.Parse("M 17,13 L 28,21 L 17,29 Z");
            context.DrawGeometry(ink, null, triangle);
            if (Ring && glow > 0) using (context.PushOpacity(glow)) context.DrawGeometry(accentInk, null, triangle);
        }
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(ActualThemeVariant)) InvalidateVisual();
    }
    private IBrush Resource(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush
        ? brush : Brushes.Gray;
}

/// <summary>A thin bar for how much of a budget is used, with a small tick where the budget period has got to, so pace is easy to judge.</summary>
public sealed class BudgetBar : Control
{
    public double Ratio { get; private set; }
    public double Pace { get; private set; }
    public BudgetBar() { Height = 10; }
    public void Set(double ratio, double pace)
    {
        if (ratio == Ratio && pace == Pace) return;
        Ratio = ratio; Pace = pace; InvalidateVisual();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(ActualThemeVariant)) InvalidateVisual();
    }
    private IBrush Resource(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width; var top = 2.0; var height = 6.0;
        context.DrawRectangle(Resource("Line"), null, new Rect(0, top, width, height), 3, 3);
        var fill = Math.Clamp(Ratio, 0, 1) * width;
        // Neutral while there is room (green means tracking only), amber from 80%, red once the budget is spent.
        var colour = Ratio >= 1 ? Resource("Overtime") : Ratio >= .8 ? Resource("BudgetWarning") : Resource("Muted");
        if (fill > 0) context.DrawRectangle(colour, null, new Rect(0, top, Math.Max(fill, height), height), 3, 3);
        // The marker says where an even spread of the budget would have got to by now. Once the budget is spent there is nothing left to pace, so it goes.
        if (Ratio < 1) using (context.PushOpacity(.7)) context.DrawRectangle(Resource("Ink"), null, new Rect(Math.Clamp(Pace, 0, 1) * (width - 2), 0, 2, 10), 1, 1);
    }
}

/// <summary>
/// A name that is cut short with an ellipsis when it does not fit, and slides sideways to show the rest while the pointer is over its row.
/// Hover(false) puts it back at once, so a name is never left half scrolled.
/// </summary>
public sealed class MarqueeText : Border
{
    private readonly TextBlock text;
    private DispatcherTimer? timer;
    private double offset, hold;
    public MarqueeText(string value, double size)
    {
        ClipToBounds = true;
        text = Ui.Text(value, size); text.TextTrimming = TextTrimming.CharacterEllipsis; text.HorizontalAlignment = HorizontalAlignment.Left;
        Child = text;
        DetachedFromVisualTree += (_, _) => Reset();
    }
    public TextBlock Label => text;
    // The box is never wider than the room it is given, however long the whole name is, so a name sliding past its edge is clipped there instead of
    // making the row wider and running over the time beside it.
    protected override Size MeasureOverride(Size available)
    {
        text.Measure(Scrolling ? new Size(double.PositiveInfinity, available.Height) : available);
        return new Size(Math.Min(text.DesiredSize.Width, available.Width), text.DesiredSize.Height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        text.Arrange(new Rect(0, 0, Math.Max(final.Width, text.DesiredSize.Width), final.Height));
        return final;
    }
    /// <summary>Whether the name is sliding now, which only happens while hovered and only when it did not fit.</summary>
    public bool Scrolling { get; private set; }
    public void Hover(bool on)
    {
        Reset();
        if (!on) return;
        // Measured without trimming, to learn how wide the whole name is against the room it has.
        text.TextTrimming = TextTrimming.None; text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var full = text.DesiredSize.Width; var room = Bounds.Width;
        if (full <= room + .5) { text.TextTrimming = TextTrimming.CharacterEllipsis; return; }
        text.Width = full; Scrolling = true; hold = .6;
        var distance = full - room + 4; var last = DateTime.UtcNow;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow; var seconds = Math.Min(.1, (now - last).TotalSeconds); last = now;
            if (hold > 0) { hold -= seconds; return; }
            offset = Math.Min(distance, offset + 28 * seconds);
            text.RenderTransform = new Avalonia.Media.TranslateTransform(-offset, 0);
            if (offset >= distance) timer?.Stop();
        };
        timer.Start();
    }
    private void Reset()
    {
        timer?.Stop(); timer = null; offset = 0; hold = 0; Scrolling = false;
        text.RenderTransform = null; text.ClearValue(Layoutable.WidthProperty); text.TextTrimming = TextTrimming.CharacterEllipsis;
    }
}

public sealed class AccountList : StackPanel
{
    private readonly TimerModel model;
    private readonly List<(TimerAccount Account, Button Row, TextBlock Today, TextBlock Budget, AccountIndicator Icon, TextBlock Dot, BudgetBar? Bar, TextBlock Period)> rows = new();
    private readonly bool reorderable, bars, compact;
    /// <summary>A row of the bar kind: the name and time on one line with the budget bar under it, and the play tile beside them.</summary>
    public const double RowHeight = 46;
    /// <param name="bars">Shows each account's budget as a bar with a pace tick and a plain play circle, as the dashboard does.</param>
    /// <param name="compact">With bars, a shorter row for the pill's narrow drawer: the name and time, the bar and the play button, without the budget words.</param>
    public AccountList(TimerModel model, bool reorderable = false, bool bars = false, bool compact = false)
    {
        this.model = model; this.reorderable = reorderable; this.bars = bars; this.compact = compact;
        Rebuild();
        // A list that sat hidden (in a pop-up) missed the changes made meanwhile, so it catches up when it is shown.
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Changed(null, new System.ComponentModel.PropertyChangedEventArgs(null)); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
    }
    // The break has its own button, set apart from the accounts, so it is not one of the rows.
    private static IEnumerable<TimerAccount> Listed(TimerModel model) => model.Accounts.Where(a => !BreakAccount.Is(a.Name));
    private void Rebuild()
    {
        Children.Clear(); rows.Clear();
        foreach (var account in Listed(model))
        {
            var row = new Button { Name = "Account" + rows.Count, Classes = { "account" } };
            // Rows can still be dragged to reorder them where that is allowed, but there is no handle: it only took room from the row.
            const int shift = 0;
            var grid = Ui.Grid(bars ? "*,Auto" : "38,*,Auto");
            var icon = new AccountIndicator(account, model) { Ring = !bars };
            var dot = Ui.Text("●", 10, "live"); dot.IsVisible = false;
            var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = bars ? new Thickness(2, 0, 0, 0) : new Thickness(10, 0, 10, 0) };
            var budget = Ui.Text(bars ? model.BudgetLine(account) : model.BudgetSummary(account), 12, "muted");
            budget.TextTrimming = TextTrimming.CharacterEllipsis;
            MarqueeText? marquee = null;
            BudgetBar? bar = null; var percent = Ui.Text("", 12, "muted");
            TextBlock today;
            if (bars)
            {
                // The account's own colour leads the name, with today's time at the end of that line. Under it the budget used and the period it covers, then
                // the bar. The play circle takes the right-hand side, and the percentage is left to the hover detail.
                var swatch = new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
                    Fill = Avalonia.Media.Brush.Parse(Ui.Visible(account.Colour)) };
                labels.Spacing = 5; icon.Sized(AccountIndicator.RowSize);
                // The percentage sits at the end of the name line, which leaves the whole line under it for the budget in words.
                var nameLine = Ui.Grid("*,Auto"); var nameStack = new NameRow { Spacing = 0 }; // The live dot follows the name closely instead of sitting out by today's time.
                marquee = new MarqueeText(account.Display, 13) { Name = "AccountName" }; var nameText = marquee; dot.Margin = new Thickness(8, 0, 0, 0);
                nameStack.Children.Add(swatch); nameStack.Children.Add(nameText); nameStack.Children.Add(dot);
                today = Ui.Text(model.Format(model.Today(account)), 13); nameStack.Margin = new Thickness(0, 0, 4, 0); today.FontWeight = Avalonia.Media.FontWeight.Bold; today.VerticalAlignment = VerticalAlignment.Center; today.TextAlignment = Avalonia.Media.TextAlignment.Right;
                Ui.Put(nameLine, nameStack); Ui.Put(nameLine, today, 1);
                // The budget used is in the text colour (red once spent) and the period it covers is quiet and lines up with today's time above it.
                budget.Classes.Remove("muted"); budget.FontSize = 13;
                var period = Ui.Text("", 12, "muted"); period.TextAlignment = Avalonia.Media.TextAlignment.Right; period.VerticalAlignment = VerticalAlignment.Center;
                // The budget used and its period are not drawn: they are in the row's tooltip, so the name and time keep the line and the panel can be narrow. The two
                // texts are still kept (off the screen) for the tooltip to be worded from.
                percent = period;
                labels.Children.Add(nameLine);
                if (account.Budget is not null) { bar = new BudgetBar(); labels.Children.Add(bar); }
                icon.Margin = new Thickness(4, 0, 0, 0);
                Ui.Put(grid, labels, shift); Ui.Put(grid, icon, 1 + shift);
                // A rounded card inset from the edges, so the recording account's soft highlight has room to show. The same one line row on the dashboard and the pill.
                row.Classes.Add("accountCard"); row.Margin = new Thickness(0); row.Height = RowHeight;
            }
            else
            {
                Ui.Put(grid, icon, shift);
                labels.Margin = new Thickness(10, 0);
                labels.Children.Add(Ui.Horizontal(Ui.Text(account.Display, 15), dot));
                labels.Children.Add(budget);
                Ui.Put(grid, labels, 1 + shift);
                today = Ui.Put(grid, Ui.Text(model.Format(model.Today(account))), 2 + shift);
            }
            row.Content = grid;
            row.Click += (_, _) => model.ToggleAccount(account.Name);
            row.PointerEntered += (_, _) => { icon.Glow = 1; marquee?.Hover(true); };
            row.PointerExited += (_, _) => { icon.Glow = 0; marquee?.Hover(false); };
            if (reorderable && !BreakAccount.Is(account.Name)) AttachReorder(row, account.Name);
            var settings = new MenuItem { Header = "Name and budget…", Name = "AccountSettingsMenu" };
            // The pill has no room for a dialog, so the dashboard shows it, and comes up first when it is hidden.
            settings.Click += (_, _) => model.OpenAccounts();
            row.ContextMenu = new ContextMenu { ItemsSource = new Control[] { settings } };
            Children.Add(row);
            rows.Add((account, row, today, budget, icon, dot, bar, percent));
        }
        Refresh();
        if (pendingGlide is { } glide)
        {
            pendingGlide = null;
            if (rows.FirstOrDefault(r => r.Account.Name == glide.Name).Row is { } moved)
            {
                moved.RenderTransform = Shift(glide.Offset); moved.ZIndex = 1; moved.Opacity = .85;
                // On the next pass the transform returns to nothing, and the transition carries the row there smoothly.
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    moved.Transitions = Glide(220); moved.RenderTransform = Shift(0); moved.Opacity = 1;
                    var done = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
                    done.Tick += (_, _) => { done.Stop(); moved.Transitions = null; moved.RenderTransform = null; moved.ZIndex = 0; };
                    done.Start();
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
        }
    }
    // Dragging a row up or down reorders the accounts. A press that never moves still starts or stops tracking as usual.
    // The rows it passes slide aside to show where the account will land, and on release it glides into its new place,
    // or back to where it was if the drag came to nothing, instead of snapping.
    private static Avalonia.Animation.Transitions Glide(int milliseconds) => new()
    { new Avalonia.Animation.TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Motion.Ease(milliseconds), Easing = new Avalonia.Animation.Easings.CubicEaseOut() } };
    private static Avalonia.Media.Transformation.TransformOperations Shift(double y)
        => Avalonia.Media.Transformation.TransformOperations.Parse($"translateY({y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}px)");
    private (string Name, double Offset)? pendingGlide;
    private void AttachReorder(Button row, string name)
    {
        Point? press = null; var dragging = false; var target = 0; var dragOffset = 0.0;
        int Index() => rows.FindIndex(r => r.Account.Name == name);
        void Finish(bool commit)
        {
            var from = Index(); var to = target; var height = row.Bounds.Height;
            press = null; dragging = false;
            if (commit && from >= 0 && to != from)
            {
                // The list is rebuilt in its new order. The moved row starts where the pointer left it and glides to its slot.
                pendingGlide = (name, from * height + dragOffset - to * height);
                model.MoveAccount(name, to);
                return;
            }
            // Nothing moved, or the drag was cancelled: everything glides back, then the animation machinery is cleared away.
            foreach (var r in rows) { r.Row.Transitions = Glide(180); r.Row.RenderTransform = Shift(0); r.Row.Opacity = 1; }
            var reset = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(230) };
            reset.Tick += (_, _) =>
            {
                reset.Stop();
                foreach (var r in rows) { r.Row.Transitions = null; r.Row.RenderTransform = null; r.Row.ZIndex = 0; }
            };
            reset.Start();
        }
        row.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { press = e.GetPosition(this); dragging = false; } }, RoutingStrategies.Tunnel);
        row.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            if (press is not { } start) return;
            var dy = e.GetPosition(this).Y - start.Y;
            if (!dragging)
            {
                if (Math.Abs(dy) < 6) return;
                dragging = true; e.Pointer.Capture(row);
                // The other rows slide when they move aside; the dragged row follows the pointer exactly, so it has no glide.
                foreach (var r in rows) { r.Row.Transitions = null; r.Row.RenderTransform = Shift(0); }
                foreach (var r in rows.Where(r => r.Account.Name != name)) r.Row.Transitions = Glide(140);
            }
            var from = Index(); var height = row.Bounds.Height;
            if (from < 0 || height <= 0) return;
            var movable = rows.Count(r => !BreakAccount.Is(r.Account.Name));
            target = Math.Clamp((int)Math.Round(from + dy / height), 0, Math.Max(0, movable - 1));
            for (var j = 0; j < rows.Count; j++)
            {
                if (j == from) continue;
                var shift = from < target && j > from && j <= target ? -height : target < from && j >= target && j < from ? height : 0;
                rows[j].Row.RenderTransform = Shift(shift);
            }
            dragOffset = Math.Clamp(dy, -from * height, (movable - 1 - from) * height);
            row.RenderTransform = Shift(dragOffset);
            row.Opacity = .8; row.ZIndex = 1; e.Handled = true;
        }, RoutingStrategies.Tunnel);
        row.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            if (press is null) return;
            if (!dragging) { press = null; return; }
            e.Handled = true; Finish(true); e.Pointer.Capture(null);
        }, RoutingStrategies.Tunnel);
        row.PointerCaptureLost += (_, _) => { if (dragging) Finish(false); press = null; };
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!rows.Select(r => r.Account).SequenceEqual(Listed(model))) Rebuild(); else Refresh();
    }
    private void Refresh()
    {
        foreach (var row in rows)
        {
            row.Row.Classes.Set("active", model.ActiveAccount == row.Account.Name);
            row.Today.Text = model.Format(model.Today(row.Account));
            row.Budget.Text = bars ? model.BudgetUsage(row.Account) : model.BudgetSummary(row.Account);
            if (bars) { row.Period.Text = model.BudgetPeriodLabel(row.Account); ToolTip.SetTip(row.Row, model.BudgetRowTip(row.Account)); }
            if (row.Bar is { } bar && model.BudgetRatio(row.Account) is { } ratio)
            {
                bar.Set(ratio, model.BudgetPace(row.Account));
                // The percentage and what the pace line means are in the hover detail, and a spent budget turns the usage red so overrunning reads at a glance.
                var detail = model.BudgetDetail(row.Account);
                ToolTip.SetTip(bar, detail); ToolTip.SetTip(row.Budget, detail);
                row.Budget.Classes.Set("overtime", ratio >= 1);
            }
            row.Icon.InvalidateVisual();
            // The live dot pulses beside the account that is recording.
            var live = model.ActiveAccount == row.Account.Name;
            row.Dot.IsVisible = live; row.Dot.Classes.Set("break", model.IsOnBreak); row.Dot.Classes.Set("live", !model.IsOnBreak);
            Ui.Pulse(row.Dot, live);
        }
    }
}

public sealed class TrackingLabels : StackPanel
{
    private readonly TextBlock status = Ui.Text("", 12);
    private readonly Avalonia.Controls.Shapes.Ellipse dot = new() { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
    public TrackingLabels(TimerModel model)
    {
        Orientation = Orientation.Vertical;
        Spacing = 3;
        // The status dot is a real shape in a box as wide as the account's colour dot above it, so the two dots share one centre line.
        var dotBox = new Border { Width = 10, Child = dot, VerticalAlignment = VerticalAlignment.Center };
        var line = Ui.Horizontal(dotBox, status); line.Spacing = 8;
        Children.Add(line);
        void Refresh()
        {
            status.Text = model.Status;
            // A break turns the status line, dot included, amber; otherwise it is green while tracking and grey when not.
            foreach (Control control in new Control[] { status, dot })
            {
                control.Classes.Set("break", model.IsOnBreak);
                control.Classes.Set("live", model.IsTracking && !model.IsOnBreak);
                control.Classes.Set("idle", !model.IsTracking);
            }
        }
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Refresh(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
    }
}

public sealed class Chevron : Control
{
    public bool Expanded { get; set; }
    public Chevron() { Width = 14; Height = 14; }
    // Drawn in the theme's text colour, which changes when the pill turns to a recording or break look, so it must be drawn again then.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(ActualThemeVariant)) InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        var brush = this.TryFindResource("Ink", ActualThemeVariant, out var value) ? value as IBrush : Brushes.Gray;
        var pen = new Pen(brush, IconGlyph.StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var y = Expanded ? 9 : 5;
        context.DrawLine(pen, new Point(2, y), new Point(7, Expanded ? 4 : 10));
        context.DrawLine(pen, new Point(7, Expanded ? 4 : 10), new Point(12, y));
    }
}

public sealed class PauseGlyph : Control
{
    // Eases from 0 to 1 while the pointer is over the button, growing the symbol and tinting it with the accent so it reads as pressable.
    public static readonly StyledProperty<double> GlowProperty = AvaloniaProperty.Register<PauseGlyph, double>(nameof(Glow));
    static PauseGlyph() => AffectsRender<PauseGlyph>(GlowProperty);
    public double Glow { get => GetValue(GlowProperty); set => SetValue(GlowProperty, value); }
    public bool Playing { get; set; }
    /// <summary>A fixed colour for the symbol, for when it sits on a filled circle and must read against that fill rather than the theme's text.</summary>
    public IBrush? Fixed { get; set; }
    public PauseGlyph()
    {
        Width = Height = 14;
        Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = GlowProperty, Duration = IconGlyph.HoverDuration } };
    }
    // Drawn in the theme's text colour, which changes when the pill turns to a recording or break look, so it must be drawn again then.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(ActualThemeVariant)) InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        var glow = Math.Clamp(Glow, 0, 1);
        using var grow = context.PushTransform(Matrix.CreateTranslation(-7, -7) * Matrix.CreateScale(1 + .08 * glow, 1 + .08 * glow) * Matrix.CreateTranslation(7, 7));
        var normal = this.TryFindResource("Ink", ActualThemeVariant, out var value) ? value as IBrush : Brushes.Gray;
        var tint = this.TryFindResource("Accent", ActualThemeVariant, out var accentValue) ? accentValue as IBrush : normal;
        // The accent can be too faint on a theme's panel, so it is only mixed in as the pointer arrives and the shape itself is what grows.
        var brush = Fixed ?? (glow > .5 ? tint : normal);
        if (Playing)
        {
            context.DrawRectangle(brush, null, new Rect(2, 1, 4, 12), 1, 1);
            context.DrawRectangle(brush, null, new Rect(8, 1, 4, 12), 1, 1);
        }
        else context.DrawGeometry(brush, null, Geometry.Parse("M 3,1 L 13,7 L 3,13 Z"));
    }
}

// A cup, for the break button. It is drawn with the break colour, or the surface colour when it sits on a filled button.
// While a break is on, two wisps of steam drift up from it and fade.
public sealed class BreakGlyph : Control
{
    public static readonly StyledProperty<double> SteamProperty = AvaloniaProperty.Register<BreakGlyph, double>(nameof(Steam));
    // How far the steam has taken over from the still wisps, from 0 to 1. It eases in and out, so hovering the cup does not snap the wisps from still to moving.
    public static readonly StyledProperty<double> FlowProperty = AvaloniaProperty.Register<BreakGlyph, double>(nameof(Flow));
    static BreakGlyph() => AffectsRender<BreakGlyph>(SteamProperty, FlowProperty);
    public double Steam { get => GetValue(SteamProperty); set => SetValue(SteamProperty, value); }
    public double Flow { get => GetValue(FlowProperty); set => SetValue(FlowProperty, value); }
    /// <summary>One slow cycle of the steam, and how long the steam takes to arrive or fade.</summary>
    public static readonly TimeSpan SteamCycle = TimeSpan.FromSeconds(3.2), SteamEase = TimeSpan.FromMilliseconds(600);
    private bool wanted;
    public bool Filled { get; set; }
    /// <summary>A fixed colour for the cup, for when it sits on a filled square and must read against that fill.</summary>
    public IBrush? Fixed { get; set; }
    /// <summary>Draws the cup in plain text colour, and in <see cref="HotKey"/> only while the pointer is over its button.</summary>
    public bool Mono { get; set; }
    private bool hot;
    public bool Hot { get => hot; set { if (hot == value) return; hot = value; InvalidateVisual(); } }
    public string HotKey { get; set; } = "Break";
    private CancellationTokenSource? steaming; private RunningAnimation? steamRun;
    // Counted as steaming only while its animation is really running, so one that stopped (its window was hidden) is started again when asked.
    public bool Steaming => wanted && steaming is not null && steamRun is { Alive: true };
    public BreakGlyph()
    {
        Width = Height = 18;
        Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = FlowProperty, Duration = Motion.Ease(SteamEase), Easing = new Avalonia.Animation.Easings.CubicEaseInOut() } };
    }
    public void SetSteaming(bool on)
    {
        // With less motion asked for, the cup shows its steam still while it is lit, and nothing moves.
        if (Motion.Reduced)
        {
            wanted = on; Flow = on ? 1 : 0; Steam = 0;
            if (steaming is not null) { steaming.Cancel(); steaming = null; }
            return;
        }
        if (on && Steaming) return;
        wanted = on;
        if (!on)
        {
            // The steam is left to fade out over its ease, and the animation is stopped once that has finished (unless it was asked for again).
            Flow = 0; var stopping = steaming;
            if (stopping is not null) DispatcherTimer.RunOnce(() => { if (!wanted && ReferenceEquals(steaming, stopping)) { stopping.Cancel(); steaming = null; Steam = 0; } }, SteamEase + TimeSpan.FromMilliseconds(100));
            return;
        }
        Flow = 1;
        if (steaming is not null && steamRun is { Alive: true }) return; // Still turning from a moment ago: it just picks up again.
        if (steaming is not null) { steaming.Cancel(); steaming = null; Steam = 0; }
        steaming = new CancellationTokenSource();
        var animation = new Avalonia.Animation.Animation
        {
            Duration = SteamCycle, IterationCount = Avalonia.Animation.IterationCount.Infinite,
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Setter(SteamProperty, 0.0) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Setter(SteamProperty, 1.0) } },
            }
        };
        steamRun = new RunningAnimation(animation.RunAsync(this, steaming.Token));
    }
    // Drawn in the theme's text colour, which changes when the pill turns to a recording or break look, so it must be drawn again then.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(ActualThemeVariant)) InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        IBrush? brush = null;
        var key = Mono ? (Hot ? HotKey : "Ink") : "Break";
        if (Fixed is not null) brush = Fixed; else if (Filled) brush = Brushes.White; else if (this.TryFindResource(key, ActualThemeVariant, out var value)) brush = value as IBrush;
        var pen = new Pen(brush ?? Brushes.Gray, IconGlyph.StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(null, pen, Geometry.Parse("M 3,7 L 3,12 Q 3,15.5 6.5,15.5 L 9.5,15.5 Q 13,15.5 13,12 L 13,7 Z"));
        context.DrawGeometry(null, pen, Geometry.Parse("M 13,8 L 14.5,8 Q 16.5,8 16.5,10 Q 16.5,12 14.5,12 L 13,12"));
        // Two wisps, half a cycle apart. Each rises a little and fades in and out; when still they sit where they always did.
        for (var i = 0; i < 2; i++)
        {
            // The still wisp and the moving one are blended by Flow, so the change between them is gradual in both directions.
            var x = 6.0 + 4 * i; var phase = (Steam + .5 * i) % 1.0; var flow = Math.Clamp(Flow, 0.0, 1.0);
            var rise = phase * 3.5 * flow; var alpha = 1.0 + (Math.Sin(phase * Math.PI) - 1.0) * flow;
            using (context.PushOpacity(Math.Clamp(alpha, 0.0, 1.0)))
                context.DrawLine(pen, new Point(x, 4 - rise), new Point(x, 1.5 - rise));
        }
    }
}

public enum IconKind { NotesList, Check, Clock, Search, Bell, Repeat, Note, Minus, Grip, Gear, Minimise, Export, Copy, Flag, Timeline, Week, Table, Entries, Plus, Undo, Redo, Merge, Trash, Today, Close, Edit, Previous, Next, Accounts, Sunrise, Sliders, Dock, Star, StarFilled }

// Small line icons for buttons that have no words. They are drawn in the ordinary text colour so every theme suits them.
public sealed class IconGlyph : Control
{
    public const double StrokeWidth = 1.6;
    public static TimeSpan HoverDuration => Motion.Ease(200);
    public static readonly StyledProperty<double> HoverProperty = AvaloniaProperty.Register<IconGlyph, double>(nameof(Hover));
    static IconGlyph() => AffectsRender<IconGlyph>(HoverProperty);
    public double Hover { get => GetValue(HoverProperty); set => SetValue(HoverProperty, value); }
    /// <summary>The three rail icons are drawn on the same 18 unit grid as the others, a little larger, and play a short loop while the pointer is over them.</summary>
    public const double RailSize = 22;
    private static bool IsRail(IconKind kind) => kind is IconKind.Timeline or IconKind.Week or IconKind.NotesList;
    public IconKind Kind { get; }
    public IconGlyph(IconKind kind)
    {
        Kind = kind; Width = Height = IsRail(kind) ? RailSize : 18;
        if (kind == IconKind.Grip) Width = 10; // A narrow strip, so the grip takes little of the row.
        Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = HoverProperty, Duration = HoverDuration } };
    }
    private Button? owner; private bool keyboardFocus;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        owner = this.GetVisualAncestors().OfType<Button>().FirstOrDefault();
        if (owner is null) return;
        owner.PropertyChanged += OwnerChanged; owner.GotFocus += OwnerFocused; owner.LostFocus += OwnerBlurred;
        UpdateHover();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (owner is not null) { owner.PropertyChanged -= OwnerChanged; owner.GotFocus -= OwnerFocused; owner.LostFocus -= OwnerBlurred; }
        owner = null; keyboardFocus = false; Hover = 0;
        Waving = false; Play(false);
        base.OnDetachedFromVisualTree(e);
    }
    private void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsPointerOverProperty || e.Property == IsEffectivelyEnabledProperty) UpdateHover();
    }
    // Keyboard focus moves an icon the way the pointer does, but focus left behind by a mouse click does not. A clicked button keeps the focus, so counting that
    // left the icon held open for good and the next mouse-over had nothing to play.
    private void OwnerFocused(object? sender, GotFocusEventArgs e) { keyboardFocus = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional; UpdateHover(); }
    private void OwnerBlurred(object? sender, RoutedEventArgs e) { keyboardFocus = false; UpdateHover(); }
    private void UpdateHover()
    {
        Hover = owner is { IsEffectivelyEnabled: true } && (owner.IsPointerOver || keyboardFocus) ? 1 : 0;
        Play(IsRail(Kind) && owner is { IsEffectivelyEnabled: true, IsPointerOver: true });
    }
    // The rail icons loop while the pointer is over them and settle back to rest when it leaves. Less motion asked for means they stay still.
    private DispatcherTimer? playTimer; private readonly System.Diagnostics.Stopwatch playClock = new(); private double playSeconds;
    private void Play(bool on)
    {
        on &= !Motion.Reduced;
        if (on && playTimer is null)
        {
            playClock.Restart(); playSeconds = 0;
            playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            playTimer.Tick += (_, _) => { playSeconds = playClock.Elapsed.TotalSeconds; InvalidateVisual(); };
            playTimer.Start();
        }
        else if (!on && playTimer is not null) { playTimer.Stop(); playTimer = null; playSeconds = 0; InvalidateVisual(); }
    }
    /// <summary>The seconds into its loop that the icon is drawn at (0 while at rest). The tests set it to look at a moment of the loop.</summary>
    public double PlaySeconds { get => playSeconds; set { playSeconds = value; InvalidateVisual(); } }
    private string? dotKey;
    /// <summary>The theme brush the pill icon's dot turns to under the pointer, such as Live while tracking, so the icon says how the day is going.</summary>
    public string? DotKey { get => dotKey; set { if (dotKey == value) return; dotKey = value; InvalidateVisual(); } }
    private string? tintKey;
    /// <summary>The theme brush this icon is drawn in, when it should not follow the text colour of its button.</summary>
    public string? TintKey { get => tintKey; set { if (tintKey == value) return; tintKey = value; InvalidateVisual(); } }
    // The icons are drawn in the theme's text colour, so they must redraw when the theme changes while they stay on screen.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(ActualThemeVariant) || change.Property == Avalonia.Controls.Documents.TextElement.ForegroundProperty) InvalidateVisual();
    }
    // Waving: the flag ripples while the pointer is over its button, and settles smoothly when it leaves.
    private double amplitude, phase; private bool waving; private DispatcherTimer? waveTimer;
    /// <summary>How far the flag is currently rippling, from 0 (still) to 1 (full).</summary>
    public bool Waving
    {
        get => waving;
        set
        {
            waving = value && !Motion.Reduced;
            if (waveTimer is not null || Kind != IconKind.Flag || !waving) return;
            waveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            waveTimer.Tick += (_, _) =>
            {
                amplitude += ((waving ? 1.0 : 0.0) - amplitude) * 0.18; phase += 0.4;
                if (!waving && amplitude < 0.02) { amplitude = 0; waveTimer!.Stop(); waveTimer = null; }
                InvalidateVisual();
            };
            waveTimer.Start();
        }
    }
    private static Geometry RoundBox(Rect box)
    {
        const double r = 1.8;
        return Geometry.Parse(FormattableString.Invariant($"M {box.X + r},{box.Y} H {box.Right - r} A {r},{r} 0 0 1 {box.Right},{box.Y + r} V {box.Bottom - r} A {r},{r} 0 0 1 {box.Right - r},{box.Bottom} H {box.X + r} A {r},{r} 0 0 1 {box.X},{box.Bottom - r} V {box.Y + r} A {r},{r} 0 0 1 {box.X + r},{box.Y} Z"));
    }
    /// <summary>
    /// A box with strokes cut out of it, each drawn as far as <paramref name="progress"/> along itself (0 leaves the box whole), so a mark can draw itself in and be
    /// taken back. The pieces are cut one at a time, since cutting them as a group lets overlapping pieces of opposite winding cancel.
    /// </summary>
    private static Geometry CutOut(Rect box, Point[][] strokes, double progress)
    {
        Geometry shape = RoundBox(box);
        if (progress <= 0.001) return shape;
        const double half = .7;
        Geometry Dot(Point at) => new EllipseGeometry(new Rect(at.X - half, at.Y - half, half * 2, half * 2));
        foreach (var stroke in strokes)
        {
            var length = 0.0; for (var i = 1; i < stroke.Length; i++) length += Math.Sqrt(Math.Pow(stroke[i].X - stroke[i - 1].X, 2) + Math.Pow(stroke[i].Y - stroke[i - 1].Y, 2));
            var left = length * Math.Min(1, progress);
            shape = new CombinedGeometry(GeometryCombineMode.Exclude, shape, Dot(stroke[0]));
            for (var i = 1; i < stroke.Length && left > 0; i++)
            {
                var from = stroke[i - 1]; var to = stroke[i]; var span = Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));
                var take = Math.Min(1, left / span); var end = new Point(from.X + (to.X - from.X) * take, from.Y + (to.Y - from.Y) * take); left -= span;
                var nx = -(to.Y - from.Y) / span * half; var ny = (to.X - from.X) / span * half;
                var quad = new PolylineGeometry(new[] { new Point(from.X + nx, from.Y + ny), new Point(end.X + nx, end.Y + ny), new Point(end.X - nx, end.Y - ny), new Point(from.X - nx, from.Y - ny) }, true);
                shape = new CombinedGeometry(GeometryCombineMode.Exclude, new CombinedGeometry(GeometryCombineMode.Exclude, shape, quad), Dot(end));
            }
        }
        return shape;
    }
    /// <summary>
    /// The theme colour the icon is drawn in: its own tint, else the accent's text colour inside a primary button (white on the Light accent), else the ink.
    /// A global style keeps a button's words in the ink, so a primary button's icon has to ask for the accent's colour itself.
    /// </summary>
    public string ColourKey => TintKey ?? (this.FindAncestorOfType<Button>() is { } host && host.Classes.Contains("primary") ? "AccentInk" : "Ink");
    public override void Render(DrawingContext context)
    {
        IBrush brush = this.TryFindResource(ColourKey, ActualThemeVariant, out var value) && value is IBrush found ? found : Brushes.Gray;
        var pen = new Pen(brush, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var hover = Math.Clamp(Hover, 0, 1);
        // Every icon keeps its hit target and layout still. Only a small part of the drawing moves.
        var angle = Kind == IconKind.Gear ? hover * Math.PI / 9 : Kind == IconKind.Undo ? -hover * Math.PI / 15 : Kind == IconKind.Redo ? hover * Math.PI / 15 : 0;
        using var turn = context.PushTransform(Matrix.CreateTranslation(-9, -9) * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(9, 9));
        using var grow = IsRail(Kind) ? context.PushTransform(Matrix.CreateScale(Bounds.Width / 18.0, Bounds.Height / 18.0)) : default;
        switch (Kind)
        {
            case IconKind.Timeline:
            {
                // Three solid bars of different lengths, staggered: the day as blocks in time. While played they slide a little, each at its own pace.
                var bars = new[] { new Rect(2.5, 3.6, 8.5, 3), new Rect(6.5, 7.6, 9, 3), new Rect(4, 11.6, 6.5, 3) };
                for (var i = 0; i < 3; i++) context.DrawRectangle(brush, null, bars[i].Translate(new Vector(RailMotion.BarOffset(i, playSeconds), 0)), 1.5, 1.5);
                break;
            }
            case IconKind.NotesList:
            {
                // A solid box with a tick cut out of it, an empty solid box under it, and a line for each: the notes as a list to tick off. While played the tick
                // draws itself in the top box and is taken back, and a cross draws itself in the bottom box in its turn.
                var thin = new Pen(brush, 1.7, lineCap: PenLineCap.Round);
                context.DrawLine(thin, new Point(10.4, 5.2), new Point(15.6, 5.2)); context.DrawLine(thin, new Point(10.4, 13.2), new Point(15.6, 13.2));
                context.DrawGeometry(brush, null, CutOut(new Rect(1.8, 2, 6.4, 6.4), new[] { new[] { new Point(3.2, 5.05), new Point(4.6, 6.45), new Point(6.9, 3.65) } }, RailMotion.TickProgress(playSeconds)));
                context.DrawGeometry(brush, null, CutOut(new Rect(1.8, 10, 6.4, 6.4), new[] { new[] { new Point(3.9, 12.1), new Point(6.1, 14.3) }, new[] { new Point(6.1, 12.1), new Point(3.9, 14.3) } }, RailMotion.CrossProgress(playSeconds)));
                break;
            }
            case IconKind.Trash:
                // A bin: a lid with a handle, and a tapering body with two slats.
                context.DrawLine(pen, new Point(3, 5), new Point(15, 5)); context.DrawLine(pen, new Point(7, 5), new Point(7, 3)); context.DrawLine(pen, new Point(7, 3), new Point(11, 3)); context.DrawLine(pen, new Point(11, 3), new Point(11, 5));
                context.DrawGeometry(null, pen, Geometry.Parse("M 4.5,5 L 5.3,15 Q 5.4,16 6.4,16 L 11.6,16 Q 12.6,16 12.7,15 L 13.5,5"));
                context.DrawLine(pen, new Point(7.6, 8), new Point(7.8, 13)); context.DrawLine(pen, new Point(10.4, 8), new Point(10.2, 13));
                break;
            case IconKind.Plus:
                context.DrawLine(pen, new Point(9, 3.5), new Point(9, 14.5)); context.DrawLine(pen, new Point(3.5, 9), new Point(14.5, 9));
                break;
            case IconKind.Search:
                context.DrawEllipse(null, pen, new Point(8, 8), 4.9, 4.9); context.DrawLine(pen, new Point(11.7, 11.7), new Point(15, 15));
                break;
            case IconKind.Check:
                // A tick: this is done.
                context.DrawGeometry(null, pen, Geometry.Parse("M 3.75,9.5 L 7.5,13.25 L 14.25,5"));
                break;
            case IconKind.Star:
            case IconKind.StarFilled:
            {
                // A five point star: this note is important. Outlined to offer it, solid once it is.
                var points = new Point[10];
                for (var i = 0; i < 10; i++) { var a = -Math.PI / 2 + i * Math.PI / 5; var r = i % 2 == 0 ? 6.6 : 2.9; points[i] = new Point(9 + r * Math.Cos(a), 9.6 + r * Math.Sin(a)); }
                context.DrawGeometry(Kind == IconKind.StarFilled ? brush : null, pen, new PolylineGeometry(points, true));
                break;
            }
            case IconKind.Clock:
                // A clock face: move this to another time.
                context.DrawEllipse(null, pen, new Point(9, 9), 6.25, 6.25);
                context.DrawGeometry(null, pen, Geometry.Parse("M 9,5.25 L 9,9 L 11.75,10.5"));
                break;
            case IconKind.Bell:
                context.DrawGeometry(null, pen, Geometry.Parse("M 4.5,12.75 L 4.5,8.25 A 4.5,4.5 0 0 1 13.5,8.25 L 13.5,12.75 L 14.625,14.25 L 3.375,14.25 Z"));
                context.DrawLine(pen, new Point(7.5, 15.75), new Point(10.5, 15.75));
                break;
            case IconKind.Repeat:
                // Two arrows chasing each other round a loop: this repeats.
                context.DrawGeometry(null, pen, Geometry.Parse("M 12.75,2.25 L 15.75,5.25 L 12.75,8.25"));
                context.DrawGeometry(null, pen, Geometry.Parse("M 2.25,8.25 L 2.25,6.75 A 1.5,1.5 0 0 1 3.75,5.25 L 15.75,5.25"));
                context.DrawGeometry(null, pen, Geometry.Parse("M 5.25,15.75 L 2.25,12.75 L 5.25,9.75"));
                context.DrawGeometry(null, pen, Geometry.Parse("M 15.75,9.75 L 15.75,11.25 A 1.5,1.5 0 0 1 14.25,12.75 L 2.25,12.75"));
                break;
            case IconKind.Note:
                // A page with a folded corner and two lines of writing: this block has a note.
                context.DrawGeometry(null, pen, Geometry.Parse("M 5.25,2.25 L 10.5,2.25 L 13.5,5.25 L 13.5,15.75 L 5.25,15.75 Z"));
                context.DrawGeometry(null, pen, Geometry.Parse("M 10.5,2.25 L 10.5,5.25 L 13.5,5.25 M 7.5,9 L 11.25,9 M 7.5,12 L 11.25,12"));
                break;
            case IconKind.Minus:
                context.DrawLine(pen, new Point(3.5, 9), new Point(14.5, 9));
                break;
            case IconKind.Undo:
                // A hook of an arrow curling back to the left.
                context.DrawGeometry(null, pen, Geometry.Parse("M 7,4.5 L 3.5,8 L 7,11.5"));
                context.DrawGeometry(null, pen, Geometry.Parse("M 3.8,8 L 10.5,8 Q 14.5,8 14.5,11.2 Q 14.5,14.2 10.5,14.2 L 8.5,14.2"));
                break;
            case IconKind.Redo:
                // Undo's hook turned the other way, curling back to the right.
                context.DrawGeometry(null, pen, Geometry.Parse("M 11,4.5 L 14.5,8 L 11,11.5"));
                context.DrawGeometry(null, pen, Geometry.Parse("M 14.2,8 L 7.5,8 Q 3.5,8 3.5,11.2 Q 3.5,14.2 7.5,14.2 L 9.5,14.2"));
                break;
            case IconKind.Merge:
                // Two links of a chain: blocks joined into one.
                context.DrawGeometry(null, pen, Geometry.Parse("M 7.5,10.5 a 3,3 0 0 0 4.275,0 l 2.25,-2.25 a 3,3 0 0 0 -4.275,-4.275 l -0.75,0.75"));
                context.DrawGeometry(null, pen, Geometry.Parse("M 10.5,7.5 a 3,3 0 0 0 -4.275,0 l -2.25,2.25 a 3,3 0 0 0 4.275,4.275 l 0.75,-0.75"));
                break;
            case IconKind.Week:
            {
                // Three solid columns on a line: the week at a glance. While played they spring up from the line one after another.
                context.DrawLine(new Pen(brush, 1.25, lineCap: PenLineCap.Round), new Point(2, 15.8), new Point(16, 15.8));
                var columns = new[] { new Rect(2.8, 8.6, 2.8, 5.6), new Rect(7.6, 4.6, 2.8, 9.6), new Rect(12.4, 7, 2.8, 7.2) };
                for (var i = 0; i < 3; i++)
                {
                    var rise = RailMotion.ColumnScale(i, playSeconds); var c = columns[i]; var h = c.Height * rise;
                    context.DrawRectangle(brush, null, new Rect(c.X, c.Bottom - h, c.Width, h), 1.4, 1.4);
                }
                break;
            }
            case IconKind.Accounts:
                // Two rows, each a round swatch and a line: the accounts as a short list.
                foreach (var y in new[] { 5.0, 12.5 })
                {
                    context.DrawEllipse(null, pen, new Point(4.8, y), 2.1, 2.1);
                    context.DrawLine(pen, new Point(9.5, y), new Point(15.5, y));
                }
                break;
            case IconKind.Sliders:
                // Two lines, each broken by a round knob: the settings of the accounts.
                context.DrawLine(pen, new Point(3, 5.3), new Point(10.5, 5.3)); context.DrawLine(pen, new Point(13.5, 5.3), new Point(15, 5.3));
                context.DrawEllipse(null, pen, new Point(12, 5.3), 1.5, 1.5);
                context.DrawLine(pen, new Point(3, 12.7), new Point(4.5, 12.7)); context.DrawLine(pen, new Point(7.5, 12.7), new Point(15, 12.7));
                context.DrawEllipse(null, pen, new Point(6, 12.7), 1.5, 1.5);
                break;
            case IconKind.Dock:
                // A window with its left side set apart: the account panel kept beside the page.
                context.DrawGeometry(null, pen, Geometry.Parse("M 4.5,3.5 L 13.5,3.5 Q 15.5,3.5 15.5,5.5 L 15.5,12.5 Q 15.5,14.5 13.5,14.5 L 4.5,14.5 Q 2.5,14.5 2.5,12.5 L 2.5,5.5 Q 2.5,3.5 4.5,3.5 Z"));
                context.DrawLine(pen, new Point(7.2, 3.8), new Point(7.2, 14.2));
                break;
            case IconKind.Sunrise:
                // A half sun on the horizon with short rays: the day beginning.
                context.DrawLine(pen, new Point(2, 13.5), new Point(16, 13.5));
                context.DrawGeometry(null, pen, Geometry.Parse("M 5.5,13.5 A 3.5,3.5 0 0 1 12.5,13.5"));
                context.DrawLine(pen, new Point(9, 4.5 - hover), new Point(9, 6.5)); context.DrawLine(pen, new Point(3.8, 7.6), new Point(5.2, 8.9));
                context.DrawLine(pen, new Point(14.2, 7.6), new Point(12.8, 8.9));
                break;
            case IconKind.Close:
                context.DrawLine(pen, new Point(4.5, 4.5), new Point(13.5, 13.5)); context.DrawLine(pen, new Point(13.5, 4.5), new Point(4.5, 13.5));
                break;
            case IconKind.Edit:
                // A pencil.
                context.DrawGeometry(null, pen, Geometry.Parse("M 3,15 L 3.8,11.6 L 12.6,2.8 Q 13.4,2 14.2,2.8 L 15.2,3.8 Q 16,4.6 15.2,5.4 L 6.4,14.2 Z M 11,4.4 L 13.6,7"));
                break;
            case IconKind.Today:
                // A calendar page with two rings, and a dot on the day.
                context.DrawRectangle(null, pen, new Rect(2.5, 3.5, 13, 12), 2.5, 2.5);
                context.DrawLine(pen, new Point(2.5, 7.5), new Point(15.5, 7.5));
                context.DrawLine(pen, new Point(6, 2), new Point(6, 5)); context.DrawLine(pen, new Point(12, 2), new Point(12, 5));
                context.DrawEllipse(brush, null, new Point(9, 11.5), 1.4, 1.4);
                break;
            case IconKind.Table:
                // A grid of cells: hours by account.
                context.DrawRectangle(null, pen, new Rect(2.5, 3, 13, 12), 2.5, 2.5);
                context.DrawLine(pen, new Point(2.5, 7), new Point(15.5, 7)); context.DrawLine(pen, new Point(2.5, 11), new Point(15.5, 11));
                context.DrawLine(pen, new Point(8, 7), new Point(8, 15));
                break;
            case IconKind.Entries:
                // Three lines each led by a dot: the entries as a plain list.
                foreach (var y in new[] { 4.5, 9.0, 13.5 })
                {
                    context.DrawEllipse(brush, null, new Point(3.8, y), 1.3, 1.3);
                    context.DrawLine(pen, new Point(7.5, y), new Point(15.5, y));
                }
                break;
            case IconKind.Grip:
                // Two columns of three dots, the usual sign that something can be picked up and moved, drawn quietly.
                IBrush quiet = this.TryFindResource("Muted", ActualThemeVariant, out var muted) && muted is IBrush mutedBrush ? mutedBrush : brush;
                foreach (var x in new[] { 3.0, 7.0 }) foreach (var y in new[] { 4.5, 9.0, 13.5 }) context.DrawEllipse(quiet, null, new Point(x, y), 1.4, 1.4);
                break;
            case IconKind.Gear:
            {
                // One continuous outline, with the same rounded stroke as the rest of the set.
                var cog = new StreamGeometry();
                using (var g = cog.Open())
                {
                    for (var i = 0; i < 32; i++)
                    {
                        var theta = i * Math.PI / 16; var radius = i % 4 is 1 or 2 ? 7.3 : 5.7;
                        var point = new Point(9 + radius * Math.Cos(theta), 9 + radius * Math.Sin(theta));
                        if (i == 0) g.BeginFigure(point, false); else g.LineTo(point);
                    }
                    g.EndFigure(true);
                }
                context.DrawGeometry(null, pen, cog);
                context.DrawEllipse(null, pen, new Point(9, 9), 2.2, 2.2);
                break;
            }
            case IconKind.Minimise:
            {
                // A miniature timer pill: its dot and timer are visible before hovering. Under the pointer it behaves like the real pill coming forward:
                // the outline fills with a soft tint, it widens a little about its middle, and the dot turns to the colour of how the day is going.
                var width = 14 + hover * 3;
                var body = new Rect(9 - width / 2, 4.5, width, 9);
                IBrush? tint = hover > 0 && brush is ISolidColorBrush ink ? new SolidColorBrush(ink.Color, .16 * hover) : null;
                context.DrawRectangle(tint, pen, body, 4.5, 4.5);
                var dotCentre = new Point(body.Left + 3.6, 9);
                context.DrawEllipse(brush, null, dotCentre, .9, .9);
                if (hover > 0.02 && this.TryFindResource(DotKey ?? "Accent", ActualThemeVariant, out var statusFound) && statusFound is IBrush status)
                    using (context.PushOpacity(hover)) context.DrawEllipse(status, null, dotCentre, 1.5, 1.5);
                context.DrawLine(pen, new Point(body.Left + 6.5, 8), new Point(body.Right - 3.1, 8));
                context.DrawLine(new Pen(brush, 1, lineCap: PenLineCap.Round), new Point(body.Left + 6.5, 10.5), new Point(body.Left + 9.5, 10.5));
                break;
            }
            case IconKind.Previous:
            case IconKind.Next:
            {
                var direction = Kind == IconKind.Previous ? -1 : 1;
                var x = 9 + direction * hover;
                context.DrawLine(pen, new Point(x - direction * 2.5, 4), new Point(x + direction * 2.5, 9));
                context.DrawLine(pen, new Point(x + direction * 2.5, 9), new Point(x - direction * 2.5, 14));
                break;
            }
            case IconKind.Flag:
            {
                // A pole with a pennant in the button's own text colour. The cloth ripples while Waving is on, fading back to still.
                IBrush flagBrush = TintKey is not null && this.TryFindResource(TintKey, ActualThemeVariant, out var tint) && tint is IBrush tinted ? tinted
                    : Avalonia.Controls.Documents.TextElement.GetForeground(this) ?? brush;
                var flagPen = new Pen(flagBrush, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                context.DrawLine(flagPen, new Point(4, 2.5), new Point(4, 16));
                double Wave(double x) => amplitude * 1.4 * Math.Sin(phase - (x - 4) * 0.7) * ((x - 4) / 10.5);
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(4, 3.5), false);
                    for (var x = 5.5; x <= 14.5; x += 1.5) g.LineTo(new Point(x, 3.5 + Wave(x)));
                    g.LineTo(new Point(12, 7 + Wave(12)));
                    g.LineTo(new Point(14.5, 10.5 + Wave(14.5)));
                    for (var x = 13.0; x >= 4; x -= 1.5) g.LineTo(new Point(x, 10.5 + Wave(x)));
                    g.LineTo(new Point(4, 10.5)); g.EndFigure(false);
                }
                context.DrawGeometry(null, flagPen, geometry);
                break;
            }
            case IconKind.Copy:
                // Two sheets, one behind the other: the usual sign for copy.
                context.DrawRectangle(null, pen, new Rect(6.5, 2.5, 8.5, 10.5), 2, 2);
                context.DrawGeometry(null, pen, Geometry.Parse("M 11.5,15.5 L 4.5,15.5 Q 3,15.5 3,14 L 3,6.5"));
                break;
            case IconKind.Export:
                // An arrow dropping into a tray: save this week out as files.
                context.DrawLine(pen, new Point(9, 2.5), new Point(9, 11)); context.DrawLine(pen, new Point(5.8, 8), new Point(9, 11.2)); context.DrawLine(pen, new Point(12.2, 8), new Point(9, 11.2));
                context.DrawLine(pen, new Point(3, 12), new Point(3, 15.5)); context.DrawLine(pen, new Point(3, 15.5), new Point(15, 15.5)); context.DrawLine(pen, new Point(15, 15.5), new Point(15, 12));
                break;
        }
    }
}
