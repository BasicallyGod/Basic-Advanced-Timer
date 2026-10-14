using Avalonia;
using Avalonia.Controls;
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
        => new(new TrackingEngine(), new StoreSettings(new() { new("A", null, null) }, false, "Light", DayOfWeek.Monday, DashboardWidth: width, DashboardHeight: height));

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
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var prefs = store.Load(engine); Assert.Null(prefs.DashboardWidth); store.Save(engine, prefs with { DashboardWidth = 900, DashboardHeight = 620 }); }
            using (var store = new TimerStore(folder)) { var prefs = store.Load(new TrackingEngine()); Assert.Equal(900, prefs.DashboardWidth); Assert.Equal(620, prefs.DashboardHeight); }
            // Setting a size on the model reports it back.
            var model = Model(); model.SetDashboardSize(900, 620); Assert.Equal((900, 620), model.DashboardSize);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
