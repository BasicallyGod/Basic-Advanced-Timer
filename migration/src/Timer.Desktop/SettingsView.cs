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
        var themeNames = new[] { ThemeLibrary.System }.Concat(model.Themes.Select(t => t.Name)).ToList();
        var theme = new ComboBox { Name = "ThemeChoice", ItemsSource = themeNames, SelectedItem = themeNames.Contains(model.Theme) ? model.Theme : ThemeLibrary.System, MinWidth = 200 };
        theme.SelectionChanged += (_, _) => { if (theme.SelectedItem is string chosen && chosen != model.Theme) model.SetTheme(chosen); };
        syncs.Add(() => { var wanted = themeNames.Contains(model.Theme) ? model.Theme : ThemeLibrary.System; if (!Equals(theme.SelectedItem, wanted)) theme.SelectedItem = wanted; });
        var editThemes = Ui.Button("Edit themes…", () => { if (TopLevel.GetTopLevel(this) is Window owner) new ThemesWindow(model).Show(owner); }, "EditThemes");
        var appearance = new StackPanel { Spacing = 10 };
        appearance.Children.Add(Row("Theme", Ui.Horizontal(theme, editThemes)));
        appearance.Children.Add(Toggle("Window shadows", "WindowShadows", () => model.Shadows, v => model.SetShadows(v)));
        appearance.Children.Add(Toggle("Show tooltips", "ShowTooltips", () => model.Tooltips, v => model.SetTooltips(v)));
        if (model.TrayAvailable) appearance.Children.Add(Toggle("Show in taskbar", "ShowInTaskbar", () => model.TaskbarButton, v => model.SetTaskbarButton(v)));
        if (model.AutoStart?.Available == true) appearance.Children.Add(Toggle("Start with Windows", "StartWithWindows", () => model.AutoStart?.IsEnabled == true, v => model.SetAutoStart(v)));
        page.Children.Add(Section("Appearance and behaviour", appearance));

        // Units, and the accounts the day is divided between.
        var general = new StackPanel { Spacing = 10 };
        general.Children.Add(Toggle("Decimal hours (1.50h instead of 1h 30m)", "DecimalHours", () => model.DecimalHours, _ => model.ToggleFormat()));
        general.Children.Add(Row("Client accounts", Ui.Button("Accounts and budgets…", () => { if (TopLevel.GetTopLevel(this) is Window owner) new AccountsWindow(model).Show(owner); }, "EditAccounts")));
        page.Children.Add(Section("Hours and accounts", general));

        // Hidden until unlocked: ten quick clicks on the About tile. Once unlocked it holds the sample-data demo and a switch to hide the features again.
        var developer = new StackPanel { Spacing = 10 };
        developer.Children.Add(Row("Sample data", Ui.Button("Open sample-data demo", Ui.StartDemo, "OpenDemo")));
        developer.Children.Add(Toggle("Developer features (overtime tester and version on the right-click menu)", "DeveloperFeatures", () => model.DeveloperUnlocked, v => model.SetDeveloper(v)));
        var developerSection = Section("Development", developer); developerSection.Name = "DevelopmentSection";
        developerSection.IsVisible = model.DeveloperUnlocked && !model.IsDemo;
        syncs.Add(() => developerSection.IsVisible = model.DeveloperUnlocked && !model.IsDemo);
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
            var now = DateTime.UtcNow; presses = now - lastPress <= UnlockWindow ? presses + 1 : 1; lastPress = now;
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
        stack.Children.Add(Ui.Text(title, 16));
        stack.Children.Add(body);
        var card = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Child = stack };
        card.Classes.Add("dayCard");
        return card;
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
