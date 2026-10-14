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
    public static TextBox Build(TimerModel model, int index, bool oneLine, string name = "PostItText")
    {
        var box = new TextBox
        {
            Name = name, Text = model.Trinkets.PostIts.ElementAtOrDefault(index)?.Text ?? "", AcceptsReturn = !oneLine, TextWrapping = oneLine ? TextWrapping.NoWrap : TextWrapping.Wrap,
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

/// <summary>Notes for the day the dashboard is showing. A time such as 14:30 typed in a note is kept as its time and makes it appear in Coming up.</summary>
public sealed class NotesView : Grid
{
    private readonly TimerModel model; private readonly StackPanel rows = new() { Spacing = 4 };
    private readonly TextBox input = new() { Name = "NoteInput", Watermark = "Add a note: 14:30 sets a time, ! marks a deadline", Padding = new Thickness(10, 6) };
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
        rows.Children.Add(Ui.Text(model.SelectedDate.ToString("dddd d MMMM", british), 12, "muted"));
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
            var row = Ui.Grid("Auto,Auto,*,Auto"); row.VerticalAlignment = VerticalAlignment.Center;
            Ui.Put(row, tick);
            if (note.At is { } at) { var time = Ui.Text(at.ToString("hh\\:mm"), 12, "figures"); time.VerticalAlignment = VerticalAlignment.Center; time.Margin = new Thickness(0, 0, 4, 0); Ui.Put(row, time, 1); }
            Ui.Put(row, text, 2); Ui.Put(row, remove, 3);
            rows.Children.Add(row);
        }
    }
}

/// <summary>What is coming up: dated notes from today on, soonest first, each marked with when it falls.</summary>
public sealed class ComingUpView : StackPanel
{
    private readonly TimerModel model;
    public ComingUpView(TimerModel model) { this.model = model; Spacing = 4; Rebuild(); }
    public static string When(DateTime day, DateTime today)
        => (day.Date - today.Date).Days switch { 0 => "Today", 1 => "Tomorrow", < 7 => day.ToString("dddd", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), _ => day.ToString("ddd d MMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")) };
    public void Rebuild()
    {
        Children.Clear();
        var coming = model.ComingUp();
        if (coming.Count == 0) Children.Add(Ui.Text("Nothing coming up. Add a dated note in Notes.", 12, "muted"));
        foreach (var note in coming)
        {
            var id = note.Id;
            var tick = new CheckBox { Name = "ComingTick_" + id, IsChecked = false, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            tick.Click += (_, _) => model.ToggleDayNote(id);
            var when = Ui.Text(When(note.Day, model.Engine.Now) + (note.At is { } at ? " " + at.ToString("hh\\:mm") : ""), 12, "muted");
            var words = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center }; words.Children.Add(new TextBlock { Text = DayNotes.DisplayText(note), FontSize = 14, TextWrapping = TextWrapping.Wrap }); words.Children.Add(when);
            var row = Ui.Grid("Auto,*"); Ui.Put(row, tick); Ui.Put(row, words, 1); Children.Add(row);
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
    private readonly TextBlock time = new() { Name = "PomodoroTime", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center, Classes = { "clock" } };
    private readonly TextBlock phase = Ui.Text("", 12, "muted"); private readonly Button toggle;
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
        var buttons = Ui.Horizontal(toggle, reset); buttons.HorizontalAlignment = HorizontalAlignment.Center; buttons.Margin = new Thickness(0, 6, 0, 0);
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
        if (p.Completed > 0) phase.Text += " · " + p.Completed;
        toggle.Content = p.Running ? "Pause" : p.Phase == PomodoroPhase.Idle || p.Nudge ? "Start" : "Resume";
        ring.Fraction = p.Phase == PomodoroPhase.Idle ? 0 : p.Fraction(now); ring.Soft = p.Nudge; ring.InvalidateVisual();
    }
}

/// <summary>
/// The time tracker tile: the day clock, who is running and whether it is tracking, with round buttons to pause or carry on and to take a break.
/// These are the two controls that matter most while working, so they live on the tile rather than behind a page.
/// </summary>
public sealed class TrackerView : Grid
{
    private readonly TimerModel model; private readonly ClockText clock; private readonly TextBlock account = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border dot = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border chip = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock chipText = new() { FontSize = 12, FontWeight = FontWeight.SemiBold };
    private readonly PauseGlyph pauseGlyph = new(); private readonly BreakGlyph cup = new();
    public TrackerView(TimerModel model, bool compact)
    {
        this.model = model; clock = new ClockText(compact ? 34 : 44, compact ? 16 : 22); clock.Name = "TrackerClock";
        ColumnDefinitions = new ColumnDefinitions("*,Auto"); VerticalAlignment = VerticalAlignment.Center;
        chip.Child = chipText;
        var who = Ui.Horizontal(dot, account, chip); who.Spacing = 8;
        var left = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center }; left.Children.Add(clock);
        if (!compact) left.Children.Add(who);
        Children.Add(left);
        if (!compact)
        {
            Button Round(Control glyph, string name, string tip, Action click)
            {
                var button = Ui.Button("", click, name); button.Classes.Add("toolButton"); button.Classes.Add("quiet");
                button.Width = 48; button.Height = 48; button.CornerRadius = new CornerRadius(24); button.Padding = new Thickness(0);
                button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center;
                button.Content = new Viewbox { Width = 22, Height = 22, Child = glyph }; ToolTip.SetTip(button, tip); Avalonia.Automation.AutomationProperties.SetName(button, tip); return button;
            }
            var pause = Round(pauseGlyph, "TrackerPause", "Pause or carry on", model.TogglePause);
            var rest = Round(cup, "TrackerBreak", "Take a break", model.ToggleBreak);
            pause.PointerEntered += (_, _) => pauseGlyph.Glow = 1; pause.PointerExited += (_, _) => pauseGlyph.Glow = 0;
            rest.PointerEntered += (_, _) => cup.SetSteaming(true); rest.PointerExited += (_, _) => cup.SetSteaming(model.IsOnBreak);
            var buttons = Ui.Horizontal(pause, rest); buttons.Spacing = 10; buttons.VerticalAlignment = VerticalAlignment.Center; SetColumn(buttons, 1); Children.Add(buttons);
        }
        Refresh();
    }
    public void Refresh()
    {
        var onBreak = model.IsOnBreak; var late = model.IsOvertime && !onBreak; var live = model.IsTracking && !onBreak && !late;
        clock.Text = model.ClockText; clock.SetState(onBreak, late, live);
        account.Text = model.AccountLabel;
        var shown = onBreak ? null : model.Accounts.FirstOrDefault(a => a.Name == (model.ActiveAccount ?? model.SelectedAccount));
        dot.IsVisible = shown is not null; if (shown is not null) dot.Background = Brush.Parse(Ui.Visible(shown.Colour));
        // Green says tracking and amber says a break, as everywhere else; anything else is a quiet grey chip.
        var key = onBreak ? "Break" : model.IsTracking ? "Live" : "Muted";
        var colour = Ui.ThemeColour(this, key, Colors.Gray);
        chip.Background = new SolidColorBrush(colour, .18); chipText.Foreground = new SolidColorBrush(colour);
        chipText.Text = onBreak ? "On break" : model.IsTracking ? "Tracking" : "Paused";
        pauseGlyph.Playing = model.IsTracking && !onBreak; pauseGlyph.InvalidateVisual();
        cup.Filled = onBreak; cup.SetSteaming(onBreak); cup.InvalidateVisual();
    }
}

/// <summary>The week as bars, one for each working day: time on accounts with the unallocated time stacked on top, and the day being shown in bold.</summary>
public sealed class WeekChart : Control
{
    private readonly TimerModel model;
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
    public override void Render(DrawingContext context)
    {
        var days = Days(); if (days.Count == 0 || Bounds.Width < 60) return;
        var ink = Ui.ThemeColour(this, "Ink", Colors.Black); var muted = new SolidColorBrush(Ui.ThemeColour(this, "Muted", Colors.Gray));
        var allocated = new SolidColorBrush(Color.Parse("#8FB3E8")); var unallocated = new SolidColorBrush(Color.Parse("#E3D3B0"));
        var top = days.Max(d => (d.Allocated + d.Unallocated).TotalHours); top = Math.Max(top, 8); // A light week does not stretch to fill the tile.
        var labelRoom = 20.0; var area = Math.Max(20, Bounds.Height - labelRoom); var slot = Bounds.Width / days.Count; var barWidth = Math.Min(44, slot * .6);
        foreach (var (i, d) in days.Select((d, i) => (i, d)))
        {
            var x = i * slot + (slot - barWidth) / 2; var a = d.Allocated.TotalHours / top * area; var u = d.Unallocated.TotalHours / top * area;
            if (u > 0.5) context.DrawRectangle(unallocated, null, new Rect(x, area - a - u, barWidth, u), 5, 5);
            if (a > 0.5) context.DrawRectangle(allocated, null, new Rect(x, area - a, barWidth, a), 5, 5);
            var current = d.Day == model.SelectedDate.Date;
            var label = new FormattedText(d.Day.ToString("ddd", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, current ? FontWeight.Bold : FontWeight.Normal), 12, current ? new SolidColorBrush(ink) : muted);
            context.DrawText(label, new Point(x + (barWidth - label.Width) / 2, area + 4));
        }
    }
}

/// <summary>A countdown to the next deadline, a note starting with !. It counts in hours and minutes, and says so plainly when the time has gone.</summary>
public sealed class DeadlineView : StackPanel
{
    private readonly TimerModel model;
    private readonly TextBlock what = new() { Name = "DeadlineWhat", FontSize = 14, FontWeight = FontWeight.SemiBold }, phrase = Ui.Text("", 12, "muted"), left = new() { Name = "DeadlineLeft", FontSize = 40, Classes = { "clock" } }, when = Ui.Text("", 13);
    public DeadlineView(TimerModel model, bool compact)
    {
        this.model = model; Spacing = 2; VerticalAlignment = VerticalAlignment.Center;
        what.TextTrimming = TextTrimming.CharacterEllipsis; left.FontSize = compact ? 30 : 40;
        Children.Add(what); Children.Add(phrase); Children.Add(left); if (!compact) Children.Add(when);
        Refresh();
    }
    public void Refresh()
    {
        var note = DayNotes.NextDeadline(model.AllDayNotes);
        if (note is null) { what.Text = "No deadlines"; phrase.Text = ""; left.Text = "--:--"; when.Text = "Start a note with ! to add one."; return; }
        var due = DayNotes.DueAt(note); var remaining = due - model.Engine.Now; var late = remaining < TimeSpan.Zero; var span = late ? -remaining : remaining;
        var hours = (int)span.TotalHours; var text = $"{hours:00}:{span.Minutes:00}";
        what.Text = DayNotes.DisplayText(note); phrase.Text = late ? "Overdue by" : "Due in";
        left.Text = late ? "-" + text : text;
        when.Text = due.ToString("ddd d MMM, HH:mm", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
    }
}
