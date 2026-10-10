using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Timer.Core;

namespace Timer.Desktop;

public static class Ui
{
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
    public static void DragHeader(Control header, Window window)
    {
        if (header is Panel panel) panel.Background = Brushes.Transparent;
        header.Name = "DragHeader";
        header.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (IsDragSurface(e.Source) && e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
            { window.BeginMoveDrag(e); e.Handled = true; }
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
            press = e; origin = e.GetPosition(header);
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
            window.BeginMoveDrag(start);
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
        var variants = new List<MenuItem>();
        foreach (var theme in new[] { "System", "Light", "Dark" })
        {
            var item = new MenuItem { Header = theme, IsChecked = model.Theme == theme,
                ToggleType = MenuItemToggleType.Radio };
            item.Click += (_, _) => model.SetTheme(theme);
            variants.Add(item);
        }
        var format = new MenuItem { Header = "Decimal hours", IsChecked = model.DecimalHours, ToggleType = MenuItemToggleType.CheckBox };
        format.Click += (_, _) => model.ToggleFormat();
        var shifts = new MenuItem { Header = "Working hours…" };
        shifts.Click += (_, _) => { if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            new ShiftWindow(model).Show(life.Windows.First(w => w.IsVisible)); };
        var accounts = new MenuItem { Header = "Accounts and budgets…" };
        accounts.Click += (_, _) => { if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            new AccountsWindow(model).Show(life.Windows.First(w => w.IsVisible)); };
        // The demo and its overtime switch sit together: outside the demo you can open it, inside it you can switch overtime on and off.
        var demo = new MenuItem { Header = "Open sample-data demo", IsVisible = !model.IsDemo };
        var overtimeTest = new MenuItem { Header = "Test overtime", ToggleType = MenuItemToggleType.CheckBox, IsVisible = model.IsDemo, Name = "TestOvertime" };
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
        var shadows = new MenuItem { Header = "Window shadows", ToggleType = MenuItemToggleType.CheckBox };
        shadows.Click += (_, _) => model.SetShadows(!model.Shadows);
        var quit = new MenuItem { Header = "Close timer" };
        quit.Click += (_, _) => model.RequestQuit();
        // Most used first: the hours format, then accounts and working hours; then how it looks and starts; then the demo; and
        // Close timer alone at the bottom, apart from everyday actions. Breaks and the day button live in the window, so they are not repeated here.
        var menu = new ContextMenu { ItemsSource = new Control[]
        { format, accounts, shifts, new Separator(), new MenuItem { Header = "Appearance", ItemsSource = variants }, shadows, startup, new Separator(), demo, overtimeTest, new Separator(), quit } };
        menu.Opened += (_, _) =>
        {
            foreach (var item in variants) item.IsChecked = (string?)item.Header == model.Theme;
            format.IsChecked = model.DecimalHours; shadows.IsChecked = model.Shadows; overtimeTest.IsChecked = model.OvertimeTest;
            startup.IsVisible = model.AutoStart?.Available == true;
            startup.IsChecked = model.AutoStart?.IsEnabled == true;
        };
        return menu;
    }
    public static Button SettingsButton(TimerModel model)
    {
        var menu = SettingsMenu(model);
        var button = new Button { Content = "Settings", Name = "Settings", ContextMenu = menu };
        button.Click += (_, _) => menu.Open(button);
        return button;
    }
}

// Vector geometry keeps the budget ring and cutlery crisp at any display scale.
public sealed class AccountIndicator : Control
{
    public TimerAccount Account { get; }
    private readonly TimerModel model;
    public AccountIndicator(TimerAccount account, TimerModel model)
    {
        Account = account;
        this.model = model;
        Width = Height = 42;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
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
        if (model.ActiveAccount == Account.Name)
        {
            context.DrawRectangle(ink, null, new Rect(15, 14, 4, 14), 1, 1);
            context.DrawRectangle(ink, null, new Rect(23, 14, 4, 14), 1, 1);
        }
        else if (Account.Name == "Lunch")
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
        else context.DrawGeometry(ink, null, Geometry.Parse("M 17,13 L 28,21 L 17,29 Z"));
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
            var grid = Ui.Grid("44,*,Auto");
            var icon = Ui.Put(grid, new AccountIndicator(account, model));
            var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0) };
            var dot = Ui.Text("●", 10, "live"); dot.IsVisible = false;
            labels.Children.Add(Ui.Horizontal(Ui.Text(account.Display, 15), dot));
            var budget = Ui.Text(model.BudgetSummary(account), 12, "muted");
            budget.TextTrimming = TextTrimming.CharacterEllipsis;
            labels.Children.Add(budget);
            Ui.Put(grid, labels, 1);
            var today = Ui.Put(grid, Ui.Text(model.Format(model.Today(account))), 2);
            row.Content = grid;
            row.Click += (_, _) => model.ToggleAccount(account.Name);
            if (reorderable && account.Name != "Lunch") AttachReorder(row, account.Name);
            // The everyday thing to do with an account comes first, then the less frequent one.
            var adjust = new MenuItem { Header = "Adjust time today…", Name = "AdjustTimeMenu" };
            var accountName = account.Name;
            adjust.Click += (_, _) => { if (TopLevel.GetTopLevel(row) is Window owner) new AdjustWindow(model, accountName).Show(owner); };
            var settings = new MenuItem { Header = "Name and budget…", Name = "AccountSettingsMenu" };
            settings.Click += (_, _) => { if (TopLevel.GetTopLevel(row) is Window owner) new AccountsWindow(model).Show(owner); };
            row.ContextMenu = new ContextMenu { ItemsSource = new Control[] { adjust, new Separator(), settings } };
            Children.Add(row);
            rows.Add((account, row, today, budget, icon, dot));
        }
        Refresh();
    }
    // Dragging a row up or down reorders the accounts. A press that never moves still starts or stops tracking as usual.
    // Lunch stays at the bottom, and the rows it passes slide aside to show where the account will land.
    private void AttachReorder(Button row, string name)
    {
        Point? press = null; var dragging = false; var target = 0;
        int Index() => rows.FindIndex(r => r.Account.Name == name);
        void Finish(bool commit)
        {
            var from = Index(); var to = target;
            press = null; dragging = false;
            foreach (var r in rows) { r.Row.RenderTransform = null; r.Row.Opacity = 1; r.Row.ZIndex = 0; }
            if (commit && from >= 0 && to != from) model.MoveAccount(name, to);
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
            }
            var from = Index(); var height = row.Bounds.Height;
            if (from < 0 || height <= 0) return;
            var movable = rows.Count(r => r.Account.Name != "Lunch");
            target = Math.Clamp((int)Math.Round(from + dy / height), 0, Math.Max(0, movable - 1));
            for (var j = 0; j < rows.Count; j++)
            {
                if (j == from) continue;
                var shift = from < target && j > from && j <= target ? -height : target < from && j >= target && j < from ? height : 0;
                rows[j].Row.RenderTransform = shift == 0 ? null : new TranslateTransform(0, shift);
            }
            row.RenderTransform = new TranslateTransform(0, Math.Clamp(dy, -from * height, (movable - 1 - from) * height));
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
            ToolTip.SetTip(row.Row, model.BudgetText(row.Account));
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
            status.Classes.Set("live", model.IsTracking);
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
    public bool Playing { get; set; }
    public PauseGlyph() { Width = Height = 14; }
    public override void Render(DrawingContext context)
    {
        var brush = this.TryFindResource("Ink", ActualThemeVariant, out var value) ? value as IBrush : Brushes.Gray;
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
