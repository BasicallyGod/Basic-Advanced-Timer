using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>A single dashboard-level rail; destinations are the dashboard's existing buttons.</summary>
public sealed class IntegratedSidebar : Grid
{
    public const double RailWidth = 88;
    public Grid Navigation { get; } = new() { Name = "IntegratedNavigation", ColumnDefinitions = new("*,*"), RowDefinitions = new("Auto,Auto,Auto"), ColumnSpacing = 2, RowSpacing = 2, Margin = new Thickness(6, 6) };
    public Grid SettingsSlot { get; } = new();
    public Grid QuitSlot { get; } = new();
    private readonly TimerModel model;
    private readonly VerticalCompactClock clock = new(true, 30, "Integrated");
    private readonly Border state = new() { Width = 6, Height = 6, CornerRadius = new(3), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly PauseGlyph pauseGlyph = new();
    private readonly BreakGlyph cup = new() { Mono = true };
    private readonly IconGlyph flag = new(IconKind.Flag);
    private readonly Button pause, breakButton, day;
    private readonly StackPanel accounts = new() { Spacing = 2, Margin = new Thickness(2, 4) };
    private readonly ScrollViewer scroll;
    private readonly List<(TimerAccount Account, Button Button, CompactBudgetIndicator Gauge, TextBlock Time, TextBlock Warning, TextBlock Live, TextBlock Details)> rows = new();
    private string signature = "";

    public IntegratedSidebar(TimerModel model)
    {
        this.model = model; Name = "IntegratedSidebar"; Width = RailWidth;
        RowDefinitions = new("Auto,Auto,*,Auto,Auto");
        var header = new StackPanel { Spacing = 2, Margin = new Thickness(0, 14, 0, 4) };
        header.Children.Add(state); header.Children.Add(clock); Children.Add(header);
        pause = Tool("IntegratedPause", "Resume tracking", new Viewbox { Child = pauseGlyph, Width = 20, Height = 20 }, model.TogglePause);
        pause.Classes.Add("pillRound"); pause.Width = pause.Height = 42;
        breakButton = Tool("IntegratedBreak", "Take a break", new Viewbox { Child = cup, Width = 19, Height = 19 }, model.ToggleBreak);
        var controls = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
        controls.Children.Add(pause); controls.Children.Add(breakButton); Ui.Put(this, controls, row: 1);
        scroll = new ScrollViewer { Name = "IntegratedAccounts", Content = accounts, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, AllowAutoHide = false };
        Ui.Put(this, Ui.WithMoreBelow(scroll), row: 2);
        Ui.Put(this, Navigation, row: 3);
        day = Tool("IntegratedDay", "Start day", flag, model.ToggleDay);
        var footer = Ui.Grid("*,*", "Auto,Auto"); footer.Name = "IntegratedFooter"; footer.Margin = new Thickness(6, 4, 6, 10);
        Ui.Put(footer, SettingsSlot); Ui.Put(footer, day, 1); Ui.Put(footer, QuitSlot, row: 1); SetColumnSpan(QuitSlot, 2);
        Ui.Put(this, footer, row: 4);
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += Changed; Sync(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Changed;
        Sync();
    }
    private static Button Tool(string name, string label, Control glyph, Action action)
    {
        var button = Ui.Button("", action, name); button.Content = glyph; button.Classes.Add("rail");
        button.Width = button.Height = 34; button.Padding = new Thickness(0); button.HorizontalAlignment = HorizontalAlignment.Center;
        Describe(button, label); return button;
    }
    private static void Describe(Control control, string text)
    {
        ToolTip.SetTip(control, text); Avalonia.Automation.AutomationProperties.SetName(control, text.Replace('\n', ' '));
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync();
    private void Rebuild(IReadOnlyList<TimerAccount> visible)
    {
        var offset = scroll.Offset; accounts.Children.Clear(); rows.Clear();
        var labels = CompactColumn.Short(visible.Select(a => (a.Name, a.Display)).ToList());
        foreach (var account in visible)
        {
            var key = Ui.NameKey(account.Name);
            var gauge = new CompactBudgetIndicator("capsules", account.Colour) { Width = 26, Height = 28, Name = "IntegratedBudget_" + key };
            var abbreviation = Ui.Text(labels[account.Name], 11); abbreviation.FontWeight = FontWeight.SemiBold; abbreviation.HorizontalAlignment = HorizontalAlignment.Center; abbreviation.VerticalAlignment = VerticalAlignment.Center;
            var chip = new Grid(); chip.Children.Add(gauge); chip.Children.Add(abbreviation);
            var time = Ui.Text("", 11, "figures"); time.Name = "IntegratedToday_" + key; time.VerticalAlignment = VerticalAlignment.Center;
            var warning = Ui.Text("!", 10, "overtime"); warning.Name = "IntegratedOver_" + key;
            var live = Ui.Text("●", 7, "live"); live.Name = "IntegratedLive_" + key;
            var marks = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; marks.Children.Add(live); marks.Children.Add(warning);
            var content = Ui.Grid("26,*,6"); content.ColumnSpacing = 3; Ui.Put(content, chip); Ui.Put(content, time, 1); Ui.Put(content, marks, 2);
            var name = account.Name; var button = Ui.Button("", () => model.ToggleAccount(name), "IntegratedAccount_" + key);
            button.Content = content; button.Width = 76; button.Height = 32; button.Padding = new Thickness(3, 0); button.Classes.Add("rail"); button.Classes.Add("accountCard"); button.Classes.Add("integratedAccount");
            var details = Ui.Text("", 13); details.TextWrapping = TextWrapping.Wrap; details.MaxWidth = 280; details.Name = "IntegratedDetails_" + key;
            button.ContextMenu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = details, StaysOpenOnClick = true } } };
            button.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Apps || e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift) { button.ContextMenu.Open(button); e.Handled = true; }
            };
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch; accounts.Children.Add(button);
            rows.Add((account, button, gauge, time, warning, live, details));
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => scroll.Offset = offset);
    }
    private void Sync()
    {
        var onBreak = model.IsOnBreak; var late = model.IsOvertime && !onBreak; var running = model.IsTracking && !onBreak;
        clock.Set(model.Elapsed, onBreak, late, running && !late);
        var stateKey = onBreak ? "Break" : late ? "Overtime" : running ? "Live" : "Clock";
        state.Background = new SolidColorBrush(Ui.ThemeColour(this, stateKey, Colors.Gray));
        Describe(clock, model.Status + " · " + model.ClockText); Describe(state, model.Status + (late ? " · Overtime" : ""));
        pauseGlyph.Playing = running; pauseGlyph.InvalidateVisual(); cup.SetSteaming(onBreak); cup.InvalidateVisual();
        Describe(pause, running ? "Pause tracking" : "Resume tracking"); Describe(breakButton, onBreak ? "End break" : "Take a break"); breakButton.Classes.Set("onBreak", onBreak);
        flag.TintKey = model.DayStarted ? "Finish" : "Start"; flag.InvalidateVisual(); Describe(day, model.DayStarted ? "Finish day" : "Start day");
        var visible = model.Accounts.Where(a => !BreakAccount.Is(a.Name)).ToList();
        var key = string.Join("|", visible.Select(a => a.Name + ":" + a.Display + ":" + a.Colour + ":" + a.Budget?.Ticks + ":" + a.Period));
        if (key != signature) { signature = key; Rebuild(visible); }
        foreach (var row in rows)
        {
            var ratio = model.BudgetRatio(row.Account); var active = model.ActiveAccount == row.Account.Name;
            row.Time.Text = model.FormatCompact(model.Today(row.Account)); row.Gauge.Set(ratio, model.BudgetPace(row.Account));
            row.Warning.IsVisible = ratio > 1; row.Live.IsVisible = active;
            row.Button.Classes.Set("active", active); row.Button.Classes.Set("selectedAccount", model.SelectedAccount == row.Account.Name);
            var budget = model.BudgetLine(row.Account) + (row.Account.Budget is null ? "" : " · " + model.BudgetPeriodNameAt(row.Account, model.Engine.Now));
            var usage = ratio is { } used ? $"{used:P0} used" + (used > 1 ? " · Over budget" : "") : "No budget";
            var detail = row.Account.Display + "\n" + row.Time.Text + " recorded today\n" + budget + "\n" + usage + (active ? " · Recording" : "");
            row.Details.Text = detail; Describe(row.Button, detail);
        }
    }
}
