using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

/// <summary>
/// The Details card under the timeline: it says what is selected and changes it, with a chip beside the title naming what it is showing. One block shows its account, start,
/// end, length and note, which are edited here (they follow each other: type a length and the end moves). A block in a stack (three or more at once) also shows the whole
/// stack as a timeline of its own, where any block is a press away and the picked one's ends can be dragged. A merged block shows what it joins and can be unmerged. Two or
/// more blocks show a row each, what Merge would do, and Merge, Copy and Delete. A meeting, or a stack of meetings, is edited here too (see SelectedBlockCard.Meeting.cs).
/// Nothing selected says how to select. It is rebuilt only when what it shows changes, so what is being typed is not lost to the clock ticking. The class keeps the name it had
/// as the Selected block card.
/// </summary>
public sealed partial class SelectedBlockCard : Border
{
    private readonly TimerModel model;
    private string? shown;
    private TimerModel.SelectedBlock? current;
    public SelectedBlockCard(TimerModel model)
    {
        this.model = model; Name = "SelectedBlockCard";
        CornerRadius = new CornerRadius(18); BorderThickness = new Thickness(1); Padding = new Thickness(18, 14, 18, 16);
        Classes.Add("dayCard");
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed;   Sync(); };
        DetachedFromVisualTree += (_, _) => { model.PropertyChanged -= Changed;   };
        Sync();
        activity = new ViewActivity(this, active => { if (active) Sync(); });
    }
    private readonly ViewActivity activity;
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (activity.Active && !model.TimelineGestureActive) Sync(); }

    private string Signature(IReadOnlyList<TimerModel.SelectedBlock> blocks)
        => string.Join("|", blocks.Select(b => $"{b.Account};{b.Start.Ticks};{b.End.Ticks};{b.Minutes};{b.Note};{b.Group?.Sources.Count}"))
            + $"#{model.EditError}#{model.CanMergeSelection}#{model.SelectedDate:yyyyMMdd}#{model.ReviewAccounts.Count}";

    /// <summary>Redraws the card if what it shows has changed since it was last drawn.</summary>
    public void Sync()
    {
        using var timing = PerformanceTimings.Measure("details sync");
        if (SyncSharedRecord()) return;
        var blocks = model.SelectedBlocks;
        // The stack the one selected block belongs to (three or more at once), which the card draws as a timeline so any of them is a press away.
        var items = blocks.Count == 1 ? ReviewViews.BlockStackOf(model, blocks[0].Rows) : null;
        var signature = Signature(blocks) + "#" + (items is null ? "" : string.Join("|", items.Select(o => $"{o.Account};{o.Start.Ticks};{o.End.Ticks};{o.Duration.Ticks};{o.Recording};{!o.Recording && model.IsSelected(o.Sources)}")));
        if (signature == shown) return;
        shown = signature; current = null; stackHost = null;
        Child = blocks.Count == 0 ? Empty() : blocks.Count == 1 ? Single(blocks[0], items) : Several(blocks);
        ShowStack(items);
    }

    // The timeline of the stack, which sits first in a single block's card. It is filled in place, so a box being typed in is not rebuilt under the cursor.
    private Border? stackHost; private TextBlock? kindText;
    private void ShowStack(IReadOnlyList<ReviewViews.StackItem>? items)
    {
        if (kindText is not null && current is not null) kindText.Text = items is null ? "Block" : StackKind(items);
        if (stackHost is null) return;
        stackHost.Child = null; stackHost.IsVisible = items is not null;
        if (items is null) return;
        var bars = new List<StackTimeline.Bar>();
        foreach (var item in items)
        {
            var colour = model.ReviewAccounts.FirstOrDefault(a => a.Name.Equals(item.Account, StringComparison.OrdinalIgnoreCase))?.Colour ?? "#91B7D5"; var look = model.BlockLook(colour);
            var isBreak = BreakAccount.Is(item.Account);
            var fill = Color.Parse(isBreak ? model.CurrentTheme.Colour("Hover") : look.Fill); var ink = isBreak ? model.CurrentTheme.Colour("Muted") : look.Text;
            var display = model.Label(item.Account); var picked = !item.Recording && model.IsSelected(item.Sources);
            var editable = picked && item.Sources.Count == 1 && !model.SelectedBlocks[0].IsGroup;
            bars.Add(new StackTimeline.Bar(item.Start.ToString("HHmmss") + "_" + bars.Count, "", model.Format(item.Duration), $"{display} · {item.Start:HH:mm}–{(item.Recording ? "now" : item.End.ToString("HH:mm"))} · {model.Format(item.Duration)}",
                item.Start, item.End, fill, Color.Parse(ink), picked, item.Recording, editable, ReviewViews.InitialsChip(display, ink, compact: true, fill: isBreak ? model.CurrentTheme.Colour("Hover") : look.Fill)));
        }
        var timeline = new StackTimeline(model, bars, 1);
        timeline.PickedBar += bar => { var item = items[bars.IndexOf(bar)]; if (!bar.Picked) model.SelectBlock(item.Sources, false); };
        timeline.Resized += (bar, start, end) =>
        {
            var item = items[bars.IndexOf(bar)]; if (item.Sources.Count != 1) return;
            var row = item.Sources[0]; model.EditEntry(row, row.Account, start, end, model.Editor.Note(row));
        };
        // A long stack scrolls inside the card, so the boxes under it keep their place.
        timeline.Margin = new Thickness(3, 0, 3, 4); // Room for the rings, which reach 2 pixels past a bar and would be cut at the scroll area's edge.
        stackHost.Child = new ScrollViewer { Content = timeline, MaxHeight = 160, Name = "StackScroll", HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0) };
    }
    private static string StackKind(IReadOnlyList<ReviewViews.StackItem> items) => "Blocks"; // No counts or notes of what is recording: the timeline under it shows them.

    private static TextBlock Label(string text)
    {
        var label = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = .5 }; label.Classes.Add("muted"); return label;
    }
    private static Border Chip(string text, bool plain = false)
    {
        var chip = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), Child = new TextBlock { Text = text, FontSize = 12 } };
        chip.Classes.Add("chip"); if (plain) chip.Classes.Add("plainChip"); return chip;
    }
    /// <summary>The account choice: a chip for each account, led by its colour, with the chosen one lit.</summary>
    private ChoiceStrip AccountStrip(string name, IReadOnlyList<string> names) => new()
    {
        Name = name, Look = ChoiceLook.Dropdown, ItemsSource = names,
        DotFor = shown => shown == NoAccount || BreakAccount.Is(BreakAccount.Stored(shown)) ? null : model.ReviewAccounts.FirstOrDefault(a => a.Name.Equals(shown, StringComparison.OrdinalIgnoreCase)) is { } found ? Ui.Visible(found.Colour) : null,
    };
    private Control Swatch(string account, double size = 14)
    {
        var colour = model.ReviewAccounts.FirstOrDefault(a => a.Name.Equals(account, StringComparison.OrdinalIgnoreCase))?.Colour ?? "#91B7D5";
        var look = model.BlockLook(colour);
        Brush fill = BreakAccount.Is(account) ? ReviewViews.Hatch(model)
            : new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(look.Fill), 0), new GradientStop(Color.Parse(ReviewViews.DeeperEnd(look)), 1) } };
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = fill, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
    }
    // The title is always Details; the chip beside it says what the card is showing (Block, Blocks · 5, Meeting, Meetings · 4 and so on), and is left out when nothing is.
    private (Grid Header, StackPanel Actions) Header(string? kind)
    {
        // A fixed height, so the title stays put whether or not there are buttons beside it (one block selected, none, or several).
        var header = Ui.Grid("Auto,*,Auto"); header.RowDefinitions = new RowDefinitions("*"); header.Height = 30;
        var words = Ui.Horizontal(new TextBlock { Text = "Details", FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Name = "SelectedTitle" });
        words.VerticalAlignment = VerticalAlignment.Center; words.Spacing = 10; words.Height = 30; kindText = null; // The header's own height, so the title's place never depends on the chip beside it.
        if (kind is not null) { var chip = Chip(kind, true); chip.Name = "DetailsKind"; kindText = (TextBlock)chip.Child!; words.Children.Add(chip); }
        Ui.Put(header, words);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center }; Ui.Put(header, actions, 2);
        return (header, actions);
    }
    // No notes under the card: what a click or a refused merge needs to say goes to the toast.
    private Grid Page(Control top, Control body)
    {
        var page = Ui.Grid("*", "Auto,Auto,*");
        Ui.Put(page, top); body.Margin = new Thickness(0, 12, 0, 0); Ui.Put(page, body, 0, 1);
        return page;
    }

    private Control Empty()
    {
        var day = model.SelectedDate.Date;
        var anything = model.Engine.Sessions.Any(s => model.CountsOn(s, day) && s.Duration > TimeSpan.Zero) || (model.Engine.Active is not null && day == model.TodayKey);
        var when = day == model.TodayKey ? "today" : "on this day";
        var (header, _) = Header(null);
        var message = new TextBlock { Name = "SelectedEmpty", FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Text = anything ? "Nothing is selected." : $"Nothing is recorded yet {when}." };
        message.Classes.Add("muted");
        return Page(header, message);
    }

    private Control Single(TimerModel.SelectedBlock block, IReadOnlyList<ReviewViews.StackItem>? items)
    {
        var group = block.Group!;
        var (header, actions) = Header($"Merged · {group.Sources.Count(r => r.Duration > TimeSpan.Zero)} blocks");
        actions.Children.Add(Ui.Ctl("UnmergeSelected", "Unmerge", IconKind.Merge, "Unmerge", () => model.UngroupEntries(group.GroupId!), small: true));
        actions.Children.Add(Ui.Ctl("CopySelected", "Copy", IconKind.Copy, "Copy", () => model.CopySelection(), small: true, key: "Ctrl C"));
        actions.Children.Add(Ui.Ctl("DeleteSelected", "Delete", IconKind.Trash, "Delete", () => model.DeleteSelection(), small: true, key: "Del"));
        var body = new StackPanel { Spacing = 12 };
        stackHost = new Border { Name = "StackHost", IsVisible = false }; body.Children.Add(stackHost);
        var fields = Ui.Grid("1.7*,*,*,*"); fields.ColumnSpacing = 10;
        var names = model.ReviewAccounts.Select(a => a.Name).Append(block.Account).Distinct(StringComparer.OrdinalIgnoreCase).Select(BreakAccount.Display).ToList();
        var account = AccountStrip("SelectedAccount", names); account.IsEnabled = false; account.SelectedItem = BreakAccount.Display(block.Account);
        var start = Box("SelectedStart", TimeText.ShowClock(block.Start, block.Start.Date), false);
        var end = Box("SelectedEnd", TimeText.ShowClock(block.End, block.Start.Date), false);
        var length = new DurationBox(DurationUnit.Minutes, model, "Length") { Name = "SelectedLength", Height = 38, IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch }; length.SetDuration(block.End - block.Start);
        Field(fields, 0, "Account", account); Field(fields, 1, "Start", start); Field(fields, 2, "End", end); Field(fields, 3, "Length", length); body.Children.Add(fields);
        var joined = new StackPanel { Spacing = 5 }; joined.Children.Add(Label("Joined blocks"));
        var chips = new WrapPanel();
        foreach (var row in group.Sources.Where(r => r.Duration > TimeSpan.Zero).OrderBy(r => r.From))
        { var chip = Chip($"{row.From:HH:mm}–{row.To:HH:mm}", true); chip.Margin = new Thickness(0, 0, 6, 6); chips.Children.Add(chip); }
        joined.Children.Add(chips); body.Children.Add(joined);
        var note = new TextBox { Name = "SelectedNote", Text = block.Note, Height = 38, IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        var noteField = new StackPanel { Spacing = 5 }; noteField.Children.Add(Label("Entry note")); noteField.Children.Add(note); body.Children.Add(noteField);
        return Page(header, body);
    }
    private Control Several(IReadOnlyList<TimerModel.SelectedBlock> blocks)
    {
        var (header, actions) = Header($"{blocks.Count} blocks selected");
        actions.Children.Add(Ui.Ctl("ClearSelection", "Clear selection", null, "Clear selection", () => model.ClearSelection(), small: true, key: "Esc"));
        var body = new StackPanel { Spacing = 12 };
        var rows = new StackPanel { Spacing = 6 };
        foreach (var block in blocks)
        {
            var line = Ui.Grid("Auto,*,Auto,52"); line.ColumnSpacing = 10;
            Ui.Put(line, Swatch(block.Account));
            var name = new TextBlock { Text = model.Label(block.Account), FontSize = 13.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Ui.Put(line, name, 1);
            var span = new TextBlock { Text = $"{block.Start:HH:mm}–{block.End:HH:mm}", FontSize = 13, VerticalAlignment = VerticalAlignment.Center }; span.Classes.Add("muted"); Ui.Put(line, span, 2);
            var amount = new TextBlock { Text = model.Format(TimeSpan.FromMinutes((double)block.Minutes)), FontSize = 13, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Ui.Put(line, amount, 3);
            var card = new Border { Child = line, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6) }; card.Classes.Add("selectedRow");
            rows.Children.Add(card);
        }
        var rowsScroll = new ScrollViewer { Content = rows, MaxHeight = 128, Name = "SelectedRows" }; Ui.LeaveRoomForTrack(rowsScroll, rows);
        body.Children.Add(rowsScroll);
        // Merge is always pressable: when it cannot work, pressing it says why in the toast.
        var merge = Ui.Ctl("MergeSelected", "Merge", IconKind.Merge, null, () => model.MergeSelection(), small: true);
        merge.Classes.Set("accentText", model.CanMergeSelection); // A momentary key: its words light, no fill and no lamp.
        var buttons = Ui.Horizontal(merge,
            Ui.Ctl("CopySelected", "Copy", IconKind.Copy, "Copy", () => model.CopySelection(), small: true, key: "Ctrl C"),
            Ui.Ctl("DeleteSelected", "Delete", IconKind.Trash, "Delete", () => model.DeleteSelection(), small: true, key: "Del"));
        body.Children.Add(buttons);
        return Page(header, body);
    }

    private static TextBox Box(string name, string text, bool enabled) => new() { Name = name, Text = text, Height = 38, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
    private static void Field(Grid grid, int column, string label, Control control, Control? extra = null)
    {
        var stack = new StackPanel { Spacing = 5 };
        if (extra is null) stack.Children.Add(Label(label));
        else { var line = Ui.Grid("*,Auto"); line.Height = 18; var text = Label(label); text.VerticalAlignment = VerticalAlignment.Center; Ui.Put(line, text); extra.HorizontalAlignment = HorizontalAlignment.Right; Ui.Put(line, extra, 1); stack.Children.Add(line); }
        stack.Children.Add(control); Ui.Put(grid, stack, column);
    }
}
