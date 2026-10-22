using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class IntegratedSidebarTests
{
    private static readonly string[] Destinations = { "BackToPill", "TimelineTab", "NotesTab", "WeekOverviewTab", "WeekHoursTab", "WeekEntriesTab" };
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Visual root, string name) { Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static DashboardWindow Open(TimerModel model, bool integrated = true)
    {
        model.CompleteSetup(); model.SetIntegratedRail(integrated); var window = new DashboardWindow(model); window.Show();
        Find<Grid>(window, "SidebarSlot").Transitions = null; using var frame = window.CaptureRenderedFrame(); return window;
    }
    [Fact]
    public void RailChoiceIsOptInAndPersistsWithoutChangingOtherLayouts()
    {
        var folder = Path.Combine(Path.GetTempPath(), "integrated-rail-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store);
                Assert.False(model.IntegratedRail); model.SetPillStyle("minimal"); model.SetCompactAccountsStyle("heatmap"); model.SetIntegratedRail(true);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store);
                Assert.True(model.IntegratedRail); Assert.Equal("minimal", model.PillStyle); Assert.Equal("heatmap", model.CompactAccountsStyle);
                model.SetIntegratedRail(false); Assert.DoesNotContain(store.Load(engine).Extras!.AppearanceLines, l => l.StartsWith("integratedrail="));
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
    }
    [AvaloniaFact]
    public void OnePersistentRailUsesTheSameNavigationButtonsAcrossEveryPaneAndRestoresThem()
    {
        var model = TimerModel.CreateDemo(); var window = Open(model, false);
        try
        {
            var buttons = Destinations.Concat(new[] { "Settings", "QuitTimer" }).ToDictionary(n => n, n => Find<Button>(window, n));
            var originalParents = buttons.ToDictionary(x => x.Key, x => x.Value.Parent);
            model.SetIntegratedRail(true); Dispatcher.UIThread.RunJobs(); using var first = window.CaptureRenderedFrame();
            var rail = Find<IntegratedSidebar>(window, "IntegratedSidebar"); Assert.True(rail.IsEffectivelyVisible); Assert.Equal(88, rail.Bounds.Width);
            foreach (var target in new[] { "NotesTab", "TimelineTab", "WeekOverviewTab", "WeekHoursTab", "WeekEntriesTab", "Settings" })
            {
                Click(window, target); using var frame = window.CaptureRenderedFrame();
                Assert.Same(rail, Find<IntegratedSidebar>(window, "IntegratedSidebar")); Assert.Contains("selected", buttons[target].Classes);
                Assert.Equal(0, Find<Grid>(window, "SidebarSlot").Width);
                Assert.False(Find<CompactColumn>(window, "CompactColumn").IsEffectivelyVisible);
                foreach (var name in Destinations) { Assert.Same(buttons[name], Find<Button>(rail, name)); Assert.Equal(34, buttons[name].Width); }
            }
            Assert.Equal(2, rail.Navigation.ColumnDefinitions.Count);
            var showPill = false; window.OpenPill = () => showPill = true; Click(rail, "BackToPill"); Assert.True(showPill);
            model.SetIntegratedRail(false); Dispatcher.UIThread.RunJobs();
            foreach (var pair in buttons) { Assert.Same(pair.Value, Find<Button>(window, pair.Key)); Assert.Same(originalParents[pair.Key], pair.Value.Parent); }
            Click(window, "TimelineTab"); Assert.Equal(340, Find<Grid>(window, "SidebarSlot").Width);
            Click(window, "NotesTab"); Assert.Equal(CompactColumn.ColumnWidth, Find<Grid>(window, "SidebarSlot").Width);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void AccountRowsRetainBudgetIdentityTrackingAndWarningsWithoutHover()
    {
        var model = TimerModel.CreateDemo(); model.SetAccounts(model.AccountSettings.Select(a => a.Name == "Account one" ? a with { BudgetHours = .5, BudgetPeriod = "day" } : a));
        var window = Open(model);
        try
        {
            var rail = Find<IntegratedSidebar>(window, "IntegratedSidebar");
            Assert.Equal(model.AccountNames.Count, rail.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("IntegratedAccount_") == true));
            foreach (var account in model.Accounts.Where(a => !BreakAccount.Is(a.Name)))
            {
                var key = Ui.NameKey(account.Name); var gauge = Find<CompactBudgetIndicator>(rail, "IntegratedBudget_" + key);
                Assert.Equal(model.BudgetRatio(account), gauge.Ratio); Assert.Equal(Color.Parse(Ui.Visible(account.Colour)), gauge.AccountColour);
                var row = Find<Button>(rail, "IntegratedAccount_" + key); Assert.Equal(32, row.Height);
                Assert.Equal(model.FormatCompact(model.Today(account)), Find<TextBlock>(rail, "IntegratedToday_" + key).Text);
                Assert.Contains(account.Display, ToolTip.GetTip(row)!.ToString()); Assert.Contains(model.BudgetLine(account), ToolTip.GetTip(row)!.ToString());
                Assert.Equal(ToolTip.GetTip(row)!.ToString()!.Replace('\n', ' '), Avalonia.Automation.AutomationProperties.GetName(row));
            }
            var target = "IntegratedAccount_" + Ui.NameKey("Account one"); var colour = Find<CompactBudgetIndicator>(rail, "IntegratedBudget_" + Ui.NameKey("Account one")).AccountColour;
            Assert.True(Find<TextBlock>(rail, "IntegratedOver_" + Ui.NameKey("Account one")).IsVisible);
            Click(rail, target); Assert.Equal("Account one", model.ActiveAccount); Assert.Contains("active", Find<Button>(rail, target).Classes);
            Assert.True(Find<TextBlock>(rail, "IntegratedLive_" + Ui.NameKey("Account one")).IsVisible);
            Assert.Equal(colour, Find<CompactBudgetIndicator>(rail, "IntegratedBudget_" + Ui.NameKey("Account one")).AccountColour);
            Click(rail, "IntegratedPause"); Assert.False(model.IsTracking); Assert.Contains("selectedAccount", Find<Button>(rail, target).Classes);
            Click(rail, "IntegratedBreak"); Assert.True(model.IsOnBreak); Assert.Contains("break", Find<TextBlock>(rail, "IntegratedClockHours").Classes);
            Click(rail, "IntegratedBreak"); Assert.False(model.IsOnBreak);
            var started = model.DayStarted; Click(rail, "IntegratedDay"); Assert.NotEqual(started, model.DayStarted);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void KeyboardTraversalReachesTrackingEveryDestinationAndTheFooter()
    {
        var model = TimerModel.CreateDemo(); var window = Open(model);
        try
        {
            var rail = Find<IntegratedSidebar>(window, "IntegratedSidebar"); var pause = Find<Button>(rail, "IntegratedPause"); pause.Focus();
            var visited = new HashSet<string>();
            for (var i = 0; i < 40; i++)
            {
                if (window.FocusManager!.GetFocusedElement() is Control { Name: { } name }) visited.Add(name);
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            }
            foreach (var name in Destinations.Concat(new[] { "IntegratedPause", "IntegratedBreak", "Settings", "IntegratedDay", "QuitTimer" })) Assert.Contains(name, visited);
            Assert.Contains("IntegratedAccount_" + Ui.NameKey("Account one"), visited);
            var account = Find<Button>(rail, "IntegratedAccount_" + Ui.NameKey("Account one")); account.Focus();
            window.KeyPressQwerty(PhysicalKey.F10, RawInputModifiers.Shift); window.KeyReleaseQwerty(PhysicalKey.F10, RawInputModifiers.Shift); Dispatcher.UIThread.RunJobs();
            Assert.True(account.ContextMenu!.IsOpen);
            var details = Assert.IsType<TextBlock>(account.ContextMenu.ItemsSource!.OfType<MenuItem>().Single().Header);
            Assert.Contains("Account one", details.Text); Assert.Contains("recorded today", details.Text); account.ContextMenu.Close();
            pause.Focus(); var running = model.IsTracking; window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); Assert.NotEqual(running, model.IsTracking);
            Click(window, "Settings"); var choice = Find<CheckBox>(window, "IntegratedRailChoice"); Assert.True(choice.IsChecked);
            choice.IsChecked = false; choice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); Assert.False(model.IntegratedRail);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData("Light", 1.0)]
    [InlineData("Light", 1.25)]
    [InlineData("Dark", 1.5)]
    [InlineData("Dark", 2.0)]
    public void DenseAccountsScrollWithoutDisplacingNavigationAtCommonDisplayScales(string theme, double scale)
    {
        var model = TimerModel.CreateDemo(); model.SetTheme(theme); model.SetAccounts(model.AccountSettings.Concat(Enumerable.Range(1, 22).Select(i => new AccountSettings("Client " + i, 8, "month"))));
        var window = Open(model);
        try
        {
            Click(window, "NotesTab"); using var initial = window.CaptureRenderedFrame();
            var rail = Find<IntegratedSidebar>(window, "IntegratedSidebar"); var scroll = Find<ScrollViewer>(rail, "IntegratedAccounts");
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            var before = rail.Navigation.TranslatePoint(default, rail); var footer = Find<Grid>(rail, "IntegratedFooter"); var footerBefore = footer.TranslatePoint(default, rail);
            scroll.Offset = new Vector(0, 120); Click(rail, "IntegratedAccount_" + Ui.NameKey("Account one")); using var frame = window.CaptureRenderedFrame();
            Assert.Equal(120, scroll.Offset.Y); Assert.Equal(before, rail.Navigation.TranslatePoint(default, rail)); Assert.Equal(footerBefore, footer.TranslatePoint(default, rail));
            Assert.InRange(footer.TranslatePoint(new Point(0, footer.Bounds.Height), rail)!.Value.Y, 0, rail.Bounds.Height);
            foreach (var clockName in new[] { "IntegratedClockHours", "IntegratedClockMinutes", "IntegratedClockSeconds" })
            {
                var digit = Find<TextBlock>(rail, clockName); Assert.InRange(digit.TranslatePoint(new Point(digit.Bounds.Width, 0), rail)!.Value.X, 0, 88);
            }
            // Apply the scale once at the application surface. Rendering a Window directly
            // into a high-DPI bitmap applies its platform transform a second time.
            var source = (Control)window.Content!; window.Content = null;
            window.Content = new LayoutTransformControl { Child = source, LayoutTransform = new ScaleTransform(scale, scale) };
            window.Width *= scale; window.Height *= scale; using var settled = window.CaptureRenderedFrame();
            var footerCorner = footer.TranslatePoint(new Point(footer.Bounds.Width, footer.Bounds.Height), window)!.Value;
            Assert.InRange(footerCorner.Y, 0, window.Bounds.Height); Assert.InRange(footerCorner.X, 0, window.Bounds.Width);
            using var scaled = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height)); scaled.Render(window);
            if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is { Length: > 0 } folder) { Directory.CreateDirectory(folder); scaled.Save(Path.Combine(folder, $"integrated-{theme}-{scale:0.##}x.png")); }
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void WritePanePreviews()
    {
        if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = TimerModel.CreateDemo(); model.SetTheme(theme); var window = Open(model);
            try
            {
                foreach (var page in new[] { "NotesTab", "TimelineTab", "WeekOverviewTab", "WeekHoursTab", "WeekEntriesTab", "Settings" })
                {
                    Click(window, page); window.MouseMove(new Point(window.Width - 5, window.Height - 5)); using var first = window.CaptureRenderedFrame(); using var image = window.CaptureRenderedFrame(); image?.Save(Path.Combine(folder, $"integrated-{theme}-{page}.png"));
                }
            }
            finally { window.Close(); }
        }
    }
    [AvaloniaFact]
    public void TimerStatesRetainColoursSecondsAndStableDigitBounds()
    {
        var model = TimerModel.CreateDemo(); var window = Open(model);
        try
        {
            Click(window, "NotesTab");
            var rail = Find<IntegratedSidebar>(window, "IntegratedSidebar");
            var hours = Find<TextBlock>(rail, "IntegratedClockHours"); var minutes = Find<TextBlock>(rail, "IntegratedClockMinutes"); var seconds = Find<TextBlock>(rail, "IntegratedClockSeconds");
            using var initial = window.CaptureRenderedFrame(); var hourBounds = hours.Bounds; var minuteBounds = minutes.Bounds;
            void Check(string state)
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.Equal(hourBounds, hours.Bounds); Assert.Equal(minuteBounds, minutes.Bounds);
                Assert.Equal(model.Elapsed.Seconds.ToString("00"), seconds.Text); Assert.True(seconds.FontSize < minutes.FontSize); Assert.Contains("muted", seconds.Classes);
                Assert.Contains(state, hours.Classes); Assert.Contains(state, minutes.Classes);
                if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is { Length: > 0 } folder) { Directory.CreateDirectory(folder); frame?.Save(Path.Combine(folder, "integrated-state-" + state + ".png")); }
            }
            Check("clockIdle"); model.ToggleAccount("Account one"); Check("live");
            model.TogglePause(); Check("clockIdle");
            model.SetAccounts(model.AccountSettings.Select(a => a.Name == "Account one" ? a with { BudgetHours = .5, BudgetPeriod = "day" } : a));
            Dispatcher.UIThread.RunJobs(); using var updatedBudget = window.CaptureRenderedFrame();
            Assert.True(Find<TextBlock>(rail, "IntegratedOver_" + Ui.NameKey("Account one")).IsVisible);
            Assert.True(Find<CompactBudgetIndicator>(rail, "IntegratedBudget_" + Ui.NameKey("Account one")).Ratio > 1);
            model.ForceOvertime(); Check("overtime"); model.ToggleBreak(); Check("break");
            Assert.DoesNotContain("overtime", hours.Classes);
        }
        finally { window.Close(); }
    }

}
