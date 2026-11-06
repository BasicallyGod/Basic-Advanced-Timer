using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class PillTests
{
    private static DateTime Day => new(2026, 10, 14);
    private static TimerModel Model() { var m = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10))); m.CompleteSetup(); return m; }
    private static T Find<T>(Window w, string name) where T : Control => w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Control c) => c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string? Tip(Control c) => ToolTip.GetTip(c)?.ToString();
    private static void ClickAt(Window w, Control c)
    {
        Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame();
        var point = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
        w.MouseDown(point, MouseButton.Left); w.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [InlineData("Account three", "AT")]
    [InlineData("Northwind Traders", "NT")]
    [InlineData("Acme", "A")]
    [InlineData("Client 7", "C7")]
    [InlineData("", "")]
    public void InitialsAreTheFirstLettersOfUpToTwoWords(string name, string expected) => Assert.Equal(expected, PillWindow.Initials(name));

    [AvaloniaFact]
    public void ThePillIsASmallWideCardWithItsWindowAtTheScaledSize()
    {
        var pill = new PillWindow(Model()); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var surface = Find<Border>(pill, "PillSurface");
            Assert.Equal(PillWindow.PillWidth, surface.Width); Assert.Equal(PillWindow.PillHeight, surface.Height);
            Assert.True(surface.Width > 4 * surface.Height);
            Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5); Assert.Equal(pill.WindowWidthNow, pill.Width, 0.5);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheLightCirclePauseAndMugFollowTheState()
    {
        using var motion = new EnabledMotionScope();
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var lights = Find<Border>(pill, "PillStateLight"); var circle = Find<Button>(pill, "PillPause"); var mug = Find<Button>(pill, "PillBreak");
            Assert.Equal("Not recording", Tip(lights)); Assert.StartsWith("Start ", Tip(circle)); Assert.Equal("Start break", Tip(mug));
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Recording", Tip(lights)); Assert.StartsWith("Pause", Tip(circle));
            Assert.Equal("ACCOUNT ONE", Find<SegmentName>(pill, "PillAccountName").Text); Assert.True(Find<Button>(pill, "PillPause").GetVisualDescendants().OfType<TransportGlyph>().Single().Paused); // The display names the running account and shows the pause bars.
            model.ToggleBreak(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("On break", Tip(lights)); Assert.StartsWith("End break", Tip(mug)); Assert.True(pill.GetVisualDescendants().OfType<CupGlyph>().Single().Steaming);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void EveryPressableZoneOfTheDisplayWashesTheSameWay()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var zones = new[] { Find<Button>(pill, "PillDashboard"), Find<Button>(pill, "PillAlertsButton"), Find<Button>(pill, "PillPause"), pill.AccountsButton, Find<Button>(pill, "PillBreak") };
            Assert.All(zones, z => Assert.Contains("zone", z.Classes)); // One style gives the hover and pressed wash to all five.
            Assert.True(pill.TryFindResource("ZoneHover", pill.ActualThemeVariant, out _)); Assert.True(pill.TryFindResource("ZonePressed", pill.ActualThemeVariant, out _));
            var display = Find<Border>(pill, "PillDisplay"); // And every divider in it is the same dotted column.
            var dividers = display.GetVisualDescendants().OfType<DottedDivider>().ToList(); Assert.Equal(3, dividers.Count);
            Assert.All(dividers, d => { Assert.Equal(DottedDivider.ColumnWidth, d.Bounds.Width, 0.5); Assert.Equal(DottedDivider.ColumnHeight, d.Bounds.Height, 0.5); });
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheReminderLightShowsOnlyWhileAReminderIsDue()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try { Assert.Equal("Nothing waiting", Avalonia.Automation.AutomationProperties.GetName(Find<Button>(pill, "PillAlertsButton"))); Assert.False(model.ReminderDue); }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ClickingTheClockOpensTheDashboardAndTheCircleStartsAndPauses()
    {
        var model = Model(); var opened = 0; var pill = new PillWindow(model) { OpenDashboard = () => opened++ }; pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            ClickAt(pill, Find<Button>(pill, "PillDashboard")); Assert.Equal(1, opened);
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs();
            ClickAt(pill, Find<Button>(pill, "PillPause")); Assert.False(model.IsTracking); Assert.Equal("Account one", model.SelectedAccount);
            ClickAt(pill, Find<Button>(pill, "PillPause")); Assert.True(model.IsTracking);
            ClickAt(pill, Find<Button>(pill, "PillBreak")); Assert.True(model.IsOnBreak);
            ClickAt(pill, Find<Button>(pill, "PillBreak")); Assert.False(model.IsOnBreak);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void DraggingFromAnyPartOfThePillDoesNotClick()
    {
        var model = Model(); var opened = 0; var pill = new PillWindow(model) { OpenDashboard = () => opened++ }; pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs(); using var initial = pill.CaptureRenderedFrame();
            foreach (var name in new[] { "PillDashboard", "PillBreak", "PillPause" })
            {
                var button = Find<Button>(pill, name);
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), pill)!.Value;
                pill.MouseDown(point, MouseButton.Left); pill.MouseMove(point + new Vector(20, 10)); pill.MouseUp(point + new Vector(20, 10), MouseButton.Left);
                Assert.Equal(0, opened); Assert.Equal("Account one", model.ActiveAccount); Assert.False(model.IsOnBreak);
            }
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountsButtonOpensTheListAndFoldsItAgainAndChoosingAnAccountLeavesItOpen()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.False(pill.PanelOpen); Assert.Equal("Account one", ToolTip.GetTip(pill.AccountsButton)); // The box shows a dot, so its tooltip names the account.
            Click(pill.AccountsButton); Assert.True(pill.PanelOpen); Assert.Equal("Close the accounts", ToolTip.GetTip(pill.AccountsButton));
            Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5); // The pill never grows for it.
            Assert.Equal(new CornerRadius(PillWindow.PillRadius), Find<Border>(pill, "PillSurface").CornerRadius); // The pill keeps its shape; the list hangs below it.
            Click(pill.AccountsButton); Assert.False(pill.PanelOpen); Assert.Equal(new CornerRadius(PillWindow.PillRadius), Find<Border>(pill, "PillSurface").CornerRadius);
            Click(pill.AccountsButton); Dispatcher.UIThread.RunJobs();
            model.ToggleAccount("Account two"); Dispatcher.UIThread.RunJobs();
            Assert.True(pill.PanelOpen); // Choosing an account leaves the list open, so a wrong choice can be put right at once.
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs(); Assert.True(pill.PanelOpen); Assert.Equal("Account one", model.ActiveAccount);
            pill.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(pill.PanelOpen); // Escape folds it away, as the button does.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void NothingOnThePillMovesWhenAGlowComesAndGoes()
    {
        var demo = TimerModel.CreateDemo(); demo.CompleteSetup(); var pill = new PillWindow(demo); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var surface = Find<Border>(pill, "PillSurface");
            Point At(Control c) => c.TranslatePoint(default, pill)!.Value;
            (double, double, Point, Point, Point, Point) Snapshot() { Dispatcher.UIThread.RunJobs(); using var f = pill.CaptureRenderedFrame();
                return (surface.BorderThickness.Left, surface.Bounds.Width, At(Find<Border>(pill, "PillStateLight")), At(Find<Button>(pill, "PillDashboard")), At(Find<Button>(pill, "PillPause")), At(pill.AccountsButton)); }
            var idle = Snapshot();
            demo.ToggleAccount("Account one"); Assert.Equal(idle, Snapshot()); // Recording.
            demo.ToggleBreak(); Assert.Equal(idle, Snapshot()); // A break.
            demo.ToggleBreak(); demo.ToggleAccount("Account three"); Assert.Equal(idle, Snapshot()); // Recording again, late in the day.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountsListHasNoManageIconBecauseSettingsAndTheMenuOwnThat()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            // The pop-up has no window of its own in a headless run, so the list's card is laid out in one made for it.
            var card = pill.MakeDropdownCard(); var host = new Window { Width = 320, Height = 600, Content = card }; host.Show(); Settle(host);
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<Button>(), b => b.Name == "PillManageAccounts"); // Managing accounts is in Settings and the right-click menu.
            var scroll = pill.AccountScroll; Assert.True(scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), card)!.Value.Y >= card.Bounds.Height - 18); // The list runs to the foot.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheListHasAMostHeightAndScrollsInsideIt()
    {
        var model = new TimerModel(new TrackingEngine(), new StoreSettings(Enumerable.Range(1, 25).Select(i => new AccountSettings("Client " + i)).ToList(), false, "System", DayOfWeek.Monday));
        var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var card = pill.MakeDropdownCard(); var host = new Window { Width = 320, Height = 700, Content = card }; host.Show(); Settle(host);
            Assert.Equal(PillWindow.DropdownMaxHeight, card.Bounds.Height, 0.5); Assert.Equal(PillWindow.DropdownWidth, card.Bounds.Width, 0.5);
            var scroll = pill.AccountScroll;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 1); // Twenty-five accounts do not fit, so it scrolls.
            Assert.True(scroll.Viewport.Height >= 3 * 54 - 1); // At least three of the short rows show.
            Assert.Empty(host.GetVisualDescendants().OfType<IconGlyph>().Where(g => g.Kind == IconKind.Grip)); // The pill's list is not reordered.
            Assert.NotEmpty(host.GetVisualDescendants().OfType<AccountIndicator>()); // Each row keeps its play button, as on the dashboard.
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "Today"); // And there is no Today heading.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ThePlayZoneTheNameAndTheCupRunOnAlongTheDisplayInThatOrder()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
        try
        {
            double X(Control c) => c.TranslatePoint(default, pill)!.Value.X;
            var primary = Find<Button>(pill, "PillPause"); var brk = Find<Button>(pill, "PillBreak"); var clock = Find<Button>(pill, "PillDashboard");
            Assert.True(X(clock) < X(primary)); Assert.True(X(primary) < X(pill.AccountsButton)); Assert.True(X(pill.AccountsButton) < X(brk));
            Assert.Equal(StatusFace.TransportZone, primary.Bounds.Width, 0.5); Assert.Equal(StatusFace.BreakZone, brk.Bounds.Width, 0.5);
            Assert.Equal(PillWindow.PillHeight, Find<Border>(pill, "PillSurface").Bounds.Height, 0.5);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheCupFillsOnABreakAndTheTransportShowsPauseWhileRecording()
    {
        using var motion = new EnabledMotionScope();
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
        try
        {
            var cup = pill.GetVisualDescendants().OfType<CupGlyph>().Single(); var transport = pill.GetVisualDescendants().OfType<TransportGlyph>().Single();
            Assert.False(cup.On); Assert.Equal(0, cup.FilledRows); Assert.False(transport.Paused);
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs(); Assert.True(transport.Paused);
            transport.Step(TransportGlyph.WipeMilliseconds + 1); Assert.Equal(1, transport.Level(0, 1)); Assert.Equal(0, transport.Level(0, 0)); // Bars at columns 1, 2, 4 and 5.
            model.ToggleBreak(); Dispatcher.UIThread.RunJobs(); Assert.True(cup.On); cup.Step(CupGlyph.RowMilliseconds * 4 + 1);
            Assert.Equal(4, cup.FilledRows); Assert.True(cup.Steaming); Assert.Equal(1, cup.Level(6, 3));
            model.ToggleBreak(); Dispatcher.UIThread.RunJobs(); cup.Step(CupGlyph.RowMilliseconds * 4 + 1); Assert.Equal(0, cup.FilledRows);
        }
        finally { pill.Close(); }
    }

    [Theory]
    [InlineData("Northwind Traders", "NORTHWIND TRADERS")]
    [InlineData("Café Résumé", "CAFE RESUME")]
    [InlineData("Straße", "STRASSE")]
    [InlineData("日本語", null)]
    [InlineData("A", null)]
    public void NamesAreCleanedForTheSegmentsOrGiveWayToAnAccountNumber(string name, string? expected)
    {
        Assert.Equal(expected, SegmentName.Normalise(name)); Assert.Equal(expected ?? "ACCT 03", SegmentName.ForAccount(name, 2));
    }

    [AvaloniaFact]
    public void ALongNameScrollsOnePassAndComesBackAndAShortOneNever()
    {
        using var motion = new EnabledMotionScope();
        var run = new SegmentName { Text = "ABCDEFGHIJ" }; run.Scroll();
        Assert.True(run.Scrolling); run.Step(SegmentName.StepMilliseconds * 4 + 1); Assert.Equal(4, run.Offset);
        run.Step(SegmentName.StepMilliseconds * 3); Assert.False(run.Scrolling); Assert.Equal(0, run.Offset);
        var shortRun = new SegmentName { Text = "ABC" }; shortRun.Scroll(); Assert.False(shortRun.Scrolling);
    }

    [AvaloniaFact]
    public void TheSidebarsBreakIsThePillsBreakAtTheEndOfTheTrackingCardBesideThePlayTile()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
        try
        {
            double Y(Control c) => c.TranslatePoint(default, window)!.Value.Y;
            var brk = Find<Button>(window, "DashboardBreak");
            Assert.Equal(Y(Find<Button>(window, "SidebarPause")), Y(brk), 1.0); // Level with the play tile, at the card's far end.
            Assert.NotNull(Find<Border>(window, "SidebarWell")); // The card is the pill's well.
            Assert.True(Y(brk) < Y(Find<Button>(window, "QuitTimer"))); // And not at the foot with Quit timer.
            var tile = brk.GetVisualDescendants().OfType<Border>().Single(b => b.Width == StatusFace.TileSize); Assert.NotEqual(((Avalonia.Media.ISolidColorBrush)tile.Background!).Color, Avalonia.Media.Color.Parse(model.CurrentTheme.Colour("Break")));
            model.ToggleBreak(); Dispatcher.UIThread.RunJobs(); Assert.Equal(255, ((Avalonia.Media.ISolidColorBrush)tile.Background!).Color.A);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePillHasNoBreakItemOnItsMenuBecauseTheMugIsTheBreakButton()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var items = pill.ContextMenu!.Items.OfType<MenuItem>().Select(i => i.Name).ToArray();
            Assert.DoesNotContain("BreakMenu", items); Assert.Contains("CollapseToTray", items);
        }
        finally { pill.Close(); }
    }

    [Fact]
    public void IdleBreakAndFinishedWordingIsSharedWithTheTray()
    {
        var model = Model();
        Assert.Equal("Ready to track", model.Status); Assert.Contains(model.Status, TrayController.Tooltip(model));
        model.ToggleAccount(model.Accounts.First(a => !BreakAccount.Is(a.Name)).Name); Assert.Equal("Tracking", model.Status);
        model.TogglePause(); Assert.Equal("Ready to track", model.Status); Assert.Contains(model.Status, TrayController.Tooltip(model));
        model.ToggleBreak(); Assert.Equal("On break", model.Status); Assert.Contains(model.Status, TrayController.Tooltip(model));
    }
}
