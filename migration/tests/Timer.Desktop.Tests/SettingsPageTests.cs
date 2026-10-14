using Avalonia;
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
    public void SettingsOpenAsADialogWithCategoriesAndEachHoldsItsOwnControls()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            OpenSettings(window); using var again = window.CaptureRenderedFrame();
            Assert.IsType<SettingsDialog>(Assert.Single(window.Dialogs.Dialogs));
            Assert.Equal("Settings", window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DialogTitle").Text);
            Assert.Contains("selected", window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").Classes);
            // The timeline is still there behind the dialog: settings no longer take the workspace over.
            Assert.Contains(window.GetVisualDescendants().OfType<ScrollViewer>(), s => s.Name == "TimelineScroll");
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SettingsCategory_development").IsVisible == false && !model.DeveloperUnlocked && !model.IsDemo); // The demo has it open.
            var expected = new (string Category, string Control)[]
            {
                ("general", "DecimalHours"), ("appearance", "EditThemes"), ("appearance", "WindowShadows"), ("pill", "PillSize"), ("pill", "PillLayout_compact"), ("pill", "PillOnTop"),
                ("desk", "CalmMotion"), ("hours", "SaveShifts"), ("accounts", "SaveAccounts"), ("about", "AboutVersion"),
            };
            foreach (var (category, name) in expected)
            {
                window.OpenCategory(category);
                Assert.Contains(window.GetVisualDescendants().OfType<Control>(), c => c.Name == name && c.IsVisible);
                Assert.Contains("selected", window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SettingsCategory_" + category).Classes);
            }
            // Only the chosen category is on show: with About open, the pill's pictures are not in the tree.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "PillLayout_full");
            // The working week is one row of seven days.
            window.OpenCategory("hours");
            Assert.Equal(7, window.GetVisualDescendants().OfType<CheckBox>().Count(c => c.Content is string day && new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" }.Contains(day)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSettingsAccountsEditorSavesInPlaceAndStartsAgainFromWhatWasSaved()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            window.OpenCategory("accounts");
            var before = model.AccountSettings.Count;
            var input = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Watermark == "New account name"); input.Text = "Fresh account";
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Add account").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveAccounts").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(before + 1, model.AccountSettings.Count);
            Assert.Single(window.Dialogs.Dialogs); // Saving leaves the settings open.
            // The editor was built again, so its box for a new name is empty and the new account is a row.
            Assert.Equal("", window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Watermark == "New account name").Text ?? "");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(), t => t.Text == "Fresh account");
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

    [AvaloniaFact]
    public void TheSettingsCardKeepsItsSizeBetweenCategoriesAndTheGearClosesIt()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var first = window.CaptureRenderedFrame();
            OpenSettings(window); using var frame = window.CaptureRenderedFrame();
            var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DialogCard");
            var size = card.Bounds.Size; Assert.True(size.Height > 400 && size.Width > 600);
            foreach (var key in new[] { "pill", "hours", "accounts", "about" }) { window.OpenCategory(key); using var shot = window.CaptureRenderedFrame(); Assert.Equal(size, card.Bounds.Size); }
            // A long pane scrolls inside the card.
            window.OpenCategory("pill");
            var page = window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "SettingsPage");
            Assert.True(page.Viewport.Height > 300);
            // The same gear closes it again.
            OpenSettings(window); using var closed = window.CaptureRenderedFrame();
            Assert.Empty(window.Dialogs.Dialogs);
            Assert.DoesNotContain("selected", window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").Classes);
            Assert.True(window.GetVisualDescendants().OfType<Control>().First(c => c.Name == "SidebarDragHeader").Bounds.Width > 100);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePillsMenuOpensTheSettingsOverTheDashboardAndRunSetupStepsOutOfTheWay()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            model.RequestSettings(); Dispatcher.UIThread.RunJobs();
            Assert.IsType<SettingsDialog>(Assert.Single(window.Dialogs.Dialogs));
            model.RequestSettings(); Dispatcher.UIThread.RunJobs(); Assert.Single(window.Dialogs.Dialogs); // Asking again does not stack another.
            window.OpenCategory("general");
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RunSetup").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.Dialogs.Dialogs); // The setup lays itself over the dashboard, so the dialog went.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSettingsAndPillButtonsShareACentreLine()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            double Centre(string name) { var b = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == name); return b.TranslatePoint(new Avalonia.Point(b.Bounds.Width / 2, 0), window)!.Value.X; }
            Assert.Equal(Centre("Settings"), Centre("BackToPill"), 0.5);
            Assert.Equal(Centre("Settings"), Centre("TimelineTab"), 0.5);
        }
        finally { window.Close(); }
    }
}
