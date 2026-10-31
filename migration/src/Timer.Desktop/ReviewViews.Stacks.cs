using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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

    /// <summary>A tile is never taller than this: on a day with no meetings the band is tall, and a tile that filled it would be mostly empty.</summary>
    public const double MaxTileHeight = 96;
    internal static double TileHeight(double bandHeight, bool compact) => Math.Min(compact ? bandHeight - LiveLane - LiveGap : bandHeight, MaxTileHeight);
    /// <summary>Where the recording block's lane sits over a stack: its gap above the tile, whatever height the tile has.</summary>
    internal static double LiveTop(double bandTop, double bandHeight) => bandTop + bandHeight - TileHeight(bandHeight, true) - LiveGap - LiveLane;

    private static string Words(int count, string one, string many) => count == 1 ? "1 " + one : count + " " + many;

    // The tile's own text and lines. The lines sit under the title, one row for each lane the stack needs, shorter rows when the tile is slim.
    private static void FillTile(Border card, double width, double height, string title, string? range, IReadOnlyList<(double Left, double Right, int Row, Color Colour, bool Lit, bool Dim)> lines, int rows, bool compact, Control? icon)
    {
        var grid = new Grid { ClipToBounds = true };
        var heading = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(10, compact ? 3 : 7, 8, 0) };
        if (icon is not null) heading.Children.Add(icon);
        var words = Ui.Text(title, 13); words.FontWeight = FontWeight.SemiBold; words.Name = "StackTitle"; heading.Children.Add(words);
        if (range is not null && width >= 190) { var span = Ui.Text(range, 11, "muted"); span.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center; heading.Children.Add(span); }
        grid.Children.Add(heading);
        var top = compact ? 22.0 : 29.0; var room = height - top - (compact ? 4 : 8);
        var pitch = Math.Min(rows > 1 ? room / rows : room, 8); var thick = Math.Clamp(Math.Round(pitch * .55), 2, 4);
        var canvas = new Canvas { IsHitTestVisible = false };
        foreach (var line in lines)
        {
            var left = Math.Max(6, line.Left); var right = Math.Min(width - 6, line.Right);
            var bar = new Border { Width = Math.Max(2, right - left), Height = line.Lit ? thick + 2 : thick, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(line.Colour), Opacity = line.Dim ? .5 : 1, Tag = "StackLine" };
            if (line.Lit) { bar.BorderThickness = new Thickness(1); bar.BorderBrush = Brush.Parse("#FFFFFF"); bar.Name = "StackLineLit"; }
            Canvas.SetLeft(bar, left); Canvas.SetTop(bar, top + line.Row * pitch - (line.Lit ? 1 : 0)); canvas.Children.Add(bar);
        }
        grid.Children.Add(canvas);
        card.Child = grid;
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
            var colour = model.ReviewAccounts.FirstOrDefault(a => a.Name.Equals(i.Account, StringComparison.OrdinalIgnoreCase))?.Colour ?? "#91B7D5";
            var tint = BreakAccount.Is(i.Account) ? Color.Parse(model.CurrentTheme.Colour("Muted")) : Color.Parse(model.ChartColour(colour));
            var lit = selectedIn && SourcesOf(i).Count > 0 && model.IsSelected(SourcesOf(i));
            return ((i.Start - start).TotalHours * pixelsPerHour - BlockInset, ((i.End < end ? i.End : end) - start).TotalHours * pixelsPerHour - BlockInset, placed[i].Lane, tint, lit, selectedIn && !lit);
        }).ToList();
        var title = Words(members.Count, "block", "blocks") + " · " + model.Format(total);
        var card = new Border { Name = "TimelineStack" + start.ToString("HHmmss"), Width = width, Height = height, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), Tag = new StackTag(members.Count), Cursor = new Cursor(StandardCursorType.Hand) };
        card.Classes.Add("stackTile");
        FillTile(card, width, height, title, $"{start:HH:mm}–{end:HH:mm}", lines, rows, compact, null);
        var accounts = members.Select(i => model.Label(i.Account)).ToList();
        var tip = new List<string> { $"{title} · {start:HH:mm}–{end:HH:mm}" };
        tip.AddRange(members.Take(8).Select(i => $"{model.Label(i.Account)} · {i.Start:HH:mm}–{i.End:HH:mm} · {model.Format(DurationOf(i))}"));
        if (members.Count > 8) tip.Add($"+{members.Count - 8} more");
        ToolTip.SetTip(card, string.Join("\n", tip));
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
        var lines = stack.Select(m => ((double Left, double Right, int Row, Color Colour, bool Lit, bool Dim))(
            ((day + m.At!.Value) - start).TotalHours * pixelsPerHour, ((day + m.End!.Value) - start).TotalHours * pixelsPerHour, placed[m].Lane,
            Color.Parse(model.CurrentTheme.Colour("Ink")), selectedIn && m.Id == model.SelectedMeetingId, selectedIn && m.Id != model.SelectedMeetingId)).ToList();
        var title = Words(stack.Count, "meeting", "meetings");
        var card = new Border { Name = "TimelineMeetingStack" + start.ToString("HHmm"), Width = width, Height = MeetingHeight, CornerRadius = new CornerRadius(12), Tag = new StackTag(stack.Count), Cursor = new Cursor(StandardCursorType.Hand) };
        card.Classes.Add("meetingCard");
        FillTile(card, width, MeetingHeight, title, $"{start:HH:mm}–{end:HH:mm}", lines, rows, false, new IconGlyph(IconKind.Today) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        var tip = new List<string> { $"{title} · {start:HH:mm}–{end:HH:mm}" };
        tip.AddRange(stack.Take(8).Select(m => $"{model.Notes.Text(m)} · {Hm(m.At)}–{Hm(m.End)}"));
        if (stack.Count > 8) tip.Add($"+{stack.Count - 8} more");
        ToolTip.SetTip(card, string.Join("\n", tip));
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
