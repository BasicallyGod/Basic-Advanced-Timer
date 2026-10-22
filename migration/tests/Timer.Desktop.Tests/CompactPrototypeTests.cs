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
    public void SettingsSwitchesPrototypeControlsWithoutChangingTheExpandedDashboard()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var column = Find<CompactColumn>(window, "CompactColumn");
            var clock = column.GetVisualDescendants().OfType<ClockText>().Single();
            var pause = Find<Button>(column, "CompactPause");
            Click(window, "Settings");
            var choice = Find<ComboBox>(window, "CompactAccountsStyle"); Assert.Equal(0, choice.SelectedIndex);
            choice.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            Assert.Equal("gauges", model.CompactAccountsStyle);
            Assert.Equal(340, Find<Grid>(window, "SidebarSlot").Width);
            Click(window, "NotesTab"); using var frame = window.CaptureRenderedFrame();
            Assert.Equal(112, column.Width); Assert.Same(clock, column.GetVisualDescendants().OfType<ClockText>().Single());
            Assert.Same(pause, Find<Button>(column, "CompactPause"));
            Assert.Equal(32, pause.Width); Assert.Equal(38, pause.Height); Assert.False(clock.IsVisible);
            Assert.True(Find<VerticalCompactClock>(column, "CompactVerticalClock").IsVisible);
            var vertical = Find<VerticalCompactClock>(column, "CompactVerticalClock");
            Assert.InRange(vertical.TranslatePoint(new Point(vertical.Bounds.Width, 0), column)!.Value.X, 0, column.Width);
            Click(window, "Settings"); Find<ComboBox>(window, "CompactAccountsStyle").SelectedIndex = 0;
            Click(window, "NotesTab"); Assert.Equal(176, column.Width);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("gauges")]
    [InlineData("heatmap")]
    [InlineData("capsules")]
    public void PrototypeClockDropsOnlyDisplayedSecondsAndIconControlsStayAtTheBottom(string style)
    {
        var instant = Now.Date.AddHours(9).AddMinutes(59).AddSeconds(59);
        var engine = new TrackingEngine(clock: () => instant);
        var model = new TimerModel(engine); model.SetCompactAccountsStyle(style);
        model.ToggleAccount(model.AccountNames[0]); var window = Open(model);
        try
        {
            var column = Find<CompactColumn>(window, "CompactColumn");
            var scroll = Find<ScrollViewer>(column, "CompactAccountScroll");
            void CheckClock()
            {
                var parts = model.ClockText.Split(':');
                Assert.Equal(parts[0], Find<TextBlock>(column, "CompactClockHours").Text);
                Assert.Equal(parts[1], Find<TextBlock>(column, "CompactClockMinutes").Text);
                Assert.False(column.GetVisualDescendants().OfType<ClockText>().Single().IsVisible);
            }
            CheckClock(); instant = instant.AddSeconds(2); model.Tick(); Dispatcher.UIThread.RunJobs(); CheckClock();
            Assert.Equal(1, model.Elapsed.Seconds); Assert.True(model.IsTracking);
            using var frame = window.CaptureRenderedFrame();
            var bottom = scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), column)!.Value.Y;
            var controls = new[] { "CompactPause", "CompactBreak", "CompactDay" }.Select(n => Find<Button>(column, n)).ToArray();
            Assert.All(controls, b =>
            {
                Assert.True(b.TranslatePoint(default, column)!.Value.Y >= bottom);
                Assert.InRange(b.TranslatePoint(new Point(b.Bounds.Width, b.Bounds.Height), column)!.Value.X, 0, column.Width);
                Assert.DoesNotContain(b.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible);
                Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(b)?.ToString()));
                Assert.Equal(ToolTip.GetTip(b), Avalonia.Automation.AutomationProperties.GetName(b));
            });
            Assert.Equal("Pause tracking", ToolTip.GetTip(controls[0]));
            Click(column, "CompactPause"); Assert.False(model.IsTracking); Assert.Equal("Resume tracking", ToolTip.GetTip(controls[0]));
            Click(column, "CompactBreak"); Assert.True(model.IsOnBreak); Assert.Equal("End break", ToolTip.GetTip(controls[1]));
            Click(column, "CompactBreak"); Assert.False(model.IsOnBreak);
            var started = model.DayStarted; Click(column, "CompactDay"); Assert.NotEqual(started, model.DayStarted);
            model.SetCompactAccountsStyle("current"); Dispatcher.UIThread.RunJobs(); using var restored = window.CaptureRenderedFrame();
            Assert.True(column.GetVisualDescendants().OfType<ClockText>().Single().IsVisible);
            Assert.Equal(38, controls[0].Width); Assert.Equal(164, controls[2].Width);
            Assert.Contains(controls[2].GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void StackedClockDigitsKeepTheirBoundsAcrossRolloverWithoutUnitLabels()
    {
        var clock = new VerticalCompactClock(); var window = new Window { Width = 112, Height = 200, Content = clock }; window.Show();
        try
        {
            Rect? hourBounds = null, minuteBounds = null;
            foreach (var elapsed in new[] { new TimeSpan(1, 11, 0), new TimeSpan(8, 59, 59), new TimeSpan(9, 0, 0), TimeSpan.FromHours(100) })
            {
                clock.Set(elapsed, false, false, false); using var frame = window.CaptureRenderedFrame();
                var hours = Find<TextBlock>(clock, "CompactClockHours"); var minutes = Find<TextBlock>(clock, "CompactClockMinutes");
                hourBounds ??= hours.Bounds; minuteBounds ??= minutes.Bounds;
                Assert.Equal(hourBounds, hours.Bounds); Assert.Equal(minuteBounds, minutes.Bounds);
                Assert.Equal(hours.Bounds.Width, minutes.Bounds.Width); Assert.Equal(hours.Bounds.X, minutes.Bounds.X);
                Assert.Equal(Avalonia.Media.FontFeature.Parse("tnum"), Assert.Single(hours.FontFeatures!));
                Assert.Equal(2, clock.GetVisualDescendants().OfType<TextBlock>().Count()); Assert.Null(ToolTip.GetTip(clock));
            }
        }
        finally { window.Close(); }
    }

}
