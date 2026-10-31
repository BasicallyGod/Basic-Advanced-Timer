using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 25: a busy day is drawn calmly. Three or more blocks (or meetings) at one moment are one tile whatever the zoom; the block recording now keeps a lane of its own above the
// tile; and the Details card (the old Selected block card) opens a stack as a timeline of its own and edits meetings as well as blocks.
public sealed class Round25Tests
{
    private static readonly DateTime Day = new(2026, 10, 21);
    private static DateTime At(double hours) => Day.AddHours(hours);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static TimerModel Model()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => At(20)), new StoreSettings(new() { new("Account one"), new("Account two"), new("Internal") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup(); return model;
    }
    private static IEnumerable<Control> Named(Window w, string prefix) => w.GetVisualDescendants().OfType<Control>().Where(c => c.Name is { } n && n.StartsWith(prefix));
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().SingleOrDefault(c => c.Name == name) ?? throw new Xunit.Sdk.XunitException("No " + typeof(T).Name + " named " + name);
    private static Point Centre(Window window, Control c, double dx = 0) => c.TranslatePoint(new Point(c.Bounds.Width / 2 + dx, c.Bounds.Height / 2), window)!.Value;
    // A point a little in from the left of a control, for one that may reach past the edge of the window at this zoom.
    private static Point Near(Window window, Control c) => c.TranslatePoint(new Point(24, c.Bounds.Height / 2), window)!.Value;
    private static void Press(Window window, Point p) { window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left); Settle(window); }
    private static string Kind(Window w) => w.GetVisualDescendants().OfType<Border>().First(b => b.Name == "DetailsKind").GetVisualDescendants().OfType<TextBlock>().First().Text!;
    private static Border Tile(Window w) => w.GetVisualDescendants().OfType<Border>().Single(b => b.Name?.StartsWith("TimelineStack") == true);
    // Every piece of text in a tile's tooltip, for looking for words in it.
    private static IEnumerable<string?> TipTexts(Control c) => ((Avalonia.LogicalTree.ILogical)ToolTip.GetTip(c)!).GetSelfAndLogicalDescendants().OfType<TextBlock>().Select(t => t.Text);
    private static double Top(Control c, Visual to) => c.TranslatePoint(default, to)!.Value.Y;

    private static DateTime T(int hour, int minute) => Day.AddHours(hour).AddMinutes(minute);
    // Five blocks an hour long, each six minutes after the one before: 09:00 to 10:24 in all.
    private static TimerModel FiveDeep()
    {
        var model = Model();
        model.Engine.AddSession("Account one", T(9, 0), T(10, 0)); model.Engine.AddSession("Account two", T(9, 6), T(10, 6)); model.Engine.AddSession("Internal", T(9, 12), T(10, 12));
        model.Engine.AddSession("Account one", T(9, 18), T(10, 18)); model.Engine.AddSession("Account two", T(9, 24), T(10, 24));
        return model;
    }
    private static TimerModel ThreeMeetings()
    {
        var model = Model();
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(11), TimeSpan.FromHours(12), "Client call", "Account one", RepeatChoice.None);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(11.5), TimeSpan.FromHours(12.25), "Design review", null, RepeatChoice.None);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(11.75), TimeSpan.FromHours(12.5), "Supplier check-in", null, RepeatChoice.None);
        return model;
    }

    [AvaloniaFact]
    public void TwoOverlappingMeetingsKeepTheirLanesAndThreeBecomeOneTileThatSaysSo()
    {
        var two = Model();
        two.Notes.AddMeeting(Day, TimeSpan.FromHours(11), TimeSpan.FromHours(12), "Client call", null, RepeatChoice.None);
        two.Notes.AddMeeting(Day, TimeSpan.FromHours(11.5), TimeSpan.FromHours(12.25), "Design review", null, RepeatChoice.None);
        var window = new DashboardWindow(two); window.Show(); Settle(window);
        try
        {
            var cards = Named(window, "TimelineMeeting").OfType<Border>().Where(c => char.IsDigit(c.Name![15])).ToList();
            Assert.Equal(2, cards.Count); Assert.All(cards, c => Assert.Equal(22, c.Bounds.Height, 0.5)); Assert.Empty(Named(window, "TimelineMeetingStack"));
        }
        finally { window.Close(); }
        var three = ThreeMeetings(); window = new DashboardWindow(three); window.Show(); Settle(window);
        try
        {
            var tile = Named(window, "TimelineMeetingStack").OfType<Border>().Single();
            Assert.Empty(Named(window, "TimelineMeeting").Where(c => c.Name![15..].All(char.IsDigit) && c.Name!.Length > 15)); // None of the three is drawn on its own.
            Assert.Equal(48, tile.Bounds.Height, 0.5);
            Assert.Contains("3 meetings", tile.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains("Client call", TipTexts(tile)); Assert.Contains("Supplier check-in", TipTexts(tile)); Assert.Contains("11:45–12:30", TipTexts(tile));
            Assert.Empty(window.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.Contains("More") == true && b.Name.StartsWith("Timeline"))); // No +N chip any more.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThreeBlocksAtOnceAreOneTileAndTwoKeepTheirLanes()
    {
        var model = Model(); model.Engine.AddSession("Account one", At(9), At(10)); model.Engine.AddSession("Account two", At(9.5), At(10.5));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try { Assert.Equal(2, Named(window, "TimelineEntry").Count()); Assert.Empty(Named(window, "TimelineStack")); }
        finally { window.Close(); }
        model = FiveDeep(); window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var tile = Tile(window);
            Assert.Empty(Named(window, "TimelineEntry")); // None of the five is drawn alone, and nothing folds into a chip.
            Assert.Contains("5 blocks · 05:00", tile.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Equal(model.TimelinePixelsPerHour * (10.4 - 9) - ReviewViews.BlockGap, tile.Bounds.Width, 1); // From the first start to the last end.
            var tip = Assert.IsType<StackPanel>(ToolTip.GetTip(tile)); var grid = Assert.IsType<Grid>(tip.Children[1]); Assert.Equal(model.Engine.Sessions.Count, grid.RowDefinitions.Count); // A row each,
            Assert.Equal(3, grid.Children.Select(Grid.GetColumn).Distinct().Count()); // in three columns, so the times and lengths line up.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePileStaysATileAtAnyZoom()
    {
        var model = FiveDeep(); model.ZoomTimeline(8, 0, 0); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try { Assert.Single(Named(window, "TimelineStack")); Assert.Empty(Named(window, "TimelineEntry")); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressingTheTileSelectsItsFirstBlockAndDetailsDrawsTheStackAsATimelineWhereAPressPicksAnother()
    {
        var model = FiveDeep(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.Equal("Details", Find<TextBlock>(window, "SelectedTitle").Text); Assert.Contains("Nothing is selected", Find<TextBlock>(window, "SelectedEmpty").Text);
            Press(window, Centre(window, Tile(window)));
            Assert.Single(model.SelectedBlocks); Assert.Equal(T(9, 0), model.SelectedBlocks[0].Start);
            Assert.Equal("Blocks", Kind(window));
            Assert.True(Tile(window).Classes.Contains("mergeSelected"));
            var bars = Named(window, "Stack_").OfType<Border>().ToList(); Assert.Equal(5, bars.Count);
            Assert.Equal(2, Named(window, "StackHandle").Count()); // Handles on the picked bar only.
            // Picking the third bar selects that block.
            Press(window, Centre(window, bars[2]));
            Assert.Equal(T(9, 12), model.SelectedBlocks.Single().Start); Assert.Equal("09:12:00", Find<TextBox>(window, "SelectedStart").Text);
            // Pressing the tile again puts the stack away.
            Press(window, Centre(window, Tile(window))); Assert.Empty(model.SelectedBlocks); Assert.Empty(Named(window, "StackTimeline"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DraggingTheEndHandleOfThePickedBarMovesThatBlocksEnd()
    {
        var model = FiveDeep(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Press(window, Centre(window, Tile(window)));
            var timeline = Find<StackTimeline>(window, "StackTimeline"); var handle = Find<Border>(window, "StackHandleEnd");
            // The window runs 09:00 to 10:30, so a minute is a ninetieth of the width: six minutes later.
            var from = Centre(window, handle); var to = from + new Vector(timeline.Bounds.Width / 90 * 6, 0);
            window.MouseDown(from, MouseButton.Left); window.MouseMove(from + new Vector(10, 0)); window.MouseMove(to); window.MouseUp(to, MouseButton.Left); Settle(window);
            var first = model.Engine.Sessions.OrderBy(s => s.From).First();
            Assert.Equal(T(9, 0), first.From); Assert.Equal(6, (first.To - T(10, 0)).TotalMinutes, 1.1);
            Assert.Equal(first.From, model.SelectedBlocks.Single().Start); // Still the picked one.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheBlockRecordingOverAStackKeepsALaneAboveASlimmerTileAndDetailsListsItLastInGreen()
    {
        var model = Model();
        for (var i = 0; i < 4; i++) model.Engine.AddSession(i % 2 == 0 ? "Account one" : "Account two", At(19.5 + i * 0.01), At(20.6));
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(15), TimeSpan.FromHours(16), "Planning", null, RepeatChoice.None); // A meeting row, so the band is the busy day's height.
        model.ToggleAccount("Internal"); // Recording from 20:00, over the four.
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var live = Find<Border>(window, "TimelineRecording"); var tile = Tile(window); var canvas = Find<Canvas>(window, "TimelineCanvas");
            Assert.Equal(ReviewViews.LiveLane, live.Bounds.Height, 0.5);
            Assert.True(Top(tile, canvas) - (Top(live, canvas) + live.Bounds.Height) >= ReviewViews.LiveGap - 0.5); // Clear of the tile, ring and all.
            var plain = FiveDeepTileHeight(); Assert.True(tile.Bounds.Height < plain - ReviewViews.LiveLane); // The tile gave up room for the lane.
            Assert.Contains("4 blocks", tile.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).First(t => t!.StartsWith("4")));
            Press(window, Centre(window, tile));
            Assert.Equal("Blocks", Kind(window));
            var bars = Named(window, "Stack_").OfType<Border>().ToList(); Assert.Equal(5, bars.Count);
            Assert.NotNull(bars[^1].BoxShadow); Assert.Contains(bars[^1].GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(), e => e.Name == "StackRecordingDot");
            var before = model.SelectedBlocks.Single().Start; Press(window, Centre(window, bars[^1])); Assert.Equal(before, model.SelectedBlocks.Single().Start); // It can't be picked.
        }
        finally { window.Close(); }
    }
    private static double FiveDeepTileHeight()
    {
        var model = FiveDeep(); model.Notes.AddMeeting(Day, TimeSpan.FromHours(15), TimeSpan.FromHours(16), "Planning", null, RepeatChoice.None);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try { return Tile(window).Bounds.Height; } finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressingTheMeetingTileShowsTheStackInDetailsAndTintsItsRowsInTheNotesCard()
    {
        var model = ThreeMeetings(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Press(window, Centre(window, Named(window, "TimelineMeetingStack").OfType<Border>().Single()));
            var first = model.Notes.MeetingsOn(Day).OrderBy(m => m.At).First();
            Assert.Equal(first.Id, model.SelectedMeetingId); Assert.Equal("Meetings", Kind(window)); Assert.Equal("Client call", Find<TextBox>(window, "DetailsMeetingName").Text);
            var bars = Named(window, "Stack_").OfType<Border>().ToList(); Assert.Equal(3, bars.Count);
            Assert.Equal(3, window.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("noteRow") && b.Classes.Contains("rowSelected")));
            Press(window, Centre(window, bars[1]));
            Assert.Equal("Design review", Find<TextBox>(window, "DetailsMeetingName").Text); Assert.Equal(model.Notes.MeetingsOn(Day).Single(m => m.End == TimeSpan.FromHours(12.25)).Id, model.SelectedMeetingId);
            // A press on a row of the Notes card shows that meeting too.
            Press(window, Centre(window, Find<Button>(window, "NoteEdit_" + first.Id)));
            Assert.Equal(first.Id, model.SelectedMeetingId);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DetailsEditsAMeetingByItsBoxesMovingTheEndWithTheStartAndDeletesIt()
    {
        var model = Model(); model.Notes.AddMeeting(Day, TimeSpan.FromHours(13), TimeSpan.FromHours(13.5), "Vendor demo", null, RepeatChoice.None);
        var id = model.Notes.MeetingsOn(Day).Single().Id; var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Press(window, Centre(window, Find<Border>(window, "TimelineMeeting" + id)));
            Assert.Equal("Meeting", Kind(window)); Assert.Null(window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "DetailsScope")); // It does not repeat: nothing to ask.
            void Commit(string name, string text) { var box = Find<TextBox>(window, name); box.Text = text; box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Settle(window); }
            Commit("DetailsMeetingName", "Vendor demo (long)"); Assert.Equal("Vendor demo (long)", model.Notes.Text(model.Notes.Find(id)!));
            Commit("DetailsMeetingStart", "14:00");
            Assert.Equal(TimeSpan.FromHours(14), model.Notes.Find(id)!.At); Assert.Equal(TimeSpan.FromHours(14.5), model.Notes.Find(id)!.End); // Half an hour long still.
            Commit("DetailsMeetingEnd", "15:00"); Assert.Equal(TimeSpan.FromHours(15), model.Notes.Find(id)!.End);
            Commit("DetailsMeetingStart", "nonsense"); Assert.Equal(TimeSpan.FromHours(14), model.Notes.Find(id)!.At); Assert.Equal("14:00", Find<TextBox>(window, "DetailsMeetingStart").Text); // Refused, and put back.
            var account = Find<ComboBox>(window, "DetailsMeetingAccount"); account.SelectedItem = "Account two"; Settle(window);
            Assert.Equal("Account two", model.Notes.AccountOf(model.Notes.Find(id)!));
            Press(window, Centre(window, Find<Button>(window, "DeleteMeeting")));
            Assert.Empty(model.Notes.MeetingsOn(Day)); Assert.Null(model.SelectedMeetingId); Assert.Empty(window.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "DetailsKind")); Assert.NotNull(Find<TextBlock>(window, "SelectedEmpty"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SelectingABlockLetsGoOfAMeetingAndTheOtherWayRound()
    {
        var model = Model(); model.Engine.AddSession("Account one", At(9), At(10)); model.Notes.AddMeeting(Day, TimeSpan.FromHours(13), TimeSpan.FromHours(14), "Planning", null, RepeatChoice.None);
        var meeting = model.Notes.MeetingsOn(Day).Single();
        model.SelectMeeting(meeting.Id); Assert.Equal(meeting.Id, model.SelectedMeetingId); Assert.Empty(model.SelectedBlocks);
        model.SelectBlock(new[] { model.Engine.Sessions[0] }, false); Assert.Null(model.SelectedMeetingId); Assert.Single(model.SelectedBlocks);
        model.SelectMeeting(meeting.Id); Assert.Empty(model.SelectedBlocks);
        model.SelectMeeting(meeting.Id); Assert.Null(model.SelectedMeetingId); // The only selected meeting, pressed again, clears.
    }

    // The point of the round is clarity, so nothing on the timeline may lie over anything else, and the Details card keeps its parts apart and inside itself.
    [AvaloniaFact]
    public void NothingOnABusyTimelineOrInTheDetailsCardOverlapsAnythingElse()
    {
        var model = Model();
        for (var i = 0; i < 4; i++) model.Engine.AddSession(i % 2 == 0 ? "Account one" : "Account two", At(9 + i * 0.1), At(10 + i * 0.1));
        model.Engine.AddSession("Account one", At(14), At(15)); model.Engine.AddSession("Account two", At(14.5), At(15.5)); // Two overlapping: lanes.
        model.Engine.AddSession("Internal", At(17), At(18));
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "A", null, RepeatChoice.None); model.Notes.AddMeeting(Day, TimeSpan.FromHours(9.25), TimeSpan.FromHours(10), "B", null, RepeatChoice.None);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(9.5), TimeSpan.FromHours(10.5), "C", null, RepeatChoice.None);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(14), TimeSpan.FromHours(15), "D", null, RepeatChoice.None); model.Notes.AddMeeting(Day, TimeSpan.FromHours(14.5), TimeSpan.FromHours(15.5), "E", null, RepeatChoice.None);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            void AuditTimeline()
            {
                var canvas = Find<Canvas>(window, "TimelineCanvas");
                var drawn = canvas.Children.OfType<Control>().Where(c => c.Name is { } n && (n.StartsWith("TimelineEntry") || n.StartsWith("TimelineStack") || n.StartsWith("TimelineRecording") || n.StartsWith("TimelineGroup")
                    || n.StartsWith("TimelineMeeting") && n != "TimelineMeetingLane")).Select(c => (c.Name!, new Rect(Canvas.GetLeft(c), Canvas.GetTop(c), c.Bounds.Width, c.Bounds.Height))).ToList();
                Assert.True(drawn.Count >= 7);
                for (var i = 0; i < drawn.Count; i++)
                    for (var j = i + 1; j < drawn.Count; j++)
                        Assert.False(drawn[i].Item2.Deflate(.4).Intersects(drawn[j].Item2.Deflate(.4)), $"{drawn[i].Item1} {drawn[i].Item2} lies over {drawn[j].Item1} {drawn[j].Item2}");
            }
            AuditTimeline();
            Press(window, Near(window, Tile(window))); Assert.True(model.SelectedBlocks.Count == 1, "Pressing the tile at " + Near(window, Tile(window)) + " selected " + model.SelectedBlocks.Count + "; tile " + Tile(window).Bounds + " in " + window.Bounds); AuditTimeline(); // Selected: the ring is outside the tile, and nothing moves.
            var card = Find<SelectedBlockCard>(window, "SelectedBlockCard"); var inside = new Rect(card.Bounds.Size).Deflate(new Thickness(14, 10));
            var parts = new[] { "StackScroll", "SelectedAccount", "SelectedStart", "SelectedEnd", "SelectedLength", "SelectedNote" }.Select(n => (n, Find<Control>(window, n))).Select(p => (p.n, new Rect(p.Item2.TranslatePoint(default, card)!.Value, p.Item2.Bounds.Size))).ToList();
            foreach (var (name, rect) in parts) Assert.True(inside.Contains(rect.TopLeft) && inside.Contains(rect.BottomRight), $"{name} {rect} is not inside the Details card {inside}");
            for (var i = 0; i < parts.Count; i++) for (var j = i + 1; j < parts.Count; j++) Assert.False(parts[i].Item2.Intersects(parts[j].Item2), $"{parts[i].n} lies over {parts[j].n}");
            // The meetings' tile, then a meeting from it.
            Press(window, Near(window, Named(window, "TimelineMeetingStack").OfType<Border>().First())); AuditTimeline();
            var meetingParts = new[] { "StackScroll", "DetailsMeetingName", "DetailsMeetingAccount", "DetailsMeetingDay", "DetailsMeetingStart", "DetailsMeetingEnd", "DetailsMeetingRepeat" }.Select(n => (n, Find<Control>(window, n))).Select(p => (p.n, new Rect(p.Item2.TranslatePoint(default, card)!.Value, p.Item2.Bounds.Size))).ToList();
            foreach (var (name, rect) in meetingParts) Assert.True(inside.Contains(rect.TopLeft) && inside.Contains(rect.BottomRight), $"{name} {rect} is not inside the Details card {inside}");
            for (var i = 0; i < meetingParts.Count; i++) for (var j = i + 1; j < meetingParts.Count; j++) Assert.False(meetingParts[i].Item2.Intersects(meetingParts[j].Item2), $"{meetingParts[i].n} lies over {meetingParts[j].n}");
        }
        finally { window.Close(); }
    }

    // Pictures for looking at it: TIMER_PREVIEW_DIR names the folder. The demo's 11:00 to 12:00 holds three stacked meetings and blocks three deep.
    [AvaloniaFact]
    public void WriteStackPictures()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 14, 14, 15, 0)); model.SetTheme(theme); model.CompleteSetup();
            var window = new DashboardWindow(model); window.Show(); Settle(window);
            try
            {
                void Shot(string name) { using var shot = window.CaptureRenderedFrame(); shot?.Save(Path.Combine(folder, $"stack-{theme}-{name}.png")); }
                Shot("rest");
                if (Named(window, "TimelineStack").FirstOrDefault() is Border tile) { Press(window, Centre(window, tile)); Shot("blocks"); }
                if (Named(window, "TimelineMeetingStack").FirstOrDefault() is Border meetings) { Press(window, Centre(window, meetings)); Shot("meetings"); }
                model.ClearSelection(); Settle(window);
                model.SelectMeeting(model.Notes.MeetingsOn(model.SelectedDate).First(m => model.Notes.SeriesOf(m) is not null).Id); Settle(window); Shot("repeating-meeting");
            }
            finally { window.Close(); }
        }
        {
            // A recording over a stack of blocks, in Dark: the lane above the slimmed tile, then the stack open in Details.
            var model = Model(); model.SetTheme("Dark");
            for (var i = 0; i < 4; i++) model.Engine.AddSession(i % 2 == 0 ? "Account one" : "Account two", At(19.2 + i * 0.1), At(20.4 + i * 0.1));
            model.ToggleAccount("Internal"); model.ZoomTimeline(4, 0, 0); model.TimelineOffset = 34 + 18.4 * model.TimelinePixelsPerHour;
            var window = new DashboardWindow(model); window.Show(); Settle(window);
            try
            {
                using (var shot = window.CaptureRenderedFrame()) shot?.Save(Path.Combine(folder, "stack-Dark-recording.png"));
                Press(window, Near(window, Tile(window))); using var picked = window.CaptureRenderedFrame(); picked?.Save(Path.Combine(folder, "stack-Dark-recording-blocks.png"));
            }
            finally { window.Close(); }
        }
    }
}
