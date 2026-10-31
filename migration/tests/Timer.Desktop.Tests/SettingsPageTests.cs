using Avalonia;
using Xunit;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Timer.Desktop.Tests;

public sealed class SettingsPageTests
{
    private static void OpenSettings(DashboardWindow window)
    {
        window.OpenSettings();
    }

    [AvaloniaFact]
    public void TheSettingsPageReplacesTheWorkspaceAndHoldsHoursAppearanceAndAccounts()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            OpenSettings(window); using var again = window.CaptureRenderedFrame();
            var all = window.GetVisualDescendants().ToList();
            Assert.Contains(all.OfType<SettingsView>(), s => s.Name == "SettingsPage");
            Assert.Contains(all.OfType<TextBlock>(), t => t.Text == "Settings" && t.Classes.Contains("clock")); // The header names the page.
            Assert.Contains(all.OfType<Button>(), b => b.Name == "SaveShifts"); // The working hours form is on the page.
            Assert.Contains(all.OfType<Button>(), b => b.Name == "ThemeLight"); Assert.Contains(all.OfType<Button>(), b => b.Name == "EditAccounts");
            Assert.Contains(all.OfType<CheckBox>(), c => c.Name == "WindowShadows");
            Assert.DoesNotContain(all.OfType<Button>(), b => b.Name == "PreviousPeriod" && b.IsVisible); // There is no date to step through here.
            Assert.Contains("on", window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").Classes); // The gear is lit while Settings shows.
            // Going back to a view brings the date controls back.
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TimelineTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<SettingsView>(), s => s.Name == "SettingsPage");
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PreviousPeriod").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheHoursFormatCheckBoxAndTheRightClickMenuStayInStep()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            OpenSettings(window);
            var box = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "DecimalHours");
            Assert.False(model.DecimalHours);
            box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // A click on the box flips it and the model.
            Assert.True(model.DecimalHours);
            model.ToggleFormat(); Dispatcher.UIThread.RunJobs(); // Changed from the menu instead.
            Assert.False(model.DecimalHours); Assert.False(box.IsChecked);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheWorkingHoursSectionHasTheDailyBreakAndSavesItWithTheHours()
    {
        var model = new TimerModel(new Timer.Core.TrackingEngine(clock: () => new DateTime(2026, 10, 9, 10, 0, 0))); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show();
        try
        {
            OpenSettings(window); window.GetVisualDescendants().OfType<SettingsView>().Single().ShowSection("hours");
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            var box = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "BreakAllowance");
            Assert.Equal("00:00", box.Text); // Nothing is taken off until one is chosen.
            box.Text = "00:30"; window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveShifts").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(TimeSpan.FromMinutes(30), model.BreakAllowance);
            box.Text = "later"; window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveShifts").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(TimeSpan.FromMinutes(30), model.BreakAllowance); // A box that is not HH:mm changes nothing.
            // The day bar under the timeline measures against the hours less the break.
            var plan = model.Engine.Plan(model.SelectedDate);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TimelineTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Contains("less 00:30 break", ToolTip.GetTip(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DayBar"))!.ToString());
            Assert.Equal(plan.End - plan.Start - TimeSpan.FromMinutes(30), model.ScheduledOn(model.SelectedDate));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSettingsPageIsNotRebuiltWhileTheModelTicksOver()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            OpenSettings(window);
            var page = window.GetVisualDescendants().OfType<SettingsView>().Single();
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs();
            Assert.Same(page, window.GetVisualDescendants().OfType<SettingsView>().Single()); // Typing in the hours boxes is safe.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSettingsPageScrollsFillsTheWindowAndClosesWithTheSameButton()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var first = window.CaptureRenderedFrame();
            OpenSettings(window); using var frame = window.CaptureRenderedFrame();
            var page = window.GetVisualDescendants().OfType<SettingsView>().Single();
            Assert.True(page.Bounds.Height > 400);
            // The chosen section sits in a scroll area, so a long one scrolls inside its card.
            Assert.NotEmpty(page.GetVisualDescendants().OfType<ScrollViewer>());
            // The sidebar stays in view, and the page runs out to the window's right edge.
            Assert.True(page.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value.X > Sidebar.Width); // Beside the sidebar.
            Assert.True(page.TranslatePoint(new Avalonia.Point(page.Bounds.Width, 0), window)!.Value.X > window.Bounds.Width - 2 * Ui.ShadowMargin - 40);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "QuitTimer" && b.IsVisible);
            // The same button closes it again and the timeline returns.
            OpenSettings(window); using var closed = window.CaptureRenderedFrame();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<SettingsView>(), s => s.Name == "SettingsPage");
            Assert.Contains(window.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "TimelineScroll");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSidebarsTimerBlockIsCentredOnItsColumnAndTheFootButtonsShareTheEdges()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            var sidebar = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Sidebar"); var middle = sidebar.TranslatePoint(new Avalonia.Point(sidebar.Bounds.Width / 2, 0), window)!.Value.X;
            double Centre(string name) { var c = window.GetVisualDescendants().OfType<Control>().Single(x => x.Name == name); return c.TranslatePoint(new Avalonia.Point(c.Bounds.Width / 2, 0), window)!.Value.X; }
            // The lights, the clock and the tracking card are all centred on the column.
            Assert.Equal(middle, Centre("SidebarDisplay"), 1.0);
            // The well and the break key outside it make one row, centred on the column as a whole.
            double Edge(string name, bool right) { var c = window.GetVisualDescendants().OfType<Control>().Single(x => x.Name == name); return c.TranslatePoint(new Avalonia.Point(right ? c.Bounds.Width : 0, 0), window)!.Value.X; }
            Assert.Equal(middle, (Edge("SidebarWell", false) + Edge("DashboardBreak", true)) / 2, 6.0);
            // Quit timer is at the foot on its own, centred on the column and the width of its foot.
            var quit = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "QuitTimer");
            Assert.Equal(middle, Centre("QuitTimer"), 1.5); Assert.True(quit.Bounds.Width > sidebar.Bounds.Width - 40);
        }
        finally { window.Close(); }
    }
}
