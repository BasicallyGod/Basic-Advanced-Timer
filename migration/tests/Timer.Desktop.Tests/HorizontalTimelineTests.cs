using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The horizontal timeline: lanes for overlapping blocks, how much each block shows, break and merged blocks, the day's figures and the contrast of the gradient fill.
public sealed class HorizontalTimelineTests
{
    private static readonly DateTime Day = new(2026, 10, 21);
    private static DateTime At(double hours) => Day.AddHours(hours);

    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static TimerModel Model(Func<DateTime>? clock = null)
    {
        var model = new TimerModel(new TrackingEngine(clock: clock ?? (() => At(20)))); model.CompleteSetup(); return model;
    }

    [Fact]
    public void BlocksThatDoNotOverlapAreEachInTheirOwnFullLane()
    {
        var blocks = new[] { ("a", At(9), At(10)), ("b", At(10), At(11)), ("c", At(12), At(13)) };
        var placed = ReviewViews.PlaceLanes(blocks);
        Assert.All(placed.Values, p => Assert.Equal((0, 1), p));
    }

    [Fact]
    public void OverlappingBlocksShareTheBandInEqualLanesAndAFreedLaneIsReused()
    {
        var placed = ReviewViews.PlaceLanes(new[] { ("a", At(9), At(11)), ("b", At(10), At(12)), ("c", At(11), At(13)) });
        Assert.Equal((0, 2), placed["a"]); Assert.Equal((1, 2), placed["b"]);
        Assert.Equal((0, 2), placed["c"]); // The first lane is free again once a has ended.
        var apart = ReviewViews.PlaceLanes(new[] { ("a", At(9), At(10)), ("b", At(9.5), At(10.5)), ("c", At(15), At(16)) });
        Assert.Equal((0, 1), apart["c"]); // A later block with nothing near it is not squeezed by an earlier cluster.
    }

    [Fact]
    public void OlderRecordingsThatOverlapByASecondOrTwoAreNotSplitIntoLanes()
    {
        var placed = ReviewViews.PlaceLanes(new[] { ("a", At(9), At(10).AddSeconds(1.5)), ("b", At(10), At(11)) });
        Assert.Equal((0, 1), placed["a"]); Assert.Equal((0, 1), placed["b"]);
    }

    [Fact]
    public void WhatABlockShowsAndHowRoundItsCornersAreFollowTheNumberOfLanes()
    {
        Assert.Equal(ReviewViews.BlockDetail.Full, ReviewViews.DetailFor(1)); Assert.Equal(ReviewViews.BlockDetail.Inline, ReviewViews.DetailFor(2));
        Assert.Equal(ReviewViews.BlockDetail.Tiny, ReviewViews.DetailFor(3)); Assert.Equal(ReviewViews.BlockDetail.Bare, ReviewViews.DetailFor(4));
        Assert.Equal(ReviewViews.BlockRadius, ReviewViews.CornerFor(1, 200, 76));
        Assert.Equal(ReviewViews.LaneRadius, ReviewViews.CornerFor(2, 200, 36));
        Assert.Equal(3, ReviewViews.CornerFor(1, 6, 76)); // A block too narrow for the corner gets less of it.
    }

    [AvaloniaFact]
    public void ABreakIsHatchedAndAnAccountBlockIsAGradientOfItsOwnColour()
    {
        var model = Model(); model.Engine.Sessions.Add(new("2026-10-21", "Account one", "09:00:00", "recorded", "60"));
        model.Engine.Sessions.Add(new("2026-10-21", BreakAccount.Name, "10:00:00", "recorded", "30"));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var work = Find<Border>(window, "TimelineEntry0"); var rest = Find<Border>(window, "TimelineEntry1");
            Assert.IsType<LinearGradientBrush>(work.Background); Assert.Equal(2, ((LinearGradientBrush)work.Background!).GradientStops.Count);
            var hatch = Assert.IsType<LinearGradientBrush>(rest.Background); Assert.Equal(GradientSpreadMethod.Repeat, hatch.SpreadMethod);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ABlockInTheSameHoursAsAnotherSitsBesideItInHalfTheBand()
    {
        var model = Model(); model.Engine.Sessions.Add(new("2026-10-21", "Account one", "09:00:00", "recorded", "60"));
        model.Engine.Sessions.Add(new("2026-10-21", "Account two", "09:30:00", "recorded", "60"));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var first = Find<Border>(window, "TimelineEntry0"); var second = Find<Border>(window, "TimelineEntry1");
            Assert.True(Canvas.GetTop(second) > Canvas.GetTop(first)); Assert.Equal(first.Height, second.Height);
            Assert.True(first.Height < 130 * .6); // A day with no meetings gives the blocks the whole height. Assert.Equal(ReviewViews.LaneRadius, first.CornerRadius.TopLeft);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AMergedBlockIsOneBlockWithAMarkAndLighterGapsBetweenItsEntries()
    {
        var model = Model();
        model.Engine.Sessions.Add(new("2026-10-21", "Account one", "09:00:00", "recorded", "30")); model.Engine.Sessions.Add(new("2026-10-21", "Account one", "10:00:00", "recorded", "30"));
        model.Editor.Group(model.Engine.Sessions.ToArray());
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var group = window.GetVisualDescendants().OfType<Border>().Single(b => b.Tag is ReviewEntry);
            Assert.Equal(model.TimelinePixelsPerHour * 1.5 - ReviewViews.BlockGap, group.Width, 1); // From the first entry's start to the last one's end.
            Assert.NotEmpty(group.GetVisualDescendants().OfType<IconGlyph>().Where(g => g.Name == "MergedMark"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheDaysFiguresShowWorkingTimeAllocatedUnallocatedAndBreaksOnTheTimelineCard()
    {
        var model = Model(); model.Engine.Sessions.Add(new("2026-10-21", "Account one", "09:00:00", "recorded", "60"));
        model.Engine.Sessions.Add(new("2026-10-21", BreakAccount.Name, "12:00:00", "recorded", "30"));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var totals = Find<StackPanel>(window, "DayTotals"); Assert.True(totals.IsEffectivelyVisible);
            string Text(string name) => Find<StackPanel>(window, name).GetVisualDescendants().OfType<TextBlock>().Last().Text!;
            Assert.Equal(model.Format(TimeSpan.FromMinutes(30)), Text("TotalsBreaks"));
            Assert.Equal(model.Format(TimeSpan.FromHours(1)), Text("TotalsAllocated"));
            Assert.True(Find<Border>(window, "TimelineCard").IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ADayWithNoScheduleKeepsTheBarAtTheSameWidthAndShowsTimeRecordedOnIt()
    {
        var model = Model(() => new DateTime(2026, 10, 10, 18, 0, 0)); // A Saturday, which is off by default.
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var bar = Find<Border>(window, "DayBar"); Assert.True(bar.IsVisible); Assert.Equal(150, bar.Width); Assert.Empty(window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "NoSchedule"));
            Assert.Contains("No schedule", ToolTip.GetTip(bar)!.ToString());
            // Time recorded on a day with no schedule is drawn as working time on the bar, at the same width, with no warning that nothing is scheduled.
            model.Engine.Sessions.Add(new SessionRow("2026-10-10", "Account one", "09:00:00", "added", "90")); model.ChangeDate(0); Settle(window);
            Assert.Equal(150, bar.Width); Assert.NotNull(bar.Child); Assert.True(bar.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "DayBarAllocated"));
            Assert.DoesNotContain("Nothing is scheduled", ToolTip.GetTip(bar)!.ToString());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheMessageFloatsBelowTheTimelineCardWithoutMakingAnyRowTaller()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var card = Find<Border>(window, "TimelineCard"); var before = card.Bounds.Height;
            model.ExplainRunning(); Settle(window);
            var bar = Find<Border>(window, "MessageBar"); Assert.True(bar.IsVisible);
            Assert.Equal(before, card.Bounds.Height, 0.5);
            Assert.True(bar.TranslatePoint(default, window)!.Value.Y > card.TranslatePoint(new Point(0, card.Bounds.Height), window)!.Value.Y - 1); // Over the page below the card, not the timeline.
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void TextOnABlockReadsAtBothEndsOfItsGradient(string themeName)
    {
        var theme = themeName == "Light" ? AppThemes.Light : AppThemes.Dark;
        foreach (var colour in new[] { "#91B7D5", "#E8A87C", "#85CDA1", "#C3A6E0", "#F2D16B", "#E58E8E", "#4F6D8F", "#2B2B2B" })
        {
            var look = ThemeDerivation.Block(theme, Ui.Visible(colour)); var ink = look.Text;
            Assert.True(ThemeColours.Contrast(ink, look.Fill) >= 4.5, $"{themeName} {colour} text on the start of the gradient");
            Assert.True(ThemeColours.Contrast(ink, ReviewViews.DeeperEnd(look)) >= 4.5, $"{themeName} {colour} text on the end of the gradient");
        }
    }
}
