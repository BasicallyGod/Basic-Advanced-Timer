using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desk;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class FloatingWidgetTests
{
    private static TimerModel Model() { var m = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0))); m.CompleteSetup(); return m; }
    private static void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static T Named<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [Fact]
    public void OnlyThreeMayFloatAndATileFloatsOnce()
    {
        var one = new FloatingWidget("a", null, null, 2, 2);
        var (list, added) = FloatingWidgets.Add(new List<FloatingWidget>(), one); Assert.True(added);
        Assert.False(FloatingWidgets.Add(list, one).Added); // Already floating.
        (list, _) = FloatingWidgets.Add(list, one with { Id = "b" }); (list, _) = FloatingWidgets.Add(list, one with { Id = "c" });
        var (full, fourth) = FloatingWidgets.Add(list, one with { Id = "d" }); Assert.False(fourth); Assert.Equal(3, full.Count);
        Assert.Equal(new[] { "b", "c" }, FloatingWidgets.Remove(list, "a").Select(f => f.Id));
    }

    [Fact]
    public void FloatingWidgetsSurviveWritingAndReadingAndDamageGivesNone()
    {
        var saved = new[] { new FloatingWidget("todo", -1200, 80, 2, 4), new FloatingWidget("clock", null, null, 1, 2) };
        var back = FloatingWidgets.FromJson(FloatingWidgets.ToJson(saved)); Assert.Equal(saved, back);
        Assert.Empty(FloatingWidgets.FromJson("{nope")); Assert.Empty(FloatingWidgets.FromJson(null));
        Assert.Single(FloatingWidgets.FromJson(FloatingWidgets.ToJson(new[] { saved[0], saved[0] with { X = 5 } }))); // Each tile once.
        Assert.Equal(new[] { "todo" }, FloatingWidgets.FromJson(FloatingWidgets.ToJson(saved), new[] { "todo" }).Select(f => f.Id)); // Unknown tiles dropped.
        Assert.Equal(3, FloatingWidgets.FromJson(FloatingWidgets.ToJson(Enumerable.Range(0, 6).Select(i => new FloatingWidget("t" + i, 0, 0, 2, 2)))).Count);
    }

    [Fact]
    public void TheModelFloatsDocksAndRefusesAFourthWithANotice()
    {
        var model = Model(); var changes = 0; model.FloatingChanged += () => changes++;
        Assert.True(model.FloatWidget("todo", 2, 3)); Assert.True(model.IsFloating("todo")); Assert.True(model.FloatWidget("todo", 2, 3)); Assert.Equal(1, changes);
        Assert.True(model.FloatWidget("clock", 1, 2)); Assert.True(model.FloatWidget("notes", 2, 3));
        Assert.False(model.FloatWidget("postit", 2, 2)); Assert.Equal("Three widgets are already floating. Dock one first.", model.Notice); Assert.False(model.IsFloating("postit"));
        model.MoveFloating("todo", 100, 200); model.ResizeFloating("todo", 3, 4);
        Assert.Equal(new FloatingWidget("todo", 100, 200, 3, 4), model.Floating.Single(f => f.Id == "todo"));
        model.DockWidget("todo"); Assert.False(model.IsFloating("todo")); Assert.True(model.FloatWidget("postit", 2, 2));
    }

    [Fact]
    public void FloatingWidgetsAndTheOnTopChoiceAreKeptInTheStore()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-float-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store);
                Assert.True(model.FloatOnTop); model.FloatWidget("todo", 2, 3); model.MoveFloating("todo", -50, 60); model.SetFloatOnTop(false);
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store);
                Assert.Equal(new FloatingWidget("todo", -50, 60, 2, 3), Assert.Single(model.Floating)); Assert.False(model.FloatOnTop);
            }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void AFloatingTileIsTheDesksCellsInPixels()
    {
        var size = DeskView.PixelsFor(2, 3); Assert.Equal(2 * 200 - DeskView.Gap, size.Width); Assert.Equal(3 * (DeskView.CellHeight + DeskView.Gap) - DeskView.Gap, size.Height);
    }

    [AvaloniaFact]
    public void TheFloatButtonLiftsATileAndLeavesAPlaceholderWithADockButton()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Width = 1200; window.Height = 800; window.Show();
        try
        {
            Click(Named<Button>(window, "DeskTab")); var desk = window.GetVisualDescendants().OfType<DeskView>().Single(); desk.Lively = false; window.UpdateLayout();
            var overlay = Named<Border>(window, "TileFloating_todo"); Assert.False(overlay.IsVisible);
            Click(Named<Button>(window, "TileFloat_todo")); window.UpdateLayout();
            Assert.True(model.IsFloating("todo")); Assert.True(overlay.IsVisible);
            Click(Named<Button>(window, "TileDockHere_todo")); window.UpdateLayout();
            Assert.False(model.IsFloating("todo")); Assert.False(overlay.IsVisible);
            Click(Named<Button>(window, "TileFloat_todo")); Click(Named<Button>(window, "TileFloat_clock")); Click(Named<Button>(window, "TileFloat_notes")); Click(Named<Button>(window, "TileFloat_coming"));
            Assert.Equal(3, model.Floating.Count); Assert.False(model.IsFloating("coming")); Assert.Contains("Dock one first", model.Notice);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AFloatingWindowShowsTheTileAtItsCellsAndFollowsResizingAndDocking()
    {
        var model = Model(); model.FloatWidget("todo", 2, 3);
        var window = new FloatingWidgetWindow(model, model.Floating.Single()); window.Show();
        try
        {
            var size = DeskView.PixelsFor(2, 3); Assert.Equal(size.Width + 2 * Ui.ShadowMargin, window.Width); Assert.Equal(size.Height + 2 * Ui.ShadowMargin, window.Height);
            Assert.True(window.Topmost); model.SetFloatOnTop(false); Assert.False(window.Topmost);
            var view = window.GetVisualDescendants().OfType<DeskView>().Single(); Assert.NotNull(view.TileControl("todo"));
            Click(Named<Button>(window, "TileDock_todo")); Assert.False(model.IsFloating("todo"));
            var grip = Named<Border>(window, "TileResize_todo"); Assert.True(grip.IsVisible); // Resizable from the corner, in whole cells.
            Assert.Null(window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == "TileFloat_todo")); // No float or open buttons on a floating tile.
        }
        finally { window.Retire(); }
    }

    [AvaloniaFact]
    public void ClosingAFloatingWindowAnyOtherWayDocksItButRetiringItDoesNot()
    {
        var model = Model(); model.FloatWidget("todo", 2, 3); model.FloatWidget("clock", 1, 2);
        var a = new FloatingWidgetWindow(model, model.Floating[0]); a.Show(); var b = new FloatingWidgetWindow(model, model.Floating[1]); b.Show();
        a.Close(); Assert.False(model.IsFloating("todo")); // Alt+F4 docks.
        b.Retire(); Assert.True(model.IsFloating("clock")); // The app closing leaves it floating for next time.
    }

    [AvaloniaFact]
    public void SettingsHasTheOnTopSwitch()
    {
        var model = Model(); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            dashboard.OpenCategory("desk"); var box = dashboard.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "FloatOnTop");
            Assert.True(box.IsChecked); box.IsChecked = false; box.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Assert.False(model.FloatOnTop);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheCoordinatorOpensAndClosesFloatingWindowsWithTheModelAndTheyOutliveHidingTheTimer()
    {
        var model = Model(); var coordinator = new WindowCoordinator(model, () => { });
        coordinator.Pill.Show();
        try
        {
            model.FloatWidget("todo", 2, 3); model.FloatWidget("clock", 1, 2);
            Assert.True(coordinator.FloatingWindowIds.SetEquals(new[] { "todo", "clock" }));
            coordinator.Hide(); // The timer goes to the tray; the widgets stay.
            Assert.True(coordinator.FloatingWindowIds.SetEquals(new[] { "todo", "clock" }));
            model.DockWidget("todo"); Assert.True(coordinator.FloatingWindowIds.SetEquals(new[] { "clock" }));
        }
        finally { coordinator.Pill.Close(); coordinator.Dashboard.Close(); }
        Assert.True(model.IsFloating("clock")); // Closing the app leaves it floating for next time.
    }

    [AvaloniaFact]
    public void HidingTheFloatingWidgetsTucksTheWindowsAwayWithoutDockingAndNewFloatingShowsThemAgain()
    {
        var model = Model(); var coordinator = new WindowCoordinator(model, () => { });
        coordinator.Pill.Show();
        try
        {
            model.SetFloatingHidden(true); Assert.False(model.FloatingHidden); // Nothing floating, nothing to hide.
            model.FloatWidget("todo", 2, 3); model.FloatWidget("clock", 1, 2);
            var menu = coordinator.Pill.ContextMenu!; menu.Open(coordinator.Pill);
            var item = menu.Items.OfType<MenuItem>().Single(m => m.Name == "FloatingMenu"); Assert.True(item.IsVisible); Assert.Equal("Hide floating widgets", item.Header);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.True(model.FloatingHidden); menu.Close(); menu.Open(coordinator.Pill); Assert.Equal("Show floating widgets", item.Header);
            Assert.Equal(2, model.Floating.Count); Assert.True(coordinator.FloatingWindowIds.SetEquals(new[] { "todo", "clock" })); Assert.False(coordinator.FloatingWindowsVisible);
            model.FloatWidget("notes", 2, 3); Assert.False(model.FloatingHidden); Assert.True(coordinator.FloatingWindowsVisible);
            model.SetFloatingHidden(true); model.DockWidget("todo"); model.DockWidget("clock"); model.DockWidget("notes"); Assert.False(model.FloatingHidden);
        }
        finally { coordinator.Pill.Close(); coordinator.Dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheStartingLayoutsApplyAndEveryTileInThemExists()
    {
        var ids = DeskView.Kinds.Select(k => k.Id).ToList();
        foreach (var starter in DeskView.Starters)
        {
            Assert.All(starter.Order, id => Assert.Contains(id, ids)); Assert.Equal(starter.Order.Distinct().Count(), starter.Order.Count);
            Assert.Equal(ids.Count, starter.Order.Count + starter.Hidden.Count); Assert.Empty(starter.Order.Intersect(starter.Hidden));
        }
        var model = Model(); var window = new DashboardWindow(model); window.Width = 1200; window.Height = 800; window.Show();
        try
        {
            Click(Named<Button>(window, "DeskTab")); var desk = window.GetVisualDescendants().OfType<DeskView>().Single(); desk.Lively = false;
            Click(Named<Button>(window, "ArrangeToggle")); window.UpdateLayout();
            var panel = (Panel)desk.BuildLayoutsPanel(); Assert.Equal(3, panel.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("LayoutStarter_") == true));
            Click(panel.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "LayoutStarter_Focus")); window.UpdateLayout();
            Assert.Equal(new[] { "working", "todo", "pomodoro", "deadline", "postit" }, desk.Order); Assert.Contains("timeline", desk.Hidden); Assert.Equal((2, 1), desk.SizeOf("deadline")); Assert.Equal(5, desk.Placements.Count);
            Click(((Panel)desk.BuildLayoutsPanel()).GetVisualDescendants().OfType<Button>().Single(b => b.Name == "LayoutStarter_Everything")); window.UpdateLayout();
            Assert.Equal(new[] { "postit2", "postit3" }, desk.Hidden.OrderBy(h => h)); Assert.Equal(DeskView.Kinds.Count - 2, desk.Placements.Count); Assert.Empty(model.DeskLayouts); // Starters are not saved layouts.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AFloatingTileCanOpenItsFullPageInTheDashboard()
    {
        var model = Model(); var dashboard = new DashboardWindow(model); dashboard.Show(); model.FloatWidget("day", 2, 1); model.FloatWidget("clock", 1, 2);
        var window = new FloatingWidgetWindow(model, model.Floating.Single(f => f.Id == "day")); window.Show();
        var clock = new FloatingWidgetWindow(model, model.Floating.Single(f => f.Id == "clock")); clock.Show();
        try
        {
            Assert.Empty(clock.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "TileOpen_clock")); // The clock has no page of its own.
            var asked = new List<string>(); model.PageRequested += asked.Add;
            Click(Named<Button>(window, "TileOpen_day")); Assert.Equal(new[] { "overview" }, asked);
            Assert.Equal("overview", model.WeekSection); // The dashboard came to that page.
            model.RequestPage("hours"); Assert.Equal("hours", model.WeekSection);
        }
        finally { window.Retire(); clock.Retire(); dashboard.Close(); }
    }

    // Pictures of floating widgets for looking at when TIMER_PREVIEW_DIR is set.
    [AvaloniaFact]
    public void FloatingWidgetPictures()
    {
        if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is not { } folder) return;
        Directory.CreateDirectory(folder);
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0)), new StoreSettings(new() { new("Account one", null, null), new("Account two", null, null) }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup(); model.AddDayNote(model.SelectedDate, "Send invoice"); model.AddDayNote(model.SelectedDate, "14:30 Call back"); model.AddTodo("Order tea");
        foreach (var (id, c, r) in new[] { ("todo", 2, 3), ("working", 2, 2), ("clock", 1, 2), ("postit", 2, 2), ("deadline", 2, 2), ("pomodoro", 2, 2) })
        {
            model.FloatWidget(id, c, r);
            var window = new FloatingWidgetWindow(model, model.Floating.Single(f => f.Id == id)); window.Show();
            try { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using (window.CaptureRenderedFrame()) { } using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, "float-" + id + ".png")); }
            finally { window.Retire(); model.DockWidget(id); }
        }
    }
}
