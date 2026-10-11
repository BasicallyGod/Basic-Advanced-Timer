using Avalonia.Headless;
using Xunit;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Desktop;

namespace Timer.Desktop.Tests;

public sealed class SettingsPageTests
{
    private static void OpenSettings(DashboardWindow window)
    {
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
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
            Assert.Contains(all.OfType<ScrollViewer>(), s => s.Name == "SettingsPage");
            Assert.Contains(all.OfType<TextBlock>(), t => t.Text == "Settings" && t.Classes.Contains("clock")); // The header names the page.
            Assert.Contains(all.OfType<Button>(), b => b.Name == "SaveShifts"); // The working hours form is on the page.
            Assert.Contains(all.OfType<Button>(), b => b.Name == "EditThemes"); Assert.Contains(all.OfType<Button>(), b => b.Name == "EditAccounts");
            Assert.Contains(all.OfType<CheckBox>(), c => c.Name == "WindowShadows");
            Assert.DoesNotContain(all.OfType<Button>(), b => b.Name == "PreviousPeriod" && b.IsVisible); // There is no date to step through here.
            Assert.Contains("selected", window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").Classes);
            // Going back to a view brings the date controls back.
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TimelineTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "SettingsPage");
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
            var page = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "SettingsPage");
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs();
            Assert.Same(page, window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "SettingsPage")); // Typing in the hours boxes is safe.
        }
        finally { window.Close(); }
    }
}
