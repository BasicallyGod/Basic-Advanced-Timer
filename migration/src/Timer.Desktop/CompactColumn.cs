using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// A slim tracking utility beside the unchanged navigation rail on Notes and Feed.
/// Timer and immediate controls stay at the top; the accounts scroll independently;
/// start/finish day stays fixed at the bottom. The full dashboard column is retained on other pages.
/// </summary>
public sealed class CompactColumn : Grid
{
    public const double ColumnWidth = 96;
    private readonly TimerModel model;
    private readonly ClockText clock = new(20, 10) { Text = "00:00:00" };
    private readonly TextBlock status = Ui.Text("", 11, "muted");
    private readonly PauseGlyph pauseGlyph = new();
    private readonly BreakGlyph breakGlyph = new() { Mono = true };
    private readonly IconGlyph dayFlag = new(IconKind.Flag);
    private readonly Button pause, breakButton, dayButton;
    private readonly StackPanel accounts = new() { Spacing = 3, Margin = new Thickness(3, 0, 3, 0) };
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

        dayButton = Tool("CompactDay", "Start day", new IconGlyph(IconKind.Flag) { TintKey = "Start" }, model.ToggleDay);
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
        var taken = new HashSet<string>(); var result = new Dictionary<string, string>();
        for (var pass = 0; pass < 3; pass++)
            foreach (var (name, display) in accounts)
            {
                if (result.ContainsKey(name)) continue;
                var words = display.Split(' ', StringSplitOptions.RemoveEmptyEntries); var last = words.Length > 0 ? words[^1] : display;
                var candidate = pass switch
                {
                    0 => PillLayouts.Initials(display),
                    1 => words.Length > 1 && last.All(char.IsDigit) ? char.ToUpperInvariant(display[0]) + last : "",
                    _ => words.Length > 1 ? (char.ToUpperInvariant(display[0]) + new string(last.Take(2).ToArray()).ToUpperInvariant()) : ""
                };
                // Only a candidate that is nobody else's first choice for this pass is taken, so the first two accounts do not both settle for the same letters.
                if (candidate.Length > 0 && !taken.Contains(candidate) && accounts.Count(o => o.Name != name && !result.ContainsKey(o.Name) && PassLabel(o.Display, pass) == candidate) == 0) { taken.Add(candidate); result[name] = candidate; }
            }
        var index = 1;
        foreach (var (name, _) in accounts) if (!result.ContainsKey(name)) { result[name] = index.ToString(); index++; }
        return result;
        static string PassLabel(string display, int pass)
        {
            var words = display.Split(' ', StringSplitOptions.RemoveEmptyEntries); var last = words.Length > 0 ? words[^1] : display;
            return pass switch { 0 => PillLayouts.Initials(display), 1 => words.Length > 1 && last.All(char.IsDigit) ? char.ToUpperInvariant(display[0]) + last : "", _ => words.Length > 1 ? (char.ToUpperInvariant(display[0]) + new string(last.Take(2).ToArray()).ToUpperInvariant()) : "" };
        }
    }


    private void OnModel(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync();

    private void RebuildAccounts(IReadOnlyList<TimerAccount> visible)
    {
        var offset = accountScroll.Offset;
        accounts.Children.Clear(); rows.Clear();
        var labels = Short(visible.Select(a => (a.Name, a.Display)).ToList());
        foreach (var account in visible)
        {
            var name = account.Name, colour = Ui.Visible(account.Colour), isRunning = model.ActiveAccount == name;
            var label = Ui.Text(labels[name], 11); label.FontWeight = FontWeight.SemiBold;
            var total = Ui.Text("", 12); total.FontWeight = FontWeight.SemiBold;
            total.TextAlignment = Avalonia.Media.TextAlignment.Right;
            var dot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5),
                Background = Brush.Parse(colour), VerticalAlignment = VerticalAlignment.Center };
            var main = Ui.Grid("Auto,*,Auto"); main.ColumnSpacing = 3;
            Ui.Put(main, dot); Ui.Put(main, label, 1); Ui.Put(main, total, 2);
            var track = new Border { Width = ColumnWidth - 24, Height = 3, CornerRadius = new CornerRadius(1.5),
                ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
            track.Classes.Add("rule");
            var fill = new Border { Width = 0, Height = 3, Background = Brush.Parse(colour), CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Left };
            track.Child = fill;
            var content = new StackPanel { Spacing = 5, Margin = new Thickness(3, 1) };
            content.Children.Add(main); content.Children.Add(track);
            var button = Ui.Button("", () => model.ToggleAccount(name), "CompactDot_" + FeedPage.Key(name));
            button.Classes.Add("rail"); button.Classes.Add("accountCard");
            button.Classes.Set("active", isRunning); button.Width = ColumnWidth - 8; button.Height = 48;
            button.Padding = new Thickness(4, 4); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
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
        status.Text = onBreak ? "Break" : late ? "Overtime" : model.IsTracking ? "Working" : "Ready";
        pauseGlyph.Playing = model.IsTracking && !onBreak; pauseGlyph.InvalidateVisual();
        breakGlyph.SetSteaming(onBreak); breakGlyph.InvalidateVisual();
        breakButton.Classes.Set("onBreak", onBreak);
        ToolTip.SetTip(breakButton, onBreak ? "End break" : "Take a break");
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
            row.Time.Text = model.FormatCompact(model.Today(row.Account));
            var ratio = model.BudgetRatio(row.Account);
            row.Track.IsVisible = ratio is not null;
            row.Fill.Width = ratio is { } used ? (ColumnWidth - 24) * Math.Clamp(used, 0, 1) : 0;
            row.Button.Classes.Set("active", model.ActiveAccount == row.Account.Name);
            var unread = model.FeedUnreadFor(row.Account.Name).Count;
            ToolTip.SetTip(row.Button, row.Account.Display + " · " + row.Time.Text + " today\n"
                + model.BudgetLine(row.Account) + (unread > 0 ? $"\n{unread} unread feed items" : ""));
        }
    }
}
