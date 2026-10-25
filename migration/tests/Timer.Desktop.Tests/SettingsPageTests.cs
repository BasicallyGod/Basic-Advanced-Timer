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
            // The rail stays in view, and the page runs out to the window's right edge.
            Assert.True(page.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value.X > DashboardWindow.RailWidth); // Beside the rail.
            Assert.True(page.TranslatePoint(new Avalonia.Point(page.Bounds.Width, 0), window)!.Value.X > window.Bounds.Width - 2 * Ui.ShadowMargin - 40);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "DashboardDay" && b.IsVisible);
            // The same button closes it again and the timeline returns.
            OpenSettings(window); using var closed = window.CaptureRenderedFrame();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<SettingsView>(), s => s.Name == "SettingsPage");
            Assert.Contains(window.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "TimelineScroll");
            Assert.True(window.GetVisualDescendants().OfType<Control>().First(c => c.Name == "RailDragHeader").Bounds.Width > 40);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheRailButtonsShareACentreLine()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            double Centre(string name) { var b = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == name); return b.TranslatePoint(new Avalonia.Point(b.Bounds.Width / 2, 0), window)!.Value.X; }
            Assert.Equal(Centre("RailAccounts"), Centre("TimelineTab"), 1.0); Assert.Equal(Centre("RailAccounts"), Centre("QuitTimer"), 1.0);
            Assert.Equal(Centre("RailAccounts"), Centre("DashboardDay"), 1.0); Assert.Equal(Centre("RailAccounts"), Centre("DashboardBreak"), 1.0); Assert.Equal(Centre("RailAccounts"), Centre("RailPause"), 1.0); Assert.Equal(Centre("RailAccounts"), Centre("RailDashboard"), 1.0);
        }
        finally { window.Close(); }
    }
}
