using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Interactivity;

namespace Timer.Desktop;

public static class Ui
{
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
        var demo = new MenuItem { Header = model.IsDemo ? "Force overtime" : "Open sample-data demo" };
        demo.Click += (_, _) =>
        {
            if (model.IsDemo) model.ForceOvertime();
            else if (Environment.ProcessPath is { } executable)
            {
                var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = true };
                if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(typeof(App).Assembly.Location);
                start.ArgumentList.Add("--demo");
                System.Diagnostics.Process.Start(start);
            }
        };
        var menu = new ContextMenu { ItemsSource = new MenuItem[]
        { new() { Header = "Appearance", ItemsSource = variants }, format, shifts, accounts, demo } };
        menu.Opened += (_, _) =>
        {
            foreach (var item in variants) item.IsChecked = (string?)item.Header == model.Theme;
            format.IsChecked = model.DecimalHours;
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
    private readonly List<(TimerAccount Account, Button Row, TextBlock Today, TextBlock Budget, AccountIndicator Icon)> rows = new();
    public AccountList(TimerModel model)
    {
        this.model = model;
        Rebuild();
        AttachedToVisualTree += (_, _) => model.PropertyChanged += Changed;
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
    }
    private void Rebuild()
    {
        Children.Clear(); rows.Clear();
        foreach (var account in model.Accounts)
        {
            var row = new Button { Name = "Account" + rows.Count, Classes = { "account" } };
            var grid = Ui.Grid("44,*,Auto");
            var icon = Ui.Put(grid, new AccountIndicator(account, model));
            var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0) };
            labels.Children.Add(Ui.Text(account.Name, 15));
            var budget = Ui.Text(model.BudgetSummary(account), 12, "muted");
            budget.TextTrimming = TextTrimming.CharacterEllipsis;
            labels.Children.Add(budget);
            Ui.Put(grid, labels, 1);
            var today = Ui.Put(grid, Ui.Text(model.Format(model.Today(account))), 2);
            row.Content = grid;
            row.Click += (_, _) => model.ToggleAccount(account.Name);
            Children.Add(row);
            rows.Add((account, row, today, budget, icon));
        }
        Refresh();
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!rows.Select(r => r.Account).SequenceEqual(model.Accounts)) Rebuild(); else Refresh();
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
