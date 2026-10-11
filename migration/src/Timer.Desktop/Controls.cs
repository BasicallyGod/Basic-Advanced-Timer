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
    public const double ShadowMargin = 22;
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
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, CancellationTokenSource> pulses = new();
    /// <summary>Fades a control in and out gently while <paramref name="on"/> is true, as the live dot does while an account is recording.</summary>
    public static void Pulse(Control control, bool on)
    {
        if (pulses.TryGetValue(control, out var running))
        {
            if (on) return;
            running.Cancel(); pulses.Remove(control); control.Opacity = 1; return;
        }
        if (!on) return;
        var source = new CancellationTokenSource(); pulses.Add(control, source);
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
        _ = animation.RunAsync(control, source.Token);
    }
    public static bool IsPulsing(Control control) => pulses.TryGetValue(control, out _);
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
    public static ContextMenu SettingsMenu(TimerModel model)
    {
        // System first, then every theme the app has (Light and Dark lead the built-in ones), then the user's own. The list is rebuilt each time the menu opens.
        List<MenuItem> ThemeItems()
        {
            var items = new List<MenuItem>();
            foreach (var theme in new[] { ThemeLibrary.System }.Concat(model.Themes.Select(t => t.Name)))
            {
                var item = new MenuItem { Header = theme, IsChecked = model.Theme == theme, ToggleType = MenuItemToggleType.Radio };
                item.Click += (_, _) => model.SetTheme(theme);
                items.Add(item);
            }
            return items;
        }
        var variants = ThemeItems();
        var appearanceMenu = new MenuItem { Header = "Appearance", ItemsSource = variants };
        var format = new MenuItem { Header = "Decimal hours", IsChecked = model.DecimalHours, ToggleType = MenuItemToggleType.CheckBox };
        format.Click += (_, _) => model.ToggleFormat();
        var shifts = new MenuItem { Header = "Working hours…" };
        shifts.Click += (_, _) => { if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            new ShiftWindow(model).Show(life.Windows.First(w => w.IsVisible)); };
        var accounts = new MenuItem { Header = "Accounts and budgets…" };
        accounts.Click += (_, _) => { if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            new AccountsWindow(model).Show(life.Windows.First(w => w.IsVisible)); };
        // The demo and its overtime switch sit together: outside the demo you can open it, inside it you can switch overtime on and off.
        var demo = new MenuItem { Header = "Open sample-data demo", IsVisible = !model.IsDemo && BuildInfo.DeveloperFeatures };
        var overtimeTest = new MenuItem { Header = "Test overtime", ToggleType = MenuItemToggleType.CheckBox, IsVisible = model.IsDemo && BuildInfo.DeveloperFeatures, Name = "TestOvertime" };
        overtimeTest.Click += (_, _) => model.SetOvertimeTest(!model.OvertimeTest);
        demo.Click += (_, _) =>
        {
            if (Environment.ProcessPath is { } executable)
            {
                var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = true };
                if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(typeof(App).Assembly.Location);
                start.ArgumentList.Add("--demo");
                System.Diagnostics.Process.Start(start);
            }
        };
        var startup = new MenuItem { Header = "Start with Windows", ToggleType = MenuItemToggleType.CheckBox };
        startup.Click += (_, _) => model.SetAutoStart(model.AutoStart?.IsEnabled != true);
        var themesEditor = new MenuItem { Header = "Themes…", Name = "ThemesMenu" };
        themesEditor.Click += (_, _) => { if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            new ThemesWindow(model).Show(life.Windows.First(w => w.IsVisible)); };
        var taskbar = new MenuItem { Header = "Show in taskbar", ToggleType = MenuItemToggleType.CheckBox, Name = "TaskbarMenu" };
        taskbar.Click += (_, _) => model.SetTaskbarButton(!model.TaskbarButton);
        var shadows = new MenuItem { Header = "Window shadows", ToggleType = MenuItemToggleType.CheckBox };
        shadows.Click += (_, _) => model.SetShadows(!model.Shadows);
        // Which build this is, greyed out because it is information, not an action.
        var version = new MenuItem { Header = BuildInfo.Current, IsEnabled = false, Name = "BuildVersion" };
        var quit = new MenuItem { Header = "Close timer" };
        quit.Click += (_, _) => model.RequestQuit();
        // Most used first: the hours format, then accounts and working hours; then how it looks and starts; then the demo; and
        // Close timer alone at the bottom, apart from everyday actions. Breaks and the day button live in the window, so they are not repeated here.
        var menu = new ContextMenu { ItemsSource = new Control[]
        { format, accounts, shifts, new Separator(), appearanceMenu, themesEditor, shadows, taskbar, startup, new Separator(), demo, overtimeTest, new Separator(), version, new Separator(), quit } };
        menu.Opened += (_, _) =>
        {
            appearanceMenu.ItemsSource = ThemeItems();
            format.IsChecked = model.DecimalHours; shadows.IsChecked = model.Shadows; overtimeTest.IsChecked = model.OvertimeTest;
            taskbar.IsVisible = model.TrayAvailable; taskbar.IsChecked = model.TaskbarButton;
            startup.IsVisible = model.AutoStart?.Available == true;
            startup.IsChecked = model.AutoStart?.IsEnabled == true;
        };
        return menu;
    }
    public static Button SettingsButton(TimerModel model)
    {
        var menu = SettingsMenu(model);
        var button = new Button { Content = new IconGlyph(IconKind.Gear), Name = "Settings", ContextMenu = menu };
        button.Classes.Add("iconButton"); ToolTip.SetTip(button, "Settings");
        Avalonia.Automation.AutomationProperties.SetName(button, "Settings");
        button.Click += (_, _) => menu.Open(button);
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
public sealed class AccountIndicator : Control
{
    // 0 at rest, 1 while the pointer is over the row. It eases in and out, so the button visibly answers the pointer and reads as pressable.
    public static readonly StyledProperty<double> GlowProperty = AvaloniaProperty.Register<AccountIndicator, double>(nameof(Glow));
    static AccountIndicator() => AffectsRender<AccountIndicator>(GlowProperty);
    public double Glow { get => GetValue(GlowProperty); set => SetValue(GlowProperty, value); }
    public TimerAccount Account { get; }
    private readonly TimerModel model;
    public AccountIndicator(TimerAccount account, TimerModel model)
    {
        Account = account;
        this.model = model;
        Width = Height = Size;
        Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = GlowProperty, Duration = TimeSpan.FromMilliseconds(160) } };
    }
    // Drawn on a 42 unit canvas and scaled down, so the ring, bars and play triangle stay in proportion at the smaller size.
    public const double Size = 36;
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var scaled = context.PushTransform(Matrix.CreateScale(Size / 42, Size / 42));
        var centre = new Point(21, 21);
        var ink = Resource("Ink");
        if (Account.Budget is { } budget)
        {
            context.DrawEllipse(null, new Pen(Resource("Line"), 3), centre, 19, 19);
            var ratio = Math.Clamp(model.BudgetUsed(Account).TotalSeconds / budget.TotalSeconds, 0, 1);
            var colour = ratio >= 1 ? Resource("Overtime") : ratio >= .8 ? Resource("BudgetWarning") : Resource("Live");
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
            using (context.PushOpacity(glow)) context.DrawEllipse(accent, null, centre, 15, 15);
        }
        using var grow = context.PushTransform(Matrix.CreateTranslation(-21, -21) * Matrix.CreateScale(1 + .18 * glow, 1 + .18 * glow) * Matrix.CreateTranslation(21, 21));
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

public sealed class AccountList : StackPanel
{
    private readonly TimerModel model;
    private readonly List<(TimerAccount Account, Button Row, TextBlock Today, TextBlock Budget, AccountIndicator Icon, TextBlock Dot)> rows = new();
    private readonly bool reorderable;
    public AccountList(TimerModel model, bool reorderable = false)
    {
        this.model = model; this.reorderable = reorderable;
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
            var grid = Ui.Grid(reorderable ? "14,38,*,Auto" : "38,*,Auto");
            if (reorderable)
            {
                var grip = new IconGlyph(IconKind.Grip) { Cursor = new Cursor(StandardCursorType.SizeAll), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Name = "Grip" + rows.Count };
                ToolTip.SetTip(grip, "Drag to change the order"); Ui.Put(grid, grip);
            }
            var icon = Ui.Put(grid, new AccountIndicator(account, model), shift);
            var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0) };
            var dot = Ui.Text("●", 10, "live"); dot.IsVisible = false;
            labels.Children.Add(Ui.Horizontal(Ui.Text(account.Display, 15), dot));
            var budget = Ui.Text(model.BudgetSummary(account), 12, "muted");
            budget.TextTrimming = TextTrimming.CharacterEllipsis;
            labels.Children.Add(budget);
            Ui.Put(grid, labels, 1 + shift);
            var today = Ui.Put(grid, Ui.Text(model.Format(model.Today(account))), 2 + shift);
            row.Content = grid;
            row.Click += (_, _) => model.ToggleAccount(account.Name);
            row.PointerEntered += (_, _) => icon.Glow = 1;
            row.PointerExited += (_, _) => icon.Glow = 0;
            if (reorderable && !BreakAccount.Is(account.Name)) AttachReorder(row, account.Name);
            var settings = new MenuItem { Header = "Name and budget…", Name = "AccountSettingsMenu" };
            settings.Click += (_, _) => { if (TopLevel.GetTopLevel(row) is Window owner) new AccountsWindow(model).Show(owner); };
            row.ContextMenu = new ContextMenu { ItemsSource = new Control[] { settings } };
            Children.Add(row);
            rows.Add((account, row, today, budget, icon, dot));
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
            row.Budget.Text = model.BudgetSummary(row.Account);
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
    public TrackingLabels(TimerModel model)
    {
        Orientation = Orientation.Vertical;
        Spacing = 3;
        Children.Add(status);
        Children.Add(overtime);
        void Refresh()
        {
            status.Text = "● " + model.Status;
            // A break turns the status line, dot included, amber; otherwise it is green while tracking and grey when not.
            status.Classes.Set("break", model.IsOnBreak);
            status.Classes.Set("live", model.IsTracking && !model.IsOnBreak);
            status.Classes.Set("idle", !model.IsTracking);
            overtime.IsVisible = model.IsOvertime;
            overtime.Text = "Overtime +" + model.Format(model.Engine.Totals(model.Engine.Now).Overtime);
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
    public override void Render(DrawingContext context)
    {
        var brush = this.TryFindResource("Ink", ActualThemeVariant, out var value) ? value as IBrush : Brushes.Gray;
        var pen = new Pen(brush, 1.8);
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
        Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = GlowProperty, Duration = TimeSpan.FromMilliseconds(160) } };
    }
    public override void Render(DrawingContext context)
    {
        var glow = Math.Clamp(Glow, 0, 1);
        using var grow = context.PushTransform(Matrix.CreateTranslation(-7, -7) * Matrix.CreateScale(1 + .25 * glow, 1 + .25 * glow) * Matrix.CreateTranslation(7, 7));
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
    static BreakGlyph() => AffectsRender<BreakGlyph>(SteamProperty);
    public double Steam { get => GetValue(SteamProperty); set => SetValue(SteamProperty, value); }
    public bool Filled { get; set; }
    private CancellationTokenSource? steaming;
    public bool Steaming => steaming is not null;
    public BreakGlyph() { Width = Height = 18; }
    public void SetSteaming(bool on)
    {
        if (on == Steaming) return;
        if (!on) { steaming!.Cancel(); steaming = null; Steam = 0; return; }
        steaming = new CancellationTokenSource();
        var animation = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromSeconds(1.8), IterationCount = Avalonia.Animation.IterationCount.Infinite,
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Setter(SteamProperty, 0.0) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Setter(SteamProperty, 1.0) } },
            }
        };
        _ = animation.RunAsync(this, steaming.Token);
    }
    public override void Render(DrawingContext context)
    {
        IBrush? brush = null;
        if (Filled) brush = Brushes.White; else if (this.TryFindResource("Break", ActualThemeVariant, out var value)) brush = value as IBrush;
        var pen = new Pen(brush ?? Brushes.Gray, 1.8, lineCap: PenLineCap.Round);
        context.DrawGeometry(null, pen, Geometry.Parse("M 3,7 L 3,12 Q 3,15.5 6.5,15.5 L 9.5,15.5 Q 13,15.5 13,12 L 13,7 Z"));
        context.DrawGeometry(null, pen, Geometry.Parse("M 13,8 L 14.5,8 Q 16.5,8 16.5,10 Q 16.5,12 14.5,12 L 13,12"));
        // Two wisps, half a cycle apart. Each rises a little and fades in and out; when still they sit where they always did.
        for (var i = 0; i < 2; i++)
        {
            var x = 6.0 + 4 * i; var phase = Steaming ? (Steam + .5 * i) % 1.0 : 0;
            var rise = phase * 3.5; var alpha = Steaming ? Math.Sin(phase * Math.PI) : 1.0;
            using (context.PushOpacity(Math.Clamp(alpha, 0.0, 1.0)))
                context.DrawLine(pen, new Point(x, 4 - rise), new Point(x, 1.5 - rise + (Steaming ? 0 : 0)));
        }
    }
}

public enum IconKind { ZoomIn, ZoomOut, FitDay, Grip, Gear, Minimise, Export, Copy, Flag }

// Small line icons for buttons that have no words. They are drawn in the ordinary text colour so every theme suits them.
public sealed class IconGlyph : Control
{
    public IconKind Kind { get; }
    public IconGlyph(IconKind kind) { Kind = kind; Width = Height = 18; }
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
        IBrush brush = this.TryFindResource("Ink", ActualThemeVariant, out var value) && value is IBrush found ? found : Brushes.Gray;
        var pen = new Pen(brush, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        switch (Kind)
        {
            case IconKind.Grip:
                // Two columns of three dots, the usual sign that something can be picked up and moved, drawn quietly.
                IBrush quiet = this.TryFindResource("Muted", ActualThemeVariant, out var muted) && muted is IBrush mutedBrush ? mutedBrush : brush;
                foreach (var x in new[] { 6.5, 11.5 }) foreach (var y in new[] { 4.5, 9.0, 13.5 }) context.DrawEllipse(quiet, null, new Point(x, y), 1.4, 1.4);
                break;
            case IconKind.ZoomIn or IconKind.ZoomOut:
                context.DrawEllipse(null, pen, new Point(8, 8), 6, 6);
                context.DrawLine(pen, new Point(12.5, 12.5), new Point(16.5, 16.5));
                context.DrawLine(pen, new Point(5.2, 8), new Point(10.8, 8));
                if (Kind == IconKind.ZoomIn) context.DrawLine(pen, new Point(8, 5.2), new Point(8, 10.8));
                break;
            case IconKind.Gear:
            {
                // A classic cog: eight teeth round a ring, with a hole in the middle.
                var tooth = new Pen(brush, 2.6, lineCap: PenLineCap.Flat);
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Math.PI / 4; var cos = Math.Cos(angle); var sin = Math.Sin(angle);
                    context.DrawLine(tooth, new Point(9 + 5.6 * cos, 9 + 5.6 * sin), new Point(9 + 8.2 * cos, 9 + 8.2 * sin));
                }
                context.DrawEllipse(null, new Pen(brush, 2.0), new Point(9, 9), 5.2, 5.2);
                context.DrawEllipse(null, pen, new Point(9, 9), 1.9, 1.9);
                break;
            }
            case IconKind.Minimise:
                // The familiar low bar of a window's minimise button.
                context.DrawLine(new Pen(brush, 1.8, lineCap: PenLineCap.Flat), new Point(3.5, 13), new Point(14.5, 13));
                break;
            case IconKind.Flag:
            {
                // A pole with a pennant in the button's own text colour. The cloth ripples while Waving is on, fading back to still.
                IBrush flagBrush = TintKey is not null && this.TryFindResource(TintKey, ActualThemeVariant, out var tint) && tint is IBrush tinted ? tinted
                    : Avalonia.Controls.Documents.TextElement.GetForeground(this) ?? brush;
                var flagPen = new Pen(flagBrush, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
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
                // Two bars with arrows between them pointing inwards: the day squeezed to fit the space.
                context.DrawLine(pen, new Point(3, 2.5), new Point(15, 2.5)); context.DrawLine(pen, new Point(3, 15.5), new Point(15, 15.5));
                context.DrawLine(pen, new Point(9, 5), new Point(9, 8.2)); context.DrawLine(pen, new Point(7, 6.4), new Point(9, 8.4)); context.DrawLine(pen, new Point(11, 6.4), new Point(9, 8.4));
                context.DrawLine(pen, new Point(9, 13), new Point(9, 9.8)); context.DrawLine(pen, new Point(7, 11.6), new Point(9, 9.6)); context.DrawLine(pen, new Point(11, 11.6), new Point(9, 9.6));
                break;
        }
    }
}
