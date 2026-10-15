using Timer.Desk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class ResizableWindowTests
{
    private static TimerModel Model(int? width = null, int? height = null)
        => new(new TrackingEngine(), new StoreSettings(new() { new("A", null, null) }, false, "Light", DayOfWeek.Monday).WithApp(new AppPrefs(DashboardWidth: width, DashboardHeight: height)));

    [AvaloniaFact]
    public void TheDashboardCanBeResizedFromItsEdgesAndCornersDownToAMinimum()
    {
        var window = new DashboardWindow(Model()); window.Show();
        try
        {
            Assert.True(window.CanResize);
            Assert.Equal(DashboardWindow.MinimumWidth + 2 * Ui.ShadowMargin, window.MinWidth);
            Assert.Equal(DashboardWindow.MinimumHeight + 2 * Ui.ShadowMargin, window.MinHeight);
            var names = new[] { "ResizeLeft", "ResizeRight", "ResizeTop", "ResizeBottom", "ResizeTopLeft", "ResizeTopRight", "ResizeBottomLeft", "ResizeBottomRight" };
            foreach (var name in names) Assert.NotNull(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == name).Cursor);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ARememberedSizeIsUsedAndHeldToTheMinimum()
    {
        Assert.Equal((DashboardWindow.DashboardWidth, DashboardWindow.DashboardHeight), DashboardWindow.PreferredSize(Model()));
        Assert.Equal((900.0, 600.0), DashboardWindow.PreferredSize(Model(900, 600)));
        Assert.Equal((DashboardWindow.MinimumWidth, DashboardWindow.MinimumHeight), DashboardWindow.PreferredSize(Model(400, 350)));
        var window = new DashboardWindow(Model(1300, 800));
        Assert.Equal(1300 + 2 * Ui.ShadowMargin, window.Width); Assert.Equal(800 + 2 * Ui.ShadowMargin, window.Height);
    }

    [Fact]
    public void TheDashboardSizeSurvivesSavingAndLoading()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-dash-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var prefs = store.Load(engine); Assert.Null(prefs.App().DashboardWidth); store.Save(engine, prefs.WithApp(a => a with { DashboardWidth = 900, DashboardHeight = 620 })); }
            using (var store = new TimerStore(folder)) { var prefs = store.Load(new TrackingEngine()); Assert.Equal(900, prefs.App().DashboardWidth); Assert.Equal(620, prefs.App().DashboardHeight); }
            // Setting a size on the model reports it back.
            var model = Model(); model.SetDashboardSize(900, 620); Assert.Equal((900, 620), model.DashboardSize);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // Pictures of the dashboard at several sizes, for looking at when TIMER_PREVIEW_DIR is set. Nothing is asserted beyond the window fitting its size.
    [AvaloniaTheory]
    [InlineData(640, 480)]
    [InlineData(800, 560)]
    [InlineData(1080, 680)]
    [InlineData(1500, 900)]
    public void TheDashboardDrawsAtEachSize(int width, int height)
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); var window = new DashboardWindow(model);
        window.Width = width + 2 * Ui.ShadowMargin; window.Height = height + 2 * Ui.ShadowMargin; window.Show();
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(folder, $"size-{width}x{height}.png"));
            }
            Assert.True(window.ClientSize.Width >= width && window.ClientSize.Height >= height);
        }
        finally { window.Close(); }
    }

    private static T Named<T>(Avalonia.Controls.Control root, string name) where T : Avalonia.Controls.Control =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>().Single(c => c.Name == name);

}
