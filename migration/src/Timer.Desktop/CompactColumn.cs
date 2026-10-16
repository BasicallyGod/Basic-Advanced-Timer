using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Timer.Core;

namespace Timer.Desktop;

/// <summary>
/// The narrow stand-in for the dashboard's left column on the Notes and Feed pages, which need the room: the clock, who is running, pause, break and
/// Finish day as icons, a column of account dots to switch with one press, and a count of unread feed items for the running account. The full column
/// (budgets, Allocated and Unallocated, the whole account list) is one press away on the Timeline and the week views. It listens to the model itself.
/// </summary>
public sealed class CompactColumn : Border
{
    public const double ColumnWidth = 156;
    private readonly TimerModel model; private readonly Action<string> openFeed;
    private readonly ClockText clock = new(30, 17) { Text = "00:00:00" };
    private readonly Avalonia.Controls.Shapes.Ellipse dot = new() { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock account = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock detail = Ui.Text("", 12, "muted");
    private readonly PauseGlyph pauseGlyph = new(); private readonly BreakGlyph breakGlyph = new() { Mono = true };
    private readonly IconGlyph dayFlag = new(IconKind.Flag);
    private readonly Button pause, breakButton, dayButton, feedButton;
    private readonly StackPanel dots = new() { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
    private string shownDots = "\u0001";

    public CompactColumn(TimerModel model, Action<string> openFeed)
    {
        this.model = model; this.openFeed = openFeed; Name = "CompactColumn"; Classes.Add("panel");
        Button Tool(string name, string tip, Control content, Action click)
        {
            var button = Ui.Button("", click, name); button.Classes.Add("rail"); button.Content = content; button.Width = 38; button.Height = 38; button.Padding = new Thickness(0);
            ToolTip.SetTip(button, tip); Avalonia.Automation.AutomationProperties.SetName(button, tip); return button;
        }
        pause = Tool("CompactPause", "Pause or resume", new Viewbox { Width = 18, Height = 18, Child = pauseGlyph }, model.TogglePause);
        breakButton = Tool("CompactBreak", "Take a break", new Viewbox { Width = 20, Height = 20, Child = breakGlyph }, model.ToggleBreak);
        dayButton = Tool("CompactDay", "Start day", dayFlag, model.ToggleDay);
        var tools = Ui.Grid("*,*,*"); foreach (var (button, column) in new[] { (pause, 0), (breakButton, 1), (dayButton, 2) }) { button.HorizontalAlignment = HorizontalAlignment.Center; Ui.Put(tools, button, column); }
        feedButton = Ui.Button("", () => { if (model.ActiveAccount is { } running) openFeed(running); }, "CompactFeed"); feedButton.Classes.Add("flat"); feedButton.FontSize = 12; feedButton.Padding = new Thickness(6, 2); feedButton.HorizontalAlignment = HorizontalAlignment.Left; feedButton.IsVisible = false;
        var who = Ui.Horizontal(dot, account); who.Spacing = 8;
        var top = new StackPanel { Spacing = 2, Margin = new Thickness(14, 18, 10, 0) };
        top.Children.Add(Ui.Text("Working time", 11, "muted")); top.Children.Add(clock); top.Children.Add(new Border { Height = 6 }); top.Children.Add(who); top.Children.Add(detail); top.Children.Add(feedButton);
        var grid = Ui.Grid("*", "Auto,Auto,Auto,*");
        Ui.Put(grid, top);
        var toolsHost = new Border { Margin = new Thickness(6, 12, 6, 6), Child = tools }; Ui.Put(grid, toolsHost, row: 1);
        var rule = Ui.Rule(); rule.Margin = new Thickness(14, 6); Ui.Put(grid, rule, row: 2);
        Ui.Put(grid, new ScrollViewer { Content = dots, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 0, 10) }, row: 3);
        Child = grid; Width = ColumnWidth;
        AttachedToVisualTree += (_, _) => { model.PropertyChanged += OnModel; Sync(); };
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= OnModel;
        Sync();
    }

    private void OnModel(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Sync();

    private void Sync()
    {
        clock.Text = model.ClockText;
        var onBreak = model.IsOnBreak; var late = model.IsOvertime && !onBreak;
        clock.SetState(onBreak, late, model.IsTracking && !onBreak && !late);
        var shown = onBreak ? null : model.Accounts.FirstOrDefault(a => a.Name == (model.ActiveAccount ?? model.SelectedAccount));
        dot.IsVisible = shown is not null; if (shown is not null) dot.Fill = Brush.Parse(Ui.Visible(shown.Colour));
        account.Text = onBreak ? "On break" : model.ActiveAccount is { } running ? model.Accounts.FirstOrDefault(a => a.Name == running)?.Display ?? running : shown?.Display ?? "Ready";
        detail.Text = onBreak ? (model.ResumeAccount is { } back ? "Then " + back : "Nothing running")
            : model.ActiveAccount is not null && shown is not null ? "Today " + model.Format(model.Today(shown)) : model.Status;
        pauseGlyph.Playing = model.IsTracking && !onBreak; pauseGlyph.InvalidateVisual();
        breakGlyph.SetSteaming(onBreak); breakGlyph.InvalidateVisual(); breakButton.Classes.Set("onBreak", onBreak);
        ToolTip.SetTip(breakButton, onBreak ? "End break" : "Take a break");
        dayFlag.TintKey = model.DayStarted ? "Finish" : "Start"; ToolTip.SetTip(dayButton, model.DayStarted ? "Finish day" : "Start day");
        var unread = model.ActiveAccount is { } active && !onBreak ? model.FeedUnreadFor(active).Count : 0;
        feedButton.IsVisible = unread > 0; feedButton.Content = unread + " unread"; ToolTip.SetTip(feedButton, "Open the feed for this account");
        // One dot per account, the running one ringed. The dots are rebuilt only when the accounts or the running one change.
        var signature = string.Join(",", model.Accounts.Where(a => !BreakAccount.Is(a.Name)).Select(a => a.Name + a.Colour)) + "|" + model.ActiveAccount + "|" + model.CurrentTheme.Name;
        if (signature == shownDots) return; shownDots = signature; dots.Children.Clear();
        foreach (var a in model.Accounts.Where(a => !BreakAccount.Is(a.Name)))
        {
            var name = a.Name; var isRunning = model.ActiveAccount == name;
            var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Brush.Parse(Ui.Visible(a.Colour)), BorderThickness = new Thickness(isRunning ? 2.5 : 0) };
            swatch.Bind(Border.BorderBrushProperty, swatch.GetResourceObservable("Ink"));
            var button = Ui.Button("", () => model.ToggleAccount(name), "CompactDot_" + FeedPage.Key(name)); button.Classes.Add("rail"); button.Content = swatch; button.Width = 30; button.Height = 30; button.Padding = new Thickness(0);
            ToolTip.SetTip(button, (isRunning ? "Stop " : "Start ") + a.Display); Avalonia.Automation.AutomationProperties.SetName(button, a.Display);
            dots.Children.Add(button);
        }
    }
}
