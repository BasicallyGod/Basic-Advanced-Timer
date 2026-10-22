using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// A compact account panel beside the unchanged navigation rail on Notes.
/// Timer and immediate controls stay at the top; the accounts scroll independently;
/// start/finish day stays fixed at the bottom. The full dashboard column is retained on other pages.
/// </summary>
public sealed class CompactColumn : Grid
{
    public const double ColumnWidth = 176;
    private readonly TimerModel model;
    private readonly ClockText clock = new(28, 14) { Text = "00:00:00" };
    private readonly TextBlock status = Ui.Text("", 11, "muted");
    private readonly PauseGlyph pauseGlyph = new();
    private readonly BreakGlyph breakGlyph = new() { Mono = true };
    private readonly TextBlock dayLabel = Ui.Text("Start day", 12);
    private readonly IconGlyph dayFlag = new(IconKind.Flag);
    private readonly Button pause, breakButton, dayButton;
    private readonly StackPanel accounts = new() { Spacing = 4, Margin = new Thickness(6, 0, 6, 0) };
    private readonly ScrollViewer accountScroll;
    private readonly List<(TimerAccount Account, Button Button, TextBlock Time, Border Fill, Border Track)> rows = new();
    private string shownAccounts = "\u0001";

    public CompactColumn(TimerModel model)
    {
        this.model = model; Name = "CompactColumn"; Width = ColumnWidth; IsVisible = false;
        RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto");
        var header = new StackPanel { Spacing = 2, Margin = new Thickness(3, 14, 3, 4), HorizontalAlignment = HorizontalAlignment.Center };
        clock.HorizontalAlignment = HorizontalAlignment.Center; status.HorizontalAlignment = HorizontalAlignment.Center;
        header.Children.Add(clock); header.Children.Add(status);
        Ui.Put(this, header);

        Button Tool(string name, string tip, Control icon, Action action)
        {
            var button = Ui.Button("", action, name); button.Classes.Add("rail");
            button.Content = icon; button.Width = 38; button.Height = 38; button.Padding = new Thickness(0);
            ToolTip.SetTip(button, tip); Avalonia.Automation.AutomationProperties.SetName(button, tip);
            return button;
        }
        pause = Tool("CompactPause", "Pause or resume tracking", new Viewbox { Width = 16, Height = 16, Child = pauseGlyph }, model.TogglePause);
        breakButton = Tool("CompactBreak", "Take a break", new Viewbox { Width = 18, Height = 18, Child = breakGlyph }, model.ToggleBreak);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        tools.Children.Add(pause); tools.Children.Add(breakButton);
        Ui.Put(this, tools, row: 1);

        accountScroll = new ScrollViewer { Name = "CompactAccountScroll", Content = accounts, AllowAutoHide = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Ui.Put(this, accountScroll, row: 2);

        dayButton = Tool("CompactDay", "Start day", Ui.Horizontal(dayFlag, dayLabel), model.ToggleDay);
        dayButton.Width = ColumnWidth - 12; dayButton.Height = 42;
        var footer = new Border { Child = dayButton, Padding = new Thickness(5, 10, 5, 12) };
        Ui.Put(this, footer, row: 3);

        AttachedToVisualTree += (_, _) => { model.PropertyChanged += OnModel; Sync(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= OnModel;
        Sync();
    }

    /// <summary>Unique short account labels; full names and budget details remain available on each row's tooltip.</summary>
    public static Dictionary<string, string> Short(IReadOnlyList<(string Name, string Display)> accounts)
    {
        // Two characters fit beside the coloured dot and HH:MM in the 96px strip.
        // Prefer meaningful initials (DLA -> DL, Client 2 -> C2, Account two -> A2);
        // resolve collisions with unique, fixed-width base-36 codes.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var choices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var numberWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4",
            ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8",
            ["nine"] = "9", ["ten"] = "A", ["eleven"] = "B", ["twelve"] = "C"
        };
        string Candidate(string display)
        {
            var words = display.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "";
            if (words.Length > 1)
            {
                var second = words[^1];
                var suffix = second.Length == 1 || second.All(char.IsDigit) ? second[..1]
                    : numberWords.TryGetValue(second, out var number) ? number : second[..1];
                return (words[0][..1] + suffix).ToUpperInvariant();
            }
            return words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant();
        }
        // Assign only labels unique across the input, so there is no arbitrary winner in a collision.
        var candidates = accounts.Select(a => (a.Name, Candidate: Candidate(a.Display))).ToList();
        foreach (var item in candidates)
        {
            if (item.Candidate.Length == 0 || candidates.Count(x => x.Candidate.Equals(item.Candidate, StringComparison.OrdinalIgnoreCase)) != 1) continue;
            if (taken.Add(item.Candidate)) choices[item.Name] = item.Candidate;
        }
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var next = 0;
        foreach (var account in accounts)
        {
            if (choices.ContainsKey(account.Name)) continue;
            string code;
            do
            {
                var n = next++;
                code = n < 1296 ? "" + alphabet[(n / 36) % 36] + alphabet[n % 36] : n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            while (!taken.Add(code));
            choices[account.Name] = code;
        }
        return choices;
    }

    private void OnModel(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync();

    private void RebuildAccounts(IReadOnlyList<TimerAccount> visible)
    {
        var offset = accountScroll.Offset;
        accounts.Children.Clear(); rows.Clear();
        foreach (var account in visible)
        {
            var name = account.Name;
            var colour = Ui.Visible(account.Colour);
            var isRunning = model.ActiveAccount == name;
            var label = Ui.Text(account.Display, 12); label.FontWeight = FontWeight.SemiBold;
            label.MaxWidth = ColumnWidth - 38; label.TextTrimming = TextTrimming.CharacterEllipsis;
            var total = Ui.Text("", 11); total.FontWeight = FontWeight.SemiBold;
            total.MinWidth = 33; total.TextAlignment = Avalonia.Media.TextAlignment.Right;
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                Background = Brush.Parse(colour), VerticalAlignment = VerticalAlignment.Center };
            var main = Ui.Grid("Auto,*", "Auto,Auto"); main.ColumnSpacing = 2;
            Ui.Put(main, dot); Ui.Put(main, label, 1); Ui.Put(main, total, 1, row: 1);
            total.HorizontalAlignment = HorizontalAlignment.Left; total.Classes.Add("muted");
            var track = new Border { Width = ColumnWidth - 24, Height = 3, CornerRadius = new CornerRadius(1.5),
                ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
            track.Classes.Add("rule");
            var fill = new Border { Width = 0, Height = 3, Background = Brush.Parse(colour), CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Left };
            track.Child = fill;
            var content = new StackPanel { Spacing = 5, Margin = new Thickness(3, 1) };
            content.Children.Add(main); content.Children.Add(track);
            var button = Ui.Button("", () => model.ToggleAccount(name), "CompactDot_" + Ui.NameKey(name));
            button.Classes.Add("rail"); button.Classes.Add("accountCard");
            button.Classes.Set("active", isRunning); button.Width = ColumnWidth - 12; button.Height = 60;
            button.Padding = new Thickness(3, 4); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Content = content;
            Avalonia.Automation.AutomationProperties.SetName(button, account.Display);
            accounts.Children.Add(button);
            rows.Add((account, button, total, fill, track));
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => accountScroll.Offset = offset);
    }

    private void Sync()
    {
        clock.Text = model.ClockText;
        var onBreak = model.IsOnBreak; var late = model.IsOvertime && !onBreak;
        clock.SetState(onBreak, late, model.IsTracking && !onBreak && !late);
        status.Text = model.Status;
        ToolTip.SetTip(status, model.Status + (late ? " · Overtime" : ""));
        pauseGlyph.Playing = model.IsTracking && !onBreak; pauseGlyph.InvalidateVisual();
        breakGlyph.SetSteaming(onBreak); breakGlyph.InvalidateVisual();
        breakButton.Classes.Set("onBreak", onBreak);
        ToolTip.SetTip(breakButton, onBreak ? "End break" : "Take a break");
        dayLabel.Text = model.DayStarted ? "Finish day" : "Start day";
        dayFlag.TintKey = model.DayStarted ? "Finish" : "Start";
        ToolTip.SetTip(dayButton, model.DayStarted ? "Finish day" : "Start day");

        var visible = model.Accounts.Where(a => !BreakAccount.Is(a.Name)).ToList();
        var signature = string.Join("|", visible.Select(a => a.Name + ":" + a.Display + ":" + a.Colour))
            + "|" + model.ActiveAccount + "|" + model.CurrentTheme.Name;
        if (signature != shownAccounts)
        {
            shownAccounts = signature;
            RebuildAccounts(visible);
        }

        foreach (var row in rows)
        {
            row.Time.Text = model.FormatCompact(model.Today(row.Account)) + (model.ActiveAccount == row.Account.Name ? " · Recording" : " today");
            var ratio = model.BudgetRatio(row.Account);
            row.Track.IsVisible = ratio is not null;
            row.Fill.Width = ratio is { } used ? (ColumnWidth - 24) * Math.Clamp(used, 0, 1) : 0;
            row.Button.Classes.Set("active", model.ActiveAccount == row.Account.Name);
            row.Button.Classes.Set("selectedAccount", model.SelectedAccount == row.Account.Name);
            ToolTip.SetTip(row.Button, row.Account.Display + " · " + row.Time.Text + "\n" + model.BudgetLine(row.Account));
        }
    }
}
