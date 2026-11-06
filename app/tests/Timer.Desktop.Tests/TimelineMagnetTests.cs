using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Overlap anywhere with a magnet (the board "Timeline behaviours"): a drag pulls flush to a neighbour's edge close by and goes through it past that, Add time and Paste
// use the time asked for, and nothing but the block being recorded refuses to share hours.
public sealed class TimelineMagnetTests
{
    private static readonly DateTime Day = new(2026, 10, 14);
    private const double PixelsPerHour = 90; // The default zoom: the ten pixel band is about 6 minutes 40.
    // A 09:00 to 09:50, B 10:00 to 10:30, C 11:00 to 11:40, on a day that is today at 15:00.
    private static TimerModel Setup(out SessionRow a, out SessionRow b, out SessionRow c)
    {
        var engine = new TrackingEngine(clock: () => Day.AddHours(15));
        engine.Sessions.Add(new("2026-10-14", "Account one", "09:00:00", "added", "50.00"));
        engine.Sessions.Add(new("2026-10-14", "Account two", "10:00:00", "added", "30.00"));
        engine.Sessions.Add(new("2026-10-14", "Account three", "11:00:00", "added", "40.00"));
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two"), new("Account three") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        a = engine.Sessions[0]; b = engine.Sessions[1]; c = engine.Sessions[2]; return model;
    }
    private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

    [Fact]
    public void ABlockCarriedCloseToANeighboursEdgeSnapsFlushWithNoGap()
    {
        var model = Setup(out var a, out var b, out _);
        var delta = TimelineGestures.Magnet(model, b, 0, Min(-8), PixelsPerHour, false, out var note); // B would start at 09:52, 2 minutes short of A's end.
        Assert.Equal(Min(-10), delta); Assert.True(note.Snapped); Assert.Equal(a.To, note.At);
        Assert.Equal(a.To, b.From + delta); // Flush: its start is exactly A's end.
        // The same for the end of a block carried towards the next one's start.
        var up = TimelineGestures.Magnet(model, a, 0, Min(9), PixelsPerHour, false, out note); // A's end would reach 09:59.
        Assert.Equal(Min(10), up); Assert.Equal(b.From, a.To + up);
    }

    [Fact]
    public void PastTheMagnetTheBlockGoesWhereThePointerIsAndOverlaps_ToTheFiveMinutes()
    {
        var model = Setup(out _, out var b, out _);
        var delta = TimelineGestures.Magnet(model, b, 0, Min(-18), PixelsPerHour, false, out var note); // 12 pixels from the edge: out of the band.
        Assert.False(note.Snapped); Assert.Equal(Min(-20), delta);
        Assert.Equal(Min(10), TimelineGestures.OverlapOf(model, b, 0, delta)); // 09:40 to 10:10 shares 10 minutes with A.
        Assert.True(model.MoveEntry(b, b.From + delta, b.To + delta), model.EditError);
        Assert.Equal(2, model.Engine.Sessions.Count(s => s.From < Day.AddHours(10) && s.To > Day.AddHours(9.7))); // Two blocks share the hours.
    }

    [Fact]
    public void HoldingAltTurnsTheMagnetOffAndMovesByTheMinute()
    {
        var model = Setup(out _, out var b, out _);
        var delta = TimelineGestures.Magnet(model, b, 0, Min(-9.4), PixelsPerHour, true, out var note);
        Assert.False(note.Snapped); Assert.Equal(Min(-9), delta);
        Assert.Equal(Min(-10), TimelineGestures.Magnet(model, b, 0, Min(-9.4), PixelsPerHour, false, out _)); // Without Alt the same drag snaps.
    }

    [Fact]
    public void AnEdgeBeingStretchedSnapsToTheNextBlocksStart_AndTheBandFollowsTheZoom()
    {
        var model = Setup(out var a, out _, out _);
        var delta = TimelineGestures.Magnet(model, a, 1, Min(9), PixelsPerHour, false, out var note);
        Assert.Equal(Min(10), delta); Assert.True(note.Snapped);
        // At a wide zoom the same 8 minutes is far more pixels, so nothing pulls.
        Assert.False(TimelineGestures.Magnet(model, a, 1, Min(7), 640, false, out note) == Min(10)); Assert.False(note.Snapped);
    }

    [Fact]
    public void NothingMayRunIntoTheBlockBeingRecorded_ButEverythingElseMayShareHours()
    {
        var now = Day.AddHours(11).AddMinutes(30);
        var engine = new TrackingEngine(clock: () => now);
        engine.Sessions.Add(new("2026-10-14", "Account one", "09:00:00", "added", "50.00"));
        now = Day.AddHours(11); engine.ToggleAccount("Account two"); now = Day.AddHours(11).AddMinutes(30);
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday));
        var a = engine.Sessions[0];
        var delta = TimelineGestures.Magnet(model, a, 1, Min(240), PixelsPerHour, false, out _); // Dragged far past it.
        Assert.True(a.To + delta <= engine.Active!.Start);
        Assert.False(model.MoveEntry(a, Day.AddHours(10.9), Day.AddHours(11.2))); Assert.NotNull(model.EditError); // Typed or dragged, it runs into the recording.
    }

    [Fact]
    public void AddTimeOpensAtTheExactTimeClickedEvenOnTopOfABlock_AndSnapsFlushWhenCloseToAnEdge()
    {
        var model = Setup(out _, out _, out _); (DateTime Start, DateTime End)? asked = null; model.EntryRequested += (s, e) => asked = (s, e);
        model.OpenGap(Day.AddHours(10).AddMinutes(10)); // Inside B: it used to slide to the end of it.
        Assert.Equal((Day.AddHours(10).AddMinutes(10), Day.AddHours(10).AddMinutes(40)), asked);
        model.OpenGap(Day.AddHours(9).AddMinutes(55)); // Five minutes after A ends: inside the magnet.
        Assert.Equal(Day.AddHours(9).AddMinutes(50), asked!.Value.Start);
        model.OpenGap(Day.AddHours(23).AddMinutes(45)); Assert.Equal(Day.AddDays(1), asked!.Value.End); // Short at the end of the day.
        Assert.True(model.AddEntry("Account three", Day.AddHours(10), Day.AddHours(10.5), "stacked")); // Overlap is accepted.
    }

    [Fact]
    public void AddTimeMovesPastTheRunningBlockAndNothingElse()
    {
        var now = Day.AddHours(11).AddMinutes(30);
        var engine = new TrackingEngine(clock: () => now);
        now = Day.AddHours(11); engine.ToggleAccount("Account one"); now = Day.AddHours(11).AddMinutes(30);
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one") }, false, "Light", DayOfWeek.Monday)); (DateTime Start, DateTime End)? asked = null; model.EntryRequested += (s, e) => asked = (s, e);
        model.OpenGap(Day.AddHours(11).AddMinutes(10));
        Assert.Equal(Day.AddHours(11).AddMinutes(30), asked!.Value.Start); // Just after what is recording.
    }

    [Fact]
    public void PasteLandsWhereAskedEvenOnRecordedTime_AndCtrlVChoosesPointerThenSelectionThenDayStart()
    {
        var model = Setup(out var a, out var b, out _);
        Assert.True(model.CopyBlocks(new[] { a }));
        Assert.True(model.PasteBlocks(Day.AddHours(10).AddMinutes(10))); // On B, which used to refuse.
        Assert.Equal(4, model.Engine.Sessions.Count);
        // Ctrl+V: the pointer's place when it is over the timeline, snapped flush to a nearby edge.
        model.PasteTime = Day.AddHours(10).AddMinutes(33); Assert.Equal(Day.AddHours(10.5), model.DefaultPasteTime()); // 3 minutes after B ends.
        model.PasteTime = Day.AddHours(13); Assert.Equal(Day.AddHours(13), model.DefaultPasteTime());
        // No pointer: straight after the selected block, and otherwise the day's start, never after the last block of the day.
        model.PasteTime = null; model.SelectBlock(new[] { b }, false); Assert.Equal(b.To, model.DefaultPasteTime());
        model.ClearSelection(); Assert.Equal(Day.AddHours(9), model.DefaultPasteTime());
    }

    [Fact]
    public void FitToPreviousAndNextClosesGapsAndOverlapsFromDetails()
    {
        var model = Setup(out var a, out var b, out _);
        Assert.True(model.FitToPrevious(b)); b = model.Engine.Sessions.Single(s => s.Account == "Account two");
        Assert.Equal(a.To, b.From); Assert.Equal(Day.AddHours(10.5), b.To); // Start moved to A's end, end kept.
        Assert.True(model.FitToNext(a)); a = model.Engine.Sessions.Single(s => s.Account == "Account one");
        Assert.Equal(b.From, a.To); // A now ends where B starts: they share nothing.
        Assert.False(model.FitToPrevious(a)); Assert.NotNull(model.EditError); // Nothing before A.
    }

    [AvaloniaFact]
    public void EveryTimelineMenuOffersAddTimeHereAndPasteHere_AndCopiedBlocksPreviewAtThePointer()
    {
        var model = Setup(out var a, out _, out _); model.CopyBlocks(new[] { a });
        var window = new Window { Width = 900, Height = 600, Content = ReviewViews.Timeline(model) }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            var canvas = window.GetVisualDescendants().OfType<Canvas>().First(c => c.Name == "TimelineCanvas");
            foreach (var card in canvas.Children.OfType<Border>().Where(c => c.ContextMenu is not null && c.Tag is SessionRow))
            {
                var items = card.ContextMenu!.Items.OfType<MenuItem>().ToList();
                Assert.Contains(items, i => i.Name == "AddTimeHereMenu"); Assert.Contains(items, i => i.Name == "PasteHereMenu" && i.IsEnabled);
            }
            window.MouseMove(canvas.TranslatePoint(new Point(ReviewViews.TimelineGutter + 12.5 * PixelsPerHour, 100), window)!.Value, Avalonia.Input.RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(canvas.Children.OfType<Border>(), b => b.Name?.StartsWith("PastePreview") == true);
            window.MouseMove(new Point(2, 2), Avalonia.Input.RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        }
        finally { window.Close(); }
    }
}
