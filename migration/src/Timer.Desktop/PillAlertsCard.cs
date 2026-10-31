using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The card that hangs under the pill when something needs a decision (the board "Pill notifications: moving between them"). It shows one thing at a time with
/// the actions to deal with it there, steps between what is waiting (the arrows, the dots, the Left and Right keys), and opens a list of everything in its own place;
/// a row of the list opens its card, with a way back. It is 336 wide like the pill and never takes the keyboard focus itself.
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
    /// <summary>What the card is showing: "card" (one alert), "list" (everything) or "detail" (one alert opened from the list, with a way back).</summary>
    public string View { get; private set; } = "card";
    /// <summary>Which alert the card or detail is on, in the order of <see cref="Items"/>.</summary>
    public int Index { get; private set; }
    public IReadOnlyList<AlertItem> Items => items;
    /// <summary>Raised when nothing is waiting any more, so the pill can fold the card away.</summary>
    public event Action? Emptied;

    public PillAlertsCard(TimerModel model, Action fold, Action openDashboard)
    {
        this.model = model; this.fold = fold; this.openDashboard = openDashboard; Name = "PillAlertsCard";
        Width = CardWidth; Padding = new Thickness(14, 12, 14, 12); CornerRadius = new CornerRadius(18); BorderThickness = new Thickness(1); MaxHeight = 420;
        Classes.Add("pillBody"); this.Bind(BorderBrushProperty, this.GetResourceObservable("Line"));
        // Left and Right step through what is waiting and Escape folds, wherever the keyboard focus is inside the card.
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Sync(true); keyHost = TopLevel.GetTopLevel(this); keyHost?.AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Bubble, true); };
        DetachedFromVisualTree += (_, _) => { model.PropertyChanged -= Changed; keyHost?.RemoveHandler(KeyDownEvent, OnKey); keyHost = null; };
        Sync(true);
    }
    private void OnKey(object? sender, Avalonia.Input.KeyEventArgs e) { if (HandleKey(e.Key)) e.Handled = true; }
    /// <summary>Left and Right step through what is waiting (a card only) and Escape folds; true when the key was used.</summary>
    public bool HandleKey(Avalonia.Input.Key key)
    {
        if (key == Avalonia.Input.Key.Escape) { fold(); return true; }
        if (View != "card" || items.Count < 2) return false;
        if (key == Avalonia.Input.Key.Right) { Next(); return true; }
        if (key == Avalonia.Input.Key.Left) { Previous(); return true; }
        return false;
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync(false);

    // ---- what is shown
    public void ShowCard(int index) { View = "card"; Index = index; Sync(true); }
    public void ShowList() { View = "list"; Sync(true); }
    public void ShowDetail(int index) { View = "detail"; Index = index; Sync(true); }
    /// <summary>Opens the way the user asked for: the list under "Light only", else the first card.</summary>
    public void Open(bool list) { if (list) ShowList(); else ShowCard(0); }
    public void Step(int by)
    {
        if (items.Count == 0) return;
        Index = (Index + by + items.Count) % items.Count; Sync(true);
    }
    public void Next() => Step(1);
    public void Previous() => Step(-1);

    private string Signature() => View + "|" + Index + "|" + string.Join(";", items.Select(i => i.Key + "," + i.Detail + "," + i.Caption)) + "|" + string.Join(";", model.Notes.ComingUp().Select(n => n.Id + n.Text));
    /// <summary>Works out what is waiting again and redraws when it changed, or when asked to.</summary>
    public void Sync(bool force)
    {
        var before = items.Count > 0 && Index < items.Count ? items[Index].Key : null;
        items = model.Alerts;
        if (items.Count == 0 && View != "list") { shown = null; Child = null; Emptied?.Invoke(); return; }
        // The card stays on the same alert when others come or go around it, and moves to the next when its own is dealt with.
        if (before is not null && Index < items.Count && items[Index].Key != before) { var at = items.ToList().FindIndex(i => i.Key == before); if (at >= 0) Index = at; }
        if (Index >= items.Count) Index = Math.Max(0, items.Count - 1);
        var signature = Signature();
        if (!force && signature == shown) return;
        shown = signature; Rebuild();
    }

    private void Rebuild()
    {
        if (View == "list") { Child = BuildList(); return; }
        var item = items[Index];
        var column = new StackPanel { Spacing = 0 };
        column.Children.Add(Header(item));
        column.Children.Add(new TextBlock { Text = item.Words, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 9), Name = "AlertWords" });
        column.Children.Add(Facts(item));
        column.Children.Add(Actions(item));
        column.Children.Add(View == "detail" ? DetailFooter() : items.Count > 1 ? NavFooter() : new Border { Height = 0 });
        // A pop-up sits in one place only, so each redraw of the card makes its own.
        rescheduling = new Popup { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, Name = "AlertRescheduleHost" };
        Child = column; column.Children.Add(rescheduling);
    }

    private static IconKind IconFor(AlertKind kind) => kind switch { AlertKind.Reminder => IconKind.Bell, AlertKind.Break => IconKind.Clock, _ => IconKind.Flag };
    private Control Header(AlertItem item)
    {
        var grid = Ui.Grid("Auto,*,Auto"); grid.ColumnSpacing = 8; grid.Margin = new Thickness(0, 0, 0, 8);
        var disc = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center, Child = new Viewbox { Width = 14, Height = 14, Child = new IconGlyph(IconFor(item.Kind)) { TintKey = item.Token } } };
        disc.Bind(Border.BackgroundProperty, disc.GetResourceObservable(item.Token == "Overtime" ? "LateTint" : "BudgetTint"));
        Ui.Put(grid, disc);
        var words = Ui.Horizontal(NoteParts.Ink(item.Headline.ToUpperInvariant(), item.Token, 11.5), NoteParts.Ink(item.Detail, "Muted", 12.5)); words.Spacing = 8;
        ((TextBlock)words.Children[0]).FontWeight = FontWeight.Bold; ((TextBlock)words.Children[0]).LetterSpacing = .6; ((TextBlock)words.Children[0]).Name = "AlertKind";
        ((TextBlock)words.Children[1]).Name = "AlertDetail";
        Ui.Put(grid, words, 1);
        var cross = Ui.IconButton(IconKind.Close, "Fold", "AlertFold", fold); cross.Width = 24; cross.Height = 24; Ui.Put(grid, cross, 2);
        return grid;
    }
    private Control Facts(AlertItem item)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        switch (item.Kind)
        {
            case AlertKind.Reminder:
                if (item.Account is { } account) row.Children.Add(NoteParts.Chip("#" + account, "plain", lead: NoteParts.Dot(model, account, 8)));
                row.Children.Add(NoteParts.Chip(item.When ?? "", "late")); break;
            case AlertKind.Budget:
            case AlertKind.BudgetSpent:
                row.Children.Add(Bar(item.Ratio, item.Kind == AlertKind.BudgetSpent)); row.Children.Add(NoteParts.Ink(item.Caption, "Muted", 12.5)); break;
            default: row.Children.Add(NoteParts.Ink(item.Caption, "Muted", 12.5)); break;
        }
        return row;
    }
    private static Control Bar(double ratio, bool spent)
    {
        var fraction = Math.Clamp(ratio, 0, 1);
        var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 120 * fraction, Height = 6 };
        fill.Bind(Border.BackgroundProperty, fill.GetResourceObservable(spent ? "Overtime" : "BudgetWarning"));
        var track = new Border { Width = 120, Height = 6, CornerRadius = new CornerRadius(3), Name = "AlertBar", Child = fill, VerticalAlignment = VerticalAlignment.Center };
        track.Bind(Border.BackgroundProperty, track.GetResourceObservable("Line")); return track;
    }

    // ---- the actions
    private Control Actions(AlertItem item)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        Button Act(string name, string words, IconKind? icon, Action click, bool main = false)
        {
            var button = Ui.Ctl(name, words, icon, words, click, small: true); button.Margin = new Thickness(0, 0, 8, 0); button.FontSize = 12.5; button.Padding = new Thickness(11, 0);
            if (main) button.Classes.Add("accentOutline"); return button;
        }
        switch (item.Kind)
        {
            case AlertKind.Reminder:
                row.Children.Add(Act("AlertDone", "Done", IconKind.Check, () => { if (item.NoteId is { } id) model.Notes.Toggle(id); }, true));
                row.Children.Add(Act("AlertLater", "In 10 min", IconKind.Clock, () => { if (item.NoteId is { } id) model.Notes.SetReminder(id, model.Engine.Now.AddMinutes(10)); }));
                Button? reschedule = null; reschedule = Act("AlertReschedule", "Reschedule", null, () => Reschedule(item, reschedule!)); row.Children.Add(reschedule); break;
            case AlertKind.Break:
                row.Children.Add(Act("AlertResume", "Resume", null, () => model.ToggleBreak(), true));
                row.Children.Add(Act("AlertStay", "Stay on break", null, () => model.SnoozeAlert(item.Key, TimeSpan.FromMinutes(15)))); break;
            default:
                row.Children.Add(Act("AlertOpenWeek", "Open week", null, () => { fold(); model.RequestPage("week"); openDashboard(); }, true));
                row.Children.Add(Act("AlertOk", "OK", null, () => model.DismissAlert(item.Key))); break;
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

    // ---- the foot
    private Control NavFooter()
    {
        var grid = Ui.Grid("Auto,Auto,Auto,Auto,*,Auto"); grid.ColumnSpacing = 6; grid.Margin = new Thickness(-14, 12, -14, -2); grid.Name = "AlertNav";
        var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -7, 0, 0) }; line.Bind(Border.BackgroundProperty, line.GetResourceObservable("Line")); Grid.SetColumnSpan(line, 6); grid.Children.Add(line);
        var previous = Step("AlertPrevious", IconKind.Previous, "Previous", Previous); previous.Margin = new Thickness(14, 0, 0, 0); Ui.Put(grid, previous, 0);
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Name = "AlertDots" };
        for (var i = 0; i < items.Count; i++)
        {
            var at = i; var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Name = "AlertDot_" + i };
            dot.Bind(Border.BackgroundProperty, dot.GetResourceObservable(i == Index ? "Accent" : "Line"));
            var press = new Button { Name = "AlertDotButton_" + i, Content = dot, Padding = new Thickness(2, 6), Background = Brushes.Transparent, BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            press.Classes.Add("pillBare"); press.Click += (_, _) => ShowCard(at); Avalonia.Automation.AutomationProperties.SetName(press, $"Go to {at + 1} of {items.Count}"); dots.Children.Add(press);
        }
        Ui.Put(grid, dots, 1);
        Ui.Put(grid, Step("AlertNext", IconKind.Next, "Next", Next), 2);
        var of = NoteParts.Ink($"{Index + 1} of {items.Count}", "Muted", 12); of.Name = "AlertOf"; of.Margin = new Thickness(6, 0, 0, 0); Ui.Put(grid, of, 3);
        var all = SeeAll($"See all {items.Count}", "AlertSeeAll", ShowList); all.Margin = new Thickness(0, 0, 14, 0); Ui.Put(grid, all, 5);
        return grid;
    }
    private static Button Step(string name, IconKind icon, string tip, Action click)
    {
        var button = Ui.IconButton(icon, tip, name, click); button.Width = 24; button.Height = 24; return button;
    }
    private Button SeeAll(string words, string name, Action click)
    {
        var button = Ui.Button("", click, name); button.Classes.Add("pillBare"); button.Padding = new Thickness(2); button.HorizontalAlignment = HorizontalAlignment.Right;
        var text = NoteParts.Ink(words, "AccentText", 12.5); text.FontWeight = FontWeight.SemiBold;
        button.Content = Ui.Horizontal(text, new IconGlyph(IconKind.Next) { TintKey = "AccentText" }); ((StackPanel)button.Content).Spacing = 2; Avalonia.Automation.AutomationProperties.SetName(button, words); return button;
    }
    private Control DetailFooter()
    {
        var grid = Ui.Grid("Auto,*,Auto"); grid.Margin = new Thickness(0, 12, 0, 0); grid.Name = "AlertDetailFoot";
        var back = Ui.Button("", ShowList, "AlertBack"); back.Classes.Add("pillBare"); back.Padding = new Thickness(2);
        var words = NoteParts.Ink($"All {items.Count}", "AccentText", 12.5); words.FontWeight = FontWeight.SemiBold;
        back.Content = Ui.Horizontal(new IconGlyph(IconKind.Previous) { TintKey = "AccentText" }, words); ((StackPanel)back.Content).Spacing = 2; Avalonia.Automation.AutomationProperties.SetName(back, "Back to all"); Ui.Put(grid, back);
        var open = SeeAll("Open in dashboard", "AlertOpenDashboard", OpenNotes); Ui.Put(grid, open, 2);
        return grid;
    }
    private void OpenNotes() { fold(); model.RequestPage("notes"); openDashboard(); }

    // ---- everything
    private Control BuildList()
    {
        var column = new StackPanel { Spacing = 2 };
        var head = Ui.Grid("Auto,*,Auto"); head.ColumnSpacing = 6; head.Margin = new Thickness(0, 0, 0, 4);
        var back = Step("AlertListBack", IconKind.Previous, "Back", () => { if (items.Count > 0) ShowCard(Math.Min(Index, items.Count - 1)); else fold(); }); Ui.Put(head, back);
        var title = Ui.Horizontal(NoteParts.Ink("NEEDS ATTENTION", "Muted", 11.5), Count(items.Count)); title.Spacing = 8; title.VerticalAlignment = VerticalAlignment.Center; ((TextBlock)title.Children[0]).FontWeight = FontWeight.Bold; ((TextBlock)title.Children[0]).LetterSpacing = .6;
        Ui.Put(head, title, 1);
        var cross = Ui.IconButton(IconKind.Close, "Fold", "AlertFold", fold); cross.Width = 24; cross.Height = 24; Ui.Put(head, cross, 2);
        column.Children.Add(head);
        if (items.Count == 0) column.Children.Add(new TextBlock { Text = "Nothing is waiting.", FontSize = 13, Name = "AlertNothing", Margin = new Thickness(2, 6, 2, 8), Foreground = null });
        for (var i = 0; i < items.Count; i++) column.Children.Add(ListRow(i));
        var soon = model.Notes.ComingUp(3);
        if (soon.Count > 0)
        {
            var sub = NoteParts.Ink("COMING UP", "Muted", 11.5); sub.FontWeight = FontWeight.Bold; sub.LetterSpacing = .6; sub.Margin = new Thickness(2, 10, 0, 4); column.Children.Add(sub);
            foreach (var note in soon)
            {
                var due = NotesModel.ReminderOf(note)!.Value; var row = Ui.Grid("Auto,*"); row.ColumnSpacing = 10; row.Margin = new Thickness(4, 5);
                var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center }; dot.Bind(Border.BackgroundProperty, dot.GetResourceObservable("Idle")); Ui.Put(row, dot);
                var words = new StackPanel { Spacing = 1 }; words.Children.Add(new TextBlock { Text = model.Notes.Text(note), FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                words.Children.Add(NoteParts.Ink((model.Notes.AccountOf(note) is { } a ? a + " · " : "") + NoteDates.When(due, model.Engine.Now), "Muted", 11.5)); Ui.Put(row, words, 1); row.Name = "AlertComing_" + note.Id; column.Children.Add(row);
            }
        }
        // The head and the way on to the dashboard stay put; only the rows between them scroll, so neither is ever pushed out of reach.
        var body = new StackPanel(); column.Children.Remove(head); body.Children.Add(column);
        var scroll = new ScrollViewer { Name = "AlertListScroll", Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var open = SeeAll("Open Notes in the dashboard", "AlertOpenDashboard", OpenNotes); open.HorizontalAlignment = HorizontalAlignment.Left; open.Margin = new Thickness(0, 8, 0, 0);
        var frame = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") }; Grid.SetRow(scroll, 1); Grid.SetRow(open, 2); frame.Children.Add(head); frame.Children.Add(scroll); frame.Children.Add(open);
        return frame;
    }
    private static Border Count(int n)
    {
        var chip = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 0), Height = 18, VerticalAlignment = VerticalAlignment.Center, Name = "AlertCount", Child = NoteParts.Ink(n.ToString(), "Overtime", 11) };
        chip.Bind(Border.BackgroundProperty, chip.GetResourceObservable("LateTint")); return chip;
    }
    private Control ListRow(int index)
    {
        var item = items[index];
        var grid = Ui.Grid("Auto,*,Auto"); grid.ColumnSpacing = 10;
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center }; dot.Bind(Border.BackgroundProperty, dot.GetResourceObservable(item.LightToken == "Accent" ? "Overtime" : item.LightToken)); Ui.Put(grid, dot);
        var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = item.Kind is AlertKind.Budget or AlertKind.BudgetSpent ? item.Account + ": " + item.Detail.ToLowerInvariant() : item.Words, FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        words.Children.Add(NoteParts.Ink(item.Kind switch { AlertKind.Reminder => (item.Account is { } a ? a + " · " : "") + item.Detail, AlertKind.Break => item.Detail, _ => item.Caption.Split(" · ").LastOrDefault() ?? item.Caption }, item.Token, 11.5));
        var press = new Button { Name = "AlertRow_" + index, Content = words, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        press.Classes.Add("pillBare"); press.Click += (_, _) => ShowDetail(index); Avalonia.Automation.AutomationProperties.SetName(press, item.Words);
        Ui.Put(grid, press, 1);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (item.Kind == AlertKind.Reminder && item.NoteId is { } id)
        {
            buttons.Children.Add(Small("AlertRowDone_" + index, IconKind.Check, "Done", () => model.Notes.Toggle(id)));
            Button? later = null; later = Small("AlertRowLater_" + index, IconKind.Clock, "In 10 min", () => model.Notes.SetReminder(id, model.Engine.Now.AddMinutes(10))); buttons.Children.Add(later);
        }
        else buttons.Children.Add(Small("AlertRowOk_" + index, IconKind.Check, item.Kind == AlertKind.Break ? "Stay on break" : "OK", () => { if (item.Kind == AlertKind.Break) model.SnoozeAlert(item.Key, TimeSpan.FromMinutes(15)); else model.DismissAlert(item.Key); }));
        Ui.Put(grid, buttons, 2);
        var card = new Border { Child = grid, CornerRadius = new CornerRadius(10), Padding = new Thickness(4, 5), Name = "AlertListRow_" + index };
        return card;
    }
    private static Button Small(string name, IconKind icon, string tip, Action click) { var b = Ui.IconButton(icon, tip, name, click); b.Width = 26; b.Height = 26; return b; }
}
