using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;
using Timer.Desk;

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
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2); var radius = size / 2 - 4;
        var inkColour = Ui.ThemeColour(this, "Ink", Colors.Black); var mutedColour = Ui.ThemeColour(this, "Muted", Colors.Gray); var surface = Ui.ThemeColour(this, "Surface", Colors.White);
        var ink = new SolidColorBrush(inkColour); var muted = new SolidColorBrush(mutedColour);
        // A soft drop shadow, then a rim, a face that is a touch lighter in the middle, and a fine inner ring.
        context.DrawEllipse(new SolidColorBrush(Colors.Black, .10), null, new Point(centre.X, centre.Y + radius * .04), radius, radius);
        context.DrawEllipse(new SolidColorBrush(inkColour, .85), null, centre, radius, radius);
        var rim = Math.Max(2.5, radius * .045); var faceRadius = radius - rim;
        var faceBrush = new RadialGradientBrush { Center = new RelativePoint(.5, .42, RelativeUnit.Relative), GradientOrigin = new RelativePoint(.5, .42, RelativeUnit.Relative), RadiusX = RelativeScalar.Parse("75%"), RadiusY = RelativeScalar.Parse("75%"),
            GradientStops = { new GradientStop(Blend(surface, Colors.White, .55), 0), new GradientStop(surface, .8), new GradientStop(Blend(surface, inkColour, .08), 1) } };
        context.DrawEllipse(faceBrush, null, centre, faceRadius, faceRadius);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(mutedColour, .35), 1), centre, faceRadius * .86, faceRadius * .86);
        // Sixty minute ticks, with a bolder one each five minutes and a numeral at each quarter, or at every hour once the face is big enough.
        for (var i = 0; i < 60; i++)
        {
            var angle = i * Math.PI / 30; var hour = i % 5 == 0;
            context.DrawLine(new Pen(hour ? ink : muted, hour ? Math.Max(2, radius * .03) : 1, lineCap: PenLineCap.Round), Along(centre, angle, faceRadius * (hour ? .80 : .88)), Along(centre, angle, faceRadius * .94));
        }
        var numerals = radius > 95; var numeralSize = Math.Clamp(radius * (numerals ? .15 : .17), 10, 20);
        for (var n = 1; n <= 12; n++)
        {
            if (!numerals && n % 3 != 0) continue;
            var text = new FormattedText(n.ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), numeralSize, n % 3 == 0 ? ink : muted);
            var at = Along(centre, n * Math.PI / 6, faceRadius * .62); context.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
        }
        var time = now(); var seconds = time.Second + time.Millisecond / 1000.0; var minutes = time.Minute + seconds / 60; var hours = time.Hour % 12 + minutes / 60;
        // The hands taper from a broad base to a rounded tip, each with a faint shadow, and the second hand has a short counterweight and a ring at its middle.
        Hand(context, centre, hours * Math.PI / 6, faceRadius * .52, Math.Max(4, radius * .085), ink);
        Hand(context, centre, minutes * Math.PI / 30, faceRadius * .78, Math.Max(3, radius * .06), ink);
        var second = new SolidColorBrush(Color.Parse("#5B76B8")); var secondAngle = seconds * Math.PI / 30;
        context.DrawLine(new Pen(new SolidColorBrush(Colors.Black, .12), 1.6, lineCap: PenLineCap.Round), Offset(Along(centre, secondAngle + Math.PI, faceRadius * .2)), Offset(Along(centre, secondAngle, faceRadius * .86)));
        context.DrawLine(new Pen(second, 1.6, lineCap: PenLineCap.Round), Along(centre, secondAngle + Math.PI, faceRadius * .2), Along(centre, secondAngle, faceRadius * .86));
        context.DrawEllipse(second, null, centre, Math.Max(3.5, radius * .05), Math.Max(3.5, radius * .05));
        context.DrawEllipse(new SolidColorBrush(surface), null, centre, Math.Max(1.4, radius * .018), Math.Max(1.4, radius * .018));
    }
    private static Point Offset(Point p) => new(p.X + 1.5, p.Y + 2);
    private static Color Blend(Color a, Color b, double towardsB) => Color.FromRgb((byte)(a.R + (b.R - a.R) * towardsB), (byte)(a.G + (b.G - a.G) * towardsB), (byte)(a.B + (b.B - a.B) * towardsB));
    private static void Hand(DrawingContext context, Point centre, double angle, double length, double baseWidth, IBrush brush)
    {
        Point Local(double along, double across) => new(centre.X + Math.Sin(angle) * along + Math.Cos(angle) * across, centre.Y - Math.Cos(angle) * along + Math.Sin(angle) * across);
        Geometry Shape(Func<Point, Point> move)
        {
            var g = new StreamGeometry();
            using var c = g.Open();
            c.BeginFigure(move(Local(-baseWidth * 1.4, 0)), true);
            c.LineTo(move(Local(0, baseWidth / 2))); c.LineTo(move(Local(length, baseWidth * .18))); c.LineTo(move(Local(length, -baseWidth * .18))); c.LineTo(move(Local(0, -baseWidth / 2)));
            c.EndFigure(true); return g;
        }
        context.DrawGeometry(new SolidColorBrush(Colors.Black, .14), null, Shape(Offset));
        context.DrawGeometry(brush, null, Shape(p => p));
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
        // The list is made of task notes, the same items Notes and Coming up show, so a tick or a removal here is a tick or a removal there.
        var todos = model.Todos; var today = model.Engine.Now.Date;
        if (todos.Count == 0) rows.Children.Add(Ui.Text("Nothing to do. Type one below.", 12, "muted"));
        foreach (var item in todos)
        {
            var id = item.Id;
            var tick = new CheckBox { Name = "TodoTick_" + id, IsChecked = item.Done, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            tick.Click += (_, _) => model.ToggleTodo(id);
            var text = new TextBlock { Text = NoteShorthand.Display(item, model.AccountNames), FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Opacity = item.Done ? .55 : 1 };
            if (item.Done) text.TextDecorations = TextDecorations.Strikethrough;
            var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center }; words.Children.Add(text);
            var line = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            if (NoteShorthand.AccountOf(item.Text, model.AccountNames) is { } account && model.Accounts.FirstOrDefault(a => a.Name == account) is { } known)
            {
                // The tag takes the account's own colour, so a to-do reads as belonging to it at a glance.
                var colour = Color.Parse(Ui.Visible(known.Colour));
                var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(colour), VerticalAlignment = VerticalAlignment.Center };
                var tag = new TextBlock { Text = "#" + known.Display, FontSize = 12, Foreground = new SolidColorBrush(TagColours.Text(colour, model)), FontWeight = FontWeight.SemiBold };
                var tagRow = Ui.Horizontal(dot, tag); tagRow.Spacing = 5; tagRow.Name = "TodoTag_" + id; line.Children.Add(tagRow);
            }
            // One carried over from an earlier day says when it was first set down.
            if (item.Day.Date < today) { var from = Ui.Text("from " + ComingUpView.When(item.Day, model.Engine.Now), 12, "muted"); from.Name = "TodoFrom_" + id; line.Children.Add(from); }
            if (line.Children.Count > 0) words.Children.Add(line);
            var remove = Ui.IconButton(IconKind.Close, "Remove", "TodoRemove_" + id, () => model.RemoveTodo(id)); remove.Width = 26; remove.Height = 26;
            var row = Ui.Grid("Auto,*,Auto"); row.Margin = new Thickness(0, 2); Ui.Put(row, tick); Ui.Put(row, words, 1); Ui.Put(row, remove, 2);
            rows.Children.Add(row);
        }
    }
}

/// <summary>A post-it: a coloured square of paper to type on, kept in the light look whatever the theme, since paper does not go dark.</summary>
public static class PostItView
{
    public static Color Paper(string colour) => Color.Parse(colour switch { "lilac" => "#E3D6FF", "rose" => "#FFD3DC", "sky" => "#CFE6FF", "mint" => "#D4F0DC", _ => "#FFF1A8" });
    public static TextBox Build(TimerModel model, int index, bool oneLine, string name = "PostItText")
    {
        var box = new TextBox
        {
            Name = name, Text = model.Trinkets.PostIts.ElementAtOrDefault(index)?.Text ?? "", AcceptsReturn = !oneLine, TextWrapping = oneLine ? TextWrapping.NoWrap : TextWrapping.Wrap, FontFamily = new FontFamily("Segoe Print, Segoe Script, Bradley Hand, Comic Sans MS, Segoe UI"),
            Watermark = "Jot something down", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), FontSize = 17, VerticalContentAlignment = VerticalAlignment.Top,
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

/// <summary>Notes for the day the dashboard is showing. A time such as 14:30 typed in a note is kept as its time and makes it appear in Coming up.</summary>
public sealed class NotesView : Grid
{
    private readonly TimerModel model; private readonly StackPanel rows = new() { Spacing = 4 };
    private readonly TextBox input = new() { Name = "NoteInput", Watermark = "Add a note: 14:30 time · ! deadline · @fri day · #Account", Padding = new Thickness(10, 6) };
    public NotesView(TimerModel model, bool compact)
    {
        this.model = model; RowDefinitions = new RowDefinitions("*,Auto");
        Children.Add(new ScrollViewer { Content = rows, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        if (!compact)
        {
            input.Margin = new Thickness(0, 8, 0, 0); SetRow(input, 1); Children.Add(input);
            input.KeyDown += (_, e) => { if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(input.Text)) { var typed = input.Text; input.Text = ""; model.AddDayNote(model.SelectedDate, typed); e.Handled = true; } };
        }
        Rebuild();
    }
    public void Rebuild()
    {
        rows.Children.Clear();
        var british = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        var dayHeading = Ui.Text(model.SelectedDate.ToString("dddd d MMMM", british), 15); dayHeading.FontWeight = FontWeight.SemiBold; rows.Children.Add(dayHeading);
        var notes = model.NotesOn(model.SelectedDate);
        if (notes.Count == 0) rows.Children.Add(Ui.Text("No notes for this day.", 12, "muted"));
        foreach (var note in notes)
        {
            var id = note.Id;
            var tick = new CheckBox { Name = "NoteTick_" + id, IsChecked = note.Done, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            tick.Click += (_, _) => model.ToggleDayNote(id);
            var text = new TextBox { Name = "NoteText_" + id, Text = note.Text, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4, 2), FontSize = 14, Opacity = note.Done ? .55 : 1 };
            // TextChanged does not fire headless, so the text property is watched instead.
            text.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) model.SetDayNoteText(id, text.Text ?? ""); };
            var remove = Ui.IconButton(IconKind.Close, "Remove", "NoteRemove_" + id, () => model.RemoveDayNote(id)); remove.Width = 26; remove.Height = 26;
            // A small calendar picks another day for the note, for when it was put on the wrong one or plans change.
            var when = Ui.IconButton(IconKind.Today, "Move to another day", "NoteMove_" + id, () => { }); when.Width = 26; when.Height = 26;
            var calendar = new Calendar { Name = "NoteCalendar_" + id, SelectedDate = note.Day, DisplayDate = note.Day };
            var flyout = new Flyout { Content = calendar, Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight };
            calendar.SelectedDatesChanged += (_, _) => { if (calendar.SelectedDate is { } picked) { flyout.Hide(); model.MoveDayNote(id, picked); } };
            when.Flyout = flyout;
            var row = Ui.Grid("Auto,*,Auto,Auto"); row.VerticalAlignment = VerticalAlignment.Center;
            Ui.Put(row, tick);
            // What was recognised in the words is shown under them as chips, each with a cross that takes it back out of the note.
            var body = new StackPanel { Spacing = 0 }; body.Children.Add(text);
            if (NoteChips.For(model, note, this) is { } chips) { chips.Margin = new Thickness(8, 0, 0, 2); body.Children.Add(chips); }
            Ui.Put(row, body, 1); Ui.Put(row, when, 2); Ui.Put(row, remove, 3);
            rows.Children.Add(row);
        }
    }
}

/// <summary>
/// The chips that show what a note's shorthand was taken to mean: its time, whether it is a deadline, and the account its #Tag links to. In Notes each has a
/// cross that takes that part back out of the note (see TimerModel.ClearNoteTime and the others); elsewhere the account chip is only shown.
/// </summary>
public static class NoteChips
{
    private static Border Chip(string name, Control? lead, string text, string tip, string? clearName, Action? clear, IBrush? ink = null)
    {
        var words = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (ink is not null) words.Foreground = ink;
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        if (lead is not null) row.Children.Add(lead);
        row.Children.Add(words);
        if (clear is not null && clearName is not null)
        {
            var cross = Ui.IconButton(IconKind.Close, "Take this out of the note", clearName, clear); cross.Width = 18; cross.Height = 18; cross.Padding = new Thickness(0); row.Children.Add(cross);
        }
        var chip = new Border { Name = name, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(8, 1, clear is null ? 8 : 3, 1), Margin = new Thickness(0, 0, 6, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = row };
        chip.Classes.Add("chip"); ToolTip.SetTip(chip, tip); return chip;
    }
    private static Color Mix(Color a, Color b, double towardsB) => Color.FromRgb((byte)(a.R + (b.R - a.R) * towardsB), (byte)(a.G + (b.G - a.G) * towardsB), (byte)(a.B + (b.B - a.B) * towardsB));
    /// <summary>The chip for the account a note's #Tag names, wearing that account's colour, or null when it names none.</summary>
    public static Control? AccountChip(TimerModel model, DayNote note, Control host, string name, bool interactive)
    {
        if (NoteShorthand.AccountOf(note.Text, model.AccountNames) is not { } account || model.Accounts.FirstOrDefault(a => a.Name == account) is not { } known) return null;
        var colour = Color.Parse(Ui.Visible(known.Colour));
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(colour), VerticalAlignment = VerticalAlignment.Center };
        var ink = new SolidColorBrush(TagColours.Text(colour, model));
        return Chip(name, dot, "#" + known.Display, "Linked to " + known.Display, interactive ? name.Replace("Chip", "Clear") : null, interactive ? () => model.ClearNoteAccount(note.Id) : null, ink);
    }
    /// <summary>All the chips for one note, or null when it has none.</summary>
    public static Control? For(TimerModel model, DayNote note, Control host)
    {
        var chips = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        var id = note.Id;
        if (note.At is { } at)
            chips.Children.Add(Chip("NoteTimeChip_" + id, new IconGlyph(IconKind.Clock) { TintKey = "Muted" }, at.ToString("hh\\:mm"), "Time, taken from the words. The cross puts it back into them.", "NoteTimeClear_" + id, () => model.ClearNoteTime(id)));
        if (DayNotes.IsDeadline(note))
            chips.Children.Add(Chip("NoteDeadlineChip_" + id, new IconGlyph(IconKind.Flag) { TintKey = "Finish" }, "Deadline", "A deadline, from the ! at the start. The cross makes it an ordinary note.", "NoteDeadlineClear_" + id, () => model.ClearNoteDeadline(id)));
        if (AccountChip(model, note, host, "NoteAccountChip_" + id, interactive: true) is { } account) chips.Children.Add(account);
        return chips.Children.Count == 0 ? null : chips;
    }
}

/// <summary>What is coming up: dated notes from today on, soonest first, each marked with when it falls.</summary>
public sealed class ComingUpView : StackPanel
{
    private readonly TimerModel model;
    private readonly bool compact;
    public ComingUpView(TimerModel model, bool compact = false) { this.model = model; this.compact = compact; Spacing = 4; Rebuild(); }
    public static string When(DateTime day, DateTime today)
        => (day.Date - today.Date).Days switch { 0 => "Today", 1 => "Tomorrow", < 7 => day.ToString("dddd", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), _ => day.ToString("ddd d MMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")) };
    public void Rebuild()
    {
        Children.Clear();
        var coming = model.ComingUp();
        if (coming.Count == 0)
        {
            Children.Add(Ui.Text("Nothing coming up", compact ? 13 : 14, "muted"));
            // One quiet way to put something here, through the same dialog the pill's menu opens.
            if (!compact) { var add = Ui.DialogButton("Add a note", () => model.RequestNote(), "ComingAdd"); add.HorizontalAlignment = HorizontalAlignment.Left; add.Height = 32; add.Margin = new Thickness(0, 4, 0, 0); Children.Add(add); }
        }
        // Small, it is the one thing coming next, on a single line.
        foreach (var note in compact ? coming.Take(1) : coming)
        {
            if (compact)
            {
                var line = new StackPanel { Spacing = 0 };
                line.Children.Add(new TextBlock { Text = When(note.Day, model.Engine.Now) + (note.At is { } t ? " " + t.ToString("hh\\:mm") : ""), FontSize = 15, FontWeight = FontWeight.Bold });
                line.Children.Add(new TextBlock { Text = NoteShorthand.Display(note, model.AccountNames), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }); Children.Add(line); continue;
            }
            var id = note.Id;
            // What kind of thing it is shows in its leading mark: a flag for a deadline (press it when done), a clock for an event, which simply passes,
            // and a tick box for a task, which is the only kind that is ticked off.
            Control tick;
            if (DayNotes.IsDeadline(note)) { var flag = Ui.IconButton(IconKind.Flag, "Deadline. Press when it is done", "ComingDone_" + id, () => model.ToggleDayNote(id)); flag.Width = 26; flag.Height = 26; tick = flag; }
            else if (note.At is not null)
            {
                var mark = new Border { Name = "ComingEvent_" + id, Width = 26, Height = 26, Child = new IconGlyph(IconKind.Clock) { TintKey = "Muted", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                ToolTip.SetTip(mark, "Event"); tick = mark;
            }
            else { var box = new CheckBox { Name = "ComingTick_" + id, IsChecked = false, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center }; box.Click += (_, _) => model.ToggleDayNote(id); tick = box; }
            var when = Ui.Text(When(note.Day, model.Engine.Now) + (note.At is { } at ? " " + at.ToString("hh\\:mm") : ""), 13); when.FontWeight = FontWeight.Bold; // Bold, so when it falls is read first.
            var words = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center }; words.Children.Add(new TextBlock { Text = NoteShorthand.Display(note, model.AccountNames), FontSize = 14, TextWrapping = TextWrapping.Wrap }); words.Children.Add(when);
            if (NoteChips.AccountChip(model, note, this, "ComingAccount_" + id, interactive: false) is { } account) { account.Margin = new Thickness(0, 2, 0, 0); words.Children.Add(account); }
            var row = Ui.Grid("Auto,*"); tick.VerticalAlignment = VerticalAlignment.Center; Ui.Put(row, tick); Ui.Put(row, words, 1); Children.Add(row);
        }
    }
}

/// <summary>A ring that fills as a fraction goes from 0 to 1, in the theme's text colour on a faint track.</summary>
public sealed class ProgressRing : Control
{
    public double Fraction { get; set; }
    public bool Soft { get; set; }
    public ProgressRing() { ActualThemeVariantChanged += (_, _) => InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height); if (size < 30) return;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2); var thickness = Math.Max(5, size * .07); var radius = size / 2 - thickness;
        var track = new Pen(new SolidColorBrush(Ui.ThemeColour(this, "Line", Colors.LightGray)), thickness, lineCap: PenLineCap.Round);
        context.DrawEllipse(null, track, centre, radius, radius);
        var fraction = Math.Clamp(Fraction, 0, 1); if (fraction <= 0.002) return;
        var ink = Ui.ThemeColour(this, "Ink", Colors.Black); var pen = new Pen(new SolidColorBrush(ink, Soft ? .55 : 1), thickness, lineCap: PenLineCap.Round);
        if (fraction >= .999) { context.DrawEllipse(null, pen, centre, radius, radius); return; }
        var end = -Math.PI / 2 + fraction * 2 * Math.PI;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(centre.X, centre.Y - radius), false);
            g.ArcTo(new Point(centre.X + Math.Cos(end) * radius, centre.Y + Math.Sin(end) * radius), new Size(radius, radius), 0, fraction > .5, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }
}

/// <summary>The pomodoro tile: a ring, the time left and, when there is room, start and reset. It ends quietly, by changing its words and softening the ring.</summary>
public sealed class PomodoroView : Grid
{
    private readonly TimerModel model; private readonly ProgressRing ring = new() { Name = "PomodoroRing" };
    private readonly TextBlock time = new() { Name = "PomodoroTime", FontSize = 46, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "clock" } };
    private readonly TextBlock phase = Ui.Text("", 12, "muted"); private readonly Button toggle, account;
    public PomodoroView(TimerModel model, bool compact)
    {
        this.model = model; RowDefinitions = new RowDefinitions(compact ? "*" : "*,Auto");
        phase.HorizontalAlignment = HorizontalAlignment.Center;
        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 0, IsHitTestVisible = false };
        middle.Children.Add(time); middle.Children.Add(phase);
        var face = new Grid(); face.Children.Add(ring); face.Children.Add(middle);
        // Where there are no buttons, the face itself starts and pauses.
        face.Background = Brushes.Transparent; face.Cursor = new Cursor(StandardCursorType.Hand);
        if (compact) face.PointerPressed += (_, _) => { model.Pomodoro.Toggle(model.Engine.Now); Refresh(); };
        Children.Add(face);
        toggle = Ui.DialogButton("Start", () => { model.Pomodoro.Toggle(model.Engine.Now); Refresh(); }, "PomodoroToggle");
        var reset = Ui.DialogButton("Reset", () => { model.Pomodoro.Reset(); Refresh(); }, "PomodoroReset");
        // Which account the focus is for. It is only a label for the session, and never starts, stops or adds recorded time.
        account = Ui.DialogButton("", () => { model.CyclePomodoroAccount(); Refresh(); }, "PomodoroAccount"); account.Classes.Remove("mainAction"); account.Height = 30; account.Padding = new Thickness(10, 0);
        ToolTip.SetTip(account, "Which account this focus is for. It never adds recorded time.");
        var buttons = Ui.Horizontal(toggle, reset, account); buttons.HorizontalAlignment = HorizontalAlignment.Center; buttons.Margin = new Thickness(0, 6, 0, 0);
        if (!compact) { SetRow(buttons, 1); Children.Add(buttons); }
        Refresh();
    }
    /// <summary>Brings the face up to date. It is called as time passes, and moves the timer on when a phase has run out.</summary>
    public void Refresh()
    {
        var now = model.Engine.Now; var p = model.Pomodoro; p.Update(now);
        var left = p.Remaining(now); time.Text = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
        phase.Text = p.Nudge ? (p.Phase == PomodoroPhase.Work ? "Back to work?" : p.Phase == PomodoroPhase.LongBreak ? "Time for a long break" : "Time for a short break")
            : p.Phase switch { PomodoroPhase.Idle => "Ready", PomodoroPhase.Work => p.Running ? "Focus" : "Paused", PomodoroPhase.ShortBreak => p.Running ? "Short break" : "Paused", _ => p.Running ? "Long break" : "Paused" };
        if (model.Pomodoro.Account is { } forAccount && p.Phase == PomodoroPhase.Work) phase.Text += " · " + model.Label(forAccount);
        if (p.Completed > 0) phase.Text += " · " + p.Completed;
        account.Content = model.Pomodoro.Account is { } chosen ? "#" + model.Label(chosen) : "No account";
        toggle.Content = p.Running ? "Pause" : p.Phase == PomodoroPhase.Idle || p.Nudge ? "Start" : "Resume";
        ring.Fraction = p.Phase == PomodoroPhase.Idle ? 0 : p.Fraction(now); ring.Soft = p.Nudge; ring.InvalidateVisual();
    }
}

/// <summary>
/// The time tracker tile. Small, it is the clock and one round button; normal, it adds who is running, the Tracking chip and a break button;
/// roomy, it adds the day's allocated and unallocated time. The pause and break buttons are the two controls that matter most while working,
/// so they live on the tile rather than behind a page.
/// </summary>
public sealed class TrackerView : Grid
{
    private readonly TimerModel model; private readonly ClockText clock; private readonly TextBlock account = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border dot = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border chip = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock chipText = new() { FontSize = 12, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock allocated = new() { Name = "TrackerBlock", FontSize = 24, FontWeight = FontWeight.Bold, Classes = { "clock" } }, unallocated = new() { Name = "TrackerAccountToday", FontSize = 24, FontWeight = FontWeight.Bold, Classes = { "clock" } };
    private readonly PauseGlyph pauseGlyph = new(); private readonly BreakGlyph cup = new();
    public TrackerView(TimerModel model, TileForm form, bool slim = false)
    {
        // Slim, in one row, it is the clock, the status and one button; otherwise the clock and who is running, with a break button, and with room the block and the account's day.
        this.model = model; var tiny = form == TileForm.Tiny || slim; var roomy = form == TileForm.Roomy && !slim;
        clock = new ClockText(slim ? 36 : tiny ? 34 : roomy ? 66 : 56, slim ? 17 : tiny ? 16 : roomy ? 30 : 26); clock.Name = "TrackerClock";
        ColumnDefinitions = new ColumnDefinitions("*,Auto"); VerticalAlignment = VerticalAlignment.Center;
        chip.Child = chipText;
        var left = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center }; left.Children.Add(clock);
        if (!tiny) { var who = Ui.Horizontal(dot, account, chip); who.Spacing = 8; left.Children.Add(who); }
        if (slim) { var row = Ui.Horizontal(left); row.Spacing = 14; if (form != TileForm.Tiny) { var status = Ui.Horizontal(dot, chip); status.Spacing = 8; status.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(status); } Children.Add(row); }
        if (roomy)
        {
            // How long this block has run and what the account has had today: the tracker's own questions, not the day's reconciliation.
            var stats = Ui.Grid("*,*"); stats.Margin = new Thickness(0, 8, 0, 0); stats.ColumnSpacing = 18;
            var a = new StackPanel(); a.Children.Add(Ui.Text("This block", 12, "muted")); a.Children.Add(allocated);
            var u = new StackPanel(); u.Children.Add(Ui.Text("Account today", 12, "muted")); u.Children.Add(unallocated);
            Ui.Put(stats, a); Ui.Put(stats, u, 1); left.Children.Add(stats);
        }
        if (!slim) Children.Add(left);
        Button Round(Control glyph, string name, string tip, Action click, double size)
        {
            var button = Ui.Button("", click, name); button.Classes.Add("toolButton"); button.Classes.Add("quiet");
            button.Width = size; button.Height = size; button.CornerRadius = new CornerRadius(size / 2); button.Padding = new Thickness(0);
            button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center;
            button.Content = new Viewbox { Width = size * .46, Height = size * .46, Child = glyph }; ToolTip.SetTip(button, tip); Avalonia.Automation.AutomationProperties.SetName(button, tip); return button;
        }
        var pause = Round(pauseGlyph, "TrackerPause", "Pause or carry on", model.TogglePause, tiny ? 40 : 52);
        pause.PointerEntered += (_, _) => pauseGlyph.Glow = 1; pause.PointerExited += (_, _) => pauseGlyph.Glow = 0;
        var buttons = Ui.Horizontal(pause); buttons.Spacing = 10; buttons.VerticalAlignment = VerticalAlignment.Center;
        if (!tiny)
        {
            var rest = Round(cup, "TrackerBreak", "Take a break", model.ToggleBreak, 52);
            rest.PointerEntered += (_, _) => cup.SetSteaming(true); rest.PointerExited += (_, _) => cup.SetSteaming(model.IsOnBreak);
            buttons.Children.Add(rest);
        }
        if (slim) { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"); buttons.Margin = new Thickness(14, 0, 0, 0); }
        SetColumn(buttons, 1); Children.Add(buttons);
        if (slim) HorizontalAlignment = HorizontalAlignment.Right;
        Refresh();
    }
    public void Refresh()
    {
        var onBreak = model.IsOnBreak; var late = model.IsOvertime && !onBreak; var live = model.IsTracking && !onBreak && !late;
        clock.Text = model.ClockText; clock.SetState(onBreak, late, live);
        account.Text = model.AccountLabel; allocated.Text = model.Format(model.Engine.ActiveElapsed);
        unallocated.Text = model.ActiveAccount is { } running && !BreakAccount.Is(running) && model.Accounts.FirstOrDefault(a => a.Name == running) is { } current ? model.Format(model.Today(current)) : model.Format(TimeSpan.Zero);
        var shown = onBreak ? null : model.Accounts.FirstOrDefault(a => a.Name == (model.ActiveAccount ?? model.SelectedAccount));
        dot.IsVisible = shown is not null; if (shown is not null) dot.Background = Brush.Parse(Ui.Visible(shown.Colour));
        // Green says tracking and amber says a break, as everywhere else; anything else is a quiet grey chip.
        var key = onBreak ? "Break" : model.IsTracking ? "Live" : "Muted";
        var colour = Ui.ThemeColour(this, key, Colors.Gray);
        chip.Background = new SolidColorBrush(colour, .18); chipText.Foreground = new SolidColorBrush(onBreak ? Ui.ThemeColour(this, "BreakInk", colour) : model.IsTracking ? Ui.ThemeColour(this, "LiveInk", colour) : colour);
        chipText.Text = onBreak ? "On break" : model.IsTracking ? "Tracking" : "Paused";
        pauseGlyph.Playing = model.IsTracking && !onBreak; pauseGlyph.InvalidateVisual();
        cup.Filled = onBreak; cup.SetSteaming(onBreak); cup.InvalidateVisual();
    }
}

/// <summary>
/// The week as bars, one for each working day. Small, it is a few thin bars with no words; normal, bars of time on accounts with the unallocated
/// time stacked on top; roomy, the same bars split by account in each account's own colour, with a legend of the week's biggest accounts.
/// </summary>
public sealed class WeekChart : Control
{
    private readonly TimerModel model;
    public TileForm Mode { get; set; } = TileForm.Normal;
    public WeekChart(TimerModel model) { this.model = model; Name = "WeekChart"; ActualThemeVariantChanged += (_, _) => InvalidateVisual(); }
    /// <summary>Each day of the week shown with its allocated and unallocated time. A day off appears only if time was tracked on it.</summary>
    public IReadOnlyList<(DateTime Day, TimeSpan Allocated, TimeSpan Unallocated)> Days()
    {
        var start = model.WeekStart(model.SelectedDate); var days = new List<(DateTime, TimeSpan, TimeSpan)>();
        for (var i = 0; i < 7; i++)
        {
            var day = start.AddDays(i); var totals = model.Engine.Totals(day);
            if (model.Engine.Schedule.Days.Contains(day.DayOfWeek) || totals.Allocated + totals.Unallocated > TimeSpan.Zero) days.Add((day, totals.Allocated, totals.Unallocated));
        }
        return days;
    }
    public TimeSpan Total() => Days().Aggregate(TimeSpan.Zero, (sum, d) => sum + d.Allocated + d.Unallocated);
    /// <summary>The time on each account on one day, biggest first, the running block included. The break is not an account here.</summary>
    public IReadOnlyList<(string Account, TimeSpan Time)> AccountsOn(DateTime day)
    {
        var key = day.ToString("yyyy-MM-dd"); var totals = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in model.Engine.Sessions.Where(s => s.Date == key && !BreakAccount.Is(s.Account))) totals[row.Account] = totals.GetValueOrDefault(row.Account) + row.Duration;
        if (model.Engine.Active is { } active && !BreakAccount.Is(active.Account) && model.Engine.ActiveElapsedOn(day) is { } running && running > TimeSpan.Zero) totals[active.Account] = totals.GetValueOrDefault(active.Account) + running;
        return totals.OrderByDescending(t => t.Value).Select(t => (t.Key, t.Value)).ToList();
    }
    /// <summary>The week's time on each account, biggest first. It is what the legend lists.</summary>
    public IReadOnlyList<(string Account, TimeSpan Time)> AccountsThisWeek()
    {
        var totals = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
        foreach (var (day, _, _) in Days()) foreach (var (account, time) in AccountsOn(day)) totals[account] = totals.GetValueOrDefault(account) + time;
        return totals.OrderByDescending(t => t.Value).Select(t => (t.Key, t.Value)).ToList();
    }
    private Color ColourOf(string account) => model.Accounts.FirstOrDefault(a => a.Name.Equals(account, StringComparison.OrdinalIgnoreCase)) is { } known ? Color.Parse(Ui.Visible(known.Colour)) : Colors.Gray; // The account's own colour, so each stays recognisable beside its dot elsewhere.
    /// <summary>With more room the legend names every account in the week; otherwise it names the biggest few and counts the rest.</summary>
    public bool FullLegend { get; set; }
    private readonly List<(Rect Area, string Tip)> hits = new();
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        // The details are under the pointer rather than in a legend that would crowd the chart: a segment names its account and time, a bar its day.
        var at = e.GetPosition(this); string? tip = null;
        foreach (var (area, text) in hits) if (area.Contains(at)) tip = text;
        ToolTip.SetTip(this, tip);
    }
    public override void Render(DrawingContext context)
    {
        hits.Clear();
        var days = Days(); if (days.Count == 0 || Bounds.Width < 40) return;
        var tiny = Mode == TileForm.Tiny; var roomy = Mode == TileForm.Roomy;
        var ink = Ui.ThemeColour(this, "Ink", Colors.Black); var inkBrush = new SolidColorBrush(ink); var muted = new SolidColorBrush(Ui.ThemeColour(this, "Muted", Colors.Gray));
        var unallocated = new SolidColorBrush(Color.Parse("#E3D3B0")); var plain = new SolidColorBrush(Color.Parse("#8FB3E8"));
        var seam = new Pen(new SolidColorBrush(Ui.ThemeColour(this, "Surface", Colors.White), .9), 1.5); // A thin seam between segments, so neighbouring colours that are alike still read as separate.
        var top = Math.Max(8, days.Max(d => (d.Allocated + d.Unallocated).TotalHours)); // A light week does not stretch to fill the tile.
        // The legend: the biggest accounts in a line, or in a full list when there is a lot of room, wrapping as it needs to.
        var all = roomy ? AccountsThisWeek() : new List<(string Account, TimeSpan Time)>();
        var legendLines = new List<List<(string Account, string Text, double Width)>>();
        if (all.Count > 0)
        {
            var shown = FullLegend ? all : all.Take(5).ToList(); var line = new List<(string, string, double)>(); var used = 0.0;
            FormattedText Measure(string t) => new(t, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 12, inkBrush);
            foreach (var (account, time) in shown)
            {
                var text = $"{account}  {model.Format(time)}"; var width = Measure(text).Width + 28;
                if (used + width + (FullLegend ? 0 : 74) > Bounds.Width && line.Count > 0) { legendLines.Add(line); line = new(); used = 0; if (!FullLegend) break; } // One line keeps room for the count of the rest.
                line.Add((account, text, width)); used += width;
            }
            if (line.Count > 0 && (FullLegend || legendLines.Count == 0)) legendLines.Add(line);
            var listed = legendLines.SelectMany(l => l).Count();
            if (!FullLegend && all.Count > listed && legendLines.Count > 0) legendLines[^1].Add(("", $"+{all.Count - listed} more", 70));
        }
        var labelRoom = tiny ? 0.0 : 20.0; var legendRoom = legendLines.Count * 20.0 + (legendLines.Count > 0 ? 6 : 0);
        var area = Math.Max(16, Bounds.Height - labelRoom - legendRoom); var slot = Bounds.Width / days.Count; var barWidth = Math.Min(tiny ? 18 : 48, slot * (tiny ? .55 : .62));
        foreach (var (i, d) in days.Select((d, i) => (i, d)))
        {
            var x = i * slot + (slot - barWidth) / 2; var u = d.Unallocated.TotalHours / top * area; var a = d.Allocated.TotalHours / top * area;
            double y = area;
            var dayText = $"{d.Day.ToString("ddd d MMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB"))}: {model.Format(d.Allocated)} on accounts, {model.Format(d.Unallocated)} unallocated";
            hits.Add((new Rect(i * slot, 0, slot, area + labelRoom), dayText));
            if (roomy)
            {
                // Each account's share, from the bottom up, in its own colour; the unallocated time sits on top as before.
                foreach (var (account, time) in AccountsOn(d.Day).AsEnumerable().Reverse())
                {
                    var h = time.TotalHours / top * area; if (h < 0.5) continue; y -= h;
                    var segment = new Rect(x, y, barWidth, h);
                    context.DrawRectangle(new SolidColorBrush(ColourOf(account)), seam, segment, 3, 3);
                    hits.Add((segment, $"{account}: {model.Format(time)} ({d.Day.ToString("ddd", System.Globalization.CultureInfo.GetCultureInfo("en-GB"))})"));
                }
            }
            else if (a > 0.5) { y -= a; context.DrawRectangle(plain, null, new Rect(x, y, barWidth, a), tiny ? 3 : 5, tiny ? 3 : 5); }
            if (u > 0.5) context.DrawRectangle(unallocated, roomy ? seam : null, new Rect(x, y - u, barWidth, u), tiny ? 3 : 5, tiny ? 3 : 5);
            if (tiny) continue;
            var current = d.Day == model.SelectedDate.Date;
            var label = new FormattedText(d.Day.ToString("ddd", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, current ? FontWeight.Bold : FontWeight.Normal), 12, current ? inkBrush : muted);
            context.DrawText(label, new Point(x + (barWidth - label.Width) / 2, area + 4));
        }
        var rowY = Bounds.Height - legendRoom + 8;
        foreach (var line in legendLines)
        {
            var cursor = 0.0;
            foreach (var (account, text, width) in line)
            {
                if (account.Length > 0) context.DrawEllipse(new SolidColorBrush(ColourOf(account)), null, new Point(cursor + 5, rowY + 8), 4.5, 4.5);
                var t = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 12, account.Length > 0 ? inkBrush : muted);
                context.DrawText(t, new Point(cursor + (account.Length > 0 ? 14 : 0), rowY + 8 - t.Height / 2)); cursor += width;
            }
            rowY += 20;
        }
    }
}

/// <summary>
/// A countdown to the next deadline, a note starting with !. Small, it is just the countdown; normal, the deadline's name, the countdown and when it
/// falls; roomy, it also says how much working time, over how many working days, is left before it. It is about one deadline; Coming up is the overview of what follows.
/// </summary>
public sealed class DeadlineView : StackPanel
{
    private readonly TimerModel model; private readonly TileForm form;
    private readonly TextBlock what = new() { Name = "DeadlineWhat", FontSize = 17, FontWeight = FontWeight.SemiBold }, phrase = Ui.Text("", 13, "muted"), left = new() { Name = "DeadlineLeft", FontSize = 40, Classes = { "clock" } }, when = Ui.Text("", 13);
    private readonly TextBlock workLeft = new() { Name = "DeadlineWorkLeft", FontSize = 14, FontWeight = FontWeight.SemiBold };
    private readonly bool slim; private readonly Button add;
    public DeadlineView(TimerModel model, TileForm form, bool slim = false)
    {
        this.model = model; this.form = slim ? TileForm.Tiny : form; this.slim = slim; form = this.form; Spacing = 2; VerticalAlignment = VerticalAlignment.Center;
        add = Ui.DialogButton("Add a deadline", () => model.RequestNote("! "), "DeadlineAdd"); add.HorizontalAlignment = HorizontalAlignment.Left; add.Height = 32; add.Margin = new Thickness(0, 8, 0, 0);
        what.TextTrimming = TextTrimming.CharacterEllipsis; left.FontSize = form == TileForm.Tiny ? 34 : form == TileForm.Roomy ? 60 : 54; left.FontWeight = FontWeight.Bold;
        if (slim) { what.FontSize = 13; what.MaxWidth = 150; left.FontSize = 36; var row = Ui.Horizontal(what, left); row.Spacing = 12; Orientation = Orientation.Vertical; Children.Add(row); HorizontalAlignment = HorizontalAlignment.Right; Refresh(); return; }
        if (form != TileForm.Tiny) { Children.Add(what); Children.Add(phrase); } else { what.FontSize = 13; Children.Add(what); }
        Children.Add(left);
        if (form != TileForm.Tiny) { Children.Add(when); Children.Add(add); }
        if (form == TileForm.Roomy) { workLeft.Margin = new Thickness(0, 6, 0, 0); Children.Add(workLeft); }
        Refresh();
    }
    // The countdown and the date take a deep tone of the tile's rose, part-way to the text colour, so they are bold without turning into a warning colour.
    private IBrush Tone()
    {
        var ink = Ui.ThemeColour(this, "Ink", Colors.Black); var rose = Color.Parse("#D4577F");
        return new SolidColorBrush(Color.FromRgb((byte)(rose.R + (ink.R - rose.R) * .35), (byte)(rose.G + (ink.G - rose.G) * .35), (byte)(rose.B + (ink.B - rose.B) * .35)));
    }
    public void Refresh()
    {
        left.Foreground = Tone(); when.Foreground = left.Foreground;
        var deadlines = model.AllDayNotes.Where(n => !n.Done && DayNotes.IsDeadline(n)).OrderBy(DayNotes.DueAt).ThenBy(n => n.Id).ToList();
        var note = deadlines.FirstOrDefault();
        // With nothing due the tile says so once, in quiet words, and offers the one thing to do about it, rather than a big figure for nothing.
        var empty = note is null; left.IsVisible = !empty; phrase.IsVisible = !empty && !slim && form != TileForm.Tiny; when.IsVisible = !empty; add.IsVisible = empty && !slim && form != TileForm.Tiny;
        if (note is null) { what.Text = "Nothing due"; what.Foreground = new SolidColorBrush(Ui.ThemeColour(this, "Muted", Colors.Gray)); phrase.Text = ""; left.Text = ""; when.Text = ""; workLeft.Text = ""; return; }
        what.ClearValue(TextBlock.ForegroundProperty);
        var now = model.Engine.Now; var due = DayNotes.DueAt(note); var remaining = due - now; var late = remaining < TimeSpan.Zero; var span = late ? -remaining : remaining;
        // A countdown reads as hours and minutes spelled out, so it cannot be mistaken for a time of day.
        var text = $"{(int)span.TotalHours}h {span.Minutes:00}m";
        what.Text = NoteShorthand.Display(note, model.AccountNames); phrase.Text = late ? "Overdue by" : "Due in";
        left.Text = late ? "-" + text : text;
        when.Text = due.ToString("ddd d MMM, HH:mm", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        if (form != TileForm.Roomy) return;
        // More context for the room: the working time that is actually left before it falls due, over the working days that remain.
        var days = 0; for (var day = now.Date; day <= due.Date; day = day.AddDays(1)) if (model.Engine.Schedule.Scheduled(day).Working) days++;
        workLeft.Text = late ? "" : $"Working time left: {model.Format(model.Engine.Schedule.WorkingTimeBetween(now, due))} over {days} working day{(days == 1 ? "" : "s")}";
    }
}


/// <summary>What the Day tile works out from the day it shows: how much of the expected working day has gone onto accounts, and the reconciliation behind it.</summary>
public sealed record DayFigures(TimeSpan Allocated, TimeSpan Unallocated, TimeSpan Target, TimeSpan Breaks, TimeSpan Overtime)
{
    public TimeSpan Left => Target > Allocated ? Target - Allocated : TimeSpan.Zero;
    /// <summary>The share of the working day recorded on accounts, 0 to 1, or null on a day with no working hours.</summary>
    public double? Fraction => Target > TimeSpan.Zero ? Math.Clamp(Allocated.TotalSeconds / Target.TotalSeconds, 0, 1) : null;
    public static DayFigures For(TimerModel model)
    {
        // Today is the working day now belongs to, which after midnight in an overnight shift is yesterday's, so the figures carry on across midnight.
        var engine = model.Engine; var day = model.SelectedDate.Date; var key = day == engine.Now.Date ? engine.CurrentKey : day;
        var totals = engine.ShiftTotals(key); var plan = engine.Plan(key);
        var (from, until) = engine.ShiftWindow(key); var breaks = engine.CreditedIn(BreakAccount.Name, from, until);
        return new(totals.Allocated, totals.Unallocated, plan.Working ? plan.End - plan.Start : TimeSpan.Zero, breaks, totals.Overtime);
    }
}

/// <summary>A thin bar for the working day: time on accounts, then the unallocated time, against what is left of the target.</summary>
public sealed class DayProgress : Control
{
    public double Recorded { get; set; } public double Unrecorded { get; set; }
    public DayProgress() { Name = "DayProgress"; Height = 12; ActualThemeVariantChanged += (_, _) => InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width; var h = Bounds.Height; if (w < 8) return;
        var track = new SolidColorBrush(Ui.ThemeColour(this, "Line", Colors.LightGray), .7);
        context.DrawRectangle(track, null, new Rect(0, 0, w, h), h / 2, h / 2);
        var a = Math.Clamp(Recorded, 0, 1) * w; var u = Math.Clamp(Unrecorded, 0, 1 - Math.Clamp(Recorded, 0, 1)) * w;
        if (a + u > 1) context.DrawRectangle(new SolidColorBrush(Color.Parse("#E3D3B0")), null, new Rect(0, 0, a + u, h), h / 2, h / 2);
        if (a > 1) context.DrawRectangle(new SolidColorBrush(Color.Parse("#8FB3E8")), null, new Rect(0, 0, a, h), h / 2, h / 2);
    }
}

/// <summary>
/// The Day tile answers one question: how much of the working day is done, and how much is left to put on accounts. Slim, it is a bar with the figures
/// beside it; standard, the figure large with its bar and what is left; roomy, the reconciliation too (unallocated time, breaks, overtime).
/// This is the day measured against its target, where the Time tracker is about the moment: how long, and what is recording now.
/// </summary>
public sealed class DayView : Grid
{
    private readonly TimerModel model; private readonly TileForm form; private readonly bool slim;
    private readonly TextBlock done = new() { Name = "DayDone", FontWeight = FontWeight.Bold, Classes = { "clock" } }, of = Ui.Text("", 13, "muted"), left = Ui.Text("", 13);
    private readonly DayProgress bar = new(); private readonly StackPanel details = new() { Spacing = 2 };
    // Start day and Finish day live here, with the day's progress, since the desk has no left column to hold them.
    private readonly Button dayButton; private readonly IconGlyph dayFlag = new(IconKind.Flag); private readonly TextBlock dayText = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
    public DayView(TimerModel model, TileForm form, bool slim = false)
    {
        this.model = model; this.form = form; this.slim = slim; VerticalAlignment = VerticalAlignment.Center;
        dayButton = Ui.Button("", model.ToggleDay, "DayStartFinish"); dayButton.Classes.Add("outlined"); dayButton.Classes.Add("quiet");
        dayButton.Content = Ui.Horizontal(dayFlag, dayText); dayButton.HorizontalAlignment = HorizontalAlignment.Left; dayButton.Height = 34; dayButton.Padding = new Thickness(12, 0);
        dayButton.PointerEntered += (_, _) => dayFlag.Waving = true; dayButton.PointerExited += (_, _) => dayFlag.Waving = false;
        if (slim)
        {
            // One row high there is only room for the flag, with the words as its tooltip.
            dayText.IsVisible = false; dayButton.Width = 34; dayButton.Padding = new Thickness(0); dayButton.HorizontalContentAlignment = HorizontalAlignment.Center; dayButton.Margin = new Thickness(10, 0, 0, 0);
            done.FontSize = 26; bar.Width = form == TileForm.Tiny ? 70 : form == TileForm.Roomy ? 140 : 100; bar.VerticalAlignment = VerticalAlignment.Center; of.VerticalAlignment = VerticalAlignment.Center;
            var row = Ui.Horizontal(bar, done); if (form == TileForm.Roomy) row.Children.Add(of); row.Children.Add(dayButton); row.Spacing = 10; HorizontalAlignment = HorizontalAlignment.Right; Children.Add(row); Refresh(); return;
        }
        RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"); done.FontSize = form == TileForm.Roomy ? 46 : 40;
        var top = Ui.Horizontal(done, of); top.Spacing = 10; top.VerticalAlignment = VerticalAlignment.Bottom; Children.Add(top);
        bar.Margin = new Thickness(0, 8, 0, 6); SetRow(bar, 1); Children.Add(bar); SetRow(left, 2); Children.Add(left);
        if (form == TileForm.Roomy) { details.Margin = new Thickness(0, 8, 0, 0); SetRow(details, 3); Children.Add(details); }
        dayButton.Margin = new Thickness(0, 10, 0, 0); SetRow(dayButton, 4); Children.Add(dayButton);
        Refresh();
    }
    public void Refresh()
    {
        // The same flag as the pill's button: green to start the day, red to finish it, so ending it is never done by accident.
        dayText.Text = model.DayStarted ? "Finish day" : "Start day"; dayFlag.TintKey = model.DayStarted ? "Finish" : "Start"; dayFlag.InvalidateVisual();
        dayButton.Classes.Set("finishDay", model.DayStarted); dayButton.Classes.Set("startDay", !model.DayStarted);
        ToolTip.SetTip(dayButton, dayText.Text);
        var f = DayFigures.For(model);
        done.Text = model.Format(f.Allocated); of.Text = f.Target > TimeSpan.Zero ? "of " + model.Format(f.Target) : "no working hours today";
        bar.Recorded = f.Target > TimeSpan.Zero ? f.Allocated.TotalSeconds / f.Target.TotalSeconds : 0;
        bar.Unrecorded = f.Target > TimeSpan.Zero ? f.Unallocated.TotalSeconds / f.Target.TotalSeconds : 0; bar.InvalidateVisual();
        if (slim) return;
        left.Text = f.Target > TimeSpan.Zero ? (f.Left > TimeSpan.Zero ? model.Format(f.Left) + " still to put on accounts" : "Working day filled") : "Time tracked on a day off counts as it is recorded";
        if (form != TileForm.Roomy) return;
        details.Children.Clear();
        void Line(string label, TimeSpan value, string name)
        {
            var row = Ui.Grid("*,Auto"); row.Name = name; Ui.Put(row, Ui.Text(label, 13, "muted"));
            var v = Ui.Text(model.Format(value), 13, "figures"); v.FontWeight = FontWeight.SemiBold; Ui.Put(row, v, 1); details.Children.Add(row);
        }
        // How much of the day's working time is still ahead, to its scheduled end, apart from how much is still to be put on accounts.
        var now = model.Engine.Now; var plan = model.Engine.Plan(model.SelectedDate.Date == now.Date ? model.Engine.CurrentKey : model.SelectedDate.Date);
        var ahead = plan.Working && model.SelectedDate.Date == now.Date && now < plan.End ? model.Engine.Schedule.WorkingTimeBetween(now, plan.End) : TimeSpan.Zero;
        Line("Working time left", ahead, "DayWorkLeft");
        Line("Unallocated", f.Unallocated, "DayUnallocated"); Line("Breaks", f.Breaks, "DayBreaks"); Line("Overtime", f.Overtime, "DayOvertime");
    }
}

/// <summary>
/// The accounts at their smallest. In one row beside the title it is a line of coloured chips with each account's name, shortened where needed, and today's time;
/// one cell wide it shows the account being recorded (or the selected one) with how many there are, and a click moves to the next.
/// </summary>
public sealed class AccountChips : WrapPanel
{
    private readonly TimerModel model; private readonly bool single; private int shown;
    public AccountChips(TimerModel model, bool single = false)
    {
        this.model = model; this.single = single; Orientation = Orientation.Horizontal; MaxHeight = 62; Name = "AccountChips";
        if (single) { Cursor = new Cursor(StandardCursorType.Hand); Background = Brushes.Transparent; PointerPressed += (_, _) => { shown++; Refresh(); }; ToolTip.SetTip(this, "Click for the next account"); }
        Refresh();
    }
    private string Short(string name) { var room = single ? 8 : 11; return name.Length <= room ? name : name[..(room - 1)] + "…"; }
    public void Refresh()
    {
        Children.Clear();
        var accounts = model.Accounts.Where(a => !BreakAccount.Is(a.Name)).ToList(); if (accounts.Count == 0) return;
        if (single)
        {
            // The running account first, then whichever the pointer has stepped on to.
            var start = Math.Max(0, accounts.FindIndex(a => a.Name == (model.ActiveAccount ?? model.SelectedAccount))); var account = accounts[(start + shown) % accounts.Count];
            var line = Chip(account, true); var count = Ui.Text($"{(accounts.IndexOf(account) + 1)} of {accounts.Count}", 11, "muted"); count.Margin = new Thickness(0, 0, 0, 0);
            var stack = new StackPanel { Spacing = 0 }; stack.Children.Add(line); stack.Children.Add(count); Children.Add(stack); return;
        }
        foreach (var account in accounts) Children.Add(Chip(account, false));
    }
    private Control Chip(TimerAccount account, bool large)
    {
        var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Brush.Parse(Ui.Visible(account.Colour)), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = Short(account.Display), FontSize = large ? 12 : 13, VerticalAlignment = VerticalAlignment.Center };
        var time = Ui.Text(model.Format(model.Today(account)), 13, "figures"); time.FontWeight = FontWeight.Bold; time.VerticalAlignment = VerticalAlignment.Center;
        var chip = Ui.Horizontal(dot, name, time); chip.Spacing = 6; chip.Margin = new Thickness(0, 0, 14, 4);
        if (large) name.FontWeight = FontWeight.SemiBold;
        return chip;
    }
}

/// <summary>Text in an account's colour for its #tag: the colour moved towards the text colour until it holds up on the surface, in a dark theme as well as a light one.</summary>
internal static class TagColours
{
    public static Color Text(Color account, TimerModel model)
    {
        // The theme in use, not a control's resources: the tag is built before it is in a window, where those cannot be found yet and a dark theme would read as light.
        var theme = model.CurrentTheme; var ink = Color.Parse(theme.Colour("Ink")); var surface = Color.Parse(theme.Colour("Surface"));
        string Hex(Color c) => ThemeColours.Hex(c.R, c.G, c.B);
        for (var amount = .45; amount <= 1.0001; amount += .05)
        {
            var candidate = Color.FromRgb((byte)(account.R + (ink.R - account.R) * amount), (byte)(account.G + (ink.G - account.G) * amount), (byte)(account.B + (ink.B - account.B) * amount));
            // Tiles are washes a little darker or lighter than the surface, so the tag is asked to do better than 4.5 to 1 against the surface itself.
            if (ThemeColours.Contrast(Hex(candidate), Hex(surface)) >= 6) return candidate;
        }
        return ink;
    }
}
