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

public sealed class PillLayoutTests
{
    private static DateTime Day => new(2026, 10, 14);
    private static TimerModel Model() { var m = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10))); m.CompleteSetup(); return m; }
    private static T Find<T>(Window w, string name) where T : Control => w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Control c) => c.RaiseEvent(new RoutedEventArgs(c is MenuItem ? MenuItem.ClickEvent : Button.ClickEvent));

    [Theory]
    [InlineData("Account three", "AT")]
    [InlineData("Northwind Traders", "NT")]
    [InlineData("Acme", "A")]
    [InlineData("Client 7", "C7")]
    [InlineData("", "")]
    public void InitialsAreTheFirstLettersOfUpToTwoWords(string name, string expected) => Assert.Equal(expected, PillLayouts.Initials(name));

    [Fact]
    public void TheLayoutChoiceSurvivesARestartAnUnknownOneIsFullAndTheFullChoiceLeavesNoLine()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-pill-layout-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store); Assert.Equal("full", model.PillStyle); model.SetPillStyle("minimal"); model.SetCaptureShortcut(HotkeySpec.Parse("Ctrl+Alt+N")); model.Save(); }
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store); Assert.Equal("minimal", model.PillStyle); Assert.Equal("Ctrl+Alt+N", model.CaptureShortcut!.ToString()); model.SetPillStyle("sideways"); Assert.Equal("full", model.PillStyle); model.SetPillStyle("compact"); model.Save(); }
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store); Assert.Equal("compact", model.PillStyle); model.SetPillStyle("full"); model.Save(); }
            using (var store = new TimerStore(folder)) { var settings = store.Load(new TrackingEngine()); Assert.DoesNotContain(settings.Extras!.AppearanceLines, l => l.StartsWith("pillstyle=", StringComparison.Ordinal)); }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public void EachLayoutGivesThePillItsOwnWidthAndShowsOnlyWhatItPromises()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            double Width() => (pill.Width / pill.Scale) - 2 * Ui.ShadowMargin;
            Assert.Equal(PillWindow.PillWidth, Width(), 1); Assert.True(Find<Control>(pill, "DayProgress").IsVisible); Assert.True(Find<Button>(pill, "PillBreak").IsVisible);
            model.SetPillStyle("compact"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(520, Width(), 1); Assert.False(Find<Control>(pill, "DayProgress").IsVisible); Assert.True(Find<Button>(pill, "PillBreak").IsVisible);
            model.SetPillStyle("minimal"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(360, Width(), 1); Assert.DoesNotContain(pill.GetVisualDescendants().OfType<Button>(), b => b.Name == "PillBreak"); // Break is not in the minimal layout.
            Assert.True(Find<Button>(pill, "PillPause").IsVisible && Find<Button>(pill, "PillDashboard").IsVisible);
            model.SetPillStyle("full"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(PillWindow.PillWidth, Width(), 1); Assert.True(Find<Control>(pill, "DayProgress").IsVisible);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheMinimalBadgeCarriesTheAccountsInitialsAndTheWindowKeepsItsWidthWhenOpened()
    {
        var model = Model(); model.SetPillStyle("minimal"); var pill = new PillWindow(model); pill.Show();
        try
        {
            model.ToggleAccount(model.Accounts.First(a => !BreakAccount.Is(a.Name)).Name); Dispatcher.UIThread.RunJobs();
            var badge = Find<Border>(pill, "PillBadge");
            Assert.Equal(PillLayouts.Initials(model.ActiveAccount!), Assert.IsType<TextBlock>(badge.Child).Text);
            pill.ToggleExpansion(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(360, pill.Width / pill.Scale - 2 * Ui.ShadowMargin, 1); // The window is no wider open than closed, so it never shifts sideways.
            pill.ToggleExpansion(); Dispatcher.UIThread.RunJobs(); Assert.Equal(360, pill.Width / pill.Scale - 2 * Ui.ShadowMargin, 1);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheFullAndCompactBadgesHoldTheAccountsColourDot()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show();
        try
        {
            model.ToggleAccount(model.Accounts.First(a => !BreakAccount.Is(a.Name)).Name); Dispatcher.UIThread.RunJobs();
            Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(Find<Border>(pill, "PillBadge").Child);
            model.SetPillStyle("compact"); Dispatcher.UIThread.RunJobs(); Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(Find<Border>(pill, "PillBadge").Child);
            // On a break the minimal badge goes back to the amber dot, since a break has no account initials.
            model.SetPillStyle("minimal"); model.ToggleBreak(); Dispatcher.UIThread.RunJobs(); Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(Find<Border>(pill, "PillBadge").Child);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountNameShowsBesideItsChevronInEveryLayoutThatHasAName()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show();
        try
        {
            var name = model.Accounts.First(a => !BreakAccount.Is(a.Name)).Name; model.ToggleAccount(name);
            foreach (var layout in new[] { "full", "compact" })
            {
                model.SetPillStyle(layout); Dispatcher.UIThread.RunJobs(); pill.UpdateLayout();
                var text = pill.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == name && t.IsEffectivelyVisible);
                Assert.True(text.Bounds.Width > 20, layout + ": the name has room"); // It once measured to nothing, leaving the chevron over the line beneath.
                var chevron = pill.GetVisualDescendants().OfType<Chevron>().Single();
                var textRight = text.TranslatePoint(new Avalonia.Point(text.Bounds.Width, 0), pill)!.Value.X; var chevronLeft = chevron.TranslatePoint(new Avalonia.Point(0, 0), pill)!.Value.X;
                Assert.True(chevronLeft >= textRight - 1, layout + ": the chevron sits after the name");
            }
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheMenuStartsAndEndsABreakBecauseTheMinimalPillHasNoBreakButton()
    {
        var model = Model(); var menu = Ui.SettingsMenu(model); var items = menu.ItemsSource!.OfType<MenuItem>().ToList();
        var breakItem = items.Single(m => m.Name == "BreakMenu"); Assert.Equal("Start break", breakItem.Header);
        Click(breakItem); Assert.True(model.IsOnBreak);
        var again = Ui.SettingsMenu(model).ItemsSource!.OfType<MenuItem>().Single(m => m.Name == "BreakMenu"); Assert.Equal("End break", again.Header);
        Click(again); Assert.False(model.IsOnBreak);
    }

    [AvaloniaFact]
    public void EachLayoutPictureFitsInsideItsFrameWithRoomToSpare()
    {
        var model = Model(); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            dashboard.OpenSettings();
            foreach (var layout in PillLayouts.All)
            {
                var frame = Find<Button>(dashboard, "PillLayout_" + layout.Key).GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("chip"));
                var picture = (Panel)frame.Child!; picture.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var room = frame.Bounds.Width - frame.Padding.Left - frame.Padding.Right - 2 * frame.BorderThickness.Left;
                Assert.True(picture.DesiredSize.Width <= room - 4, $"{layout.Key}: the picture is {picture.DesiredSize.Width:0} wide in {room:0}");
            }
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void SettingsShowEachLayoutAsAPictureWithItsWordsAndTheChosenOneIsMarked()
    {
        var model = Model(); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            dashboard.OpenSettings();
            foreach (var layout in PillLayouts.All)
            {
                var button = Find<Button>(dashboard, "PillLayout_" + layout.Key);
                Assert.Contains(layout.Title, button.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
                Assert.Contains(layout.Description, button.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            }
            Assert.Contains("mainAction", Find<Button>(dashboard, "PillLayout_full").Classes);
            Click(Find<Button>(dashboard, "PillLayout_compact")); Dispatcher.UIThread.RunJobs();
            Assert.Equal("compact", model.PillStyle);
            Assert.Contains("mainAction", Find<Button>(dashboard, "PillLayout_compact").Classes); Assert.DoesNotContain("mainAction", Find<Button>(dashboard, "PillLayout_full").Classes);
        }
        finally { dashboard.Close(); }
    }
    [AvaloniaFact]
    public void EveryLayoutKeepsAccountTimerAndActionsInOrderWithoutChangingItsWidth()
    {
        var model = Model();
        var old = model.AccountNames[0];
        var name = "Account with a deliberately long name for the quarterly client reporting programme";
        model.SetAccounts(model.AccountSettings.Select(a => a.Name == old ? a with { Name = name } : a), new Dictionary<string, string> { [old] = name });
        model.ToggleAccount(name);
        var pill = new PillWindow(model); pill.Show();
        try
        {
            foreach (var layout in PillLayouts.All)
            {
                model.SetPillStyle(layout.Key); Dispatcher.UIThread.RunJobs();
                using var frame = pill.CaptureRenderedFrame();
                var account = Find<Button>(pill, "PillAccounts"); var timer = Find<Button>(pill, "PillDashboard"); var pause = Find<Button>(pill, "PillPause");
                Assert.True(account.Bounds.Right <= timer.Bounds.Left, layout.Key);
                Assert.True(timer.Bounds.Right <= pause.Bounds.Left, layout.Key);
                Assert.Equal(layout.Width, pill.Width / pill.Scale - 2 * Ui.ShadowMargin, 1);
                Assert.Contains(name, ToolTip.GetTip(account)?.ToString());
                if (layout.Key != "minimal")
                {
                    var label = Find<TextBlock>(pill, "PillAccountName");
                    Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, label.TextTrimming);
                    Assert.Equal(name, ToolTip.GetTip(label)); Assert.True(label.Bounds.Width > 20);
                    var right = label.TranslatePoint(new Point(label.Bounds.Width, 0), account)!.Value.X;
                    Assert.True(right <= account.Bounds.Width, layout.Key + ": long names stay inside the account area");
                }
                if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is { Length: > 0 } previews)
                {
                    Directory.CreateDirectory(previews); frame?.Save(Path.Combine(previews, "pill-consistent-" + layout.Key + ".png"));
                }
            }
            model.SetPillStyle("full"); Dispatcher.UIThread.RunJobs();
            var progress = Find<Grid>(pill, "DayProgress");
            Assert.True(progress.IsHitTestVisible);
            var tip = ToolTip.GetTip(progress)?.ToString();
            Assert.Contains("today's working day", tip); Assert.Contains("09:00", tip); Assert.Contains("17:00", tip);
        }
        finally { pill.Close(); }
    }

    [Fact]
    public void IdleBreakAndFinishedWordingIsSharedWithTheTray()
    {
        var model = Model();
        Assert.Equal("Ready to track", model.Status); Assert.Contains(model.Status, TrayController.Tooltip(model));
        model.ToggleAccount(model.AccountNames[0]); Assert.Equal("Tracking", model.Status);
        model.TogglePause(); Assert.Equal("Ready to track", model.Status); Assert.Contains(model.Status, TrayController.Tooltip(model));
        model.ToggleBreak(); Assert.Equal("On break", model.Status); Assert.Contains(model.Status, TrayController.Tooltip(model));
        model.ToggleBreak(); model.ToggleDay();
        Assert.Equal("Day finished", model.Status); Assert.Contains(model.Status, TrayController.Tooltip(model));
    }

}
