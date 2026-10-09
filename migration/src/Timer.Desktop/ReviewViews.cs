using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Timer.Desktop;

public static class ReviewViews
{
    public static Control Timeline(PreviewModel model)
    {
        var root = Ui.Grid("*", "Auto,*");
        var caption = Ui.Text("Illustrative day · timeline editing follows in the next stage", 11, "muted");
        caption.Margin = new Thickness(0, 0, 0, 10);
        Ui.Put(root, caption);
        const double pixelsPerHour = 160;
        var canvas = new Canvas { Height = 10 * pixelsPerHour, MinWidth = 460 };
        for (var hour = 8; hour <= 17; hour++)
        {
            var label = Ui.Text($"{hour:00}:00", 12, "muted");
            Canvas.SetTop(label, (hour - 8) * pixelsPerHour); canvas.Children.Add(label);
            var rule = Ui.Rule(); rule.Width = 390;
            Canvas.SetLeft(rule, 60); Canvas.SetTop(rule, (hour - 8) * pixelsPerHour + 8); canvas.Children.Add(rule);
        }
        var start = 9.0;
        foreach (var index in new[] { 0, 1, 4, 2, 3 })
        {
            var a = model.Accounts[index];
            var duration = a.Today.TotalHours;
            var text = new StackPanel { Spacing = 6 };
            text.Children.Add(Ui.Text(a.Name, 15));
            text.Children.Add(Ui.Text($"{TimeSpan.FromHours(start):hh\\:mm} – {TimeSpan.FromHours(start + duration):hh\\:mm} · {model.Format(a.Today)}", 12));
            foreach (TextBlock child in text.Children) child.Foreground = Brush.Parse("#24312B");
            var card = new Border { Background = Brush.Parse(a.Colour), CornerRadius = new CornerRadius(10),
                Width = 380, Height = duration * pixelsPerHour - 6, Padding = new Thickness(14, 10), Child = text };
            Canvas.SetLeft(card, 64); Canvas.SetTop(card, (start - 8) * pixelsPerHour + 3); canvas.Children.Add(card);
            start += duration;
        }
        Ui.Put(root, new ScrollViewer { Content = canvas, Offset = new Vector(0, 145),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }, row: 1);
        return root;
    }

    public static Control Week(PreviewModel model)
    {
        var grid = Ui.Grid("125,*,*,*,*,*,75", "Auto,Auto,Auto,Auto,Auto,Auto,Auto");
        grid.MinWidth = 510;
        var day = model.SelectedDate.AddDays(-((7 + (int)model.SelectedDate.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        Ui.Put(grid, Ui.Text("Account", 12, "muted"));
        for (var i = 0; i < 5; i++) Ui.Put(grid, Ui.Text(day.AddDays(i).ToString("ddd"), 12, "muted"), i + 1);
        Ui.Put(grid, Ui.Text("Total", 12, "muted"), 6);
        var row = 1;
        foreach (var a in model.Accounts)
        {
            Ui.Put(grid, Ui.Text(a.Name, 13), row: row).Margin = new Thickness(0, 16);
            var earlier = (a.Week - a.Today) / 4;
            for (var i = 0; i < 5; i++) Ui.Put(grid, Ui.Text(model.Format(i == 4 ? model.Today(a) : earlier), 12), i + 1, row);
            Ui.Put(grid, Ui.Text(model.Format(model.Week(a)), 12), 6, row);
            row++;
        }
        var root = new StackPanel { Spacing = 16 };
        root.Children.Add(Ui.Text("Illustrative week · totals include the preview timer", 11, "muted"));
        root.Children.Add(grid);
        return new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    }
}
