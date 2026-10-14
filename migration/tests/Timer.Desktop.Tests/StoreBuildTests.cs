using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The tests are built without the Store flag, so these switch the developer features off the way a Store package has them, and check that nothing leaks.
public sealed class StoreBuildTests
{
    // A few passes, because a new page's visuals appear only after a layout and render pass, and a slow Windows runner has been seen to need more than one.
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static StoreSettings WithDeveloperOn() => new(new() { new("Account one") }, false, ThemeLibrary.Default, DayOfWeek.Monday, Developer: true);

    [AvaloniaFact]
    public void AStoreBuildNeverUnlocksDeveloperFeaturesEvenFromASavedSettingOrTheDemo()
    {
        BuildInfo.DeveloperFeaturesOverride = false;
        try
        {
            var saved = new TimerModel(new TrackingEngine(), WithDeveloperOn());
            Assert.False(saved.DeveloperUnlocked); // A settings file that says developer=on makes no difference.
            saved.SetDeveloper(true); Assert.False(saved.DeveloperUnlocked);
            var demo = TimerModel.CreateDemo();
            Assert.False(demo.DeveloperUnlocked); // Not even the demo model.
            Assert.Throws<InvalidOperationException>(() => new TimerModel(new TrackingEngine()).SetOvertimeTest(true));
        }
        finally { BuildInfo.DeveloperFeaturesOverride = null; }
    }

    [AvaloniaFact]
    public void TheStoreMenuAndSettingsPageShowNoDeveloperFeaturesAndTenClicksDoNothing()
    {
        BuildInfo.DeveloperFeaturesOverride = false;
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 9, 10, 0, 0)), WithDeveloperOn());
        model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show();
        try
        {
            window.ContextMenu!.Open(); Dispatcher.UIThread.RunJobs();
            var menu = ((IEnumerable<Control>)window.ContextMenu!.ItemsSource!).OfType<MenuItem>().ToList();
            Assert.False(menu.Single(m => m.Name == "BuildVersion").IsVisible); Assert.False(menu.Single(m => m.Name == "TestOvertime").IsVisible);
            Assert.True(menu.Single(m => (string?)m.Header == "Collapse to tray").IsVisible);
            window.OpenCategory("about"); Settle(window);
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SettingsCategory_development").IsVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == "OpenDemo" && b.IsVisible && b.GetVisualAncestors().All(a => a is not Control c || c.IsVisible));
            // About still shows which version this is, since that is not a developer feature.
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "AboutVersion" && !string.IsNullOrEmpty(t.Text));
            var about = window.GetVisualDescendants().OfType<StackPanel>().Single(b => b.Name == "AboutSection");
            var point = about.TranslatePoint(new Point(20, 20), window)!.Value;
            for (var i = 0; i < SettingsView.UnlockClicks + 3; i++) { window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); }
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.DeveloperUnlocked);
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SettingsCategory_development").IsVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "UnlockHint" && t.IsVisible); // Not even a countdown.
        }
        finally { window.Close(); BuildInfo.DeveloperFeaturesOverride = null; }
    }
}
