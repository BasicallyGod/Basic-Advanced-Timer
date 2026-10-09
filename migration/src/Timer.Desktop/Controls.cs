using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

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
    public static ComboBox Themes(PreviewModel model)
    {
        var c = new ComboBox { ItemsSource = new[] { "System", "Light", "Dark" }, SelectedItem = model.Theme, Width = 108 };
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is string theme && model.Theme != theme) model.SetTheme(theme); };
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PreviewModel.Elapsed)) c.SelectedItem = model.Theme;
        }
        c.AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; c.SelectedItem = model.Theme; };
        c.DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
        ToolTip.SetTip(c, "Appearance");
        return c;
    }
}

// Vector geometry keeps the budget ring and cutlery crisp at any display scale.
public sealed class AccountIndicator : Control
{
    public PreviewAccount Account { get; }
    private readonly PreviewModel model;
    public AccountIndicator(PreviewAccount account, PreviewModel model)
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
            var ratio = Math.Clamp(model.Week(Account).TotalSeconds / budget.TotalSeconds, 0, 1);
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
    private readonly PreviewModel model;
    private readonly List<(PreviewAccount Account, Button Row, TextBlock Today, TextBlock Budget, AccountIndicator Icon)> rows = new();
    public AccountList(PreviewModel model)
    {
        this.model = model;
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
        AttachedToVisualTree += (_, _) => model.PropertyChanged += Changed;
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
        Refresh();
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
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
    public TrackingLabels(PreviewModel model)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 12;
        Children.Add(status);
        Children.Add(overtime);
        void Refresh()
        {
            status.Text = model.IsTracking ? "● Tracking" : "● Not tracking";
            status.Classes.Set("live", model.IsTracking);
            status.Classes.Set("idle", !model.IsTracking);
            overtime.IsVisible = model.IsOvertime;
        }
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Refresh(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
    }
}
