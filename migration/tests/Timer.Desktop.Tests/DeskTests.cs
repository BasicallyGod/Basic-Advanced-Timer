using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class DeskTests
{
    private static TimerModel Model(bool desk = true, params string[] order)
        => new(new TrackingEngine(), new StoreSettings(new() { new("Account one", null, null), new("Account two", null, null) }, false, "Light", DayOfWeek.Monday, Desk: desk, DeskOrder: order));

    private static DashboardWindow Open(TimerModel model, int width = 1080, int height = 680)
    {
        var window = new DashboardWindow(model); window.Width = width + 2 * Ui.ShadowMargin; window.Height = height + 2 * Ui.ShadowMargin; window.Show();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); return window;
    }
    private static T Named<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Press(Control root, string name) { Named<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void TheDeskTabAppearsOnlyWhenThePreviewIsOn()
    {
        var off = Open(Model(false)); var on = Open(Model());
        try { Assert.False(Named<Button>(off, "DeskTab").IsVisible); Assert.True(Named<Button>(on, "DeskTab").IsVisible); }
        finally { off.Close(); on.Close(); }
    }

    [AvaloniaFact]
    public void TheDeskShowsItsTilesInTheSavedOrderAndHidesTheAccountsColumn()
    {
        var window = Open(Model(true, "day", "working", "accounts"));
        try
        {
            Press(window, "DeskTab"); window.UpdateLayout();
            var desk = window.GetVisualDescendants().OfType<DeskView>().Single();
            Assert.Equal(new[] { "day", "working", "accounts" }, desk.Order);
            Assert.Equal(3, desk.Placements.Count); Assert.Equal("day", desk.Placements[0].Id);
            Assert.False(Named<Control>(window, "SidebarPanel").IsVisible);
            Assert.Equal(TileForm.Normal, desk.Placements.Single(p => p.Id == "working").Form);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DroppingATileMovesItAndTheOrderIsKept()
    {
        var model = Model(); var window = Open(model);
        try
        {
            Press(window, "DeskTab");
            var desk = window.GetVisualDescendants().OfType<DeskView>().Single();
            desk.Drop("accounts", 0); window.UpdateLayout();
            Assert.Equal(new[] { "accounts", "working", "day" }, desk.Order);
            Assert.Equal(new[] { "accounts", "working", "day" }, model.DeskOrder);
            Assert.Equal(0, desk.Placements.Single(p => p.Id == "accounts").Row);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ATileOpensItsFullPage()
    {
        var window = Open(Model());
        try
        {
            Press(window, "DeskTab");
            Press(window, "TileOpen_day");
            Assert.Empty(window.GetVisualDescendants().OfType<DeskView>());
            Assert.True(Named<Button>(window, "WeekOverviewTab").Classes.Contains("selected"));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void TheDeskSwitchAndOrderSurviveSavingAndLoading()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-desk-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var prefs = store.Load(engine); Assert.False(prefs.Desk); store.Save(engine, prefs with { Desk = true, DeskOrder = new[] { "day", "accounts" } }); }
            using (var store = new TimerStore(folder)) { var prefs = store.Load(new TrackingEngine()); Assert.True(prefs.Desk); Assert.Equal(new[] { "day", "accounts" }, prefs.DeskOrder); }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // Pictures of the desk for looking at when TIMER_PREVIEW_DIR is set.
    [AvaloniaTheory]
    [InlineData(640, 480)]
    [InlineData(1080, 680)]
    [InlineData(1500, 900)]
    public void TheDeskDrawsAtEachSize(int width, int height)
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); model.SetDesk(true);
        var window = Open(model, width, height);
        try
        {
            Press(window, "DeskTab"); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("selected", Named<Button>(window, "TimelineTab").Classes); Assert.Contains("selected", Named<Button>(window, "DeskTab").Classes);
            if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is { } folder)
            {
                Directory.CreateDirectory(folder); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, $"desk-{width}x{height}.png"));
            }
        }
        finally { window.Close(); }
    }
}
