using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
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
    public static Border Shadowed(Border card, double radius)
    {
        card.CornerRadius = new CornerRadius(radius);
        var host = new Border { Margin = new Thickness(ShadowMargin), CornerRadius = new CornerRadius(radius), Child = card, Name = "ShadowHost" };
        // The shadow and the glow fade into each other, so pressing play or pause does not make the edge jump.
        host.Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.BoxShadowsTransition { Property = Border.BoxShadowProperty, Duration = TimeSpan.FromMilliseconds(350) } };
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
            host.BoxShadow = new BoxShadows(new BoxShadow { Blur = 26, Spread = 2, Color = Color.FromArgb(150, colour.R, colour.G, colour.B) });
        }
        else if (shadows)
            host.BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 22, Spread = 0, Color = ThemeColour(host, "ShadowColor", Color.FromArgb(90, 0, 0, 0)) });
        else host.BoxShadow = default;
    }
    // An animation that has stopped (its window was hidden or the control left the tree, which ends a repeating one) must not still count as running,
    // or asking for it again does nothing and the dot stays still for good. So the task is kept, and a finished one is started afresh.
    private sealed record PulseRun(CancellationTokenSource Source, RunningAnimation Run);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, PulseRun> pulses = new();
    /// <summary>Fades a control in and out gently while <paramref name="on"/> is true, as the live dot does while an account is recording.</summary>
    public static void Pulse(Control control, bool on)
    {
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
    private static readonly string[] MainActions = { "Save", "Export CSV files", "Apply selected fills", "Group selected", "Move later blocks" };
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
    public static bool IsDragSurface(object? source) => source is Visual visual
        && visual is not Avalonia.Controls.Button && !visual.GetVisualAncestors().OfType<Button>().Any();
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
            if (IsDragSurface(e.Source) && e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
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
    /// The right-click menu is kept short on purpose: the hours format, which build this is, the overtime tester (demo only) and Close timer.
    /// Everything else lives on the settings page.
    /// </summary>
    public static ContextMenu SettingsMenu(TimerModel model, bool pill = false)
    {
        // Inside the demo you can switch overtime on and off to see how it looks. Like the version, it shows only once developer features are unlocked.
        var overtimeTest = new MenuItem { Header = "Test overtime", ToggleType = MenuItemToggleType.CheckBox, IsVisible = false, Name = "TestOvertime" };
        overtimeTest.Click += (_, _) => model.SetOvertimeTest(!model.OvertimeTest);
        // Which build this is, greyed out because it is information, not an action.
        var version = new MenuItem { Header = BuildInfo.Current, IsEnabled = false, IsVisible = false, Name = "BuildVersion" };
        // Shown for a few minutes after going from one account to another and back, to pick the earlier block up again.
        var continueItem = new MenuItem { Name = "ContinuePreviousMenu", Header = "Continue earlier block", IsVisible = false };
        continueItem.Click += (_, _) => model.ContinuePrevious();
        // Tucks the timer away without stopping it. Closing it for good is the cross at the foot of the dashboard's rail.
        var collapse = new MenuItem { Header = "Collapse to tray", Name = "CollapseToTray" };
        collapse.Click += (_, _) => model.RequestHide();
        // Quick actions: the things done most often, without opening the dashboard.
        var openItem = new MenuItem { Name = "OpenDeskMenu" }; openItem.Click += (_, _) => model.RequestDashboard();
        var switchItem = new MenuItem { Name = "QuickSwitchMenu", Header = "Quick switch account" };
        var breakItem = new MenuItem { Name = "BreakMenu" }; breakItem.Click += (_, _) => model.ToggleBreak();
        var noteItem = new MenuItem { Name = "AddNoteMenu", Header = "Add note…" }; noteItem.Click += (_, _) => model.RequestNote();
        var settingsItem = new MenuItem { Name = "SettingsMenu", Header = "Settings…" }; settingsItem.Click += (_, _) => model.RequestSettings();
        var pinItem = new MenuItem { Name = "PinMenu", Header = "Pin to screen", ToggleType = MenuItemToggleType.CheckBox };
        pinItem.Click += (_, _) => model.SetPillOnTop(!model.PillOnTop);
        var quickRule = new Separator();
        var versionRule = new Separator { IsVisible = false };
        var collapseRule = new Separator { IsVisible = false };
        var menu = new ContextMenu { ItemsSource = new Control[] { continueItem, overtimeTest, versionRule, version, collapseRule, collapse } };
        // Quick actions live on the pill's menu, where there is no other way to do them. The dashboard has its own buttons for all of these.
        if (pill) menu.ItemsSource = new Control[] { openItem, switchItem, breakItem, noteItem, quickRule, pinItem, settingsItem, continueItem, overtimeTest, versionRule, version, collapseRule, collapse };
        void Sync()
        {
            openItem.Header = "Open Desk";
            breakItem.Header = model.IsOnBreak ? "End break" : "Start break";
            pinItem.IsChecked = model.PillOnTop;
            // One entry per account, the running one ticked. Choosing another stops this one and starts that one.
            switchItem.ItemsSource = model.Accounts.Where(a => !BreakAccount.Is(a.Name)).Select(a =>
            {
                var item = new MenuItem { Header = a.Name, ToggleType = MenuItemToggleType.CheckBox, IsChecked = model.ActiveAccount == a.Name };
                item.Click += (_, _) => { if (model.ActiveAccount != a.Name) model.ToggleAccount(a.Name); };
                return (Control)item;
            }).ToList();
            var developer = model.DeveloperUnlocked;
            overtimeTest.IsChecked = model.OvertimeTest;
            overtimeTest.IsVisible = developer && model.IsDemo; version.IsVisible = developer;
            var offer = model.ContinuePreviousText; continueItem.IsVisible = offer is not null; if (offer is not null) continueItem.Header = offer;
            // A divider only goes between two things that are showing, so it never sits at the top of the menu or above the only item.
            versionRule.IsVisible = (continueItem.IsVisible || overtimeTest.IsVisible) && version.IsVisible;
            collapseRule.IsVisible = pill || continueItem.IsVisible || overtimeTest.IsVisible || version.IsVisible;
        }
        menu.Opened += (_, _) => Sync(); Sync();
        return menu;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, object> ownWheel = new();
    /// <summary>Whether this scroll viewer keeps the mouse wheel to itself (see <see cref="OwnWheel"/>).</summary>
    public static bool OwnsWheel(ScrollViewer scroll) => ownWheel.TryGetValue(scroll, out _);
    /// <summary>
    /// Makes every scroll viewer in a widget (and the widget itself if it is one) keep the mouse wheel while the pointer is over it and it has
    /// something to scroll. Left alone, a list that has reached its end hands the wheel on to the desk around it, so the whole desk jumps. One with
    /// nothing to scroll still lets the wheel through, and holding Ctrl is left to whatever uses it (the timeline's zoom).
    /// </summary>
    public static void OwnWheel(Control root)
    {
        var all = root.GetLogicalDescendants().OfType<ScrollViewer>().ToList(); if (root is ScrollViewer self) all.Add(self);
        foreach (var scroll in all)
        {
            if (ownWheel.TryGetValue(scroll, out _)) continue;
            ownWheel.Add(scroll, new object());
            scroll.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
            {
                if (!e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control) && scroll.Extent.Height > scroll.Viewport.Height + 1) e.Handled = true;
            }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        }
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
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var scaled = context.PushTransform(Matrix.CreateScale(Size / 42, Size / 42));
        var centre = new Point(21, 21);
        var ink = Resource("Ink");
        var running = model.ActiveAccount == Account.Name;
        if (!Ring) context.DrawEllipse(running ? Resource("Accent") : null, running ? null : new Pen(Resource("Line"), 2), centre, 19, 19);
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
        if (glow > 0 && !BreakAccount.Is(Account.Name))
        {
            using (context.PushOpacity(glow)) context.DrawEllipse(accent, null, centre, Ring ? 15 : 19, Ring ? 15 : 19);
        }
        // On the plain circle a running account is already filled with the accent, so its pause bars are drawn in the accent's own text colour.
        if (!Ring && running) ink = accentInk;
        using var grow = context.PushTransform(Matrix.CreateTranslation(-21, -21) * Matrix.CreateScale(1 + .08 * glow, 1 + .08 * glow) * Matrix.CreateTranslation(21, 21));
        if (model.ActiveAccount == Account.Name)
        {
            context.DrawRectangle(ink, null, new Rect(15, 14, 4, 14), 1, 1);
            context.DrawRectangle(ink, null, new Rect(23, 14, 4, 14), 1, 1);
            if (glow > 0)
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
            if (glow > 0) using (context.PushOpacity(glow)) context.DrawGeometry(accentInk, null, triangle);
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
        text.Width = full; Scrolling = true; hold = .35;
        var distance = full - room + 4; var last = DateTime.UtcNow;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow; var seconds = Math.Min(.1, (now - last).TotalSeconds); last = now;
            if (hold > 0) { hold -= seconds; return; }
            offset = Math.Min(distance, offset + 55 * seconds);
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
    private readonly bool reorderable, bars;
    /// <param name="bars">Shows each account's budget as a bar with a pace tick and a plain play circle, as the dashboard does.</param>
    public AccountList(TimerModel model, bool reorderable = false, bool bars = false)
    {
        this.model = model; this.reorderable = reorderable; this.bars = bars;
        Rebuild();
        AttachedToVisualTree += (_, _) => model.PropertyChanged += Changed;
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
            // Where rows can be dragged to reorder them, a grip at the left says so.
            var shift = reorderable ? 1 : 0;
            var grid = Ui.Grid(reorderable ? "10,*,Auto" : bars ? "*,Auto" : "38,*,Auto");
            if (reorderable)
            {
                var grip = new IconGlyph(IconKind.Grip) { Cursor = new Cursor(StandardCursorType.SizeAll), VerticalAlignment = VerticalAlignment.Center, Name = "Grip" + rows.Count };
                ToolTip.SetTip(grip, "Drag to change the order"); Ui.Put(grid, grip);
            }
            var icon = new AccountIndicator(account, model) { Ring = !bars };
            var dot = Ui.Text("●", 10, "live"); dot.IsVisible = false;
            var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(bars ? 8 : 10, 0, 10, 0) };
            var budget = Ui.Text(bars ? model.BudgetLine(account) : model.BudgetSummary(account), 12, "muted");
            budget.TextTrimming = TextTrimming.CharacterEllipsis;
            MarqueeText? marquee = null;
            BudgetBar? bar = null; var percent = Ui.Text("", 12, "muted");
            TextBlock today;
            if (bars)
            {
                // The account's own colour leads the name, with today's time at the end of that line. Under it the budget used and the period it covers, then
                // the bar. The play circle takes the right-hand side, and the percentage is left to the hover detail.
                var swatch = new Avalonia.Controls.Shapes.Ellipse { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
                    Fill = Avalonia.Media.Brush.Parse(Ui.Visible(account.Colour)) };
                labels.Spacing = 3;
                // The percentage sits at the end of the name line, which leaves the whole line under it for the budget in words.
                var nameLine = Ui.Grid("*,Auto"); var nameStack = new NameRow { Spacing = 0 }; // The live dot follows the name closely instead of sitting out by today's time.
                marquee = new MarqueeText(account.Display, 15) { Name = "AccountName" }; var nameText = marquee; dot.Margin = new Thickness(8, 0, 0, 0);
                nameStack.Children.Add(swatch); nameStack.Children.Add(nameText); nameStack.Children.Add(dot);
                today = Ui.Text(model.Format(model.Today(account)), 15); today.Classes.Add("figures"); today.FontWeight = Avalonia.Media.FontWeight.SemiBold; today.VerticalAlignment = VerticalAlignment.Center; today.TextAlignment = Avalonia.Media.TextAlignment.Right;
                Ui.Put(nameLine, nameStack); Ui.Put(nameLine, today, 1);
                // The budget used is in the text colour (red once spent) and the period it covers is quiet and lines up with today's time above it.
                budget.Classes.Remove("muted"); budget.FontSize = 13;
                var period = Ui.Text("", 12, "muted"); period.TextAlignment = Avalonia.Media.TextAlignment.Right; period.VerticalAlignment = VerticalAlignment.Center;
                var budgetLine = Ui.Grid("*,Auto"); Ui.Put(budgetLine, budget); Ui.Put(budgetLine, period, 1); period.Name = "BudgetPeriod";
                percent = period; // The row's second quiet text, kept under the old name in the row list.
                labels.Children.Add(nameLine); labels.Children.Add(budgetLine);
                if (account.Budget is not null) { bar = new BudgetBar(); labels.Children.Add(bar); }
                icon.Margin = new Thickness(8, 0, 0, 0);
                Ui.Put(grid, labels, shift); Ui.Put(grid, icon, 1 + shift);
                // A rounded card inset from the edges, so the recording account's soft highlight has room to show.
                row.Classes.Add("accountCard"); row.Margin = new Thickness(10, 0); row.Height = 70;
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
    { new Avalonia.Animation.TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(milliseconds), Easing = new Avalonia.Animation.Easings.CubicEaseOut() } };
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
            if (bars) row.Period.Text = model.BudgetPeriodLabel(row.Account);
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
    private readonly TextBlock overtime = Ui.Text("Overtime", 12, "overtime");
    private readonly Avalonia.Controls.Shapes.Ellipse dot = new() { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
    public TrackingLabels(TimerModel model)
    {
        Orientation = Orientation.Vertical;
        Spacing = 3;
        // The status dot is a real shape in a box as wide as the account's colour dot above it, so the two dots share one centre line.
        var dotBox = new Border { Width = 10, Child = dot, VerticalAlignment = VerticalAlignment.Center };
        var line = Ui.Horizontal(dotBox, status); line.Spacing = 8;
        Children.Add(line);
        Children.Add(overtime);
        overtime.Margin = new Thickness(18, 0, 0, 0); // Lined up under the status words.
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
            overtime.IsVisible = model.IsOvertime;
            overtime.Text = "Overtime +" + model.Format(model.Engine.Totals(model.Engine.Now).Overtime);
        }
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Refresh(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
    }
}

/// <summary>A clock whose hours and minutes are heavy and whose seconds are lighter and dimmer, so the figure that matters stands out.</summary>
public sealed class ClockText : StackPanel
{
    private readonly TextBlock main, seconds;
    public ClockText(double size, double secondsSize)
    {
        Orientation = Orientation.Horizontal;
        main = new TextBlock { FontSize = size, Classes = { "clock" }, VerticalAlignment = VerticalAlignment.Bottom };
        seconds = new TextBlock { FontSize = secondsSize, Classes = { "clockLight", "muted" }, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 0, size * .12) };
        Children.Add(main); Children.Add(seconds);
    }
    /// <summary>Colours the hours and minutes by a style class (break, overtime, live or clockIdle). The seconds stay dim.</summary>
    public void SetState(bool onBreak, bool late, bool live)
    {
        main.Classes.Set("break", onBreak); main.Classes.Set("overtime", late); main.Classes.Set("live", live);
        main.Classes.Set("clockIdle", !onBreak && !late && !live);
        // The seconds take the same colour as the rest of the clock when it is tracking, on a break or in overtime, only lighter.
        seconds.Classes.Set("break", onBreak); seconds.Classes.Set("overtime", late); seconds.Classes.Set("live", live); seconds.Opacity = onBreak || late || live ? .78 : 1; // Quieter than the main digits, but still easy to read.
    }
    /// <summary>The whole clock, as hours:minutes:seconds. Setting it splits the seconds off to be drawn lighter.</summary>
    public string Text
    {
        get => main.Text + seconds.Text;
        set { var cut = value.LastIndexOf(':'); main.Text = cut > 0 ? value[..cut] : value; seconds.Text = cut > 0 ? value[cut..] : ""; }
    }
}

public sealed class Chevron : Control
{
    public bool Expanded { get; set; }
    public Chevron() { Width = 14; Height = 14; }
    // Drawn in the theme's text colour, which changes when the pill turns to a recording, break or overtime look, so it must be drawn again then.
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
    public PauseGlyph()
    {
        Width = Height = 14;
        Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = GlowProperty, Duration = IconGlyph.HoverDuration } };
    }
    // Drawn in the theme's text colour, which changes when the pill turns to a recording, break or overtime look, so it must be drawn again then.
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
        var brush = glow > .5 ? tint : normal;
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
        Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = FlowProperty, Duration = SteamEase, Easing = new Avalonia.Animation.Easings.CubicEaseInOut() } };
    }
    public void SetSteaming(bool on)
    {
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
    // Drawn in the theme's text colour, which changes when the pill turns to a recording, break or overtime look, so it must be drawn again then.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(ActualThemeVariant)) InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        IBrush? brush = null;
        var key = Mono ? (Hot ? HotKey : "Ink") : "Break";
        if (Filled) brush = Brushes.White; else if (this.TryFindResource(key, ActualThemeVariant, out var value)) brush = value as IBrush;
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

public enum IconKind { ZoomIn, ZoomOut, FitDay, Grip, Gear, Minimise, Export, Copy, Flag, Timeline, Week, Table, Entries, Plus, Undo, Merge, Trash, Today, Close, Edit, Previous, Next, Desk, Clock, Check, Bars, Note }

// Small line icons for buttons that have no words. They are drawn in the ordinary text colour so every theme suits them.
public sealed class IconGlyph : Control
{
    public const double StrokeWidth = 1.6;
    public static readonly TimeSpan HoverDuration = TimeSpan.FromMilliseconds(200);
    public static readonly StyledProperty<double> HoverProperty = AvaloniaProperty.Register<IconGlyph, double>(nameof(Hover));
    static IconGlyph() => AffectsRender<IconGlyph>(HoverProperty);
    public double Hover { get => GetValue(HoverProperty); set => SetValue(HoverProperty, value); }
    public IconKind Kind { get; }
    public IconGlyph(IconKind kind)
    {
        Kind = kind; Width = Height = 18;
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
        Waving = false;
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
    private void UpdateHover() => Hover = owner is { IsEffectivelyEnabled: true } && (owner.IsPointerOver || keyboardFocus) ? 1 : 0;
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
    public double WaveAmount => amplitude;
    public bool Waving
    {
        get => waving;
        set
        {
            waving = value;
            if (waveTimer is not null || Kind != IconKind.Flag) return;
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
    public override void Render(DrawingContext context)
    {
        IBrush brush = this.TryFindResource(TintKey ?? "Ink", ActualThemeVariant, out var value) && value is IBrush found ? found : Brushes.Gray;
        var pen = new Pen(brush, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var hover = Math.Clamp(Hover, 0, 1);
        // Every icon keeps its hit target and layout still. Only a small part of the drawing moves.
        var angle = Kind == IconKind.Gear ? hover * Math.PI / 9 : Kind == IconKind.Undo ? -hover * Math.PI / 15 : 0;
        using var turn = context.PushTransform(Matrix.CreateTranslation(-9, -9) * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(9, 9));
        switch (Kind)
        {
            case IconKind.Timeline:
                // A spine with three blocks of different lengths beside it, drawn apart from each other: the day as a column of time.
                context.DrawLine(pen, new Point(3.5, 2), new Point(3.5, 16));
                // Solid bars rather than outlines, so the gaps between them stay open at button size.
                for (var i = 0; i < 3; i++)
                {
                    var shift = hover * (i == 1 ? -1 : 1);
                    context.DrawLine(pen, new Point(7.5 + shift, 3 + i * 6), new Point(14.5 + shift - (i == 1 ? 2 : 0), 3 + i * 6));
                }
                break;
            case IconKind.Trash:
                // A bin: a lid with a handle, and a tapering body with two slats.
                context.DrawLine(pen, new Point(3, 5), new Point(15, 5)); context.DrawLine(pen, new Point(7, 5), new Point(7, 3)); context.DrawLine(pen, new Point(7, 3), new Point(11, 3)); context.DrawLine(pen, new Point(11, 3), new Point(11, 5));
                context.DrawGeometry(null, pen, Geometry.Parse("M 4.5,5 L 5.3,15 Q 5.4,16 6.4,16 L 11.6,16 Q 12.6,16 12.7,15 L 13.5,5"));
                context.DrawLine(pen, new Point(7.6, 8), new Point(7.8, 13)); context.DrawLine(pen, new Point(10.4, 8), new Point(10.2, 13));
                break;
            case IconKind.Plus:
                context.DrawLine(pen, new Point(9, 3.5), new Point(9, 14.5)); context.DrawLine(pen, new Point(3.5, 9), new Point(14.5, 9));
                break;
            case IconKind.Undo:
                // A hook of an arrow curling back to the left.
                context.DrawGeometry(null, pen, Geometry.Parse("M 7,4.5 L 3.5,8 L 7,11.5"));
                context.DrawGeometry(null, pen, Geometry.Parse("M 3.8,8 L 10.5,8 Q 14.5,8 14.5,11.2 Q 14.5,14.2 10.5,14.2 L 8.5,14.2"));
                break;
            case IconKind.Merge:
                // Two lines running together into one.
                context.DrawGeometry(null, pen, Geometry.Parse("M 3.5,3.5 L 3.5,6 Q 3.5,9 9,9 M 14.5,3.5 L 14.5,6 Q 14.5,9 9,9 M 9,9 L 9,14.5"));
                context.DrawGeometry(null, pen, Geometry.Parse("M 6.5,12 L 9,14.5 L 11.5,12"));
                break;
            case IconKind.Week:
                // Three bars of different heights on a baseline: an overview of the week, not a calendar.
                context.DrawLine(pen, new Point(2.5, 15.5), new Point(15.5, 15.5));
                context.DrawLine(pen, new Point(4.5, 13), new Point(4.5, 9 - hover));
                context.DrawLine(pen, new Point(9, 13), new Point(9, 3.5 - hover));
                context.DrawLine(pen, new Point(13.5, 13), new Point(13.5, 6.5 - hover));
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
            case IconKind.Desk:
                // Three tiles of different sizes, as laid out on the desk.
                context.DrawRectangle(null, pen, new Rect(2.5, 3, 7, 7), 2, 2); context.DrawRectangle(null, pen, new Rect(11.5, 3, 4, 4), 1.5, 1.5);
                context.DrawRectangle(null, pen, new Rect(11.5, 9, 4, 6), 1.5, 1.5); context.DrawRectangle(null, pen, new Rect(2.5, 12, 7, 3), 1.5, 1.5);
                break;
            case IconKind.Clock:
                // A round face with its two hands.
                context.DrawEllipse(null, pen, new Point(9, 9), 6.5, 6.5); context.DrawLine(pen, new Point(9, 5.2), new Point(9, 9)); context.DrawLine(pen, new Point(9, 9), new Point(11.8, 10.6));
                break;
            case IconKind.Check:
                // A tick in a round outline: something done.
                context.DrawEllipse(null, pen, new Point(9, 9), 6.5, 6.5); context.DrawGeometry(null, pen, Geometry.Parse("M 6,9.2 L 8.2,11.4 L 12,6.8"));
                break;
            case IconKind.Bars:
                // Three bars of different heights standing on a line: a small chart.
                context.DrawLine(pen, new Point(3.5, 15), new Point(14.5, 15));
                context.DrawLine(pen, new Point(5.5, 15), new Point(5.5, 10)); context.DrawLine(pen, new Point(9, 15), new Point(9, 4)); context.DrawLine(pen, new Point(12.5, 15), new Point(12.5, 7.5));
                break;
            case IconKind.Note:
                // A page with a few lines of writing.
                context.DrawRectangle(null, pen, new Rect(3.5, 3, 11, 12), 2.5, 2.5);
                context.DrawLine(pen, new Point(6.5, 7), new Point(11.5, 7)); context.DrawLine(pen, new Point(6.5, 10), new Point(11.5, 10)); context.DrawLine(pen, new Point(6.5, 13), new Point(9, 13));
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
            case IconKind.ZoomIn or IconKind.ZoomOut:
                context.DrawEllipse(null, pen, new Point(8, 8), 6, 6);
                context.DrawLine(pen, new Point(12.5, 12.5), new Point(16.5, 16.5));
                context.DrawLine(pen, new Point(5.2, 8), new Point(10.8, 8));
                if (Kind == IconKind.ZoomIn) context.DrawLine(pen, new Point(8, 5.2), new Point(8, 10.8));
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
            case IconKind.FitDay:
                // Two arrows pointing away from each other across the corner: the day stretched to fill the space.
                context.DrawLine(pen, new Point(10, 8), new Point(15, 3)); context.DrawGeometry(null, pen, Geometry.Parse("M 10.6,3 L 15,3 L 15,7.4"));
                context.DrawLine(pen, new Point(8, 10), new Point(3, 15)); context.DrawGeometry(null, pen, Geometry.Parse("M 7.4,15 L 3,15 L 3,10.6"));
                break;
        }
    }
}
