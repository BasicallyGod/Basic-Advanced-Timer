using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;
using Timer.Storage;

namespace Timer.Desktop;

/// <summary>
/// The getting started setup, shown over the dashboard on a new database. Four short steps, one choice each: client accounts (with optional budgets),
/// working hours, how time is shown, and a last page of things worth knowing. Each step is applied when Next is pressed, so leaving part way keeps
/// what was already done, and the whole setup can be skipped at any point.
/// </summary>
public sealed class OnboardingView : Grid
{
    public const int StepCount = 4;
    private readonly TimerModel model;
    private int step;
    private readonly TextBlock stepLabel = Ui.Text("", 12, "muted");
    private readonly TextBlock title = new() { FontSize = 26, Classes = { "clock" } };
    private readonly TextBlock subtitle = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, FontSize = 14 };
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Classes = { "overtime" }, FontSize = 13, IsVisible = false };
    private readonly ContentControl body = new();
    private readonly StackPanel dots = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button back, next;
    // What each step keeps between visits, so going back shows what was typed.
    private readonly List<(string? Original, string? Colour, TextBox Name, TextBox Budget, ComboBox Period, CheckBox Show)> accountRows = new();
    private StackPanel? accountList;
    private TextBox startBox = new(), endBox = new();
    private readonly List<(DayOfWeek Day, CheckBox Box)> dayBoxes = new();
    private bool customHours, decimalChoice;
    private ComboBox weekStartBox = new();
    private Button hoursTile = null!, decimalTile = null!;
    public event Action? Finished;
    public int Step => step;

    public OnboardingView(TimerModel model)
    {
        this.model = model; Name = "SetupOverlay";
        // A veil over the dashboard, which is also blurred beneath it by the window, and a card in the middle.
        Background = new SolidColorBrush(Color.FromArgb(120, 8, 10, 14));
        decimalChoice = model.DecimalHours;
        var card = new Border { Name = "SetupCard", Width = 560, MinHeight = 440, CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1), Padding = new Thickness(30, 24, 30, 22), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 12, Blur = 40, Color = Color.FromArgb(90, 0, 0, 0) }) };
        card.Classes.Add("dayCard");
        var layout = new StackPanel { Spacing = 14 };
        var top = Ui.Grid("*,Auto"); Ui.Put(top, stepLabel);
        var skip = Ui.Button("Skip setup", () => Skip(), "SetupSkip"); skip.Classes.Add("flat"); Ui.Put(top, skip, 1);
        layout.Children.Add(top); layout.Children.Add(title); layout.Children.Add(subtitle); layout.Children.Add(body); layout.Children.Add(error);
        back = Ui.DialogButton("Back", () => Go(-1), "SetupBack", main: false);
        next = Ui.DialogButton("Next", () => Advance(), "SetupNext", main: true);
        var footer = Ui.Grid("Auto,*,Auto"); footer.Margin = new Thickness(0, 8, 0, 0);
        Ui.Put(footer, back); Ui.Put(footer, dots, 1); dots.HorizontalAlignment = HorizontalAlignment.Center; Ui.Put(footer, next, 2);
        layout.Children.Add(footer);
        card.Child = layout; Children.Add(card);
        Focusable = true;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Skip(); e.Handled = true; } };
        Show(0);
    }

    private void Skip() { model.CompleteSetup(); Finished?.Invoke(); }
    private void Go(int by) { error.IsVisible = false; Show(Math.Clamp(step + by, 0, StepCount - 1)); }
    private void Fail(string text) { error.Text = text; error.IsVisible = true; }

    /// <summary>Applies what the current step asks for, then moves on. The last step finishes the setup.</summary>
    public void Advance()
    {
        error.IsVisible = false;
        try
        {
            switch (step)
            {
                case 0: if (!ApplyAccounts()) return; break;
                case 1: if (!ApplyHours()) return; break;
                case 2: ApplyFormat(); break;
                case 3: model.CompleteSetup(); Finished?.Invoke(); return;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException or InvalidDataException or IOException)
        { Fail(ex.Message); return; }
        Show(step + 1);
    }

    private void Show(int index)
    {
        step = index;
        stepLabel.Text = $"Step {index + 1} of {StepCount}";
        back.IsVisible = index > 0;
        next.Content = index == StepCount - 1 ? "Start tracking" : "Next";
        dots.Children.Clear();
        for (var i = 0; i < StepCount; i++)
        {
            var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8 };
            dot.Classes.Add(i == index ? "stepDot" : "idle");
            dots.Children.Add(dot);
        }
        (title.Text, subtitle.Text, body.Content) = index switch
        {
            0 => ("Your client accounts", "These are the clients you track time against. Rename the examples or add your own. Budgets are optional: leave them blank for no limit.", AccountsStep()),
            1 => ("Your working hours", "The day clock starts at your start time and overtime begins after your end time. Overnight shifts are not supported yet.", HoursStep()),
            2 => ("How you count time", "This changes every total and the exports. You can switch any time from the right-click menu.", FormatStep()),
            _ => ("You are all set", "A few things worth knowing before you start.", ReadyStep())
        };
    }

    // ---- step 1: accounts -------------------------------------------------------------------------------------------------------------

    private Control AccountsStep()
    {
        var panel = new StackPanel { Spacing = 8 };
        var headings = Ui.Grid("*,76,100,72");
        Ui.Put(headings, Ui.Text("Account", 12, "muted")); Ui.Put(headings, Ui.Text("Budget", 12, "muted"), 1); Ui.Put(headings, Ui.Text("Per", 12, "muted"), 2); Ui.Put(headings, Ui.Text("In tracker", 12, "muted"), 3);
        panel.Children.Add(headings);
        accountList = new StackPanel { Spacing = 8 };
        if (accountRows.Count == 0)
            foreach (var account in model.AccountSettings) AddRow(account, existing: true);
        else foreach (var row in accountRows) accountList.Children.Add(RowGrid(row));
        panel.Children.Add(new ScrollViewer { Name = "SetupAccounts", Content = accountList, MaxHeight = 232, AllowAutoHide = true, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var add = Ui.Button("Add an account", () => { AddRow(new AccountSettings(""), existing: false); if (accountRows[^1].Name is { } box) box.Focus(); }, "SetupAddAccount");
        add.Classes.Add("toolButton"); add.Classes.Add("quiet"); add.HorizontalAlignment = HorizontalAlignment.Left; add.Height = 38;
        panel.Children.Add(add);
        return panel;
    }
    private void AddRow(AccountSettings account, bool existing)
    {
        var name = new TextBox { Text = account.Name, Watermark = "Client name", Margin = new Thickness(0, 0, 8, 0), Name = "SetupName" + accountRows.Count };
        var budget = new TextBox { Text = account.BudgetHours?.ToString("0.###", CultureInfo.InvariantCulture) ?? "", Watermark = "none", Margin = new Thickness(0, 0, 8, 0), Name = "SetupBudget" + accountRows.Count };
        var period = new ComboBox { ItemsSource = new[] { "day", "week", "month", "quarter" }, SelectedItem = account.BudgetPeriod, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 8, 0) };
        var show = new CheckBox { IsChecked = !account.Hidden, HorizontalAlignment = HorizontalAlignment.Center };
        var row = (existing ? account.Name : null, account.Colour, name, budget, period, show);
        accountRows.Add(row);
        accountList?.Children.Add(RowGrid(row));
    }
    // A control can only have one parent. Coming back to a step builds its layout again, so the boxes that hold what was typed are taken out of the old layout first.
    private static T Detach<T>(T control) where T : Control { if (control.Parent is Panel panel) panel.Children.Remove(control); return control; }
    private static Grid RowGrid((string? Original, string? Colour, TextBox Name, TextBox Budget, ComboBox Period, CheckBox Show) row)
    {
        var grid = Ui.Grid("*,76,100,72");
        Ui.Put(grid, Detach(row.Name)); Ui.Put(grid, Detach(row.Budget), 1); Ui.Put(grid, Detach(row.Period), 2); Ui.Put(grid, Detach(row.Show), 3);
        return grid;
    }
    private bool ApplyAccounts()
    {
        var accounts = new List<AccountSettings>(); var renames = new Dictionary<string, string>();
        foreach (var row in accountRows)
        {
            var name = (row.Name.Text ?? "").Trim();
            if (name.Length == 0) { if (row.Original is null) continue; Fail("Every account needs a name. To drop one, untick Show instead."); return false; }
            double? budget = null;
            if (!string.IsNullOrWhiteSpace(row.Budget.Text))
            {
                if (!double.TryParse(row.Budget.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) || hours <= 0) { Fail("Use positive budget hours, such as 7.5, or leave the budget blank."); return false; }
                budget = hours;
            }
            accounts.Add(new AccountSettings(name, budget, (string?)row.Period.SelectedItem ?? "week", row.Show.IsChecked != true, row.Colour));
            if (row.Original is not null && row.Original != name) renames[row.Original] = name;
        }
        if (accounts.All(a => a.Hidden)) { Fail("Keep at least one account showing."); return false; }
        // Nothing changed, nothing to save, so pressing Next straight through leaves the starting accounts exactly as they were.
        if (accounts.SequenceEqual(model.AccountSettings)) return true;
        model.SetAccounts(accounts, renames);
        if (model.LastError is not null) { Fail(model.LastError); return false; }
        // Rows now describe saved accounts, so a later visit starts from them.
        accountRows.Clear(); accountList = null;
        return true;
    }

    // ---- step 2: hours -----------------------------------------------------------------------------------------------------------------

    private Control HoursStep()
    {
        var panel = new StackPanel { Spacing = 14 };
        var schedule = model.Engine.Schedule;
        if (dayBoxes.Count == 0)
        {
            var first = schedule.Days.OrderBy(d => ((int)d + 6) % 7).FirstOrDefault(DayOfWeek.Monday);
            var shift = schedule.Shifts.GetValueOrDefault(first) ?? schedule.Default;
            startBox = new TextBox { Text = shift.Start.ToString(@"hh\:mm"), Width = 90, Name = "SetupStart" }; endBox = new TextBox { Text = shift.End.ToString(@"hh\:mm"), Width = 90, Name = "SetupEnd" };
            foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
                dayBoxes.Add((day, new CheckBox { Content = day.ToString()[..3], IsChecked = schedule.Days.Contains(day), Name = "SetupDay" + day.ToString()[..3] }));
        }
        var days = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (_, box) in dayBoxes) { box.Margin = new Thickness(0, 0, 14, 0); days.Children.Add(Detach(box)); }
        panel.Children.Add(Ui.Text("Working days", 12, "muted")); panel.Children.Add(days);
        panel.Children.Add(Ui.Text("Usual hours (24 hour clock)", 12, "muted"));
        panel.Children.Add(Ui.Horizontal(Detach(startBox), Ui.Dash(), Detach(endBox)));
        var different = Ui.Button("Different hours on some days…", () =>
        {
            if (TopLevel.GetTopLevel(this) is DashboardWindow dashboard) { customHours = true; dashboard.Dialogs.Show(new ShiftDialog(model)); }
        }, "SetupDifferentHours");
        different.Classes.Add("toolButton"); different.Classes.Add("quiet"); different.HorizontalAlignment = HorizontalAlignment.Left; different.Height = 38;
        panel.Children.Add(different);
        return panel;
    }
    private bool ApplyHours()
    {
        // Hours set one day at a time in their own dialog are kept as they are, rather than flattened to one shift.
        if (customHours) return true;
        var start = TimeSpan.ParseExact((startBox.Text ?? "").Trim(), @"hh\:mm", CultureInfo.InvariantCulture, TimeSpanStyles.None);
        var end = TimeSpan.ParseExact((endBox.Text ?? "").Trim(), @"hh\:mm", CultureInfo.InvariantCulture, TimeSpanStyles.None);
        var chosen = dayBoxes.Where(d => d.Box.IsChecked == true).Select(d => d.Day).ToList();
        if (chosen.Count == 0 || end <= start) { Fail("Use HH:mm, with an end after the start, and at least one working day."); return false; }
        var current = model.Engine.Schedule;
        var same = current.Days.SetEquals(chosen) && chosen.All(d => (current.Shifts.GetValueOrDefault(d) ?? current.Default) == new Shift(start, end));
        if (!same) model.SetShifts(chosen.ToDictionary(d => d, _ => new Shift(start, end)), false);
        if (model.LastError is not null) { Fail(model.LastError); return false; }
        return true;
    }

    // ---- step 3: format ----------------------------------------------------------------------------------------------------------------

    private Control FormatStep()
    {
        var panel = new StackPanel { Spacing = 16 };
        Button Tile(string heading, string example, string name, bool decimalValue)
        {
            var text = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Left };
            text.Children.Add(Ui.Text(heading, 14)); var big = Ui.Text(example, 26); big.Classes.Add("clock"); text.Children.Add(big);
            var tile = Ui.Button("", () => { decimalChoice = decimalValue; Mark(); }, name);
            tile.Classes.Add("choiceTile"); tile.Content = text; tile.HorizontalAlignment = HorizontalAlignment.Stretch; tile.HorizontalContentAlignment = HorizontalAlignment.Left;
            return tile;
        }
        hoursTile = Tile("Hours and minutes", "1h 30m", "SetupHoursMinutes", false); decimalTile = Tile("Decimal hours", "1.50h", "SetupDecimal", true);
        var tiles = Ui.Grid("*,*"); hoursTile.Margin = new Thickness(0, 0, 6, 0); decimalTile.Margin = new Thickness(6, 0, 0, 0);
        Ui.Put(tiles, hoursTile); Ui.Put(tiles, decimalTile, 1);
        panel.Children.Add(tiles);
        Mark();
        panel.Children.Add(Ui.Text("Your week starts on", 12, "muted"));
        weekStartBox = new ComboBox { Name = "SetupWeekStart", ItemsSource = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }, SelectedItem = model.WeekStartsOn.ToString(), Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(weekStartBox);
        return panel;
    }
    private void Mark() { hoursTile.Classes.Set("selected", !decimalChoice); decimalTile.Classes.Set("selected", decimalChoice); }
    private void ApplyFormat()
    {
        model.SetDecimalHours(decimalChoice);
        if (weekStartBox.SelectedItem is string day && Enum.TryParse<DayOfWeek>(day, out var parsed)) model.SetWeekStart(parsed);
    }

    // ---- step 4: ready -----------------------------------------------------------------------------------------------------------------

    private Control ReadyStep()
    {
        var panel = new StackPanel { Spacing = 12 };
        StackPanel Point(string heading, string text)
        {
            var block = new StackPanel { Spacing = 2 };
            block.Children.Add(new TextBlock { Text = heading, FontSize = 14, FontWeight = FontWeight.SemiBold });
            block.Children.Add(new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
            return block;
        }
        panel.Children.Add(Point("The tray.", "Closing a window hides the timer to the notification area and tracking carries on. Use the cross at the foot of the dashboard to close the timer for good."));
        panel.Children.Add(Point("The pill and this dashboard.", "Click the clock on the pill to open the dashboard, and the account name to pick what you are working on."));
        panel.Children.Add(Point("Your data.", model.DataFolder is { } folder ? $"Stays on this computer in {folder}, with a backup each day." : "Stays on this computer, with a backup each day."));
        if (model.AutoStart?.Available == true)
        {
            var startup = new CheckBox { Content = "Start the timer when I sign in to Windows", IsChecked = model.AutoStart.IsEnabled, Name = "SetupStartup", Margin = new Thickness(0, 6, 0, 0) };
            startup.Click += (_, _) => { model.SetAutoStart(startup.IsChecked == true); startup.IsChecked = model.AutoStart?.IsEnabled == true; };
            panel.Children.Add(startup);
        }
        return panel;
    }
}
