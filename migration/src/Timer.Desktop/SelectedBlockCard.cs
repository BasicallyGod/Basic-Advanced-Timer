using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The card under the timeline that says what is selected and changes it. One block shows its account, start, end, length and note, which are edited here
/// (they follow each other: type a length and the end moves). A merged block shows what it joins and can be unmerged. Two or more blocks show a row each,
/// what Merge would do, and Merge, Copy and Delete. Nothing selected says how to select. It is rebuilt only when what it shows changes, so what is
/// being typed is not lost to the clock ticking.
/// </summary>
public sealed class SelectedBlockCard : Border
{
    private readonly TimerModel model;
    private string? shown;
    public SelectedBlockCard(TimerModel model)
    {
        this.model = model; Name = "SelectedBlockCard";
        CornerRadius = new CornerRadius(20); BorderThickness = new Thickness(1); Padding = new Thickness(18, 16);
        Classes.Add("dayCard");
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Sync(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
        Sync();
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync();

    private string Signature(IReadOnlyList<TimerModel.SelectedBlock> blocks)
        => string.Join("|", blocks.Select(b => $"{b.Account};{b.Start.Ticks};{b.End.Ticks};{b.Minutes};{b.Note};{b.Group?.Sources.Count}"))
            + $"#{model.EditError}#{model.CanMergeSelection}#{model.SelectionNote}#{model.SelectedDate:yyyyMMdd}#{model.ReviewAccounts.Count}";

    /// <summary>Redraws the card if what it shows has changed since it was last drawn.</summary>
    public void Sync()
    {
        var blocks = model.SelectedBlocks;
        var signature = Signature(blocks);
        if (signature == shown) return;
        shown = signature;
        Child = blocks.Count == 0 ? Empty() : blocks.Count == 1 ? Single(blocks[0]) : Several(blocks);
    }

    private static TextBlock Label(string text)
    {
        var label = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeight.SemiBold, LetterSpacing = .5 }; label.Classes.Add("muted"); return label;
    }
    private static Border Chip(string text, bool plain = false)
    {
        var chip = new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), Child = new TextBlock { Text = text, FontSize = 12 } };
        chip.Classes.Add("chip"); if (plain) chip.Classes.Add("plainChip"); return chip;
    }
    private Control Swatch(string account, double size = 14)
    {
        var colour = model.ReviewAccounts.FirstOrDefault(a => a.Name.Equals(account, StringComparison.OrdinalIgnoreCase))?.Colour ?? "#91B7D5";
        var look = model.BlockLook(colour);
        Brush fill = BreakAccount.Is(account) ? ReviewViews.Hatch(model)
            : new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(look.Fill), 0), new GradientStop(Color.Parse(ReviewViews.DeeperEnd(look)), 1) } };
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = fill, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
    }
    private (Grid Header, StackPanel Actions) Header(string title, params Control[] chips)
    {
        var header = Ui.Grid("Auto,*,Auto"); header.MinHeight = 30;
        var words = Ui.Horizontal(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Name = "SelectedTitle" });
        foreach (var chip in chips) words.Children.Add(chip);
        Ui.Put(header, words);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center }; Ui.Put(header, actions, 2);
        return (header, actions);
    }
    private TextBlock Hint(string text) { var hint = new TextBlock { Name = "SelectedHint", Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Bottom }; hint.Classes.Add("muted"); return hint; }
    private Grid Page(Control top, Control body, string hint)
    {
        var page = Ui.Grid("*", "Auto,Auto,*,Auto"); page.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
        Ui.Put(page, top); body.Margin = new Thickness(0, 12, 0, 0); Ui.Put(page, body, 0, 1);
        var foot = Hint(hint); foot.Margin = new Thickness(0, 12, 0, 0); Ui.Put(page, foot, 0, 3);
        return page;
    }

    private Control Empty()
    {
        var day = model.SelectedDate.Date;
        var anything = model.Engine.Sessions.Any(s => s.Day == day && s.Duration > TimeSpan.Zero) || (model.Engine.Active is not null && day == model.Engine.Now.Date);
        var when = day == model.Engine.Now.Date ? "today" : "on this day";
        var (header, _) = Header("Selected block");
        var message = new TextBlock { Name = "SelectedEmpty", FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Text = anything ? "Nothing is selected. Click a block on the timeline." : $"Nothing is recorded yet {when}. Start the day, or press play on an account." };
        message.Classes.Add("muted");
        return Page(header, message, "Shift-click a second block to select both and merge them. Drag an edge to resize.");
    }

    private Control Single(TimerModel.SelectedBlock block)
    {
        var group = block.IsGroup;
        var (header, actions) = Header(group ? "Merged block" : "Selected block", group ? new[] { (Control)Chip($"Merged, {block.Group!.Sources.Count(r => r.Duration > TimeSpan.Zero)} blocks", true) } : Array.Empty<Control>());
        if (group) actions.Children.Add(Ui.Ctl("UnmergeSelected", "Unmerge", IconKind.Merge, "Put the blocks back as they were", () => model.UngroupEntries(block.Group!.GroupId!), small: true));
        actions.Children.Add(Ui.Ctl("CopySelected", "Copy", IconKind.Copy, "Copy the block (Ctrl+C), then paste it on the timeline (Ctrl+V)", () => model.CopySelection(), small: true));
        actions.Children.Add(Ui.Ctl("DeleteSelected", "Delete", IconKind.Trash, "Delete this block", () => model.DeleteSelection(), small: true));
        var day = block.Start.Date;
        var body = new StackPanel { Spacing = 12 };
        var fields = Ui.Grid("1.7*,*,*,*"); fields.ColumnSpacing = 10;
        // Account: one of the accounts, shown with its colour. A merged block's account and times are read-only.
        var names = model.ReviewAccounts.Select(a => a.Name).Append(block.Account).Distinct(StringComparer.OrdinalIgnoreCase).Select(BreakAccount.Display).ToList();
        if (!names.Contains(BreakAccount.Display(BreakAccount.Name))) names.Add(BreakAccount.Display(BreakAccount.Name));
        var account = new ComboBox { Name = "SelectedAccount", ItemsSource = names, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = !group, Height = 38,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) => Ui.Horizontal(Swatch(BreakAccount.Stored(name ?? "")), new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center })) };
        account.SelectedItem = names.FirstOrDefault(n => n.Equals(BreakAccount.Display(block.Account), StringComparison.OrdinalIgnoreCase));
        if (!group)
            account.SelectionChanged += (_, _) =>
            {
                if (account.SelectedItem is not string chosen || chosen.Equals(BreakAccount.Display(block.Account), StringComparison.OrdinalIgnoreCase)) return;
                model.MoveEntries(block.Rows, BreakAccount.Stored(chosen));
            };
        var start = Box("SelectedStart", TimeText.ShowClock(block.Start, day), !group); var end = Box("SelectedEnd", TimeText.ShowClock(block.End, day), !group);
        var length = Box("SelectedLength", TimeText.ShowLength(block.End - block.Start), !group);
        Field(fields, 0, "Account", account); Field(fields, 1, "Start", start); Field(fields, 2, "End", end); Field(fields, 3, "Length", length);
        body.Children.Add(fields);
        if (!group)
        {
            void Commit(string which)
            {
                if (!TimeText.TryParseClock(start.Text, day, false, out var newStart)) { model.Refuse("Start looks like 09:30."); return; }
                DateTime newEnd;
                if (which == "length")
                {
                    if (TimeText.ParseLength(length.Text) is not { } span) { model.Refuse("Length looks like 1:30, or 90 for minutes."); return; }
                    newEnd = newStart + span;
                }
                else if (!TimeText.TryParseClock(end.Text, day, true, out newEnd)) { model.Refuse("End looks like 10:30."); return; }
                if (newEnd <= newStart) { model.Refuse("End has to be after the start."); return; }
                if (newEnd > day.AddDays(1)) { model.Refuse("That runs past midnight."); return; }
                if (newStart == block.Start && newEnd == block.End) { shown = null; Sync(); return; }
                model.EditEntry(block.Row!, block.Row!.Account, newStart, newEnd, block.Note);
            }
            Committing(start, () => Commit("start")); Committing(end, () => Commit("end")); Committing(length, () => Commit("length"));
        }
        else
        {
            var joined = new StackPanel { Spacing = 5 }; joined.Children.Add(Label("Joined blocks"));
            var chips = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var row in block.Group!.Sources.Where(r => r.Duration > TimeSpan.Zero).OrderBy(r => r.From))
            { var chip = Chip($"{row.From:HH:mm}–{row.To:HH:mm}", true); chip.Margin = new Thickness(0, 0, 6, 6); chips.Children.Add(chip); }
            joined.Children.Add(chips); body.Children.Add(joined);
        }
        var note = new TextBox { Name = "SelectedNote", Text = block.Note, Height = 38, Watermark = group ? "" : "Add a note", IsEnabled = !group, HorizontalAlignment = HorizontalAlignment.Stretch };
        var noteField = new StackPanel { Spacing = 5 }; noteField.Children.Add(Label("Entry note")); noteField.Children.Add(note); body.Children.Add(noteField);
        if (!group) Committing(note, () => { if ((note.Text ?? "").Trim() != block.Note.Trim()) model.EditEntry(block.Row!, block.Row!.Account, block.Start, block.End, note.Text ?? ""); });
        return Page(header, body, "Shift-click a second block to select both and merge them. Drag an edge to resize.");
    }

    private Control Several(IReadOnlyList<TimerModel.SelectedBlock> blocks)
    {
        var (header, actions) = Header($"{blocks.Count} blocks selected");
        actions.Children.Add(Ui.Ctl("ClearSelection", "Clear selection", null, "Clear the selection (Esc)", () => model.ClearSelection(), small: true));
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
        body.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 128, Name = "SelectedRows" });
        var note = new TextBlock { Name = "SelectedNote", Text = model.SelectionNote ?? "", FontSize = 13, TextWrapping = TextWrapping.Wrap }; body.Children.Add(note);
        var merge = Ui.Ctl("MergeSelected", "Merge", IconKind.Merge, model.MergeHint, () => model.MergeSelection(), small: true);
        merge.IsEnabled = model.CanMergeSelection; merge.Classes.Set("primary", model.CanMergeSelection);
        var buttons = Ui.Horizontal(merge,
            Ui.Ctl("CopySelected", "Copy", IconKind.Copy, "Copy the blocks (Ctrl+C), then paste them on the timeline (Ctrl+V)", () => model.CopySelection(), small: true),
            Ui.Ctl("DeleteSelected", "Delete", IconKind.Trash, "Delete the selected blocks", () => model.DeleteSelection(), small: true));
        body.Children.Add(buttons);
        return Page(header, body, "Shift-click a selected block to take it out of the selection.");
    }

    private static TextBox Box(string name, string text, bool enabled) => new() { Name = name, Text = text, Height = 38, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
    private static void Field(Grid grid, int column, string label, Control control)
    {
        var stack = new StackPanel { Spacing = 5 }; stack.Children.Add(Label(label)); stack.Children.Add(control); Ui.Put(grid, stack, column);
    }
    /// <summary>Applies a box when Enter is pressed in it or when it loses the focus.</summary>
    private static void Committing(TextBox box, Action commit)
    {
        var done = false;
        void Once() { if (done) return; done = true; commit(); }
        box.LostFocus += (_, _) => Once();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Once(); e.Handled = true; } };
        box.GotFocus += (_, _) => done = false;
    }
}
