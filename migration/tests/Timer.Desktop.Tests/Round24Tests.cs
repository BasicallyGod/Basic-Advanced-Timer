using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 24: a busy timeline. Meetings stack two deep and fold the rest into a +N chip with a list; overlapped blocks tell their account by its initials.
public sealed class Round24Tests
{
    private static readonly DateTime Day = new(2026, 10, 21);
    private static DateTime At(double hours) => Day.AddHours(hours);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static TimerModel Model()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => At(20)), new StoreSettings(new() { new("Account one"), new("Account two"), new("Internal") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup(); return model;
    }
    private static IEnumerable<Control> Cards(Window w, string prefix) => w.GetVisualDescendants().OfType<Control>().Where(c => c.Name is { } n && n.StartsWith(prefix));

    [AvaloniaFact]
    public void ThreeStackedMeetingsKeepTheRowsHeightWithTwoLanesAndAPlusOneThatListsTheThird()
    {
        var model = Model();
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(11), TimeSpan.FromHours(12), "Client call", null, RepeatChoice.None);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(11.5), TimeSpan.FromHours(12.25), "Design review", null, RepeatChoice.None);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(11.75), TimeSpan.FromHours(12.5), "Supplier check-in", null, RepeatChoice.None);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var cards = Cards(window, "TimelineMeeting").Where(c => c is Border && c.Name!.Length > "TimelineMeeting".Length && char.IsDigit(c.Name[15])).ToList();
            Assert.Equal(2, cards.Count); // The third is not drawn in the row.
            Assert.All(cards, c => Assert.Equal(22, c.Bounds.Height, 0.5)); // Two lanes of 22 in the 48 pixel row.
            var chip = Cards(window, "TimelineMeetingMore").OfType<Button>().Single();
            Assert.Contains("+1", chip.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)); Assert.Equal("1 more meeting", ToolTip.GetTip(chip)?.ToString());
            Assert.Equal(22, chip.Bounds.Height, 0.5);
            // It lies to the right of the stack, clear of the cards in its lane.
            var chipLeft = chip.TranslatePoint(default, window)!.Value.X; var chipTop = chip.TranslatePoint(default, window)!.Value.Y;
            foreach (var c in cards) { var p = c.TranslatePoint(default, window)!.Value; if (Math.Abs(p.Y - chipTop) < 1) Assert.True(p.X + c.Bounds.Width <= chipLeft + 0.5, "the chip is over a meeting"); }
            // Pressing it opens the list, and a row opens that meeting for editing.
            long? asked = null; model.Notes.EditMeetingRequested += id => asked = id;
            chip.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window);
            var popup = window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single(p => p.Name == "TimelineMeetingMorePopup"); Assert.True(popup.IsOpen);
            var third = model.Notes.All.Single(n => model.Notes.Text(n) == "Supplier check-in");
            var row = popup.Child!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "MoreMeeting_" + third.Id);
            row.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.Equal(third.Id, asked); Assert.False(popup.IsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TwoOverlappingMeetingsAreBothDrawnWithNoChipAndFiveFoldIntoPlusThree()
    {
        var model = Model();
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "One", null, RepeatChoice.None);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(9.25), TimeSpan.FromHours(10.25), "Two", null, RepeatChoice.None);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try { Assert.Empty(Cards(window, "TimelineMeetingMore")); } finally { window.Close(); }
        foreach (var name in new[] { "Three", "Four", "Five" }) model.Notes.AddMeeting(Day, TimeSpan.FromHours(9.5), TimeSpan.FromHours(10.5), name, null, RepeatChoice.None);
        window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var chip = Cards(window, "TimelineMeetingMore").OfType<Button>().Single();
            Assert.Contains("+3", chip.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)); Assert.Equal("3 more meetings", ToolTip.GetTip(chip)?.ToString());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void BlocksOverlappedThreeDeepShowInitialsAndTheDurationAndFourDeepInitialsAlone()
    {
        var model = Model();
        model.Engine.AddSession("Account one", At(9), At(10)); model.Engine.AddSession("Account two", At(9.2), At(10.2)); model.Engine.AddSession("Internal", At(9.4), At(10.4));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var initials = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "BlockInitials").Select(t => t.Text).OrderBy(t => t).ToList();
            Assert.Equal(new[] { "AO", "AT", "I" }, initials);
            Assert.Equal(3, window.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name == "BlockDuration")); // And each still has its duration.
        }
        finally { window.Close(); }
        model.Engine.AddSession("Account one", At(9.5), At(10.5));
        window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.Equal(4, window.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name == "BlockInitials"));
            Assert.Empty(window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "BlockDuration")); // Initials alone with four.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WithAMeetingRowTheBandIsShortSoFourLanesKeepInitialsAndFiveOrMoreShowOnlyColour()
    {
        var model = Model();
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(14), TimeSpan.FromHours(15), "Review", null, RepeatChoice.None); // A day with meetings has the shorter band.
        for (var i = 0; i < 4; i++) model.Engine.AddSession(i % 2 == 0 ? "Account one" : "Account two", At(9 + i * 0.1), At(10 + i * 0.1));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try { Assert.Equal(4, window.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name == "BlockInitials")); } finally { window.Close(); }
        model.Engine.AddSession("Internal", At(9.5), At(10.5));
        window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            // Five lanes of about 11 pixels: no words that would be cut off, and the colour and tooltip remain.
            Assert.Empty(window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name is "BlockInitials" or "BlockDuration"));
            Assert.Equal(5, window.GetVisualDescendants().OfType<Border>().Count(b => b.Name is { } n && n.StartsWith("TimelineEntry")));
        }
        finally { window.Close(); }
    }
}
