using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
    public void TheClockIsStackedAsHoursMinutesAndSeconds()
    {
        var clock = new StackedClock { Text = "01:30:45" };
        Assert.Equal("01:30:45", clock.Text);
        var lines = clock.Children.OfType<TextBlock>().Select(t => t.Text).ToArray();
        Assert.Equal(new[] { "01", "30", "45" }, lines);
        Assert.True(clock.Children.OfType<TextBlock>().Last().FontSize < clock.Children.OfType<TextBlock>().First().FontSize); // The seconds are smaller.
    }

    [AvaloniaFact]
    public void ThePillIsASmallVerticalCardWithItsWindowAtTheScaledSize()
    {
        var pill = new PillWindow(Model()); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var surface = Find<Border>(pill, "PillSurface");
            Assert.Equal(PillWindow.PillWidth, surface.Width); Assert.Equal(PillWindow.PillHeight, surface.Height);
            Assert.True(surface.Height > 3 * surface.Width);
            Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5); Assert.Equal(pill.WindowWidthNow, pill.Width, 0.5);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheLightCirclePauseAndMugFollowTheState()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var lights = Find<StackPanel>(pill, "PillLights"); var circle = Find<Button>(pill, "PillPause"); var mug = Find<Button>(pill, "PillBreak");
            Assert.Equal("Not recording", Tip(lights)); Assert.StartsWith("Start ", Tip(circle)); Assert.Equal("Start break", Tip(mug));
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Recording", Tip(lights)); Assert.StartsWith("Pause", Tip(circle));
            Assert.Contains(pill.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "AO"); // The circle carries the running account's initials.
            model.ToggleBreak(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("On break", Tip(lights)); Assert.StartsWith("End break", Tip(mug)); Assert.True(pill.GetVisualDescendants().OfType<BreakGlyph>().Single().Steaming);
        }
        finally { pill.Close(); }
        // Overtime can only be forced in the demo.
        var demo = TimerModel.CreateDemo(); var late = new PillWindow(demo); late.Show();
        try { demo.SetOvertimeTest(true); demo.ToggleAccount("Account three"); Dispatcher.UIThread.RunJobs(); Assert.Equal("Overtime", Tip(Find<StackPanel>(late, "PillLights"))); }
        finally { late.Close(); }
    }

    [AvaloniaFact]
    public void HoveringTheCircleShiftsItsColourInsteadOfGrowingItSoNothingIsClipped()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var button = Find<Button>(pill, "PillPause"); var fill = button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Single();
            Dispatcher.UIThread.RunJobs(); using var frame = pill.CaptureRenderedFrame();
            var rest = ((Avalonia.Media.ISolidColorBrush)fill.Fill!).Color;
            var over = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), pill)!.Value;
            pill.MouseMoveUntil(over, () => ((Avalonia.Media.ISolidColorBrush)fill.Fill!).Color != rest);
            Assert.NotEqual(rest, ((Avalonia.Media.ISolidColorBrush)fill.Fill!).Color);
            Assert.True(button.Content is Grid host && host.RenderTransform is Avalonia.Media.ScaleTransform { ScaleX: 1, ScaleY: 1 }); // The size does not change.
            pill.MouseMove(new Point(2, 2)); Assert.Equal(rest, ((Avalonia.Media.ISolidColorBrush)fill.Fill!).Color);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheReminderLightShowsOnlyWhileAReminderIsDue()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try { Assert.False(Find<Border>(pill, "PillReminderLight").IsVisible); Assert.False(model.ReminderDue); }
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
    public void HoverShowsFewAccountsAndAllAccountsOpensTheListWhichClosesOnAChoice()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.False(pill.PeekOpen); Assert.False(pill.PanelOpen);
            pill.ShowPeek(); Assert.True(pill.PeekOpen); Assert.False(pill.PanelOpen);
            pill.ShowPanel(); Assert.False(pill.PeekOpen); Assert.True(pill.PanelOpen);
            Assert.Equal(pill.WindowHeightNow, pill.Height, 0.5); // The pill never grows for them.
            model.ToggleAccount("Account two"); Dispatcher.UIThread.RunJobs();
            Assert.False(pill.PanelOpen); // Choosing an account puts the list away.
            pill.ShowPeek(); pill.CloseFlyouts(); Assert.False(pill.PeekOpen);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void AManyAccountListStopsAtThreeFifthsOfTheScreenAndScrolls()
    {
        var model = new TimerModel(new TrackingEngine(), new StoreSettings(Enumerable.Range(1, 25).Select(i => new AccountSettings("Client " + i)).ToList(), false, "System", DayOfWeek.Monday));
        var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            pill.ShowPanel(); Dispatcher.UIThread.RunJobs();
            var screen = pill.Screens.ScreenFromWindow(pill); var screenHeight = screen is null ? 800 : screen.WorkingArea.Height / screen.Scaling;
            Assert.True(pill.AccountScroll.MaxHeight * pill.Scale <= PillWindow.OpenShareOfScreen * screenHeight, $"{pill.AccountScroll.MaxHeight} of {screenHeight}");
            Assert.True(pill.AccountScroll.MaxHeight >= PillWindow.FewestRows * 70);
        }
        finally { pill.Close(); }
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
        model.ToggleBreak(); model.ToggleDay();
        Assert.Equal("Day finished", model.Status); Assert.Contains(model.Status, TrayController.Tooltip(model));
    }
}
