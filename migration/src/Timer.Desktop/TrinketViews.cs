using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>An analogue clock face that draws itself in the theme's colours. It shows the computer's time, not the day's working clock.</summary>
public sealed class AnalogueClock : Control
{
    private readonly Func<DateTime> now;
    public AnalogueClock(Func<DateTime> now)
    {
        this.now = now; Name = "AnalogueClock";
        ActualThemeVariantChanged += (_, _) => InvalidateVisual(); // The tile can change theme variant, so a stale brush must not linger.
    }
    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height); if (size < 20) return;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2); var radius = size / 2 - 3;
        var ink = new SolidColorBrush(Ui.ThemeColour(this, "Ink", Colors.Black)); var line = new SolidColorBrush(Ui.ThemeColour(this, "Muted", Colors.Gray));
        var face = new SolidColorBrush(Ui.ThemeColour(this, "Surface", Colors.White));
        context.DrawEllipse(face, new Pen(line, 1.5), centre, radius, radius);
        for (var i = 0; i < 12; i++)
        {
            var angle = i * Math.PI / 6; var major = i % 3 == 0;
            context.DrawLine(new Pen(major ? ink : line, major ? 2.2 : 1.2, lineCap: PenLineCap.Round), Along(centre, angle, radius * (major ? .8 : .86)), Along(centre, angle, radius * .94));
        }
        var time = now(); var seconds = time.Second + time.Millisecond / 1000.0; var minutes = time.Minute + seconds / 60; var hours = time.Hour % 12 + minutes / 60;
        context.DrawLine(new Pen(ink, Math.Max(3, radius * .07), lineCap: PenLineCap.Round), centre, Along(centre, hours * Math.PI / 6, radius * .5));
        context.DrawLine(new Pen(ink, Math.Max(2.2, radius * .05), lineCap: PenLineCap.Round), centre, Along(centre, minutes * Math.PI / 30, radius * .75));
        context.DrawLine(new Pen(line, 1.2, lineCap: PenLineCap.Round), Along(centre, Math.PI + seconds * Math.PI / 30, radius * .12), Along(centre, seconds * Math.PI / 30, radius * .82));
        context.DrawEllipse(ink, null, centre, radius * .05, radius * .05);
    }
    private static Point Along(Point centre, double clockwiseFromTop, double length) => new(centre.X + Math.Sin(clockwiseFromTop) * length, centre.Y - Math.Cos(clockwiseFromTop) * length);
}

/// <summary>The to-do list: ticks, words, and a #Tag that links an item to an account and gives it that account's colour.</summary>
public sealed class TodoView : Grid
{
    private readonly TimerModel model; private readonly StackPanel rows = new() { Spacing = 2 };
    private readonly TextBox input = new() { Name = "TodoInput", Watermark = "Add a to-do, #Account to link", Padding = new Thickness(10, 6) };
    public TodoView(TimerModel model, bool compact)
    {
        this.model = model; RowDefinitions = new RowDefinitions("*,Auto");
        var scroll = new ScrollViewer { Content = rows, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Children.Add(scroll);
        if (!compact)
        {
            input.Margin = new Thickness(0, 8, 0, 0); SetRow(input, 1); Children.Add(input);
            input.KeyDown += (_, e) => { if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(input.Text)) { var typed = input.Text; input.Text = ""; model.AddTodo(typed); e.Handled = true; } };
        }
        Rebuild();
    }
    // The dot is the account's own colour; its words are drawn part-way to the text colour so a pale account still reads on the tile.
    private static Color Mix(Color a, Color b, double towardsB) => Color.FromRgb((byte)(a.R + (b.R - a.R) * towardsB), (byte)(a.G + (b.G - a.G) * towardsB), (byte)(a.B + (b.B - a.B) * towardsB));
    /// <summary>Redraws the rows. The input box stays as it is, so typing is never interrupted.</summary>
    public void Rebuild()
    {
        rows.Children.Clear();
        var todos = model.Trinkets.Todos;
        if (todos.Count == 0) rows.Children.Add(Ui.Text("Nothing to do. Type one below.", 12, "muted"));
        for (var i = 0; i < todos.Count; i++)
        {
            var index = i; var item = todos[i];
            var tick = new CheckBox { Name = "TodoTick_" + i, IsChecked = item.Done, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            tick.Click += (_, _) => model.ToggleTodo(index);
            var text = new TextBlock { Text = item.Text, FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Opacity = item.Done ? .55 : 1 };
            if (item.Done) text.TextDecorations = TextDecorations.Strikethrough;
            var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center }; words.Children.Add(text);
            if (item.Account is { } account && model.Accounts.FirstOrDefault(a => a.Name == account) is { } known)
            {
                // The tag takes the account's own colour, so a to-do reads as belonging to it at a glance.
                var colour = Color.Parse(Ui.Visible(known.Colour));
                var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(colour), VerticalAlignment = VerticalAlignment.Center };
                var tag = new TextBlock { Text = "#" + known.Display, FontSize = 12, Foreground = new SolidColorBrush(Mix(colour, Ui.ThemeColour(this, "Ink", Colors.Black), .45)), FontWeight = FontWeight.SemiBold };
                var tagRow = Ui.Horizontal(dot, tag); tagRow.Spacing = 5; tagRow.Name = "TodoTag_" + i; words.Children.Add(tagRow);
            }
            var remove = Ui.IconButton(IconKind.Close, "Remove", "TodoRemove_" + i, () => model.RemoveTodo(index)); remove.Width = 26; remove.Height = 26;
            var row = Ui.Grid("Auto,*,Auto"); row.Margin = new Thickness(0, 2); Ui.Put(row, tick); Ui.Put(row, words, 1); Ui.Put(row, remove, 2);
            rows.Children.Add(row);
        }
    }
}

/// <summary>A post-it: a coloured square of paper to type on, kept in the light look whatever the theme, since paper does not go dark.</summary>
public static class PostItView
{
    public static Color Paper(string colour) => Color.Parse(colour switch { "lilac" => "#E3D6FF", "rose" => "#FFD3DC", _ => "#FFF1A8" });
    public static TextBox Build(TimerModel model, int index, bool oneLine)
    {
        var box = new TextBox
        {
            Name = "PostItText", Text = model.Trinkets.PostIts.ElementAtOrDefault(index)?.Text ?? "", AcceptsReturn = !oneLine, TextWrapping = oneLine ? TextWrapping.NoWrap : TextWrapping.Wrap,
            Watermark = "Jot something down", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), FontSize = 15, VerticalContentAlignment = VerticalAlignment.Top,
        };
        box.Classes.Add("postit");
        // TextChanged does not fire headless, so the text property is watched instead.
        box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) model.SetPostItText(index, box.Text ?? ""); };
        return box;
    }
}

/// <summary>
/// A strip of the selected day: each recorded block as a bar along the hours, in its account's block colour, with the running block reaching to now.
/// It is for seeing the shape of the day at a glance, not for editing, so it has no controls.
/// </summary>
public sealed class DayStrip : Control
{
    private readonly TimerModel model;
    public bool Labels { get; set; } = true;
    public DayStrip(TimerModel model) { this.model = model; Name = "DayStrip"; ActualThemeVariantChanged += (_, _) => InvalidateVisual(); }

    /// <summary>The blocks of the day as (account, from, to), the running one ending now. Exposed so the window can be tested without drawing.</summary>
    public IReadOnlyList<(string Account, DateTime From, DateTime To)> Blocks()
    {
        var day = model.SelectedDate.Date; var blocks = new List<(string, DateTime, DateTime)>();
        foreach (var row in model.Engine.Sessions.Where(s => s.Date == day.ToString("yyyy-MM-dd"))) blocks.Add((row.Account, row.From, row.To));
        if (model.Engine.Active is { } active && active.Start < day.AddDays(1) && model.Engine.Now > day)
            blocks.Add((active.Account, active.Start < day ? day : active.Start, model.Engine.Now > day.AddDays(1) ? day.AddDays(1) : model.Engine.Now));
        return blocks;
    }
    /// <summary>The hours the strip covers: the working hours at least, stretched to hold any block outside them.</summary>
    public (double From, double To) Window(IReadOnlyList<(string Account, DateTime From, DateTime To)> blocks)
    {
        var day = model.SelectedDate.Date; var from = 8.0; var to = 18.0;
        if (blocks.Count > 0) { from = Math.Min(from, Math.Floor((blocks.Min(b => b.From) - day).TotalHours)); to = Math.Max(to, Math.Ceiling((blocks.Max(b => b.To) - day).TotalHours)); }
        return (Math.Max(0, from), Math.Min(24, Math.Max(to, from + 1)));
    }
    public override void Render(DrawingContext context)
    {
        if (Bounds.Width < 40) return;
        var blocks = Blocks(); var (from, to) = Window(blocks); var day = model.SelectedDate.Date;
        var labelRoom = Labels ? 16 : 0; var barHeight = Math.Max(8, Bounds.Height - labelRoom - 2); var width = Bounds.Width;
        var track = new SolidColorBrush(Ui.ThemeColour(this, "Line", Colors.LightGray), .5);
        context.DrawRectangle(track, null, new Rect(0, 0, width, barHeight), 6, 6);
        double X(DateTime t) => Math.Clamp(((t - day).TotalHours - from) / (to - from), 0, 1) * width;
        foreach (var (account, start, end) in blocks)
        {
            var colour = model.Accounts.FirstOrDefault(a => a.Name == account) is { } known ? Color.Parse(model.BlockLook(known.Colour).Fill) : Ui.ThemeColour(this, "Muted", Colors.Gray);
            var left = X(start); var right = Math.Max(X(end), left + 3);
            context.DrawRectangle(new SolidColorBrush(colour), null, new Rect(left, 0, right - left, barHeight), 4, 4);
        }
        if (day == model.Engine.Now.Date)
        {
            var now = X(model.Engine.Now); context.DrawLine(new Pen(new SolidColorBrush(Ui.ThemeColour(this, "Ink", Colors.Black)), 2), new Point(now, -2), new Point(now, barHeight + 2));
        }
        if (!Labels) return;
        var ink = new SolidColorBrush(Ui.ThemeColour(this, "Muted", Colors.Gray));
        var step = to - from > 12 ? 4 : to - from > 6 ? 2 : 1;
        for (var hour = (int)Math.Ceiling(from); hour <= to; hour++)
        {
            if ((hour - (int)Math.Ceiling(from)) % step != 0) continue;
            var text = new FormattedText($"{hour:00}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 11, ink);
            context.DrawText(text, new Point(Math.Clamp((hour - from) / (to - from) * width - text.Width / 2, 0, width - text.Width), barHeight + 3));
        }
    }
}
