using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
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

    [Fact]
    public void AMonthExportCoversEveryDayOfTheMonthAndTheHundredthsStillAddUpExactly()
    {
        var engine = new TrackingEngine(clock: () => new DateTime(2026, 10, 31, 18, 0, 0));
        // Three odd lengths on different weeks, so rounding has to be shared out across the whole month.
        engine.Sessions.Add(new("2026-10-01", "Account one", "09:00:00", "added", "20"));
        engine.Sessions.Add(new("2026-10-14", "Account one", "09:00:00", "added", "20"));
        engine.Sessions.Add(new("2026-10-30", "Account two", "09:00:00", "added", "20"));
        engine.Sessions.Add(new("2026-09-30", "Account one", "09:00:00", "added", "60")); // Last month, left out.
        var export = WeekExport.ForMonth(new LedgerEditor(engine), engine, new DateTime(2026, 10, 17));
        Assert.True(export.IsMonth);
        Assert.Equal((new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)), export.Period);
        Assert.Equal(100, export.TotalUnits); // One hour in all, shared as 33, 33 and 34 hundredths.
        Assert.Equal(100, export.Accounts.Values.Sum(days => days.Sum()));
        var summary = export.SummaryCsv(new[] { "Account one", "Account two" });
        Assert.Contains("Thu 01/10", summary); Assert.Contains("Sat 31/10", summary); Assert.DoesNotContain("Wed 30/09", summary);
        Assert.Equal(3, export.Entries.Count);
        var weekExport = new WeekExport(new LedgerEditor(engine), engine, new DateTime(2026, 10, 12));
        Assert.False(weekExport.IsMonth); Assert.Equal(33, weekExport.TotalUnits); // A week is unchanged.
    }

    [AvaloniaFact]
    public void TheExportWindowOffersAWeekOrAWholeMonthAndWritesTheMonth()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0));
        var window = new ExportWindow(model); window.Show(); Settle(window);
        var week = window.GetVisualDescendants().OfType<RadioButton>().Single(b => b.Name == "ExportWeekChoice");
        var month = window.GetVisualDescendants().OfType<RadioButton>().Single(b => b.Name == "ExportMonthChoice");
        Assert.True(week.IsChecked); Assert.Contains("October 2026", (string)month.Content!);
        month.IsChecked = true;
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportCsv").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.NotNull(model.ExportPath); Assert.Contains("Month-202610", model.ExportPath);
        Assert.True(File.Exists(Path.Combine(model.ExportPath!, "Detail.csv")) && File.Exists(Path.Combine(model.ExportPath!, "Summary.csv")));
        window.Close();
    }
}
