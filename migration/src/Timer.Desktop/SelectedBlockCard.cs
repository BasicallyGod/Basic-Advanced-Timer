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
    // The one selected block as the card last saw it, and its boxes. An edit made here replaces the block's row, and the card is then brought up to date in place (see
    // Refresh) rather than rebuilt, so a box that has the focus, or that is about to get it, is still the live one and every commit reads the block as it is now.
    private TimerModel.SelectedBlock? current; private TextBox? startBox, endBox, lengthBox, noteBox; private ComboBox? accountBox; private int builtAccounts; private bool settingAccount;
    public SelectedBlockCard(TimerModel model)
    {
        this.model = model; Name = "SelectedBlockCard";
        CornerRadius = new CornerRadius(20); BorderThickness = new Thickness(1); Padding = new Thickness(18, 14, 18, 16);
        Classes.Add("dayCard");
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Sync(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
        Sync();
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync();

    /// <summary>
    /// Takes off the card's own chrome (its fill, edge and padding) so it can stand inside another card, as the Selected block tab of the Notes card does while the
    /// account panel is docked; put back when it returns to its place under the timeline.
    /// </summary>
    public void SetDocked(bool inTab)
    {
        if (this.inTab == inTab) return;
        this.inTab = inTab; shown = null; // The tab names it already, so the title is left out and the card is drawn again.
        Classes.Set("dayCard", !inTab);
        BorderThickness = inTab ? default : new Thickness(1); Padding = inTab ? default : new Thickness(18, 14, 18, 16); CornerRadius = inTab ? default : new CornerRadius(20);
        Sync();
    }
    private bool inTab;

    private string Signature(IReadOnlyList<TimerModel.SelectedBlock> blocks)
        => string.Join("|", blocks.Select(b => $"{b.Account};{b.Start.Ticks};{b.End.Ticks};{b.Minutes};{b.Note};{b.Group?.Sources.Count}"))
            + $"#{model.EditError}#{model.CanMergeSelection}#{model.SelectedDate:yyyyMMdd}#{model.ReviewAccounts.Count}";

    /// <summary>Redraws the card if what it shows has changed since it was last drawn.</summary>
    public void Sync()
    {
        var blocks = model.SelectedBlocks;
        // The blocks that overlap the selected one (three or more deep), which the card lists so any of them is a press away, folded into the timeline's +N chip or not.
        var overlaps = blocks.Count == 1 ? ReviewViews.OverlapsOfSelection(model) : Array.Empty<ReviewViews.OverlapItem>();
        var signature = Signature(blocks) + "#" + string.Join("|", overlaps.Select(o => $"{o.Account};{o.Duration.Ticks};{o.Selected};{o.Folded};{o.Sources.Count}"));
        if (signature == shown) return;
        if (Refresh(blocks)) { shown = signature; ShowOverlaps(overlaps); return; }
        shown = signature; current = null; overlapHost = null;
        Child = blocks.Count == 0 ? Empty() : blocks.Count == 1 ? Single(blocks[0]) : Several(blocks);
        ShowOverlaps(overlaps);
    }

    // The row of overlapping blocks, which sits first in a single block's card. It is filled in place, so a box being typed in is not rebuilt under the cursor.
    private StackPanel? overlapHost;
    private void ShowOverlaps(IReadOnlyList<ReviewViews.OverlapItem> items)
    {
        if (overlapHost is null) return;
        overlapHost.Children.Clear(); overlapHost.IsVisible = items.Count > 0;
        if (items.Count == 0) return;
        var folded = items.Count(i => i.Folded);
        var heading = Ui.Grid("*,Auto"); heading.Children.Add(new TextBlock { Text = items.Count + " overlapping blocks", FontSize = 11.5, Classes = { "muted" } });
        if (folded > 0) { var note = new TextBlock { Text = $"Dashed: folded into +{folded}", FontSize = 11.5, Classes = { "muted" } }; Ui.Put(heading, note, 1); }
        overlapHost.Children.Add(heading);
        var row = new WrapPanel { Orientation = Orientation.Horizontal, Name = "OverlapRow" };
        var ink = model.CurrentTheme.Colour("Ink"); var muted = Color.Parse(model.CurrentTheme.Colour("Muted"));
        var index = 0;
        foreach (var item in items)
        {
            var content = new Grid();
            // A block folded into the chip has a dashed edge, so the row says which ones the timeline is not drawing a lane for.
            if (item.Folded) content.Children.Add(new Avalonia.Controls.Shapes.Rectangle { RadiusX = 9, RadiusY = 9, StrokeThickness = 1, Stroke = new SolidColorBrush(muted), StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 2 },
                Margin = new Thickness(-6, 0, -10, 0), IsHitTestVisible = false });
            var line = Ui.Horizontal(ReviewViews.InitialsChip(model.Label(item.Account), ink), new TextBlock { Text = model.Format(item.Duration), FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }); line.Spacing = 7;
            content.Children.Add(line);
            var button = new Button { Name = "Overlap_" + index++, Content = content, Margin = new Thickness(0, 6, 6, 0) };
            button.Classes.Add("overlapSeg"); button.Classes.Set("on", item.Selected);
            Avalonia.Automation.AutomationProperties.SetName(button, $"{model.Label(item.Account)}, {model.Format(item.Duration)}" + (item.Folded ? ", folded" : ""));
            ToolTip.SetTip(button, model.Label(item.Account));
            var sources = item.Sources; button.Click += (_, _) => model.SelectBlock(sources, false);
            row.Children.Add(button);
        }
        overlapHost.Children.Add(row);
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
        // A fixed height, so the title stays put whether or not there are buttons beside it (one block selected, none, or several).
        var header = Ui.Grid("Auto,*,Auto"); header.RowDefinitions = new RowDefinitions("*"); header.Height = 30;
        var words = Ui.Horizontal(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Name = "SelectedTitle", IsVisible = !(inTab && title == "Selected block") });
        words.VerticalAlignment = VerticalAlignment.Center;
        foreach (var chip in chips) words.Children.Add(chip);
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
        var anything = model.Engine.Sessions.Any(s => s.Day == day && s.Duration > TimeSpan.Zero) || (model.Engine.Active is not null && day == model.Engine.Now.Date);
        var when = day == model.Engine.Now.Date ? "today" : "on this day";
        var (header, _) = Header("Selected block");
        var message = new TextBlock { Name = "SelectedEmpty", FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Text = anything ? "Nothing is selected. Click a block on the timeline." : $"Nothing is recorded yet {when}. Start the day, or press play on an account." };
        message.Classes.Add("muted");
        return Page(header, message);
    }

    private Control Single(TimerModel.SelectedBlock block)
    {
        var group = block.IsGroup; current = group ? null : block; builtAccounts = model.ReviewAccounts.Count;
        var (header, actions) = Header(group ? "Merged block" : "Selected block", group ? new[] { (Control)Chip($"Merged, {block.Group!.Sources.Count(r => r.Duration > TimeSpan.Zero)} blocks", true) } : Array.Empty<Control>());
        if (group) actions.Children.Add(Ui.Ctl("UnmergeSelected", "Unmerge", IconKind.Merge, "Unmerge", () => model.UngroupEntries(block.Group!.GroupId!), small: true));
        actions.Children.Add(Ui.Ctl("CopySelected", "Copy", IconKind.Copy, "Copy", () => model.CopySelection(), small: true, key: "Ctrl C"));
        actions.Children.Add(Ui.Ctl("DeleteSelected", "Delete", IconKind.Trash, "Delete", () => model.DeleteSelection(), small: true, key: "Del"));
        var day = block.Start.Date;
        var body = new StackPanel { Spacing = 12 };
        overlapHost = new StackPanel { Name = "OverlapHost", IsVisible = false }; body.Children.Add(overlapHost);
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
                if (settingAccount || current is not { } now || account.SelectedItem is not string chosen || chosen.Equals(BreakAccount.Display(now.Account), StringComparison.OrdinalIgnoreCase)) return;
                model.MoveEntries(now.Rows, BreakAccount.Stored(chosen));
            };
        var start = Box("SelectedStart", TimeText.ShowClock(block.Start, day), !group); var end = Box("SelectedEnd", TimeText.ShowClock(block.End, day), !group);
        var length = Box("SelectedLength", TimeText.ShowLength(block.End - block.Start), !group);
        Field(fields, 0, "Account", account); Field(fields, 1, "Start", start); Field(fields, 2, "End", end); Field(fields, 3, "Length", length);
        body.Children.Add(fields);
        if (!group)
        {
            void Commit(string which)
            {
                // The block as it is now: an earlier edit from this card has replaced its row, so the one this box was built with may be gone.
                if (current is not { Row: not null } block) return;
                var day = block.Start.Date;
                if (!TimeText.TryParseClock(start.Text, day, false, out var newStart)) { model.Refuse("Start: try 09:30"); return; }
                DateTime newEnd;
                if (which == "length")
                {
                    if (TimeText.ParseLength(length.Text) is not { } span) { model.Refuse("Length: try 1:30 or 90"); return; }
                    newEnd = newStart + span;
                }
                else if (!TimeText.TryParseClock(end.Text, day, true, out newEnd)) { model.Refuse("End: try 10:30"); return; }
                if (newEnd <= newStart) { model.Refuse("End must be after start"); return; }
                if (newEnd > day.AddDays(1)) { model.Refuse("Can’t pass midnight"); return; }
                if (newStart == block.Start && newEnd == block.End) { ShowBoxes(block, true); return; } // Nothing changed, so the boxes are only tidied (09:5 becomes 09:05).
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
        if (!group) Committing(note, () => { if (current is { Row: not null } now && (note.Text ?? "").Trim() != now.Note.Trim()) model.EditEntry(now.Row, now.Row.Account, now.Start, now.End, note.Text ?? ""); });
        if (!group) { startBox = start; endBox = end; lengthBox = length; noteBox = note; accountBox = account; }
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
        body.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 128, Name = "SelectedRows" });
        // Merge is always pressable: when it cannot work, pressing it says why in the toast.
        var merge = Ui.Ctl("MergeSelected", "Merge", IconKind.Merge, null, () => model.MergeSelection(), small: true);
        merge.Classes.Set("primary", model.CanMergeSelection);
        var buttons = Ui.Horizontal(merge,
            Ui.Ctl("CopySelected", "Copy", IconKind.Copy, "Copy", () => model.CopySelection(), small: true, key: "Ctrl C"),
            Ui.Ctl("DeleteSelected", "Delete", IconKind.Trash, "Delete", () => model.DeleteSelection(), small: true, key: "Del"));
        body.Children.Add(buttons);
        return Page(header, body);
    }

    /// <summary>
    /// Brings a card that already shows one block up to date with that block's new row, times and note, without rebuilding it. A box that has the focus keeps what is being
    /// typed in it, unless the edit was refused, when every box goes back to the saved values. Returns false when the card has to be rebuilt (another kind of card, a different
    /// list of accounts, or a different day).
    /// </summary>
    private bool Refresh(IReadOnlyList<TimerModel.SelectedBlock> blocks)
    {
        if (blocks.Count != 1 || blocks[0].IsGroup || current is not { } old || startBox is null || endBox is null || lengthBox is null || noteBox is null || accountBox is null
            ) return false;
        var block = blocks[0];
        if (block.Start.Date != old.Start.Date || model.ReviewAccounts.Count != builtAccounts) return false;
        if (accountBox.ItemsSource is not IEnumerable<string> names || !names.Any(n => n.Equals(BreakAccount.Display(block.Account), StringComparison.OrdinalIgnoreCase))) return false;
        current = block;
        ShowBoxes(block, model.EditError is not null);
        settingAccount = true;
        try { accountBox.SelectedItem = names.First(n => n.Equals(BreakAccount.Display(block.Account), StringComparison.OrdinalIgnoreCase)); }
        finally { settingAccount = false; }
        return true;
    }

    // Writes the block's values into the boxes. A box that has the focus is left alone (its owner is typing in it) unless every box is to go back to the saved values.
    private void ShowBoxes(TimerModel.SelectedBlock block, bool all)
    {
        void Set(TextBox? box, string text) { if (box is not null && (all || !box.IsFocused) && box.Text != text) box.Text = text; }
        Set(startBox, TimeText.ShowClock(block.Start, block.Start.Date)); Set(endBox, TimeText.ShowClock(block.End, block.Start.Date));
        Set(lengthBox, TimeText.ShowLength(block.End - block.Start)); Set(noteBox, block.Note);
    }

    private static TextBox Box(string name, string text, bool enabled) => new() { Name = name, Text = text, Height = 38, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
    private static void Field(Grid grid, int column, string label, Control control)
    {
        var stack = new StackPanel { Spacing = 5 }; stack.Children.Add(Label(label)); stack.Children.Add(control); Ui.Put(grid, stack, column);
    }
    /// <summary>Applies a box when Enter is pressed in it or when it loses the focus (once for the two together, until it is typed in again).</summary>
    private static void Committing(TextBox box, Action commit)
    {
        var done = false;
        void Once() { if (done) return; done = true; commit(); }
        box.LostFocus += (_, _) => Once();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Once(); e.Handled = true; } };
        box.GotFocus += (_, _) => done = false;
        // The same box serves one edit after another now, so typing in it again is a new edit even though it never lost the focus.
        box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) done = false; };
    }
}
