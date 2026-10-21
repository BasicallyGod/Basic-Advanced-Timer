using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Timer.Core;

namespace Timer.Desktop;

public enum NoteKind { Task, Meeting, Deadline }

/// <summary>What the Details panel asks to be saved: the kind of note, its words, day, times, account and repeat, and whether a change to a repeating note reaches the later ones.</summary>
public sealed record NoteEdit(NoteKind Kind, string Words, DateTime Day, TimeSpan? Start, TimeSpan? End, string? Account, Recurrence? Repeat, bool AllLater);

/// <summary>
/// The panel that adds or edits a note, so none of it has to be typed in the shorthand: a Task, Meeting or Deadline, its words, day, start and end, account, and how it
/// repeats. It is the content of a flyout beside the note's row (or beside Add note) and builds fresh each time it opens.
/// </summary>
public static class NoteDetails
{
    private static readonly string[] Repeats = { "Never", "Every day", "Every weekday", "Every week", "Every 2 weeks", "Choose days" };
    private static readonly DayOfWeek[] Week = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
    private static readonly string[] Letters = { "M", "T", "W", "T", "F", "S", "S" };
    private const string NoAccount = "No account";

    private static int RepeatIndex(Recurrence rule) => rule switch
    {
        { EveryWeeks: 1, DaysMask: Recurrence.AllDays } => 1, { EveryWeeks: 1, DaysMask: Recurrence.Weekdays } => 2, { EveryWeeks: 1, DaysMask: 0 } => 3, { EveryWeeks: 2, DaysMask: 0 } => 4, _ => 5
    };
    private static TimeSpan? Time(string? text)
        => TimeSpan.TryParseExact((text ?? "").Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var time) && time < TimeSpan.FromDays(1) ? time : null;
    private static Control Field(string label, Control control, string? name = null)
    {
        var box = new StackPanel { Spacing = 3 }; box.Children.Add(Ui.Text(label, 11.5, "muted")); box.Children.Add(control); if (name is not null) box.Name = name; return box;
    }

    /// <summary>Builds the panel for a new note on <paramref name="day"/> (note null), or for an existing note. <paramref name="close"/> hides the flyout.</summary>
    public static Control Build(TimerModel model, DayNote? note, DateTime day, Action close)
    {
        var series = note is null ? null : model.SeriesOf(note);
        var kind = note is null ? NoteKind.Task : TimerModel.KindOf(note);
        var date = (note?.Day ?? day).Date;
        var british = CultureInfo.GetCultureInfo("en-GB");
        var root = new StackPanel { Name = "NoteDetailsPanel", Width = 340, Spacing = 10 };
        Avalonia.Automation.AutomationProperties.SetName(root, note is null ? "Add a note" : "Note details");

        // Task, Meeting or Deadline.
        var kinds = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }; var kindButtons = new Dictionary<NoteKind, Button>();
        var startBox = new TextBox { Name = "NoteDetailsStart", Text = note?.At is { } at ? at.ToString(@"hh\:mm") : "09:00", Watermark = "HH:mm" };
        var endBox = new TextBox { Name = "NoteDetailsEnd", Text = note?.End is { } until ? until.ToString(@"hh\:mm") : kind == NoteKind.Meeting ? "" : "10:00", Watermark = "HH:mm" };
        var startField = Field("Start", startBox); var endField = Field("End", endBox);
        void ShowKind()
        {
            foreach (var (k, button) in kindButtons) button.Classes.Set("filterOn", k == kind);
            startField.IsVisible = kind != NoteKind.Task; endField.IsVisible = kind == NoteKind.Meeting;
            ((TextBlock)((StackPanel)startField).Children[0]).Text = kind == NoteKind.Deadline ? "Due time (optional)" : "Start";
        }
        foreach (var (k, text) in new[] { (NoteKind.Task, "Task"), (NoteKind.Meeting, "Meeting"), (NoteKind.Deadline, "Deadline") })
        {
            var picked = k; var button = Ui.Button(text, () => { kind = picked; ShowKind(); }, "NoteKind_" + text); button.Classes.Add("flat"); button.Height = 28; button.Padding = new Thickness(12, 0); button.FontSize = 13; kindButtons[k] = button; kinds.Children.Add(button);
        }
        root.Children.Add(kinds);

        var words = new TextBox { Name = "NoteDetailsWords", Text = note is null ? "" : NoteShorthand.Display(note, model.AccountNames), Watermark = "What is it?", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        root.Children.Add(Field("Title", words));

        // The day, with arrows and a calendar that opens inside the panel (a second pop-up inside a flyout is avoided).
        var dayText = new TextBlock { Name = "NoteDetailsDay", VerticalAlignment = VerticalAlignment.Center, FontSize = 14, MinWidth = 120, TextAlignment = Avalonia.Media.TextAlignment.Center };
        var calendar = new SoftDatePicker { Name = "NoteDetailsCalendar", IsVisible = false };
        void ShowDay() { dayText.Text = date.ToString("ddd d MMM yyyy", british); calendar.View(date, date); }
        var previous = Ui.IconButton(IconKind.Previous, "Day before", "NoteDetailsPrevious", () => { date = date.AddDays(-1); ShowDay(); });
        var next = Ui.IconButton(IconKind.Next, "Day after", "NoteDetailsNext", () => { date = date.AddDays(1); ShowDay(); });
        var pick = Ui.IconButton(IconKind.Today, "Pick a day", "NoteDetailsPick", () => calendar.IsVisible = !calendar.IsVisible);
        calendar.DayPicked += picked => { date = picked.Date; ShowDay(); calendar.IsVisible = false; };
        var dayRow = Ui.Grid("Auto,*,Auto,Auto"); Ui.Put(dayRow, previous); Ui.Put(dayRow, dayText, 1); Ui.Put(dayRow, next, 2); Ui.Put(dayRow, pick, 3);
        root.Children.Add(Field("Day", dayRow)); root.Children.Add(calendar); ShowDay();

        var times = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        Grid.SetColumn(endField, 1); times.Children.Add(startField); times.Children.Add(endField); root.Children.Add(times);

        var accounts = new ComboBox { Name = "NoteDetailsAccount", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { NoAccount }.Concat(model.AccountNames).ToList() };
        accounts.SelectedItem = note is not null && NoteShorthand.AccountOf(note.Text, model.AccountNames) is { } linked ? linked : NoAccount;
        Avalonia.Automation.AutomationProperties.SetName(accounts, "Account");
        root.Children.Add(Field("Account", accounts));

        // How it repeats: a few common choices, or the days picked one by one, and an optional last day.
        var repeat = new ComboBox { Name = "NoteDetailsRepeat", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Repeats, SelectedIndex = series is null ? 0 : RepeatIndex(series.Rule) };
        Avalonia.Automation.AutomationProperties.SetName(repeat, "Repeat");
        var dayToggles = new StackPanel { Name = "NoteDetailsDays", Orientation = Orientation.Horizontal, Spacing = 4 }; var toggles = new List<ToggleButton>();
        for (var i = 0; i < 7; i++)
        {
            var on = series is not null && RepeatIndex(series.Rule) == 5 && (series.Rule.DaysMask & (1 << (int)Week[i])) != 0;
            var toggle = new ToggleButton { Name = "NoteDetailsDay_" + i, Content = Letters[i], IsChecked = on, Width = 36, Height = 30, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
            Avalonia.Automation.AutomationProperties.SetName(toggle, Week[i].ToString()); toggles.Add(toggle); dayToggles.Children.Add(toggle);
        }
        var untilBox = new TextBox { Name = "NoteDetailsUntil", Watermark = "No end · e.g. 31dec or 2026-12-31", Text = series?.Rule.Until?.ToString("yyyy-MM-dd") ?? "" };
        var untilField = Field("Repeat until", untilBox);
        var repeatField = new StackPanel { Spacing = 6 }; repeatField.Children.Add(Field("Repeat", repeat)); repeatField.Children.Add(dayToggles); repeatField.Children.Add(untilField);
        root.Children.Add(repeatField);
        void ShowRepeat() { dayToggles.IsVisible = repeat.SelectedIndex == 5; untilField.IsVisible = repeat.SelectedIndex > 0; }
        repeat.SelectionChanged += (_, _) => ShowRepeat();

        // A note in a series says whether a change is to this one or to this one and every later one. Only the second can change how it repeats.
        var one = new RadioButton { Name = "NoteDetailsScopeOne", GroupName = "scope" + (note?.Id ?? 0), Content = "This one only", IsChecked = true };
        var later = new RadioButton { Name = "NoteDetailsScopeLater", GroupName = "scope" + (note?.Id ?? 0), Content = "This and all later" };
        var scope = new StackPanel { Spacing = 2, IsVisible = series is not null };
        scope.Children.Add(Ui.Text("Apply changes to", 12, "muted")); scope.Children.Add(one); scope.Children.Add(later);
        void ShowScope() { repeatField.IsEnabled = series is null || later.IsChecked == true; }
        one.IsCheckedChanged += (_, _) => ShowScope(); later.IsCheckedChanged += (_, _) => ShowScope();
        root.Children.Add(scope);

        var error = new TextBlock { Name = "NoteDetailsError", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12.5 }; error.Classes.Add("overtime");
        root.Children.Add(error);

        // Buttons. Remove is for an existing note, and for one in a series it asks which.
        var removeChoices = new StackPanel { Spacing = 6, IsVisible = false };
        if (note is not null)
        {
            var id = note.Id;
            removeChoices.Children.Add(Ui.DialogButton("Remove this one", () => { close(); model.RemoveDayNote(id); }, "NoteDetailsRemoveOne"));
            removeChoices.Children.Add(Ui.DialogButton("This and all later", () => { close(); model.RemoveSeriesFrom(id); }, "NoteDetailsRemoveLater"));
        }
        var remove = Ui.Button("Delete", () => { if (note is null) return; if (series is null) { close(); model.RemoveDayNote(note.Id); } else removeChoices.IsVisible = !removeChoices.IsVisible; }, "NoteDetailsRemove");
        remove.Classes.Add("flat"); remove.IsVisible = note is not null; remove.FontSize = 13; remove.HorizontalAlignment = HorizontalAlignment.Left;
        remove.Content = Ui.Horizontal(new IconGlyph(IconKind.Trash), new TextBlock { Text = "Delete", VerticalAlignment = VerticalAlignment.Center, FontSize = 13 }); Avalonia.Automation.AutomationProperties.SetName(remove, "Delete note");
        var save = Ui.DialogButton(note is null ? "Add" : "Save", () =>
        {
            TimeSpan? start = null, end = null;
            if (kind != NoteKind.Task && !string.IsNullOrWhiteSpace(startBox.Text)) { start = Time(startBox.Text); if (start is null) { error.Text = "Times are written HH:mm."; return; } }
            if (kind == NoteKind.Meeting && !string.IsNullOrWhiteSpace(endBox.Text)) { end = Time(endBox.Text); if (end is null) { error.Text = "Times are written HH:mm."; return; } }
            DateTime? last = null;
            if (repeat.SelectedIndex > 0 && !string.IsNullOrWhiteSpace(untilBox.Text))
            {
                last = NoteShorthand.DayFor(untilBox.Text.Trim(), model.Engine.Now);
                if (last is null) { error.Text = "Repeat until: try 31dec or 2026-12-31."; return; }
            }
            var mask = toggles.Select((t, i) => t.IsChecked == true ? 1 << (int)Week[i] : 0).Sum();
            if (repeat.SelectedIndex == 5 && mask == 0) { error.Text = "Pick at least one day to repeat on."; return; }
            Recurrence? rule = repeat.SelectedIndex switch
            {
                1 => new Recurrence(1, Recurrence.AllDays, last), 2 => new Recurrence(1, Recurrence.Weekdays, last), 3 => new Recurrence(1, 0, last), 4 => new Recurrence(2, 0, last),
                5 => new Recurrence(series is not null && RepeatIndex(series.Rule) == 5 ? series.Rule.EveryWeeks : 1, mask, last), _ => null
            };
            var account = accounts.SelectedItem as string; if (account == NoAccount) account = null;
            var edit = new NoteEdit(kind, words.Text ?? "", date, start, end, account, rule, series is not null && later.IsChecked == true);
            var problem = note is null ? model.AddNote(edit) : model.SaveNote(note.Id, edit);
            if (problem is not null) error.Text = problem; else close();
        }, note is null ? "NoteDetailsAdd" : "NoteDetailsSave", true); save.Classes.Add("primary");
        var cancel = Ui.DialogButton("Cancel", close, "NoteDetailsCancel");
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(cancel, 1); Grid.SetColumn(save, 2); buttons.Children.Add(remove); buttons.Children.Add(cancel); buttons.Children.Add(save);
        root.Children.Add(buttons); root.Children.Add(removeChoices);
        ShowKind(); ShowRepeat(); ShowScope();
        return root;
    }
}
