using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class DeskConsolidationTests
{
    private static void Settle(Window w) { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var f = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }

    // A scrollable list inside a taller scroll viewer, with the list already at its end.
    private static (Window Window, ScrollViewer Outer, ScrollViewer Inner) Nested(bool own)
    {
        var innerRows = new StackPanel(); for (var i = 0; i < 30; i++) innerRows.Children.Add(new TextBlock { Text = "row " + i, Height = 30 });
        var inner = new ScrollViewer { Content = innerRows, Height = 200, Name = "Inner" };
        var outerRows = new StackPanel { Children = { new TextBlock { Text = "above", Height = 100 }, inner } };
        for (var i = 0; i < 20; i++) outerRows.Children.Add(new TextBlock { Text = "below " + i, Height = 40 });
        var outer = new ScrollViewer { Content = outerRows, Name = "Outer" };
        var window = new Window { Width = 400, Height = 400, Content = outer }; window.Show();
        if (own) Ui.OwnWheel(outer);
        Settle(window);
        outer.Offset = new Vector(0, 60); Settle(window); // The list is under the pointer, whatever the desk has scrolled to.
        inner.Offset = new Vector(0, 100000); Settle(window);
        return (window, outer, inner);
    }

    [AvaloniaFact]
    public void AListAtItsEndKeepsTheWheelSoTheSurroundingScrollDoesNotMove()
    {
        // Control: left alone, the wheel passes from the list at its end to the scroll viewer around it, so this check can fail.
        var (plain, plainOuter, plainInner) = Nested(false);
        try
        {
            var before = plainOuter.Offset.Y; var at = plainInner.TranslatePoint(new Point(50, 100), plain)!.Value;
            plain.MouseWheel(at, new Vector(0, -3)); Settle(plain);
            Assert.True(plainOuter.Offset.Y > before, "Without the fix the surrounding scroll should have moved.");
        }
        finally { plain.Close(); }
        var (window, outer, inner) = Nested(true);
        try
        {
            var before = outer.Offset.Y; var innerEnd = inner.Offset.Y; var at = inner.TranslatePoint(new Point(50, 100), window)!.Value;
            window.MouseWheel(at, new Vector(0, -3)); Settle(window);
            Assert.Equal(before, outer.Offset.Y); Assert.Equal(innerEnd, inner.Offset.Y); Assert.True(Ui.OwnsWheel(inner));
            // The list still scrolls itself in the other direction.
            window.MouseWheel(at, new Vector(0, 3)); Settle(window);
            Assert.True(inner.Offset.Y < innerEnd); Assert.Equal(before, outer.Offset.Y);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AListWithNothingToScrollLetsTheWheelThrough()
    {
        var rows = new StackPanel(); for (var i = 0; i < 3; i++) rows.Children.Add(new TextBlock { Text = "row " + i, Height = 30 });
        var inner = new ScrollViewer { Content = rows, Height = 200 };
        var outerRows = new StackPanel { Children = { inner } }; for (var i = 0; i < 20; i++) outerRows.Children.Add(new TextBlock { Text = "below " + i, Height = 40 });
        var outer = new ScrollViewer { Content = outerRows }; var window = new Window { Width = 400, Height = 400, Content = outer }; window.Show();
        try
        {
            Ui.OwnWheel(outer); Settle(window);
            window.MouseWheel(inner.TranslatePoint(new Point(50, 50), window)!.Value, new Vector(0, -3)); Settle(window);
            Assert.True(outer.Offset.Y > 0); // Nothing in the list to scroll, so the desk takes it.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheDesksListsAndTimelineKeepTheWheel()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0))); model.CompleteSetup(); model.SetDesk(true);
        var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        try
        {
            dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DeskTab").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(dashboard);
            var inside = dashboard.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.GetVisualAncestors().OfType<ScrollViewer>().Any() && !s.GetVisualAncestors().OfType<TextBox>().Any()).ToList(); // A text box has a scroll viewer of its own inside.
            Assert.NotEmpty(inside);
            Assert.All(inside, s => Assert.True(Ui.OwnsWheel(s), $"{s.Name ?? s.Content?.GetType().Name} chains the wheel to the desk."));
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void AnOpenPillOnAShortScreenScrollsItsAccountsAndKeepsFinishDayInView()
    {
        var accounts = Enumerable.Range(1, 40).Select(i => new AccountSettings("Client " + i, null, null)).ToList();
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0)), new StoreSettings(accounts, false, "Light", DayOfWeek.Monday));
        var pill = new PillWindow(model); pill.Show(); pill.ToggleExpansion(); Settle(pill);
        try
        {
            var screen = pill.Screens.ScreenFromWindow(pill); var screenHeight = screen is null ? 1080.0 : screen.WorkingArea.Height / screen.Scaling;
            Assert.True(pill.Height <= screenHeight * PillWindow.OpenShareOfScreen + 2 * Ui.ShadowMargin + 2, $"The open pill is {pill.Height} high on a {screenHeight} screen.");
            var list = pill.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "PillAccountScroll");
            Assert.True(list.Extent.Height > list.Viewport.Height + 10, "The list should scroll inside its box.");
            var day = pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PillDay");
            var bottom = day.TranslatePoint(new Point(0, day.Bounds.Height), pill)!.Value.Y;
            Assert.True(bottom <= pill.Bounds.Height + 1, "The Finish day button runs off the bottom of the pill.");
            // Quick switching still works from the list.
            var first = pill.GetVisualDescendants().OfType<Button>().First(b => b.Name == "Account0");
            first.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Client 1", model.ActiveAccount);
        }
        finally { pill.Close(); }
    }
}
