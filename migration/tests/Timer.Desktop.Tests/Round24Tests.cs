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
            // Four deep: three lanes keep their blocks and the fourth folds into a +1 chip.
            Assert.Equal(3, window.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name == "BlockInitials"));
            Assert.Equal(3, window.GetVisualDescendants().OfType<Border>().Count(b => b.Name is { } n && n.StartsWith("TimelineEntry")));
            Assert.Contains("+1", window.GetVisualDescendants().OfType<Button>().Single(b => b.Name?.StartsWith("TimelineBlocksMore") == true).GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        }
        finally { window.Close(); }
    }

    private static TimerModel FiveDeep()
    {
        var model = Model();
        model.Engine.AddSession("Account one", At(9), At(10)); model.Engine.AddSession("Account two", At(9.1), At(10.1)); model.Engine.AddSession("Internal", At(9.2), At(10.2));
        model.Engine.AddSession("Account one", At(9.3), At(10.3)); model.Engine.AddSession("Account two", At(9.4), At(10.4));
        return model;
    }
    private static Button Chip(Window w) => w.GetVisualDescendants().OfType<Button>().Single(b => b.Name?.StartsWith("TimelineBlocksMore") == true);

    [AvaloniaFact]
    public void FiveDeepKeepsThreeLanesAndAPlusTwoThatListsTheRestAndChoosingOneSelectsIt()
    {
        var model = FiveDeep();
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(14), TimeSpan.FromHours(15), "Review", null, RepeatChoice.None); // A day with meetings has the shorter band.
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.Equal(3, window.GetVisualDescendants().OfType<Border>().Count(b => b.Name is { } n && n.StartsWith("TimelineEntry")));
            var chip = Chip(window); Assert.Contains("+2", chip.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)); Assert.StartsWith("2 more blocks", ToolTip.GetTip(chip)?.ToString());
            Assert.All(window.GetVisualDescendants().OfType<Border>().Where(b => b.Name is { } n && n.StartsWith("TimelineEntry")), b => Assert.True(b.Bounds.Height >= 15)); // No lane too short for its words.
            chip.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window);
            var popup = window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single(p => p.Name == "TimelineBlocksMorePopup"); Assert.True(popup.IsOpen);
            var rows = popup.Child!.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("MoreBlock") == true).ToList(); Assert.Equal(2, rows.Count);
            // Choosing a row selects the block, lights the chip and keeps a solid outline on its time.
            rows[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Assert.Equal(1, model.SelectedBlockCount); Assert.False(popup.IsOpen);
            Assert.Contains("sel", Chip(window).Classes); Assert.Single(window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>(), r => r.Name == "TimelineFoldedSelected");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheCardListsTheOverlappingBlocksWithTheFoldedOnesMarkedAndAPressSwitchesTheSelection()
    {
        var model = FiveDeep(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var first = model.Engine.Sessions[0]; model.SelectBlock(new[] { first }, false); Settle(window);
            var row = window.GetVisualDescendants().OfType<WrapPanel>().Single(p => p.Name == "OverlapRow"); var buttons = row.Children.OfType<Button>().ToList();
            Assert.Equal(5, buttons.Count); Assert.Contains("on", buttons[0].Classes);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "5 overlapping blocks"); Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Dashed: folded into +2");
            Assert.Contains("folded", Avalonia.Automation.AutomationProperties.GetName(buttons[4])); Assert.DoesNotContain("folded", Avalonia.Automation.AutomationProperties.GetName(buttons[0]));
            buttons[4].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Assert.Equal(model.Engine.Sessions[4].From, model.SelectedBlocks.Single().Start);
            Assert.Contains("on", window.GetVisualDescendants().OfType<WrapPanel>().Single(p => p.Name == "OverlapRow").Children.OfType<Button>().ToList()[4].Classes); // The row follows.
        }
        finally { window.Close(); }
        // Two overlapping blocks: no row.
        var plain = Model(); plain.Engine.AddSession("Account one", At(9), At(10)); plain.Engine.AddSession("Account two", At(9.5), At(10.5)); plain.SelectBlock(new[] { plain.Engine.Sessions[0] }, false);
        var other = new DashboardWindow(plain); other.Show(); Settle(other);
        try { Assert.Empty(other.GetVisualDescendants().OfType<WrapPanel>().Where(p => p.Name == "OverlapRow")); } finally { other.Close(); }
    }

    [AvaloniaFact]
    public void TheBlockRecordingNowIsNeverFoldedAway()
    {
        var model = Model();
        for (var i = 0; i < 4; i++) model.Engine.AddSession(i % 2 == 0 ? "Account one" : "Account two", At(19.5 + i * 0.01), At(20.6));
        model.ToggleAccount("Internal"); // Recording from 20:00, over four others: it would be the fifth lane.
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try { Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "TimelineRecording"); }
        finally { window.Close(); }
    }

    // Pictures of the busy day for looking at it: TIMER_PREVIEW_DIR names the folder. The demo's 11:00 to 12:00 holds three stacked meetings and blocks three deep.
    [AvaloniaFact]
    public void WriteBusyTimelinePictures()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 14, 14, 15, 0)); model.SetTheme(theme); model.CompleteSetup();
            model.ZoomTimeline(6, 0, 0); model.TimelineOffset = 34 + 10.4 * model.TimelinePixelsPerHour;
            var window = new DashboardWindow(model); window.Show(); Settle(window);
            try
            {
                using (var shot = window.CaptureRenderedFrame()) shot?.Save(Path.Combine(folder, $"busy-{theme}.png"));
                // The +N list open.
                var chip = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name?.StartsWith("TimelineMeetingMore") == true);
                if (chip is not null) { chip.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window); using var shot = window.CaptureRenderedFrame(); shot?.Save(Path.Combine(folder, $"busy-{theme}-list.png")); }
            }
            finally { window.Close(); }
        }
        // Five deep (the demo's three plus two more over the same minutes): the +2 chip, its list, and a folded block chosen from it with the card's row of overlapping blocks.
        {
            var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 14, 14, 15, 0)); model.SetTheme("Dark"); model.CompleteSetup();
            model.Engine.AddSession("Account four", new DateTime(2026, 10, 14, 11, 27, 0), new DateTime(2026, 10, 14, 11, 52, 0));
            model.Engine.AddSession("Account six", new DateTime(2026, 10, 14, 11, 29, 0), new DateTime(2026, 10, 14, 11, 44, 0));
            model.ZoomTimeline(6, 0, 0); model.TimelineOffset = 34 + 10.4 * model.TimelinePixelsPerHour;
            var window = new DashboardWindow(model); window.Show(); Settle(window);
            try
            {
                using (var shot = window.CaptureRenderedFrame()) shot?.Save(Path.Combine(folder, "busy-Dark-5-deep.png"));
                var chip = window.GetVisualDescendants().OfType<Button>().First(b => b.Name?.StartsWith("TimelineBlocksMore") == true);
                chip.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window);
                using (var shot = window.CaptureRenderedFrame()) shot?.Save(Path.Combine(folder, "busy-Dark-5-deep-list.png"));
                var row = window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().First(p => p.Name == "TimelineBlocksMorePopup").Child!.GetVisualDescendants().OfType<Button>().First(b => b.Name?.StartsWith("MoreBlock") == true);
                row.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window);
                using (var shot = window.CaptureRenderedFrame()) shot?.Save(Path.Combine(folder, "busy-Dark-5-deep-picked.png"));
            }
            finally { window.Close(); }
        }
    }
}
