using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The settings page, shown in the dashboard's workspace. Everything that used to hang off the settings menu lives here in sections:
/// working hours, how the app looks and behaves, hours format, accounts, and (in development builds) the sample-data demo.
/// </summary>
public sealed class SettingsView : ScrollViewer
{
    // Styles and the scrolling template are found by exact type, so a subclass must borrow ScrollViewer's or it draws no scroll bar and never scrolls.
    protected override Type StyleKeyOverride => typeof(ScrollViewer);
    /// <summary>How many quick clicks on the About tile unlock the developer features, and the longest pause allowed between them.</summary>
    public const int UnlockClicks = 10;
    public static readonly TimeSpan UnlockWindow = TimeSpan.FromMilliseconds(700);
    /// <summary>The clock the quick clicks are timed by; tests replace it, because a slow runner can take longer than the window to deliver ten clicks.</summary>
    public static Func<DateTime> UnlockClock { get; set; } = () => DateTime.UtcNow;
    private readonly TimerModel model;
    private readonly List<Action> syncs = new();
    public SettingsView(TimerModel model)
    {
        this.model = model;
        Name = "SettingsPage"; AllowAutoHide = false;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        var page = new StackPanel { Spacing = 16, Margin = new Thickness(0, 0, 14, 8), HorizontalAlignment = HorizontalAlignment.Stretch };

        // Working hours, with the same form as its window.
        page.Children.Add(Section("Working hours", new ShiftEditor(model, () => { }, null)));

        // How the app looks and behaves.
        List<string> ThemeNames() => new[] { ThemeLibrary.System }.Concat(model.Themes.Select(t => t.Name)).ToList();
        var themeNames = ThemeNames();
        var theme = new ComboBox { Name = "ThemeChoice", ItemsSource = themeNames, SelectedItem = themeNames.Contains(model.Theme) ? model.Theme : ThemeLibrary.System, MinWidth = 200 };
        theme.SelectionChanged += (_, _) => { if (theme.SelectedItem is string chosen && chosen != model.Theme) model.SetTheme(chosen); };
        syncs.Add(() =>
        {
            // The page is built once, so themes made or deleted in the editor afterwards are picked up here.
            var current = ThemeNames();
            if (!current.SequenceEqual(themeNames)) { themeNames = current; theme.ItemsSource = themeNames; }
            var wanted = themeNames.Contains(model.Theme) ? model.Theme : ThemeLibrary.System; if (!Equals(theme.SelectedItem, wanted)) theme.SelectedItem = wanted;
        });
        var editThemes = Ui.DialogButton("Edit themes…", () => { if (TopLevel.GetTopLevel(this) is DashboardWindow dashboard) dashboard.Dialogs.Show(new ThemesDialog(model)); }, "EditThemes");
        var appearance = new StackPanel { Spacing = 10 };
        appearance.Children.Add(Row("Theme", Ui.Horizontal(theme, editThemes)));
        // The pill is sized for the screen it is on. This overrides that with a size of your own.
        var pillChoices = new List<string> { "Automatic" }; pillChoices.AddRange(TimerModel.PillScaleChoices.Select(p => p + "%"));
        string PillText(int? percent) => percent is { } p ? p + "%" : "Automatic";
        var pillSize = new ComboBox { Name = "PillSize", ItemsSource = pillChoices, SelectedItem = PillText(model.PillScalePercent), MinWidth = 140 };
        ToolTip.SetTip(pillSize, "Automatic fits the pill to the screen.");
        pillSize.SelectionChanged += (_, _) =>
        {
            if (pillSize.SelectedItem is not string chosen || chosen == PillText(model.PillScalePercent)) return;
            model.SetPillScale(chosen == "Automatic" ? null : int.Parse(chosen.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture));
        };
        syncs.Add(() => { if (!Equals(pillSize.SelectedItem, PillText(model.PillScalePercent))) pillSize.SelectedItem = PillText(model.PillScalePercent); });
        appearance.Children.Add(Row("Pill size", pillSize));
        // The three pictures need the page's whole width, so the label sits above them instead of in the narrow column beside them.
        var layoutLabel = Ui.Text("Pill layout", 14, "muted"); layoutLabel.Margin = new Thickness(0, 4, 0, 0); appearance.Children.Add(layoutLabel);
        appearance.Children.Add(PillLayoutChoices(model, syncs));
        appearance.Children.Add(Toggle("Keep the pill on top", "PillOnTop", () => model.PillOnTop, v => model.SetPillOnTop(v)));
        appearance.Children.Add(Toggle("Keep the dashboard on top", "DashboardOnTop", () => model.DashboardOnTop, v => model.SetDashboardOnTop(v)));
        appearance.Children.Add(Toggle("Window shadows", "WindowShadows", () => model.Shadows, v => model.SetShadows(v)));
        appearance.Children.Add(Toggle("Show tooltips", "ShowTooltips", () => model.Tooltips, v => model.SetTooltips(v)));
        var tipNote = Ui.Text("With tooltips off, budget percentages are still in Week overview.", 12, "muted"); tipNote.Name = "TooltipsNote"; tipNote.TextWrapping = Avalonia.Media.TextWrapping.Wrap; appearance.Children.Add(tipNote);
        if (model.TrayAvailable) appearance.Children.Add(Toggle("Show in taskbar", "ShowInTaskbar", () => model.TaskbarButton, v => model.SetTaskbarButton(v)));
        if (model.AutoStart?.Available == true) appearance.Children.Add(Toggle("Start with Windows", "StartWithWindows", () => model.AutoStart?.IsEnabled == true, v => model.SetAutoStart(v)));
        page.Children.Add(Section("Appearance and behaviour", appearance));

        // Units, and the accounts the day is divided between.
        var general = new StackPanel { Spacing = 10 };
        general.Children.Add(Toggle("Decimal hours (1.50h instead of 1h 30m)", "DecimalHours", () => model.DecimalHours, _ => model.ToggleFormat()));
        general.Children.Add(Toggle("Move later blocks without asking when a longer entry runs into them", "MoveLaterAlways", () => model.MoveLaterAlways, v => model.SetMoveLaterAlways(v)));
        general.Children.Add(Row("Client accounts", Ui.DialogButton("Accounts and budgets…", () => { if (TopLevel.GetTopLevel(this) is DashboardWindow dashboard) dashboard.Dialogs.Show(new AccountsDialog(model)); }, "EditAccounts")));
        page.Children.Add(Section("Hours and accounts", general));

        // Quick capture: a small box for a note from anywhere. The shortcut is set by pressing it in the box; it needs Ctrl, Alt or the Windows key.
        var capture = new StackPanel { Spacing = 10 };
        var shortcut = new TextBox { Name = "CaptureShortcut", IsReadOnly = true, Width = 200, Watermark = "Press a shortcut", Text = model.CaptureShortcut?.ToString() ?? "" };
        shortcut.KeyDown += (_, e) =>
        {
            if (GlobalHotkey.FromKey(e.Key, e.KeyModifiers) is { } spec) { model.SetCaptureShortcut(spec); e.Handled = true; }
            else if (e.Key is Avalonia.Input.Key.Back or Avalonia.Input.Key.Delete) { model.SetCaptureShortcut(null); e.Handled = true; }
        };
        capture.Children.Add(Row("Shortcut", Ui.Horizontal(shortcut, Ui.DialogButton("Clear", () => model.SetCaptureShortcut(null), "CaptureShortcutClear"))));
        capture.Children.Add(Row("Open now", Ui.DialogButton("Quick capture", () => model.RequestQuickCapture(), "CaptureOpen")));
        var captureNote = Ui.Text("Press the keys in the box, with Ctrl, Alt or Win. The right-click menu has Add note too.", 12, "muted"); captureNote.TextWrapping = Avalonia.Media.TextWrapping.Wrap; capture.Children.Add(captureNote);
        syncs.Add(() => { var shown = model.CaptureShortcut?.ToString() ?? ""; if (shortcut.Text != shown) shortcut.Text = shown; });
        page.Children.Add(Section("Quick capture", capture));

        // Reminders: notes with a time (and deadlines) say so on the pill and the dashboard when they are near, due and overdue.
        var remind = new StackPanel { Spacing = 10 };
        remind.Children.Add(Toggle("Remind me of notes with a time", "RemindersOn", () => model.RemindersOn, v => model.SetRemindersOn(v)));
        remind.Children.Add(Toggle("Play a short sound when a reminder is due", "ReminderSound", () => model.ReminderSoundOn, v => model.SetReminderSoundOn(v)));
        string Minutes(int m) => m == 0 ? "None" : m + " minutes";
        ComboBox Minutes2(string name, int[] choices, Func<int> get, Action<int> set)
        {
            var box = new ComboBox { Name = name, ItemsSource = choices.Select(Minutes).ToList(), SelectedItem = Minutes(get()), MinWidth = 140 };
            box.SelectionChanged += (_, _) => { if (box.SelectedItem is string chosen && chosen != Minutes(get())) set(choices.First(c => Minutes(c) == chosen)); };
            syncs.Add(() => { if (!Equals(box.SelectedItem, Minutes(get()))) box.SelectedItem = Minutes(get()); });
            return box;
        }
        remind.Children.Add(Row("Heads-up before", Minutes2("ReminderLead", ReminderRules.LeadChoices, () => model.ReminderLead, model.SetReminderLead)));
        remind.Children.Add(Row("Snooze for", Minutes2("ReminderSnooze", ReminderRules.SnoozeChoices, () => model.ReminderSnooze, model.SetReminderSnooze)));
        var remindNote = Ui.Text("A heads-up is a quiet dot. At the time a card appears with Snooze and Done, and stays until the note is ticked off. Deadlines with no time remind at 17:00.", 12, "muted"); remindNote.TextWrapping = Avalonia.Media.TextWrapping.Wrap; remind.Children.Add(remindNote);
        page.Children.Add(Section("Reminders", remind));

        // The getting started setup, for anyone who skipped it or wants to look again. Nothing is lost: it starts from the current settings.
        page.Children.Add(Section("Getting started", Row("Setup", Ui.DialogButton("Run setup again", () => model.RestartSetup(), "RunSetup"))));
        // Hidden until unlocked: ten quick clicks on the About tile. Once unlocked it holds the sample-data demo and a switch to hide the features again.
        var developer = new StackPanel { Spacing = 10 };
        // In the demo the developer features are simply on: there is no demo to open from inside it, and the switch is fixed on and greyed out.
        var openDemo = Row("Sample data", Ui.DialogButton("Open sample-data demo", Ui.StartDemo, "OpenDemo")); openDemo.IsVisible = !model.IsDemo;
        developer.Children.Add(openDemo);
        var inDemo = Ui.Text("You are in the sample-data demo, so the developer features are on. Nothing here is saved.", 13, "muted"); inDemo.Name = "InDemoNote"; inDemo.TextWrapping = Avalonia.Media.TextWrapping.Wrap; inDemo.IsVisible = model.IsDemo;
        developer.Children.Add(inDemo);
        var developerToggle = Toggle("Developer features (overtime tester and version on the right-click menu)", "DeveloperFeatures", () => model.DeveloperUnlocked, v => model.SetDeveloper(v));
        developerToggle.IsEnabled = !model.IsDemo;
        developer.Children.Add(developerToggle);
        var developerSection = Section("Development", developer); developerSection.Name = "DevelopmentSection";
        developerSection.IsVisible = model.DeveloperUnlocked;
        syncs.Add(() => developerSection.IsVisible = model.DeveloperUnlocked);
        page.Children.Add(developerSection);
        var aboutText = new StackPanel { Spacing = 4 };
        var about = Ui.Text(BuildInfo.Current, 13, "muted"); about.Name = "AboutVersion"; aboutText.Children.Add(about);
        var unlockHint = Ui.Text("", 12, "muted"); unlockHint.Name = "UnlockHint"; unlockHint.IsVisible = false; aboutText.Children.Add(unlockHint);
        var aboutSection = Section("About", aboutText); aboutSection.Name = "AboutSection";
        // Ten presses, each soon after the one before, unlock the developer features. A pause starts the count again, so ordinary clicks never do it.
        var presses = 0; var lastPress = DateTime.MinValue;
        aboutSection.PointerPressed += (_, e) =>
        {
            if (!BuildInfo.DeveloperFeatures || model.DeveloperUnlocked) return;
            var now = UnlockClock(); presses = now - lastPress <= UnlockWindow ? presses + 1 : 1; lastPress = now;
            if (presses >= UnlockClicks) { model.SetDeveloper(true); presses = 0; unlockHint.Text = "Developer features unlocked."; unlockHint.IsVisible = true; }
            else if (presses >= UnlockClicks - 4) { unlockHint.Text = $"{UnlockClicks - presses} more clicks to unlock developer features."; unlockHint.IsVisible = true; }
        };
        page.Children.Add(aboutSection);
        Content = page;

        // The page is built once, so it listens for changes made elsewhere (the right-click menu's decimal hours, a theme edit) and keeps its boxes true.
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { foreach (var sync in syncs) sync(); }
        AttachedToVisualTree += (_, _) => model.PropertyChanged += Changed;
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
    }

    private static Border Section(string title, Control body)
    {
        var stack = new StackPanel { Spacing = 12, Margin = new Thickness(20, 16) };
        stack.Children.Add(Ui.Heading(title, 18));
        stack.Children.Add(body);
        var card = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Child = stack };
        card.Classes.Add("dayCard");
        return card;
    }
    // Each layout is a small picture of the pill with its name and what it shows, so the choice is clear before it is made. The chosen one has the accent outline.
    private static Control PillLayoutChoices(TimerModel model, List<Action> syncs)
    {
        static Border Bar(double width, double height, string key, double radius = 3)
        {
            var bar = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(radius), VerticalAlignment = VerticalAlignment.Center };
            bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable(key)); return bar;
        }
        static Control Dots(int count) { var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center }; for (var i = 0; i < count; i++) row.Children.Add(Bar(9, 9, "Line", 5)); return row; }
        static Control Lines(bool progress)
        {
            var stack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(Bar(34, 5, "Ink")); stack.Children.Add(Bar(22, 4, "Muted"));
            if (progress) stack.Children.Add(Bar(34, 3, "Line"));
            return stack;
        }
        static Control Picture(string key)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            switch (key)
            {
                case "compact": row.Children.Add(Bar(11, 11, "Accent", 4)); row.Children.Add(Lines(false)); row.Children.Add(Bar(28, 9, "Ink")); row.Children.Add(Dots(2)); break;
                case "minimal": row.Children.Add(Bar(14, 14, "Accent", 5)); row.Children.Add(Bar(34, 9, "Ink")); row.Children.Add(Dots(1)); break;
                default: row.Children.Add(Bar(28, 9, "Ink")); row.Children.Add(Bar(1, 20, "Line", 0)); row.Children.Add(Bar(11, 11, "Accent", 4)); row.Children.Add(Lines(true)); row.Children.Add(Dots(2)); break;
            }
            var frame = new Border { Height = 46, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = row, Padding = new Thickness(6, 0), ClipToBounds = true };
            frame.Classes.Add("chip"); return frame;
        }
        var choices = new WrapPanel { Orientation = Orientation.Horizontal };
        var buttons = new List<(string Key, Button Button)>();
        foreach (var layout in PillLayouts.All)
        {
            var text = new StackPanel { Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };
            text.Children.Add(Ui.Text(layout.Title, 14));
            var note = Ui.Text(layout.Description, 12, "muted"); note.FontWeight = Avalonia.Media.FontWeight.Normal; note.TextWrapping = Avalonia.Media.TextWrapping.Wrap; text.Children.Add(note);
            var card = new StackPanel { Width = 146 }; card.Children.Add(Picture(layout.Key)); card.Children.Add(text);
            var button = Ui.DialogButton("", () => model.SetPillStyle(layout.Key), "PillLayout_" + layout.Key, false);
            button.Content = card; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Margin = new Thickness(0, 0, 10, 10); button.Padding = new Thickness(10);
            button.VerticalAlignment = VerticalAlignment.Top;
            buttons.Add((layout.Key, button)); choices.Children.Add(button);
        }
        void Mark() { foreach (var (key, button) in buttons) button.Classes.Set("mainAction", key == model.PillStyle); }
        syncs.Add(Mark); Mark();
        return choices;
    }
    private static Grid Row(string label, Control control)
    {
        var row = Ui.Grid("170,*");
        var text = Ui.Text(label, 14, "muted"); text.VerticalAlignment = VerticalAlignment.Center;
        Ui.Put(row, text); Ui.Put(row, control, 1);
        return row;
    }
    private CheckBox Toggle(string label, string name, Func<bool> read, Action<bool> write)
    {
        var box = new CheckBox { Content = label, Name = name, IsChecked = read() };
        // Clicking applies the change and then reads the truth back, so a refused change (such as hiding the taskbar button with no tray) never leaves the box lying.
        box.Click += (_, _) => { write(box.IsChecked == true); box.IsChecked = read(); };
        syncs.Add(() => { if (box.IsChecked != read()) box.IsChecked = read(); });
        return box;
    }
}
