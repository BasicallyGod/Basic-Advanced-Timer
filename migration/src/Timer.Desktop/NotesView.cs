using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The Notes page: the notes for the day the dashboard is showing, one line each, with Add note and a quick box to add one, and what is coming up beside them.
/// A note's kind, day, times, account and repeat are set in its Details (a flyout, NoteDetails), and the shorthand (14:30, !, @fri, #Account, every mon) still
/// works in the quick box. What was read from the words shows as quiet marks on the row. The page is built once and listens to the model itself, so a tick of the clock never rebuilds it under the pointer, and while a note's words are
/// being edited it waits until the box is left, so typing is never interrupted.
/// </summary>
public sealed class NotesPage : Grid
{
    private readonly TimerModel model;
    private readonly StackPanel rows = new() { Spacing = 4 };
    private readonly ComingUpView coming;
    private readonly TextBlock heading = new() { FontSize = 16, FontWeight = FontWeight.SemiBold };
    private readonly TextBox input = new() { Name = "NoteInput", Watermark = "Quick add: type a note, press Enter", Padding = new Thickness(10, 7), FontSize = 14 };
    private string shown = ""; private bool dirty; private Button undoRemove = null!;
    private string kind = "all"; // Which notes the day's list shows: all, tasks, events or deadlines.
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private static readonly (string Key, string Text)[] Kinds = { ("all", "All"), ("tasks", "Tasks"), ("events", "Events"), ("deadlines", "Deadlines"), ("missed", "Missed") };
    private static readonly (int Days, string Text)[] Ranges = { (7, "Next 7 days"), (30, "Next 30 days"), (180, "Next 6 months") };
    private DateTime? targetDay; private Border dayChip = null!; private readonly TextBlock chipText = new() { FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    public NotesPage(TimerModel model)
    {
        this.model = model; coming = new ComingUpView(model);
        ColumnDefinitions = new ColumnDefinitions("3*,16,2*"); Margin = new Thickness(0, 0, 0, 4);
        var left = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(20, 16) }; left.Classes.Add("dayCard");
        var leftGrid = Ui.Grid("*", "Auto,Auto,*,Auto,Auto"); leftGrid.RowSpacing = 10;
        // The day's name on the left and the one main action on the right; under them the tabs that narrow the list to one kind of note.
        var headRow = Ui.Grid("*,Auto,Auto"); headRow.ColumnSpacing = 6; Ui.Put(headRow, heading);
        // A removed note can be put back, so one stray click on the cross never loses it.
        undoRemove = Ui.DialogButton("Undo remove", () => model.RestoreRemovedNote(), "NoteUndoRemove"); undoRemove.Height = 32; undoRemove.IsVisible = false; Ui.Put(headRow, undoRemove, 1);
        // Add note opens the same panel as a note's Details, so a meeting, a deadline or a repeat is set with controls, not typed. The box below stays for quick text.
        var add = Ui.DialogButton("Add note", () => { }, "NoteAddButton", true); add.Height = 32; add.Flyout = DetailsFlyout(null); Ui.Put(headRow, add, 2);
        Ui.Put(leftGrid, headRow); Ui.Put(leftGrid, tabs, row: 1);
        Ui.Put(leftGrid, Ui.WithMoreBelow(new ScrollViewer { Content = rows, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }), row: 2);
        // A note can be put on any day: the small calendar beside the box picks it for the next note, and it shows as a chip until the note is added.
        dayChip = new Border { Name = "NoteDayChip", CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(8, 1, 3, 1), HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        dayChip.Classes.Add("chip");
        var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        chipRow.Children.Add(chipText);
        var chipClear = Ui.IconButton(IconKind.Close, "Add it on the day showing instead", "NoteDayClear", () => { targetDay = null; UpdateDayChip(); }); chipClear.Width = 18; chipClear.Height = 18; chipClear.Padding = new Thickness(0); chipRow.Children.Add(chipClear);
        dayChip.Child = chipRow; Ui.Put(leftGrid, dayChip, row: 3);
        var inputRow = Ui.Grid("*,Auto"); inputRow.ColumnSpacing = 6; Ui.Put(inputRow, input);
        var pickDay = Ui.IconButton(IconKind.Today, "Add it on another day", "NoteDayPick", () => { }); pickDay.Width = 36; pickDay.Height = 36; Ui.Put(inputRow, pickDay, 1);
        var dayCalendar = new SoftDatePicker { Name = "NoteDayCalendar" }; var dayFlyout = new Flyout { Content = dayCalendar, Placement = Avalonia.Controls.PlacementMode.TopEdgeAlignedRight };
        dayCalendar.DayPicked += picked => { targetDay = picked.Date; dayFlyout.Hide(); UpdateDayChip(); input.Focus(); };
        dayFlyout.Opening += (_, _) => dayCalendar.View(targetDay ?? model.SelectedDate, targetDay);
        pickDay.Flyout = dayFlyout;
        Avalonia.Automation.AutomationProperties.SetName(input, "Add a note"); Avalonia.Automation.AutomationProperties.SetName(tabs, "Kinds of note");
        Ui.Put(leftGrid, inputRow, row: 4);
        TagPicker.Attach(input, leftGrid, model, above: true);
        left.Child = leftGrid; Children.Add(left);
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            e.Handled = true;
            model.AddDayNote(targetDay ?? model.SelectedDate, input.Text);
            input.Text = ""; targetDay = null; UpdateDayChip();
        };
        var right = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(20, 16) }; right.Classes.Add("dayCard");
        var rightGrid = Ui.Grid("*", "Auto,*"); rightGrid.RowSpacing = 10;
        var comingHeading = new TextBlock { Text = "Coming up", FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var range = new ComboBox { Name = "ComingRange", ItemsSource = Ranges.Select(r => r.Text).ToList(), SelectedIndex = 1, Height = 28, MinHeight = 28, Padding = new Thickness(10, 0), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(range, "How far ahead Coming up looks");
        range.SelectionChanged += (_, _) => { coming.Days = Ranges[Math.Max(0, range.SelectedIndex)].Days; coming.Rebuild(); };
        var comingHead = Ui.Grid("*,Auto"); Ui.Put(comingHead, comingHeading); Ui.Put(comingHead, range, 1); Ui.Put(rightGrid, comingHead);
        Ui.Put(rightGrid, Ui.WithMoreBelow(new ScrollViewer { Content = coming, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }), row: 1);
        right.Child = rightGrid; SetColumn(right, 2); Children.Add(right);
        Rebuild();
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += OnModel; };
        DetachedFromVisualTree += (_, _) => { model.PropertyChanged -= OnModel; };
        // A rebuild that was held back while a note was being edited happens as soon as that box is left.
        AddHandler(LostFocusEvent, (_, _) => { if (dirty && !Editing()) Rebuild(); });
    }

    /// <summary>The flyout for adding a note (note null) or for one note's Details. Its panel is built each time it opens, so it always starts from the note as it is.</summary>
    private Flyout DetailsFlyout(DayNote? note)
    {
        var flyout = new Flyout { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight };
        flyout.Opening += (_, _) => flyout.Content = NoteDetails.Build(model, note, model.SelectedDate, () => flyout.Hide());
        return flyout;
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
        + string.Join(";", model.AllDayNotes.Select(n => $"{n.Id}:{n.Day:yyyyMMdd}:{n.At}:{n.End}:{n.SeriesId}:{n.Done}:{n.Text}")) + "|" + model.Engine.Now.ToString("yyyyMMdd") + "|" + kind;

    public void Rebuild()
    {
        dirty = false; shown = Signature(); rows.Children.Clear();
        var british = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        var missed = model.MissedNotes();
        heading.Text = kind == "missed" ? "Missed" : model.SelectedDate.ToString("dddd d MMMM", british);
        undoRemove.IsVisible = model.LastRemovedNote is not null;
        tabs.Children.Clear();
        foreach (var (key, text) in Kinds)
        {
            var label = key == "missed" && missed.Count > 0 ? $"Missed {missed.Count}" : text;
            var picked = key; var tab = Ui.Button(label, () => { kind = picked; Rebuild(); }, "NotesFilter_" + key);
            tab.Classes.Add("flat"); tab.Height = 28; tab.Padding = new Thickness(10, 0); tab.FontSize = 13; tab.Classes.Set("filterOn", kind == key); tabs.Children.Add(tab);
        }
        var notes = kind == "missed" ? missed.ToList() : model.NotesOn(model.SelectedDate).Where(n => kind switch
        {
            "tasks" => DayNotes.IsTask(n), "events" => n.At is not null && !DayNotes.IsDeadline(n), "deadlines" => DayNotes.IsDeadline(n), _ => true
        }).ToList();
        if (notes.Count == 0) rows.Children.Add(Ui.Text(kind == "all" ? "No notes for this day." : kind == "missed" ? "Nothing missed. Tasks and deadlines you did not tick off on an earlier day show here." : "Nothing of this kind on this day.", 13, "muted"));
        // Missed notes come from many days, so they are grouped under their day, with one button to carry the lot over to today.
        if (kind == "missed" && notes.Count > 0)
        {
            var carry = Ui.DialogButton("Move all to today", () => model.MoveMissedToToday(), "NotesMissedToToday"); carry.HorizontalAlignment = HorizontalAlignment.Left; rows.Children.Add(carry);
        }
        DateTime? groupDay = null;
        foreach (var note in notes)
        {
            if (kind == "missed" && groupDay != note.Day.Date)
            {
                groupDay = note.Day.Date;
                var dayHeading = Ui.Text(note.Day.ToString("dddd d MMMM", british), 12, "muted"); dayHeading.FontWeight = FontWeight.SemiBold; dayHeading.Margin = new Thickness(0, 8, 0, 0); rows.Children.Add(dayHeading);
            }
            var id = note.Id;
            var tick = new CheckBox { Name = "NoteTick_" + id, IsChecked = note.Done, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            tick.Click += (_, _) => model.ToggleDayNote(id);
            // The words show without the ! and the #Tag, which have their own marks on the right.
            var shownWords = NoteShorthand.Display(note, model.AccountNames);
            var text = new TextBox { Name = "NoteText_" + id, Text = shownWords, TextWrapping = TextWrapping.NoWrap, AcceptsReturn = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4, 2), FontSize = 14, Opacity = note.Done ? .55 : 1, VerticalAlignment = VerticalAlignment.Center };
            // Each control says which note it is for, since the rows look alike to a screen reader.
            Avalonia.Automation.AutomationProperties.SetName(tick, "Done: " + shownWords); Avalonia.Automation.AutomationProperties.SetName(text, "Note text");
            // TextChanged does not fire headless, so the text property is watched instead.
            text.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty && text.Text != shownWords) model.SetDayNoteWords(id, text.Text ?? ""); };
            // A note that repeats asks which: just this one, or this one and every later one.
            var repeats = model.SeriesOf(note) is not null; Flyout? removeFlyout = null;
            var remove = Ui.IconButton(IconKind.Close, "Remove", "NoteRemove_" + id, () => { if (!repeats) model.RemoveDayNote(id); });
            if (repeats)
            {
                var choice = new StackPanel { Spacing = 6, Width = 190 };
                choice.Children.Add(Ui.DialogButton("Remove this one", () => { removeFlyout?.Hide(); model.RemoveDayNote(id); }, "NoteRemoveOne_" + id));
                choice.Children.Add(Ui.DialogButton("This and all later", () => { removeFlyout?.Hide(); model.RemoveSeriesFrom(id); }, "NoteRemoveLater_" + id));
                removeFlyout = new Flyout { Content = choice, Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight }; remove.Flyout = removeFlyout;
            }
            remove.Width = 26; remove.Height = 26; remove.Classes.Add("hoverReveal"); Avalonia.Automation.AutomationProperties.SetName(remove, "Remove note: " + shownWords);
            // Everything else about the note (kind, day, times, account, repeat) is in its Details, which open beside the row.
            var details = Ui.Button("Details", () => { }, "NoteDetails_" + id); details.Classes.Add("toolButton"); details.Classes.Add("quiet"); details.Classes.Add("hoverReveal"); details.Height = 26; details.Padding = new Thickness(10, 0); details.FontSize = 12;
            Avalonia.Automation.AutomationProperties.SetName(details, "Details: " + shownWords); details.Flyout = DetailsFlyout(note);
            var row = Ui.Grid("Auto,*,Auto,Auto"); row.VerticalAlignment = VerticalAlignment.Center; row.Margin = new Thickness(0, 2);
            Ui.Put(row, tick); Ui.Put(row, text, 1);
            // What was read from the words is quiet marks on the right: the time, a flag for a deadline, a repeat mark and the account's dot.
            var marks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) };
            if (note.At is not null) { var when = Ui.Text(NoteTimes.Of(note), 12.5, "muted"); when.Name = "NoteTime_" + id; marks.Children.Add(when); }
            if (DayNotes.IsDeadline(note)) { var flag = new Border { Name = "NoteDeadlineMark_" + id, Child = new IconGlyph(IconKind.Flag) { TintKey = "Finish" } }; ToolTip.SetTip(flag, "Deadline"); marks.Children.Add(flag); }
            if (model.SeriesOf(note) is { } series) { var again = new Border { Name = "NoteRepeatMark_" + id, Child = new IconGlyph(IconKind.Week) { TintKey = "Muted" } }; ToolTip.SetTip(again, series.Rule.Words()); marks.Children.Add(again); }
            if (NoteShorthand.AccountOf(note.Text, model.AccountNames) is { } linked && model.Accounts.FirstOrDefault(a => a.Name == linked) is { } account)
            {
                var dot = new Border { Name = "NoteAccountMark_" + id, Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.Parse(Ui.Visible(account.Colour))), VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(dot, account.Display); marks.Children.Add(dot);
            }
            Ui.Put(row, marks, 2);
            var actions = Ui.Horizontal(details, remove); actions.VerticalAlignment = VerticalAlignment.Center; actions.Spacing = 2;
            Ui.Put(row, actions, 3);
            Ui.RevealOnHover(row);
            rows.Children.Add(row);
        }
        coming.Rebuild();
    }
}

/// <summary>The account chip a note's #Tag gives, shown in Coming up. (In Notes the same thing is a small dot on the row, and the rest is in Details.)</summary>
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
}

/// <summary>What is coming up: dated notes from today on, soonest first, each marked with when it falls.</summary>
public sealed class ComingUpView : StackPanel
{
    private readonly TimerModel model;
    private readonly bool compact;
    /// <summary>How far ahead the list looks, in days.</summary>
    public int Days { get; set; } = 30;
    public ComingUpView(TimerModel model, bool compact = false) { this.model = model; this.compact = compact; Spacing = 6; Rebuild(); }
    public static string When(DateTime day, DateTime today)
        => (day.Date - today.Date).Days switch { 0 => "Today", 1 => "Tomorrow", < 7 => day.ToString("dddd", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), _ => day.ToString("ddd d MMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")) };
    public void Rebuild()
    {
        Children.Clear();
        var coming = model.ComingUp(Days);
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
                line.Children.Add(new TextBlock { Text = When(note.Day, model.Engine.Now) + (note.At is not null ? " " + NoteTimes.Of(note) : ""), FontSize = 15, FontWeight = FontWeight.Bold });
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
            if (DayNotes.IsDeadline(note)) { var flag = Ui.IconButton(IconKind.Flag, "Deadline. Press when it is done", "ComingDone_" + id, () => model.ToggleDayNote(id)); flag.Width = 24; flag.Height = 24; Avalonia.Automation.AutomationProperties.SetName(flag, "Mark deadline done: " + note.Text); tick = flag; }
            else if (note.At is not null)
            {
                var mark = new Border { Name = "ComingEvent_" + id, Width = 24, Height = 24, Child = new IconGlyph(IconKind.Clock) { TintKey = "Muted", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                ToolTip.SetTip(mark, "Event"); Avalonia.Automation.AutomationProperties.SetName(mark, "Event: " + note.Text); tick = mark;
            }
            else { var box = new CheckBox { Name = "ComingTick_" + id, IsChecked = false, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center }; box.Click += (_, _) => model.ToggleDayNote(id); Avalonia.Automation.AutomationProperties.SetName(box, "Done: " + note.Text); tick = box; }
            var at = Ui.Text(NoteTimes.Of(note), 12, "muted"); at.VerticalAlignment = VerticalAlignment.Top; at.Margin = new Thickness(8, 3, 0, 0);
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
