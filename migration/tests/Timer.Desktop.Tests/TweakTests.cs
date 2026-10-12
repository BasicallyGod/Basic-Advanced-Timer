using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

// Small fixes from the first Store feedback: a smaller pill on small screens, clear space beside Merge, and messages that fade.
public sealed class TweakTests
{
    private static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); }

    [Fact]
    public void ThePillShrinksOnSmallScreensButNeverBelowTheLimitOrAboveFullSize()
    {
        Assert.Equal(1.0, PillWindow.ScaleFor(2560));
        Assert.Equal(1.0, PillWindow.ScaleFor(1800));
        Assert.InRange(PillWindow.ScaleFor(1366), 0.75, 0.77); // A common laptop screen.
        Assert.Equal(PillWindow.SmallestScale, PillWindow.ScaleFor(800));
    }

    [AvaloniaFact]
    public void AScaledPillKeepsItsProportionsAndGrowsBackOnABigScreen()
    {
        var pill = new PillWindow(TimerModel.CreateDemo()); pill.Show(); Settle(pill);
        var full = (pill.Width, pill.Height);
        Assert.Equal(1.0, pill.Scale);
        pill.FitToScreen(null); Assert.Equal(1.0, pill.Scale); // No screen to look at means full size.
        Assert.Equal(PillWindow.CollapsedWindowHeight, pill.Height, 0.5);
        Assert.Equal(PillWindow.PillWidth + 2 * Ui.ShadowMargin, full.Width, 0.5);
        pill.Close();
    }

    [AvaloniaFact]
    public void FooterNotesFadeAfterThirtySecondsButAFailedSaveDoesNot()
    {
        var model = TimerModel.CreateDemo();
        Assert.Equal(TimeSpan.FromSeconds(30), TimerModel.MessageLifetime);
        model.ExplainRunning();
        Assert.NotNull(model.EditError);
        model.ExpireMessages();
        Assert.Null(model.EditError); Assert.Null(model.Notice);
    }

    [AvaloniaFact]
    public void MergeAndTheZoomBarHaveRoomBetweenThem()
    {
        var model = TimerModel.CreateDemo(); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        var merge = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "MergeSelected");
        var zoom = dashboard.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineControls");
        var mergeRight = merge.TranslatePoint(new Point(merge.Bounds.Width, 0), dashboard)!.Value.X;
        var zoomLeft = zoom.TranslatePoint(new Point(0, 0), dashboard)!.Value.X;
        Assert.True(zoomLeft - mergeRight >= 24, $"Only {zoomLeft - mergeRight} px between Merge and the zoom bar.");
        dashboard.Close();
    }
}
