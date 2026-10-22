using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class CompactPrototypeTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0);
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Visual root, string name) { Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static DashboardWindow Open(TimerModel model)
    {
        model.CompleteSetup(); var window = new DashboardWindow(model); window.Show();
        Find<Grid>(window, "SidebarSlot").Transitions = null;
        Click(window, "NotesTab"); using var frame = window.CaptureRenderedFrame(); return window;
    }
    private static TimerModel Model()
    {
        var accounts = new List<AccountSettings>
        {
            new("Daily account", 2, "day"), new("Weekly account", 4, "week"),
            new("Monthly account", 8, "month"), new("Quarterly account", 16, "quarter"),
            new("No budget"), new("Over budget", .5, "day"), new("Empty budget", 2, "week")
        };
        var engine = new TrackingEngine(clock: () => Now);
        foreach (var account in accounts.Where(a => a.Name != "Empty budget"))
        {
            engine.Sessions.Add(new("2026-10-22", account.Name, "09:00:00", "added", "60"));
            engine.Sessions.Add(new("2026-10-21", account.Name, "09:00:00", "added", "120"));
            engine.Sessions.Add(new("2026-10-01", account.Name, "09:00:00", "added", "240"));
        }
        return new TimerModel(engine, new StoreSettings(accounts, false, "Light", DayOfWeek.Monday));
    }

    [Theory]
    [InlineData("gauges")]
    [InlineData("heatmap")]
    [InlineData("capsules")]
    public void PrototypeChoiceIsOptInPersistentAndDoesNotChangeOtherAppearanceSettings(string style)
    {
        var folder = Path.Combine(Path.GetTempPath(), "compact-style-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store);
                Assert.Equal("current", model.CompactAccountsStyle);
                model.SetPillStyle("minimal"); model.SetCompactAccountsStyle(style);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store);
                Assert.Equal(style, model.CompactAccountsStyle); Assert.Equal("minimal", model.PillStyle);
                model.SetCompactAccountsStyle("unknown"); Assert.Equal("current", model.CompactAccountsStyle);
                Assert.DoesNotContain(store.Load(engine).Extras!.AppearanceLines, l => l.StartsWith("compactaccounts="));
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
    }

    [AvaloniaTheory]
    [InlineData("gauges")]
    [InlineData("heatmap")]
    [InlineData("capsules")]
    public void EachPrototypeUsesConfiguredBudgetWindowsAndRetainsTrackingAndOrder(string style)
    {
        var model = Model(); model.SetCompactAccountsStyle(style); var window = Open(model);
        try
        {
            var column = Find<CompactColumn>(window, "CompactColumn");
            Assert.True(column.CurrentWidth < 120); Assert.Equal(column.CurrentWidth, Find<Grid>(window, "SidebarSlot").Width);
            Assert.Equal(model.AccountNames, column.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("CompactDot_") == true).Select(b => model.AccountNames.Single(n => b.Name == "CompactDot_" + Ui.NameKey(n))));
            foreach (var account in model.Accounts.Where(a => !BreakAccount.Is(a.Name)))
            {
                var key = Ui.NameKey(account.Name); var gauge = Find<CompactBudgetIndicator>(column, "CompactBudget_" + key);
                Assert.Equal(model.BudgetRatio(account), gauge.Ratio); Assert.Equal(model.BudgetPace(account), gauge.Pace);
                Assert.Equal(Avalonia.Media.Color.Parse(Ui.Visible(account.Colour)), gauge.AccountColour);
                Assert.Equal(model.FormatCompact(model.Today(account)), Find<TextBlock>(column, "CompactTime_" + key).Text);
                var button = Find<Button>(column, "CompactDot_" + key); var tip = ToolTip.GetTip(button)!.ToString();
                Assert.Contains(account.Display, tip); Assert.Contains(model.BudgetLine(account), tip);
            }
            Assert.Equal(.5, Find<CompactBudgetIndicator>(column, "CompactBudget_" + Ui.NameKey("Daily account")).Ratio);
            Assert.Equal(.75, Find<CompactBudgetIndicator>(column, "CompactBudget_" + Ui.NameKey("Weekly account")).Ratio);
            Assert.Equal(.875, Find<CompactBudgetIndicator>(column, "CompactBudget_" + Ui.NameKey("Monthly account")).Ratio);
            Assert.Equal(.4375, Find<CompactBudgetIndicator>(column, "CompactBudget_" + Ui.NameKey("Quarterly account")).Ratio);
            Assert.Null(Find<CompactBudgetIndicator>(column, "CompactBudget_" + Ui.NameKey("No budget")).Ratio);
            Assert.Equal(0, Find<CompactBudgetIndicator>(column, "CompactBudget_" + Ui.NameKey("Empty budget")).Ratio);
            var over = Find<TextBlock>(column, "CompactOver_" + Ui.NameKey("Over budget")); Assert.True(over.IsVisible);
            var target = Find<Button>(column, "CompactDot_" + Ui.NameKey("Weekly account"));
            Click(column, target.Name!); Assert.Equal("Weekly account", model.ActiveAccount);
            Assert.Same(target, Find<Button>(column, target.Name!)); Assert.Contains("active", target.Classes);
            Assert.True(Find<TextBlock>(column, "CompactLive_" + Ui.NameKey("Weekly account")).IsVisible);
            Click(column, "CompactPause"); Assert.Null(model.ActiveAccount); Assert.DoesNotContain("active", target.Classes); Assert.Contains("selectedAccount", target.Classes);
            model.GoToDate(Now.AddMonths(-1)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(.875, Find<CompactBudgetIndicator>(column, "CompactBudget_" + Ui.NameKey("Monthly account")).Ratio);
            model.SetCompactAccountsStyle("current"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(CompactColumn.ColumnWidth, column.Width);
            Assert.Empty(column.GetVisualDescendants().OfType<CompactBudgetIndicator>());
            Click(window, "TimelineTab"); Assert.Equal(340, Find<Grid>(window, "SidebarSlot").Width);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("gauges", "Light")]
    [InlineData("heatmap", "Light")]
    [InlineData("capsules", "Light")]
    [InlineData("gauges", "Dark")]
    [InlineData("heatmap", "Dark")]
    [InlineData("capsules", "Dark")]
    public void DensePrototypesScrollAndKeepTimerControlsFixed(string style, string theme)
    {
        var model = Model(); model.SetTheme(theme);
        model.SetAccounts(model.AccountSettings.Concat(Enumerable.Range(1, 22).Select(i => new AccountSettings("Client " + i, 4, "month"))));
        model.SetCompactAccountsStyle(style); var window = Open(model);
        try
        {
            var column = Find<CompactColumn>(window, "CompactColumn");
            var scroll = Find<ScrollViewer>(column, "CompactAccountScroll");
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            var pause = Find<Button>(column, "CompactPause"); var before = pause.TranslatePoint(default, window);
            scroll.Offset = new Vector(0, 120); using var frame = window.CaptureRenderedFrame();
            Click(column, "CompactDot_" + Ui.NameKey("Daily account"));
            Assert.Equal(120, scroll.Offset.Y); Assert.Equal(before, pause.TranslatePoint(default, window));
            var day = Find<Button>(column, "CompactDay");
            Assert.InRange(day.TranslatePoint(new Point(0, day.Bounds.Height), column)!.Value.Y, 0, column.Bounds.Height);
            Assert.All(column.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("CompactDot_") == true), b => Assert.Equal(42, b.Height));
            scroll.Offset = default;
            if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder); using var settling = window.CaptureRenderedFrame(); using var image = window.CaptureRenderedFrame(); image?.Save(Path.Combine(folder, theme + "-compact-scroll-" + style + ".png"));
            }
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void WriteComparisonPreviewsFromTheSameAccountData()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var style in new[] { "current", "gauges", "heatmap", "capsules" })
        {
            var model = Model(); model.SetTheme(theme); model.SetCompactAccountsStyle(style);
            model.ToggleAccount("Daily account"); var window = Open(model);
            try
            {
                using var settled = window.CaptureRenderedFrame(); using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(folder, theme + "-compact-" + style + ".png"));
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void SettingsSwitchesPrototypesWithoutChangingTimerControlsOrTheExpandedDashboard()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var column = Find<CompactColumn>(window, "CompactColumn");
            var clock = column.GetVisualDescendants().OfType<ClockText>().Single();
            var pause = Find<Button>(column, "CompactPause"); var pauseSize = pause.Bounds.Size;
            Click(window, "Settings");
            var choice = Find<ComboBox>(window, "CompactAccountsStyle"); Assert.Equal(0, choice.SelectedIndex);
            choice.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            Assert.Equal("gauges", model.CompactAccountsStyle);
            Assert.Equal(340, Find<Grid>(window, "SidebarSlot").Width);
            Click(window, "NotesTab"); using var frame = window.CaptureRenderedFrame();
            Assert.Equal(112, column.Width); Assert.Same(clock, column.GetVisualDescendants().OfType<ClockText>().Single());
            Assert.Equal(pauseSize, pause.Bounds.Size);
            Assert.InRange(clock.TranslatePoint(new Point(clock.Bounds.Width, 0), column)!.Value.X, 0, column.Width);
            Click(window, "Settings"); Find<ComboBox>(window, "CompactAccountsStyle").SelectedIndex = 0;
            Click(window, "NotesTab"); Assert.Equal(176, column.Width);
        }
        finally { window.Close(); }
    }

}
