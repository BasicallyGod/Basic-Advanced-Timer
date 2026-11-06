using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The card that hangs under the pill when something needs a decision (the board "Notifications: calmer at a glance", option B). It is always one short list, most
/// urgent first, with one row open: its words, who and when, and the buttons to deal with it there. Pressing another row opens that one and folds the first, Up and Down
/// move the open row, and what comes up later folds to one line under the list. There are no cards to step through. It is 336 wide like the pill and never takes the
/// keyboard focus itself.
/// </summary>
public sealed class PillAlertsCard : Border
{
    public const double CardWidth = 336;
    private readonly TimerModel model;
    private readonly Action fold, openDashboard;
    private Popup rescheduling = new();
    private TopLevel? keyHost;
    private IReadOnlyList<AlertItem> items = Array.Empty<AlertItem>();
    private string? shown;
    /// <summary>Which alert is open, in the order of <see cref="Items"/>.</summary>
    public int Index { get; private set; }
    private bool allowEmpty, showComing;
    public IReadOnlyList<AlertItem> Items => items;
    /// <summary>Raised when nothing is waiting any more, so the pill can fold the card away.</summary>
    public event Action? Emptied;

    public PillAlertsCard(TimerModel model, Action fold, Action openDashboard)
    {
        this.model = model; this.fold = fold; this.openDashboard = openDashboard; Name = "PillAlertsCard";
        Width = CardWidth; Padding = new Thickness(10, 12, 10, 12); CornerRadius = new CornerRadius(PillWindow.PillRadius); BorderThickness = new Thickness(1); MaxHeight = 420;
        Classes.Add("pillBody"); this.Bind(BorderBrushProperty, this.GetResourceObservable("Line"));
        // Left and Right step through what is waiting and Escape folds, wherever the keyboard focus is inside the card.
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Sync(true); keyHost = TopLevel.GetTopLevel(this); keyHost?.AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Bubble, true); };
        DetachedFromVisualTree += (_, _) => { model.PropertyChanged -= Changed; keyHost?.RemoveHandler(KeyDownEvent, OnKey); keyHost = null; };
        Sync(true);
    }
    private void OnKey(object? sender, Avalonia.Input.KeyEventArgs e) { if (HandleKey(e.Key)) e.Handled = true; }
    /// <summary>Up and Down move the open row and Escape folds; true when the key was used.</summary>
    public bool HandleKey(Avalonia.Input.Key key)
    {
        if (key == Avalonia.Input.Key.Escape) { fold(); return true; }
        if (items.Count < 2) return false;
        if (key == Avalonia.Input.Key.Down) { Step(1); return true; }
        if (key == Avalonia.Input.Key.Up) { Step(-1); return true; }
        return false;
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync(false);

    // ---- what is shown
    /// <summary>Opens the list with the most urgent alert open. With nothing waiting it says so, and stays until it is put away.</summary>
    public void Open(bool list = false) { Index = 0; allowEmpty = model.Alerts.Count == 0; Sync(true); }
    /// <summary>Opens the alert at <paramref name="index"/> and folds the one that was open.</summary>
    public void OpenRow(int index) { if (index < 0 || index >= items.Count) return; Index = index; Sync(true); }
    public void Step(int by)
    {
        if (items.Count == 0) return;
        Index = (Index + by + items.Count) % items.Count; Sync(true);
    }

    private string Signature() => Index + "|" + showComing + "|" + string.Join(";", items.Select(i => i.Key + "," + i.Detail + "," + i.Caption)) + "|" + string.Join(";", model.Notes.ComingUp().Select(n => n.Id + n.Text));
    /// <summary>Works out what is waiting again and redraws when it changed, or when asked to.</summary>
    public void Sync(bool force)
    {
        var before = items.Count > 0 && Index < items.Count ? items[Index].Key : null;
        items = model.Alerts;
        if (items.Count == 0 && !allowEmpty) { shown = null; Child = null; Emptied?.Invoke(); return; }
        // The open row stays on the same alert when others come or go around it, and the next one takes its place when its own is dealt with.
        if (before is not null && Index < items.Count && items[Index].Key != before) { var at = items.ToList().FindIndex(i => i.Key == before); if (at >= 0) Index = at; }
        if (Index >= items.Count) Index = Math.Max(0, items.Count - 1);
        var signature = Signature();
        if (!force && signature == shown) return;
        shown = signature; Rebuild();
    }

    private static Border Dot(string token = "Overtime")
    {
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center }; dot.Bind(Border.BackgroundProperty, dot.GetResourceObservable(token)); return dot;
    }
    private static TextBlock Bold(string words, string token, double size)
    {
        var text = NoteParts.Ink(words, token, size); text.FontWeight = FontWeight.SemiBold; return text;
    }

    private void Rebuild()
    {
        // A pop-up sits in one place only, so each redraw of the card makes its own.
        rescheduling = new Popup { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Name = "AlertRescheduleHost" };
        var head = Ui.Grid("*,Auto"); head.Margin = new Thickness(6, 0, 0, 4); head.Height = 26;
        var title = Bold(items.Count == 0 ? "Nothing waiting" : $"{items.Count} waiting", "Ink", 12.5); title.Name = "AlertTitle"; title.VerticalAlignment = VerticalAlignment.Center; Ui.Put(head, title);
        var cross = Ui.IconButton(IconKind.Close, "Fold", "AlertFold", fold); cross.Width = 24; cross.Height = 24; Ui.Put(head, cross, 1);
        var rows = new StackPanel { Spacing = 2, Name = "AlertRows" };
        for (var i = 0; i < items.Count; i++) rows.Children.Add(i == Index ? OpenRowFor(items[i]) : ClosedRow(i));
        var soon = model.Notes.ComingUp(3);
        if (showComing) foreach (var note in soon) rows.Children.Add(ComingRow(note));
        var scroll = new ScrollViewer { Name = "AlertListScroll", Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Ui.LeaveRoomForTrack(scroll, rows);
        var foot = Ui.Grid("Auto,*,Auto"); foot.Margin = new Thickness(6, 6, 6, 0);
        if (soon.Count > 0) Ui.Put(foot, Link($"{soon.Count} coming up", "AlertComingToggle", () => { showComing = !showComing; Sync(true); }, IconKind.Next, "Muted"));
        Ui.Put(foot, Link("Open Notes", "AlertOpenDashboard", OpenNotes, null, "Ink"), 2);
        var frame = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(scroll, 1); Grid.SetRow(foot, 2); frame.Children.Add(head); frame.Children.Add(scroll); frame.Children.Add(foot); frame.Children.Add(rescheduling);
        Child = frame;
    }
    private static Button Link(string words, string name, Action click, IconKind? trailing, string token)
    {
        var button = Ui.Button("", click, name); button.Classes.Add("pillBare"); button.Padding = new Thickness(2); button.HorizontalAlignment = HorizontalAlignment.Left;
        var text = Bold(words, token, 12.5); var row = Ui.Horizontal(text); row.Spacing = 2;
        if (trailing is { } icon) row.Children.Add(new IconGlyph(icon) { TintKey = token });
        button.Content = row; Avalonia.Automation.AutomationProperties.SetName(button, words); return button;
    }

    // ---- the open row: its words, who and when, and the buttons
    private Control OpenRowFor(AlertItem item)
    {
        var column = new StackPanel { Spacing = 0 };
        var top = Ui.Grid("Auto,*,Auto"); top.ColumnSpacing = 10;
        var lead = Dot(); lead.VerticalAlignment = VerticalAlignment.Top; lead.Margin = new Thickness(0, 6, 0, 0); Ui.Put(top, lead);
        var words = new TextBlock { Text = item.Words, FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Name = "AlertWords", VerticalAlignment = VerticalAlignment.Center }; Ui.Put(top, words, 1);
        var late = NoteParts.Ink(item.Detail, item.Token, 12); late.Name = "AlertDetail"; late.VerticalAlignment = VerticalAlignment.Top; late.Margin = new Thickness(0, 2, 0, 0); Ui.Put(top, late, 2);
        column.Children.Add(top);
        var meta = Ui.Horizontal(); meta.Spacing = 7; meta.Margin = new Thickness(18, 4, 0, 10); meta.Name = "AlertMeta";
        switch (item.Kind)
        {
            case AlertKind.Reminder:
                if (item.Account is { } account) { meta.Children.Add(NoteParts.Dot(model, account, 8)); meta.Children.Add(NoteParts.Ink(account, "Muted", 12.5)); meta.Children.Add(NoteParts.Ink("·", "Muted", 12.5)); }
                meta.Children.Add(NoteParts.Ink("was due " + (item.When ?? ""), "Muted", 12.5)); break;
            case AlertKind.Budget:
            case AlertKind.BudgetSpent:
                meta.Children.Add(Bar(item.Ratio, item.Kind == AlertKind.BudgetSpent)); meta.Children.Add(NoteParts.Ink(item.Caption, "Muted", 12.5)); break;
            default: meta.Children.Add(NoteParts.Ink(item.Caption, "Muted", 12.5)); break;
        }
        column.Children.Add(meta);
        var actions = Actions(item); actions.Margin = new Thickness(18, 0, 0, 0); column.Children.Add(actions);
        var card = new Border { Child = column, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 10, 10, 12), Name = "AlertOpenRow" };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Well")); card.Bind(Border.BoxShadowProperty, card.GetResourceObservable("ShadowSunk"));
        return card;
    }
    private static Control Bar(double ratio, bool spent)
    {
        var fraction = Math.Clamp(ratio, 0, 1);
        var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 90 * fraction, Height = 6 };
        fill.Bind(Border.BackgroundProperty, fill.GetResourceObservable(spent ? "Overtime" : "BudgetWarning"));
        var track = new Border { Width = 90, Height = 6, CornerRadius = new CornerRadius(3), Name = "AlertBar", Child = fill, VerticalAlignment = VerticalAlignment.Center };
        track.Bind(Border.BackgroundProperty, track.GetResourceObservable("Line")); return track;
    }

    // ---- the actions
    private Control Actions(AlertItem item)
    {
        // Spacing between the buttons, not a margin on each: a margin after the last one counted against the width and wrapped a button onto a second line on Windows.
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 6, Name = "AlertActions" };
        Button Act(string name, string words, IconKind? icon, Action click, bool main = false)
        {
            var button = Ui.Ctl(name, words, icon, words, click, small: true); button.FontSize = 12.5; button.Padding = new Thickness(10, 0);
            if (main) button.Classes.Add("accentOutline"); return button;
        }
        switch (item.Kind)
        {
            case AlertKind.Reminder:
                Ui.Put(row, Act("AlertDone", "Done", IconKind.Check, () => { if (item.NoteId is { } id) model.Notes.Toggle(id); }, true));
                Ui.Put(row, Act("AlertLater", "In 10 min", null, () => { if (item.NoteId is { } id) model.Notes.SetReminder(id, model.Engine.Now.AddMinutes(10)); }), 1);
                // Reschedule is the least used, so it is words and not a third key.
                Button? reschedule = null; reschedule = Link("Reschedule", "AlertReschedule", () => Reschedule(item, reschedule!), null, "Muted"); reschedule.HorizontalAlignment = HorizontalAlignment.Right; Ui.Put(row, reschedule, 3); break;
            case AlertKind.Break:
                Ui.Put(row, Act("AlertResume", "Resume", null, () => model.ToggleBreak(), true));
                Ui.Put(row, Act("AlertStay", "Stay on break", null, () => model.SnoozeAlert(item.Key, TimeSpan.FromMinutes(15))), 1); break;
            default:
                Ui.Put(row, Act("AlertOpenWeek", "Open week", null, () => { fold(); model.RequestPage("week"); openDashboard(); }, true));
                Ui.Put(row, Act("AlertOk", "OK", null, () => model.DismissAlert(item.Key)), 1); break;
        }
        return row;
    }
    private void Reschedule(AlertItem item, Button anchor)
    {
        if (item.NoteId is not { } id || model.Notes.Find(id) is not { } note) return;
        rescheduling.PlacementTarget = anchor;
        rescheduling.Child = ReminderPopup.Roomy(new ReminderPopup(() => model.Engine.Now, model.Notes.Text(note), NotesModel.ReminderOf(note), answered =>
        { model.Notes.SetReminder(id, answered); rescheduling.IsOpen = false; rescheduling.Child = null; }, () => { rescheduling.IsOpen = false; rescheduling.Child = null; }), rescheduling);
        rescheduling.IsOpen = true;
    }
    private void OpenNotes() { fold(); model.RequestPage("notes"); openDashboard(); }

    // ---- the other rows: one line each
    private Control ClosedRow(int index)
    {
        var item = items[index];
        var grid = Ui.Grid("Auto,*,Auto"); grid.ColumnSpacing = 10;
        Ui.Put(grid, Dot());
        var title = new TextBlock { Text = item.Kind is AlertKind.Budget or AlertKind.BudgetSpent ? item.Account + ": " + item.Detail.ToLowerInvariant() : item.Words, FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var press = new Button { Name = "AlertRow_" + index, Content = title, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        press.Classes.Add("pillBare"); press.Click += (_, _) => OpenRow(index); Avalonia.Automation.AutomationProperties.SetName(press, item.Words);
        Ui.Put(grid, press, 1);
        // The lateness sits at the right; under the pointer or the keyboard a reminder's tick and clock take its place.
        var late = NoteParts.Ink(item.Kind is AlertKind.Budget or AlertKind.BudgetSpent ? "" : item.Detail, item.Token, 12); late.VerticalAlignment = VerticalAlignment.Center; late.Name = "AlertRowLate_" + index;
        var side = new Panel { MinHeight = 26 }; side.Children.Add(late);
        StackPanel? hover = null;
        if (item.Kind == AlertKind.Reminder && item.NoteId is { } id)
        {
            hover = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
            hover.Children.Add(Small("AlertRowDone_" + index, IconKind.Check, "Done", () => model.Notes.Toggle(id)));
            hover.Children.Add(Small("AlertRowLater_" + index, IconKind.Clock, "In 10 min", () => model.Notes.SetReminder(id, model.Engine.Now.AddMinutes(10))));
            side.Children.Add(hover);
        }
        Ui.Put(grid, side, 2);
        var card = new Border { Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 5), Name = "AlertListRow_" + index, Background = Brushes.Transparent };
        void Over(bool on)
        {
            if (on) card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Hover")); else { card.ClearValue(Border.BackgroundProperty); card.Background = Brushes.Transparent; }
            if (hover is not null) { hover.IsVisible = on; late.IsVisible = !on; }
        }
        card.PointerEntered += (_, _) => Over(true); card.PointerExited += (_, _) => Over(false);
        card.GotFocus += (_, _) => Over(true); card.LostFocus += (_, _) => Over(card.IsPointerOver);
        return card;
    }
    private Control ComingRow(DayNote note)
    {
        var due = NotesModel.ReminderOf(note)!.Value; var row = Ui.Grid("Auto,*,Auto"); row.ColumnSpacing = 10; row.Margin = new Thickness(8, 5); row.Name = "AlertComing_" + note.Id;
        Ui.Put(row, Dot("Idle"));
        Ui.Put(row, new TextBlock { Text = model.Notes.Text(note), FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, 1);
        Ui.Put(row, NoteParts.Ink((model.Notes.AccountOf(note) is { } a ? a + " · " : "") + NoteDates.When(due, model.Engine.Now), "Muted", 11.5), 2);
        return row;
    }
    private static Button Small(string name, IconKind icon, string tip, Action click) { var b = Ui.IconButton(icon, tip, name, click); b.Width = 26; b.Height = 26; return b; }
}
