using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>The settings, as a dialog over the dashboard, so they open the same way from the pill, the desk and the dashboard.</summary>
public sealed class SettingsDialog : Dialog
{
    public SettingsDialog(TimerModel model)
    {
        Title = "Settings"; Width = 940; Height = 640; // The card keeps this size, so moving between categories never makes it jump.
        Content = new SettingsView(model);
    }
}

/// <summary>
/// The settings, in categories down the left with one pane each on the right: General, Appearance, Pill, Desk, Working hours, Accounts and budgets,
/// About, and (once unlocked) Development. Every pane is built once and swapped in, so nothing a person has typed is lost by looking at another one.
/// </summary>
public sealed class SettingsView : Grid
{
    /// <summary>How many quick clicks on the About tile unlock the developer features, and the longest pause allowed between them.</summary>
    public const int UnlockClicks = 10;
    public static readonly TimeSpan UnlockWindow = TimeSpan.FromMilliseconds(700);
    private readonly TimerModel model;
    private readonly List<Action> syncs = new();
    private readonly Dictionary<string, Button> categoryButtons = new();
    private readonly Dictionary<string, Control> panes = new();
    private readonly ScrollViewer scroll = new() { Name = "SettingsPage", AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private readonly Border accountsHost = new() { Name = "AccountsPane", IsVisible = false };
    private static readonly (string Key, string Title)[] Categories =
    {
        ("general", "General"), ("appearance", "Appearance"), ("pill", "Pill"), ("desk", "Desk"), ("hours", "Working hours"),
        ("accounts", "Accounts and budgets"), ("about", "About"), ("development", "Development"),
    };
    public string Current { get; private set; } = "general";

    public SettingsView(TimerModel model)
    {
        this.model = model;
        ColumnDefinitions = new ColumnDefinitions("210,*");

        panes["general"] = General();
        panes["appearance"] = Appearance();
        panes["pill"] = PillPane();
        panes["desk"] = DeskPane();
        panes["hours"] = Pane("Working hours", new ShiftEditor(model, () => { }, null, week: true));
        BuildAccounts();
        panes["development"] = Development();
        panes["about"] = About();

        var rail = new StackPanel { Spacing = 2, Margin = new Thickness(16, 4, 8, 16) };
        foreach (var (key, title) in Categories)
        {
            var button = Ui.Button(title, () => Select(key), "SettingsCategory_" + key);
            button.Classes.Add("settingsCategory"); categoryButtons[key] = button; rail.Children.Add(button);
        }
        // Development is only there once unlocked.
        categoryButtons["development"].IsVisible = model.DeveloperUnlocked;
        syncs.Add(() => { categoryButtons["development"].IsVisible = model.DeveloperUnlocked; if (!model.DeveloperUnlocked && Current == "development") Select("about"); });
        Ui.Put(this, rail);
        var content = new Grid(); content.Children.Add(scroll); content.Children.Add(accountsHost);
        Ui.Put(this, content, 1);
        Select("general");

        // The dialog is built once, so it listens for changes made elsewhere (the right-click menu's decimal hours, a theme edit) and keeps its boxes true.
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { foreach (var sync in syncs) sync(); }
        AttachedToVisualTree += (_, _) => model.PropertyChanged += Changed;
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
    }

    /// <summary>Shows one category's pane.</summary>
    public void Select(string key)
    {
        if (!panes.ContainsKey(key)) return;
        Current = key;
        foreach (var (name, button) in categoryButtons) button.Classes.Set("selected", name == key);
        // The accounts editor has a list that scrolls by itself, so it sits straight in the dialog; the rest share one scroll viewer.
        var own = key == "accounts";
        accountsHost.IsVisible = own; scroll.IsVisible = !own;
        if (!own) { scroll.Content = panes[key]; scroll.Offset = default; }
    }

    private static StackPanel Pane(string title, params Control[] parts)
    {
        var pane = new StackPanel { Spacing = 12, Margin = new Thickness(8, 4, 24, 20) };
        pane.Children.Add(Ui.Heading(title, 22));
        foreach (var part in parts) pane.Children.Add(part);
        return pane;
    }
    private static TextBlock Group(string text) { var label = Ui.Text(text, 13, "muted"); label.Margin = new Thickness(0, 10, 0, 0); return label; }

    private Control General()
    {
        var pane = Pane("General");
        pane.Children.Add(Group("Hours"));
        pane.Children.Add(Toggle("Decimal hours (1.50h instead of 1h 30m)", "DecimalHours", () => model.DecimalHours, _ => model.ToggleFormat()));
        pane.Children.Add(Toggle("Move later blocks without asking when a longer entry runs into them", "MoveLaterAlways", () => model.MoveLaterAlways, v => model.SetMoveLaterAlways(v)));
        pane.Children.Add(Group("Windows"));
        pane.Children.Add(Toggle("Keep the dashboard on top", "DashboardOnTop", () => model.DashboardOnTop, v => model.SetDashboardOnTop(v)));
        if (model.TrayAvailable) pane.Children.Add(Toggle("Show in taskbar", "ShowInTaskbar", () => model.TaskbarButton, v => model.SetTaskbarButton(v)));
        if (model.AutoStart?.Available == true) pane.Children.Add(Toggle("Start with Windows", "StartWithWindows", () => model.AutoStart?.IsEnabled == true, v => model.SetAutoStart(v)));
        pane.Children.Add(Toggle("Show tooltips", "ShowTooltips", () => model.Tooltips, v => model.SetTooltips(v)));
        var tipNote = Ui.Text("With tooltips off, budget percentages are still in Week overview.", 12, "muted"); tipNote.Name = "TooltipsNote"; tipNote.TextWrapping = Avalonia.Media.TextWrapping.Wrap; pane.Children.Add(tipNote);
        // Quick capture: a small box for a note from anywhere. The shortcut is set by pressing it in the box; it needs Ctrl, Alt or the Windows key.
        pane.Children.Add(Group("Quick capture"));
        var shortcut = new TextBox { Name = "CaptureShortcut", IsReadOnly = true, Width = 200, Watermark = "Press a shortcut", Text = model.CaptureShortcut?.ToString() ?? "" };
        shortcut.KeyDown += (_, e) =>
        {
            if (GlobalHotkey.FromKey(e.Key, e.KeyModifiers) is { } spec) { model.SetCaptureShortcut(spec); e.Handled = true; }
            else if (e.Key is Avalonia.Input.Key.Back or Avalonia.Input.Key.Delete) { model.SetCaptureShortcut(null); e.Handled = true; }
        };
        var clearShortcut = Ui.DialogButton("Clear", () => model.SetCaptureShortcut(null), "CaptureShortcutClear");
        pane.Children.Add(Row("Shortcut", Ui.Horizontal(shortcut, clearShortcut)));
        pane.Children.Add(Row("Open now", Ui.DialogButton("Quick capture", () => model.RequestQuickCapture(), "CaptureOpen")));
        var captureNote = Ui.Text("Press the keys in the box, with Ctrl, Alt or Win. The pill's menu has Add note too.", 12, "muted"); captureNote.TextWrapping = Avalonia.Media.TextWrapping.Wrap; pane.Children.Add(captureNote);
        syncs.Add(() => { var shown = model.CaptureShortcut?.ToString() ?? ""; if (shortcut.Text != shown) shortcut.Text = shown; });
        // The getting started setup, for anyone who skipped it or wants to look again. Nothing is lost: it starts from the current settings.
        pane.Children.Add(Group("Getting started"));
        pane.Children.Add(Row("Setup", Ui.DialogButton("Run setup again", () =>
        {
            // The setup lays itself over the dashboard, so this dialog steps out of its way first.
            this.GetVisualAncestors().OfType<Dialog>().FirstOrDefault()?.Close(); model.RestartSetup();
        }, "RunSetup")));
        return pane;
    }

    private Control Appearance()
    {
        List<string> ThemeNames() => new[] { ThemeLibrary.System }.Concat(model.Themes.Select(t => t.Name)).ToList();
        var themeNames = ThemeNames();
        var theme = new ComboBox { Name = "ThemeChoice", ItemsSource = themeNames, SelectedItem = themeNames.Contains(model.Theme) ? model.Theme : ThemeLibrary.System, MinWidth = 200 };
        theme.SelectionChanged += (_, _) => { if (theme.SelectedItem is string chosen && chosen != model.Theme) model.SetTheme(chosen); };
        syncs.Add(() =>
        {
            // The dialog is built once, so themes made or deleted in the editor afterwards are picked up here.
            var current = ThemeNames();
            if (!current.SequenceEqual(themeNames)) { themeNames = current; theme.ItemsSource = themeNames; }
            var wanted = themeNames.Contains(model.Theme) ? model.Theme : ThemeLibrary.System; if (!Equals(theme.SelectedItem, wanted)) theme.SelectedItem = wanted;
        });
        var editThemes = Ui.DialogButton("Edit themes…", () => { if (TopLevel.GetTopLevel(this) is DashboardWindow dashboard) dashboard.Dialogs.Show(new ThemesDialog(model)); }, "EditThemes");
        var pane = Pane("Appearance");
        pane.Children.Add(Row("Theme", Ui.Horizontal(theme, editThemes)));
        pane.Children.Add(Toggle("Window shadows", "WindowShadows", () => model.Shadows, v => model.SetShadows(v)));
        return pane;
    }

    private Control PillPane()
    {
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
        var pane = Pane("Pill");
        // The choices need the whole width to sit side by side, so they go under their label instead of in a row.
        pane.Children.Add(Group("Layout")); pane.Children.Add(PillLayoutChoices(model, syncs));
        pane.Children.Add(Group("Size and position"));
        pane.Children.Add(Row("Pill size", pillSize));
        pane.Children.Add(Toggle("Keep the pill on top", "PillOnTop", () => model.PillOnTop, v => model.SetPillOnTop(v)));
        return pane;
    }

    private Control DeskPane()
    {
        var pane = Pane("Desk");
        pane.Children.Add(Toggle("Calm motion: tiles move without bouncing", "CalmMotion", () => model.Calm, v => model.SetCalm(v)));
        return pane;
    }

    // The accounts list is edited right here. After Save or Revert it is built again from what is now saved.
    private void BuildAccounts()
    {
        void Rebuild()
        {
            var body = new AccountsDialog(model, Rebuild).TakeBody(); body.Margin = new Thickness(8, 0, 12, 12);
            var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            var heading = Ui.Heading("Accounts and budgets", 22); heading.Margin = new Thickness(8, 4, 0, 8);
            layout.Children.Add(heading); Ui.Put(layout, body, row: 1);
            accountsHost.Child = layout;
        }
        Rebuild();
        panes["accounts"] = accountsHost;
    }

    private Control Development()
    {
        // Hidden until unlocked: ten quick clicks on the About tile. Once unlocked it holds the sample-data demo and a switch to hide the features again.
        // In the demo the developer features are simply on: there is no demo to open from inside it, and the switch is fixed on and greyed out.
        var openDemo = Row("Sample data", Ui.DialogButton("Open sample-data demo", Ui.StartDemo, "OpenDemo")); openDemo.IsVisible = !model.IsDemo;
        var inDemo = Ui.Text("You are in the sample-data demo, so the developer features are on. Nothing here is saved.", 13, "muted"); inDemo.Name = "InDemoNote"; inDemo.TextWrapping = Avalonia.Media.TextWrapping.Wrap; inDemo.IsVisible = model.IsDemo;
        var developerToggle = Toggle("Developer features (overtime tester and version on the right-click menu)", "DeveloperFeatures", () => model.DeveloperUnlocked, v => model.SetDeveloper(v));
        developerToggle.IsEnabled = !model.IsDemo;
        var pane = Pane("Development", openDemo, inDemo, developerToggle); pane.Name = "DevelopmentSection";
        return pane;
    }

    private Control About()
    {
        var pane = Pane("About");
        var about = Ui.Text(BuildInfo.Current, 13, "muted"); about.Name = "AboutVersion"; pane.Children.Add(about);
        var unlockHint = Ui.Text("", 12, "muted"); unlockHint.Name = "UnlockHint"; unlockHint.IsVisible = false; pane.Children.Add(unlockHint);
        pane.Name = "AboutSection";
        // Ten presses, each soon after the one before, unlock the developer features. A pause starts the count again, so ordinary clicks never do it.
        var presses = 0; var lastPress = DateTime.MinValue;
        pane.PointerPressed += (_, e) =>
        {
            if (!BuildInfo.DeveloperFeatures || model.DeveloperUnlocked) return;
            var now = DateTime.UtcNow; presses = now - lastPress <= UnlockWindow ? presses + 1 : 1; lastPress = now;
            if (presses >= UnlockClicks) { model.SetDeveloper(true); presses = 0; unlockHint.Text = "Developer features unlocked."; unlockHint.IsVisible = true; }
            else if (presses >= UnlockClicks - 4) { unlockHint.Text = $"{UnlockClicks - presses} more clicks to unlock developer features."; unlockHint.IsVisible = true; }
        };
        pane.Background = Avalonia.Media.Brushes.Transparent; // So a press anywhere on the pane counts, not only on its text.
        return pane;
    }

    // Each layout is a small picture of the pill with its name and what it shows, so the choice is clear before it is made. The chosen one has the accent outline.
    private static Control PillLayoutChoices(TimerModel model, List<Action> syncs)
    {
        static Border Bar(double width, double height, string key, double radius = 3)
        {
            var bar = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(radius), VerticalAlignment = VerticalAlignment.Center };
            bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable(key)); return bar;
        }
        static Control Dots(int count) { var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center }; for (var i = 0; i < count; i++) row.Children.Add(Bar(11, 11, "Line", 6)); return row; }
        static Control Lines(bool progress)
        {
            var stack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(Bar(46, 5, "Ink")); stack.Children.Add(Bar(30, 4, "Muted"));
            if (progress) stack.Children.Add(Bar(46, 3, "Line"));
            return stack;
        }
        static Control Picture(string key)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            switch (key)
            {
                case "compact": row.Children.Add(Bar(12, 12, "Accent", 4)); row.Children.Add(Lines(false)); row.Children.Add(Bar(34, 9, "Ink")); row.Children.Add(Dots(2)); break;
                case "minimal": row.Children.Add(Bar(14, 14, "Accent", 5)); row.Children.Add(Bar(34, 9, "Ink")); row.Children.Add(Dots(1)); break;
                default: row.Children.Add(Bar(34, 9, "Ink")); row.Children.Add(Bar(1, 20, "Line", 0)); row.Children.Add(Bar(12, 12, "Accent", 4)); row.Children.Add(Lines(true)); row.Children.Add(Dots(2)); break;
            }
            var frame = new Border { Height = 46, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = row, Padding = new Thickness(6, 0) };
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
