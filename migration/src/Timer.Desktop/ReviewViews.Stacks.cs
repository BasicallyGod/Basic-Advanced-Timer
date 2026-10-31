using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

// Three or more blocks (or meetings) at one moment are drawn as one tile for the whole stretch, whatever the zoom, so a busy morning is a calm shape instead of lanes too thin to
// read. The tile says what is in it and draws a thin line for each lane at the true time, in the account's colour; pressing it selects the first block (or meeting) and the Details
// card opens the stack as a timeline of its own, where each one is a press away and its ends can be dragged.
public static partial class ReviewViews
{
    /// <summary>What a stack tile on the canvas stands for, so a press on it is known not to be a press on empty space.</summary>
    internal sealed record StackTag(int Members);

    private static string Words(int count, string one, string many) => count == 1 ? "1 " + one : count + " " + many;

    /// <summary>A tile is never taller than this: on a day with no meetings the band is tall, and a tile that filled it would be mostly empty.</summary>
    public const double MaxTileHeight = 96;
    internal static double TileHeight(double bandHeight, bool compact) => Math.Min(compact ? bandHeight - LiveLane - LiveGap : bandHeight, MaxTileHeight);
    /// <summary>Where the recording block's lane sits over a stack: its gap above the tile, whatever height the tile has.</summary>
    internal static double LiveTop(double bandTop, double bandHeight) => bandTop + bandHeight - TileHeight(bandHeight, true) - LiveGap - LiveLane;

    /// <summary>
    /// The thin lines inside a tile, one row to a lane. They are drawn on whole device pixels (a row's height and the gap between rows are the same for every row at any Windows
    /// scale), because lines placed on fractions of a pixel came out with uneven gaps, as if spaced by eye.
    /// </summary>
    public sealed class StackLines : Control
    {
        public sealed record Line(double Left, double Right, int Row, Color Colour, bool Lit, bool Dim);
        /// <summary>The lines it draws, in order.</summary>
        public IReadOnlyList<Line> Lines => lines;
        private readonly IReadOnlyList<Line> lines; private readonly double top, room; private readonly int rows; private readonly Color outline;
        public StackLines(IReadOnlyList<Line> lines, double top, double room, int rows, Color outline) { this.lines = lines; this.top = top; this.room = room; this.rows = Math.Max(1, rows); this.outline = outline; IsHitTestVisible = false; }
        public override void Render(DrawingContext context)
        {
            var scale = this.GetVisualRoot()?.RenderScaling ?? 1.0;
            var roomPixels = Math.Floor(room * scale);
            var pitch = Math.Max(1, Math.Min(Math.Floor(roomPixels / rows), Math.Round(8 * scale)));
            var thick = Math.Min(Math.Max(2, Math.Floor(pitch / 2)), Math.Max(1, pitch - 1));
            var topPixels = Math.Round(top * scale);
            foreach (var line in lines)
            {
                var y = (topPixels + line.Row * pitch) / scale; var x = Math.Round(line.Left * scale) / scale;
                var rect = new Rect(x, y, Math.Max(2 / scale, Math.Round((line.Right - line.Left) * scale) / scale), thick / scale);
                if (line.Lit) context.DrawRectangle(new SolidColorBrush(outline), null, rect.Inflate(1 / scale), 2, 2);
                var colour = line.Dim ? Color.FromArgb((byte)(line.Colour.A * .55), line.Colour.R, line.Colour.G, line.Colour.B) : line.Colour;
                context.DrawRectangle(new SolidColorBrush(colour), null, rect, 1, 1);
            }
        }
    }

    // The tile's own text and lines. The lines sit under the title, one row for each lane the stack needs.
    private static void FillTile(Border card, double width, double height, string title, string? range, IReadOnlyList<StackLines.Line> lines, int rows, bool compact, Control? icon, Color outline)
    {
        var grid = new Grid { ClipToBounds = true };
        var heading = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(10, compact ? 3 : 7, 8, 0) };
        if (icon is not null) heading.Children.Add(icon);
        var words = Ui.Text(title, 13); words.FontWeight = FontWeight.SemiBold; words.Name = "StackTitle"; heading.Children.Add(words);
        if (range is not null && width >= 190) { var span = Ui.Text(range, 11, "muted"); span.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center; heading.Children.Add(span); }
        grid.Children.Add(heading);
        var top = compact ? 22.0 : 29.0; var room = height - top - (compact ? 4 : 8);
        grid.Children.Add(new StackLines(lines, top, room, rows, outline));
        card.Child = grid;
    }

    /// <summary>
    /// A tile's tooltip: a heading, then a row for each block or meeting with its times and length in columns, so the times line up and can be run down by eye (tabular figures
    /// keep the digits the same width).
    /// </summary>
    private static Control StackTip(string heading, IReadOnlyList<(string Name, string Times, string? Length)> rows, int more)
    {
        var features = new FontFeatureCollection { FontFeature.Parse("tnum") };
        var column = new StackPanel { Spacing = 6 };
        column.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold });
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 3 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto)); grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto)); grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = rows[i].Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220 };
            var times = new TextBlock { Text = rows[i].Times, FontFeatures = features }; times.Classes.Add("muted");
            Grid.SetRow(name, i); Grid.SetRow(times, i); Grid.SetColumn(times, 1); grid.Children.Add(name); grid.Children.Add(times);
            if (rows[i].Length is { } length) { var amount = new TextBlock { Text = length, FontFeatures = features, TextAlignment = TextAlignment.Right, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right }; Grid.SetRow(amount, i); Grid.SetColumn(amount, 2); grid.Children.Add(amount); }
        }
        column.Children.Add(grid);
        if (more > 0) { var rest = new TextBlock { Text = $"+{more} more" }; rest.Classes.Add("muted"); column.Children.Add(rest); }
        return column;
    }

    // A line's colour in a tile: the account's deeper edge colour, taken towards the text colour until it holds 3 to 1 on the tile's fill, so it reads on Light as well as on Dark.
    private static Color LineColour(TimerModel model, string account)
    {
        var tileFill = model.CurrentTheme.Colour("Hover"); var ink = model.CurrentTheme.Colour("Ink");
        if (BreakAccount.Is(account)) return Color.Parse(model.CurrentTheme.Colour("Muted"));
        var colour = model.ReviewAccounts.FirstOrDefault(a => a.Name.Equals(account, StringComparison.OrdinalIgnoreCase))?.Colour ?? "#91B7D5";
        var shade = model.BlockLook(colour).Edge;
        for (var amount = 0.0; amount < 1 && ThemeColours.Contrast(shade, tileFill) < 3; amount += .1) shade = ThemeColours.Mix(model.BlockLook(colour).Edge, ink, amount + .1);
        return Color.Parse(shade);
    }

    // A tile for a stack of blocks. With the recording block over it (compact), the tile is slimmer and sits under that block's lane.
    private static void AddBlockTile(Canvas canvas, TimerModel model, List<Interval> members, bool compact, double bandTop, double bandHeight, double pixelsPerHour, Func<DateTime, double> X)
    {
        var start = members.Min(i => i.Start); var day = start.Date; var end = members.Max(i => i.End); if (end > day.AddDays(1)) end = day.AddDays(1);
        var width = Math.Max(MinimumBlockWidth, (end - start).TotalHours * pixelsPerHour - BlockGap);
        var height = TileHeight(bandHeight, compact); var top = bandTop + bandHeight - height; // Sits on the foot of the band, where the blocks end.
        var total = members.Aggregate(TimeSpan.Zero, (sum, i) => sum + DurationOf(i));
        var placed = PlaceLanes(members.Select(i => (i, i.Start, i.End))); var rows = placed.Values.Max(v => v.Count);
        var selectedIn = members.Any(i => SourcesOf(i).Count > 0 && model.IsSelected(SourcesOf(i)));
        var lines = members.Select(i =>
        {
            var lit = selectedIn && SourcesOf(i).Count > 0 && model.IsSelected(SourcesOf(i));
            return new StackLines.Line(Math.Max(6, (i.Start - start).TotalHours * pixelsPerHour - BlockInset), Math.Min(width - 6, ((i.End < end ? i.End : end) - start).TotalHours * pixelsPerHour - BlockInset), placed[i].Lane, LineColour(model, i.Account), lit, selectedIn && !lit);
        }).ToList();
        var title = Words(members.Count, "block", "blocks") + " · " + model.Format(total);
        var card = new Border { Name = "TimelineStack" + start.ToString("HHmmss"), Width = width, Height = height, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), Tag = new StackTag(members.Count), Cursor = new Cursor(StandardCursorType.Hand) };
        card.Classes.Add("stackTile");
        FillTile(card, width, height, title, $"{start:HH:mm}–{end:HH:mm}", lines, rows, compact, null, Color.Parse(model.CurrentTheme.Colour("Ink")));
        var heading = $"{title} · {start:HH:mm}–{end:HH:mm}";
        ToolTip.SetTip(card, StackTip(heading, members.Take(8).Select(i => (model.Label(i.Account), $"{i.Start:HH:mm}–{i.End:HH:mm}", (string?)model.Format(DurationOf(i)))).ToList(), Math.Max(0, members.Count - 8)));
        Avalonia.Automation.AutomationProperties.SetName(card, $"{title}, {start:HH:mm} to {end:HH:mm}. Press to open them in Details");
        card.PointerEntered += (_, _) => card.Classes.Add("over"); card.PointerExited += (_, _) => card.Classes.Remove("over");
        if (selectedIn) { card.Classes.Add("mergeSelected"); card.BoxShadow = new BoxShadows(new BoxShadow { Spread = 3, Color = Color.Parse(model.CurrentTheme.Colour("Accent")) }); card.ZIndex = 3; }
        Point? press = null;
        card.PointerPressed += (_, e) => { if (e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) { press = e.GetPosition(canvas); e.Handled = true; } };
        card.PointerReleased += (_, e) =>
        {
            if (press is { } p && e.InitialPressMouseButton == MouseButton.Left && Math.Pow(e.GetPosition(canvas).X - p.X, 2) + Math.Pow(e.GetPosition(canvas).Y - p.Y, 2) <= 25)
            {
                // The first block of the stack is the one selected, and a press on a stack that is already open puts it away.
                if (selectedIn) model.ClearSelection(); else model.SelectBlock(SourcesOf(members.OrderBy(i => i.Start).First(i => SourcesOf(i).Count > 0)), false);
            }
            press = null; e.Handled = true;
        };
        Canvas.SetLeft(card, X(start) + BlockInset); Canvas.SetTop(card, top); canvas.Children.Add(card);
    }

    // A tile for a stack of meetings, the height of the meeting row.
    private static void AddMeetingTile(Canvas canvas, TimerModel model, DateTime day, List<DayNote> stack, double meetingTop, double pixelsPerHour, Func<DateTime, double> X)
    {
        var start = day + stack.Min(m => m.At!.Value); var end = day + stack.Max(m => m.End!.Value);
        var width = Math.Max(24, (end - start).TotalHours * pixelsPerHour - 2);
        var placed = PlaceLanes(stack.Select(m => (m, day + m.At!.Value, day + m.End!.Value))); var rows = placed.Values.Max(v => v.Count);
        var selectedIn = stack.Any(m => m.Id == model.SelectedMeetingId);
        var ink = Color.Parse(model.CurrentTheme.Colour("Ink"));
        var lines = stack.Select(m => new StackLines.Line(Math.Max(8, ((day + m.At!.Value) - start).TotalHours * pixelsPerHour), Math.Min(width - 8, ((day + m.End!.Value) - start).TotalHours * pixelsPerHour), placed[m].Lane,
            ink, selectedIn && m.Id == model.SelectedMeetingId, selectedIn && m.Id != model.SelectedMeetingId)).ToList();
        var title = Words(stack.Count, "meeting", "meetings");
        var card = new Border { Name = "TimelineMeetingStack" + start.ToString("HHmm"), Width = width, Height = MeetingHeight, CornerRadius = new CornerRadius(12), Tag = new StackTag(stack.Count), Cursor = new Cursor(StandardCursorType.Hand) };
        card.Classes.Add("meetingCard");
        FillTile(card, width, MeetingHeight, title, $"{start:HH:mm}–{end:HH:mm}", lines, rows, false, new IconGlyph(IconKind.Today) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, ink);
        ToolTip.SetTip(card, StackTip($"{title} · {start:HH:mm}–{end:HH:mm}", stack.Take(8).Select(m => (model.Notes.Text(m), $"{Hm(m.At)}–{Hm(m.End)}", (string?)null)).ToList(), Math.Max(0, stack.Count - 8)));
        Avalonia.Automation.AutomationProperties.SetName(card, $"{title}, {start:HH:mm} to {end:HH:mm}. Press to open them in Details");
        card.PointerEntered += (_, _) => card.Classes.Add("over"); card.PointerExited += (_, _) => card.Classes.Remove("over");
        if (selectedIn) { card.Classes.Add("mergeSelected"); card.BoxShadow = new BoxShadows(new BoxShadow { Spread = 3, Color = Color.Parse(model.CurrentTheme.Colour("Accent")) }); card.ZIndex = 3; }
        Point? press = null;
        card.PointerPressed += (_, e) => { if (e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) { press = e.GetPosition(canvas); e.Handled = true; } };
        card.PointerReleased += (_, e) =>
        {
            if (press is { } p && e.InitialPressMouseButton == MouseButton.Left && Math.Pow(e.GetPosition(canvas).X - p.X, 2) + Math.Pow(e.GetPosition(canvas).Y - p.Y, 2) <= 25)
            { if (selectedIn) model.ClearSelection(); else model.SelectMeeting(stack.OrderBy(m => m.At).First().Id); }
            press = null; e.Handled = true;
        };
        Canvas.SetLeft(card, X(start) + 1); Canvas.SetTop(card, meetingTop); canvas.Children.Add(card);
    }
}
