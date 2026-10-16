using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The Notes page: the notes for the day the dashboard is showing, with a box to add one, and what is coming up beside them. A note is typed in the
/// shorthand (14:30 sets a time, ! a deadline, @fri a day, #Account links an account) and each part it understood shows as a chip that can be taken back
/// out. The page is built once and listens to the model itself, so a tick of the clock never rebuilds it under the pointer, and while a note's words are
/// being edited it waits until the box is left, so typing is never interrupted.
/// </summary>
public sealed class NotesPage : Grid
{
    private readonly TimerModel model;
    private readonly StackPanel rows = new() { Spacing = 4 };
    private readonly ComingUpView coming;
    private readonly TextBlock heading = new() { FontSize = 16, FontWeight = FontWeight.SemiBold };
    private readonly TextBox input = new() { Name = "NoteInput", Watermark = "Add a note: 14:30 · ! · @fri · #Account", Padding = new Thickness(10, 7), FontSize = 14 };
    private string shown = ""; private bool dirty;
    private DateTime? targetDay; private Border dayChip = null!; private readonly TextBlock chipText = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    public NotesPage(TimerModel model)
    {
        this.model = model; coming = new ComingUpView(model);
        ColumnDefinitions = new ColumnDefinitions("3*,16,2*"); Margin = new Thickness(0, 0, 0, 4);
        var left = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(20, 16) }; left.Classes.Add("dayCard");
        var leftGrid = Ui.Grid("*", "Auto,*,Auto,Auto"); leftGrid.RowSpacing = 10;
        Ui.Put(leftGrid, heading);
        Ui.Put(leftGrid, Ui.WithMoreBelow(new ScrollViewer { Content = rows, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }), row: 1);
        // A note can be put on any day: the small calendar beside the box picks it for the next note, and it shows as a chip until the note is added.
        dayChip = new Border { Name = "NoteDayChip", CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(8, 1, 3, 1), HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        dayChip.Classes.Add("chip");
        var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        chipRow.Children.Add(chipText);
        var chipClear = Ui.IconButton(IconKind.Close, "Add it on the day showing instead", "NoteDayClear", () => { targetDay = null; UpdateDayChip(); }); chipClear.Width = 18; chipClear.Height = 18; chipClear.Padding = new Thickness(0); chipRow.Children.Add(chipClear);
        dayChip.Child = chipRow; Ui.Put(leftGrid, dayChip, row: 2);
        var inputRow = Ui.Grid("*,Auto"); inputRow.ColumnSpacing = 6; Ui.Put(inputRow, input);
        var pickDay = Ui.IconButton(IconKind.Today, "Add it on another day", "NoteDayPick", () => { }); pickDay.Width = 36; pickDay.Height = 36; Ui.Put(inputRow, pickDay, 1);
        var dayCalendar = new Calendar { Name = "NoteDayCalendar" }; var dayFlyout = new Flyout { Content = dayCalendar, Placement = Avalonia.Controls.PlacementMode.TopEdgeAlignedRight };
        dayCalendar.SelectedDatesChanged += (_, _) => { if (dayCalendar.SelectedDate is { } picked) { targetDay = picked.Date; dayFlyout.Hide(); UpdateDayChip(); input.Focus(); } };
        dayFlyout.Opening += (_, _) => { dayCalendar.SelectedDate = null; dayCalendar.DisplayDate = targetDay ?? model.SelectedDate; };
        pickDay.Flyout = dayFlyout;
        Ui.Put(leftGrid, inputRow, row: 3);
        TagPicker.Attach(input, leftGrid, model, above: true);
        left.Child = leftGrid; Children.Add(left);
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            var typed = input.Text; input.Text = ""; var day = targetDay ?? model.SelectedDate; targetDay = null; UpdateDayChip(); model.AddDayNote(day, typed); e.Handled = true;
        };
        var right = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(20, 16) }; right.Classes.Add("dayCard");
        var rightGrid = Ui.Grid("*", "Auto,*"); rightGrid.RowSpacing = 10;
        var comingHeading = new TextBlock { Text = "Coming up", FontSize = 16, FontWeight = FontWeight.SemiBold }; Ui.Put(rightGrid, comingHeading);
        Ui.Put(rightGrid, Ui.WithMoreBelow(new ScrollViewer { Content = coming, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }), row: 1);
        right.Child = rightGrid; SetColumn(right, 2); Children.Add(right);
        Rebuild();
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += OnModel; };
        DetachedFromVisualTree += (_, _) => { model.PropertyChanged -= OnModel; };
        // A rebuild that was held back while a note was being edited happens as soon as that box is left.
        AddHandler(LostFocusEvent, (_, _) => { if (dirty && !Editing()) Rebuild(); });
    }

    private void UpdateDayChip()
    {
        dayChip.IsVisible = targetDay is not null;
        if (targetDay is { } day) chipText.Text = "For " + day.ToString("ddd d MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
    }

    private bool Editing() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox box && box != input && this.IsVisualAncestorOf(box);

    // The clock ticks every second and the model announces itself every few; only a change to what the page shows rebuilds it.
    private void OnModel(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimerModel.Elapsed)) return;
        if (Signature() == shown) return;
        if (Editing()) { dirty = true; return; }
        Rebuild();
    }

    private string Signature() => model.SelectedDate.ToString("yyyyMMdd") + "|" + model.CurrentTheme.Name + "|" + string.Join(",", model.AccountNames) + "|"
        + string.Join(";", model.AllDayNotes.Select(n => $"{n.Id}:{n.Day:yyyyMMdd}:{n.At}:{n.Done}:{n.Text}")) + "|" + model.Engine.Now.ToString("yyyyMMdd");

    public void Rebuild()
    {
        dirty = false; shown = Signature(); rows.Children.Clear();
        var british = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        heading.Text = model.SelectedDate.ToString("dddd d MMMM", british);
        var notes = model.NotesOn(model.SelectedDate);
        if (notes.Count == 0) rows.Children.Add(Ui.Text("No notes for this day.", 13, "muted"));
        foreach (var note in notes)
        {
            var id = note.Id;
            var tick = new CheckBox { Name = "NoteTick_" + id, IsChecked = note.Done, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            tick.Click += (_, _) => model.ToggleDayNote(id);
            var text = new TextBox { Name = "NoteText_" + id, Text = note.Text, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4, 2), FontSize = 14, Opacity = note.Done ? .55 : 1 };
            // TextChanged does not fire headless, so the text property is watched instead.
            text.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && text.Text != note.Text) model.SetDayNoteText(id, text.Text ?? ""); };
            var remove = Ui.IconButton(IconKind.Close, "Remove", "NoteRemove_" + id, () => model.RemoveDayNote(id)); remove.Width = 26; remove.Height = 26; remove.Classes.Add("hoverReveal");
            // A small calendar picks another day for the note, for when it was put on the wrong one or plans change.
            var when = Ui.IconButton(IconKind.Today, "Move to another day", "NoteMove_" + id, () => { }); when.Width = 26; when.Height = 26; when.Classes.Add("hoverReveal");
            var calendar = new Calendar { Name = "NoteCalendar_" + id, SelectedDate = note.Day, DisplayDate = note.Day };
            var flyout = new Flyout { Content = calendar, Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight };
            calendar.SelectedDatesChanged += (_, _) => { if (calendar.SelectedDate is { } picked) { flyout.Hide(); model.MoveDayNote(id, picked); } };
            when.Flyout = flyout;
            var row = Ui.Grid("Auto,*,Auto"); row.VerticalAlignment = VerticalAlignment.Center; row.Margin = new Thickness(0, 2);
            Ui.Put(row, tick);
            // What was recognised in the words is shown under them as quiet marks, each with a cross (shown while the row is under the pointer or has focus) that takes it back out of the note.
            var body = new StackPanel { Spacing = 0 }; body.Children.Add(text);
            if (NoteChips.For(model, note, this) is { } chips) { chips.Margin = new Thickness(8, 0, 0, 2); body.Children.Add(chips); }
            var actions = Ui.Horizontal(when, remove); actions.VerticalAlignment = VerticalAlignment.Top; actions.Spacing = 0;
            Ui.Put(row, body, 1); Ui.Put(row, actions, 2);
            Ui.RevealOnHover(row);
            rows.Children.Add(row);
        }
        coming.Rebuild();
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
        if (ink is not null) words.Foreground = ink; else words.Classes.Add("muted");
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        if (lead is not null) row.Children.Add(lead);
        row.Children.Add(words);
        if (clear is not null && clearName is not null)
        {
            var cross = Ui.IconButton(IconKind.Close, "Take this out of the note", clearName, clear); cross.Width = 18; cross.Height = 18; cross.Padding = new Thickness(0); cross.Classes.Add("hoverReveal"); row.Children.Add(cross);
        }
        // Quiet marks, not outlined pills: the words are the content and these only say how they were read.
        var chip = new Border { Name = name, Padding = new Thickness(0, 1, clear is null ? 10 : 2, 1), HorizontalAlignment = HorizontalAlignment.Left, Child = row };
        ToolTip.SetTip(chip, tip); return chip;
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
    public ComingUpView(TimerModel model, bool compact = false) { this.model = model; this.compact = compact; Spacing = 6; Rebuild(); }
    public static string When(DateTime day, DateTime today)
        => (day.Date - today.Date).Days switch { 0 => "Today", 1 => "Tomorrow", < 7 => day.ToString("dddd", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), _ => day.ToString("ddd d MMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")) };
    public void Rebuild()
    {
        Children.Clear();
        var coming = model.ComingUp();
        if (coming.Count == 0)
        {
            Children.Add(Ui.Text("Nothing coming up", compact ? 13 : 14, "muted"));
        }
        // Small, it is the one thing coming next, on a single line.
        DateTime? day = null;
        foreach (var note in compact ? coming.Take(1) : coming)
        {
            if (compact)
            {
                var line = new StackPanel { Spacing = 0 };
                line.Children.Add(new TextBlock { Text = When(note.Day, model.Engine.Now) + (note.At is { } t ? " " + t.ToString("hh\\:mm") : ""), FontSize = 15, FontWeight = FontWeight.Bold });
                line.Children.Add(new TextBlock { Text = NoteShorthand.Display(note, model.AccountNames), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }); Children.Add(line); continue;
            }
            var id = note.Id;
            // Notes are grouped under their day, so each row only needs its time.
            if (day != note.Day.Date)
            {
                day = note.Day.Date;
                var heading = Ui.Text(When(day.Value, model.Engine.Now), 12, "muted"); heading.FontWeight = FontWeight.SemiBold; heading.Margin = new Thickness(0, Children.Count == 0 ? 0 : 10, 0, 0); Children.Add(heading);
            }
            // What kind of thing it is shows in its leading mark: a flag for a deadline (press it when done), a clock for an event, which simply passes,
            // and a tick box for a task, which is the only kind that is ticked off.
            Control tick;
            if (DayNotes.IsDeadline(note)) { var flag = Ui.IconButton(IconKind.Flag, "Deadline. Press when it is done", "ComingDone_" + id, () => model.ToggleDayNote(id)); flag.Width = 24; flag.Height = 24; tick = flag; }
            else if (note.At is not null)
            {
                var mark = new Border { Name = "ComingEvent_" + id, Width = 24, Height = 24, Child = new IconGlyph(IconKind.Clock) { TintKey = "Muted", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                ToolTip.SetTip(mark, "Event"); tick = mark;
            }
            else { var box = new CheckBox { Name = "ComingTick_" + id, IsChecked = false, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center }; box.Click += (_, _) => model.ToggleDayNote(id); tick = box; }
            var at = Ui.Text(note.At is { } time ? time.ToString("hh\\:mm") : "", 12, "muted"); at.VerticalAlignment = VerticalAlignment.Top; at.Margin = new Thickness(8, 3, 0, 0);
            var words = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center }; words.Children.Add(new TextBlock { Text = NoteShorthand.Display(note, model.AccountNames), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            if (NoteChips.AccountChip(model, note, this, "ComingAccount_" + id, interactive: false) is { } account) words.Children.Add(account);
            var row = Ui.Grid("Auto,*,Auto"); tick.VerticalAlignment = VerticalAlignment.Top; tick.Margin = new Thickness(0, 0, 4, 0); Ui.Put(row, tick); Ui.Put(row, words, 1); Ui.Put(row, at, 2); Children.Add(row);
        }
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
