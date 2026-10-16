using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// What the left column becomes on the Notes and Feed pages: not a column of its own but a block folded into the icon rail, which widens a little to hold it.
/// It has the clock with a word for what is happening, pause, break and Finish day as icons, and an avatar for each account (its initials in its colour, the
/// running one ringed, a small dot when the feed has something unread for it) to start or stop it with one press. The full column (budgets, Allocated and
/// Unallocated, the whole account list) is one press away on the Timeline and the week views. It listens to the model itself.
/// </summary>
public sealed class CompactColumn : StackPanel
{
    /// <summary>The width of the icon rail while this block is in it (it is 60 otherwise).</summary>
    public const double RailWidth = 92;
    private readonly TimerModel model;
    private readonly ClockText clock = new(24, 13) { Text = "00:00:00" };
    private readonly TextBlock status = Ui.Text("", 11, "muted");
    private readonly PauseGlyph pauseGlyph = new(); private readonly BreakGlyph breakGlyph = new() { Mono = true };
    private readonly IconGlyph dayFlag = new(IconKind.Flag);
    private readonly Button pause, breakButton, dayButton;
    private readonly WrapPanel avatars = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Width = 76 };
    private string shownAvatars = "\u0001";

    public CompactColumn(TimerModel model)
    {
        this.model = model; Name = "CompactColumn"; Spacing = 6; HorizontalAlignment = HorizontalAlignment.Center; Width = RailWidth - 8; IsVisible = false;
        Button Tool(string name, string tip, Control content, Action click)
        {
            var button = Ui.Button("", click, name); button.Classes.Add("rail"); button.Content = content; button.Width = 28; button.Height = 28; button.Padding = new Thickness(0); button.Margin = new Thickness(1, 0);
            ToolTip.SetTip(button, tip); Avalonia.Automation.AutomationProperties.SetName(button, tip); return button;
        }
        pause = Tool("CompactPause", "Pause or resume", new Viewbox { Width = 14, Height = 14, Child = pauseGlyph }, model.TogglePause);
        breakButton = Tool("CompactBreak", "Take a break", new Viewbox { Width = 16, Height = 16, Child = breakGlyph }, model.ToggleBreak);
        dayButton = Tool("CompactDay", "Start day", dayFlag, model.ToggleDay);
        clock.HorizontalAlignment = HorizontalAlignment.Center; status.HorizontalAlignment = HorizontalAlignment.Center;
        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        tools.Children.Add(pause); tools.Children.Add(breakButton); tools.Children.Add(dayButton);
        Children.Add(clock); Children.Add(status); Children.Add(tools); Children.Add(avatars);
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += OnModel; Sync(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= OnModel;
        Sync();
    }

    /// <summary>
    /// A short label for each account's avatar that no other account shares: its initials when they are different, then the first letter with the number it
    /// ends in, then the first letter with the first two of its last word, and last of all its place in the list.
    /// </summary>
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

    private void Sync()
    {
        clock.Text = model.ClockText;
        var onBreak = model.IsOnBreak; var late = model.IsOvertime && !onBreak;
        clock.SetState(onBreak, late, model.IsTracking && !onBreak && !late);
        status.Text = onBreak ? "Break" : late ? "Overtime" : model.IsTracking ? "Working" : "Ready";
        pauseGlyph.Playing = model.IsTracking && !onBreak; pauseGlyph.InvalidateVisual();
        breakGlyph.SetSteaming(onBreak); breakGlyph.InvalidateVisual(); breakButton.Classes.Set("onBreak", onBreak);
        ToolTip.SetTip(breakButton, onBreak ? "End break" : "Take a break");
        dayFlag.TintKey = model.DayStarted ? "Finish" : "Start"; ToolTip.SetTip(dayButton, model.DayStarted ? "Finish day" : "Start day");
        // One avatar per account. They are rebuilt only when the accounts, the running one or an unread count change.
        var accounts = model.Accounts.Where(a => !BreakAccount.Is(a.Name)).ToList();
        var signature = string.Join(",", accounts.Select(a => a.Name + a.Colour + model.FeedUnreadFor(a.Name).Count)) + "|" + model.ActiveAccount + "|" + model.CurrentTheme.Name;
        if (signature == shownAvatars) return; shownAvatars = signature; avatars.Children.Clear();
        var Labels = Short(accounts.Select(a => (a.Name, a.Display)).ToList());
        foreach (var a in accounts)
        {
            var name = a.Name; var isRunning = model.ActiveAccount == name; var unread = model.FeedUnreadFor(name).Count;
            var colour = Ui.Visible(a.Colour);
            var face = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Brush.Parse(colour), BorderThickness = new Thickness(isRunning ? 2.5 : 0),
                Child = new TextBlock { Text = Labels[a.Name], FontSize = 8.5, FontWeight = FontWeight.Bold, Foreground = Brush.Parse(Ui.InkOn(colour)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            face.Bind(Border.BorderBrushProperty, face.GetResourceObservable("Ink"));
            var content = new Grid { Width = 26, Height = 26 }; content.Children.Add(face);
            if (unread > 0) { var dot = new Border { Name = "CompactUnread_" + FeedPage.Key(name), Width = 8, Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false }; dot.Classes.Add("accentEdge"); content.Children.Add(dot); }
            var button = Ui.Button("", () => model.ToggleAccount(name), "CompactDot_" + FeedPage.Key(name)); button.Classes.Add("rail"); button.Content = content; button.Width = 38; button.Height = 28; button.Padding = new Thickness(0);
            ToolTip.SetTip(button, (isRunning ? "Stop " : "Start ") + a.Display + (unread > 0 ? $" · {unread} unread" : "")); Avalonia.Automation.AutomationProperties.SetName(button, a.Display);
            avatars.Children.Add(button);
        }
    }
}
