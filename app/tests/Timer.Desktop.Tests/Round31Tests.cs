using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 31: the day clock is a seven-segment display set into the card with the state LED to its left and the alert count to its right, on the pill and on the
// dashboard's sidebar, always the same width.
public sealed class Round31Tests
{
    private static readonly DateTime DemoNow = new(2026, 10, 14, 14, 15, 7);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Save(Window window, string name) { if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is not { } folder) return; Directory.CreateDirectory(folder); Settle(window); using var shot = window.CaptureRenderedFrame(); shot?.Save(Path.Combine(folder, name + ".png")); }
    private static TimerModel Demo(string theme)
    {
        var model = TimerModel.CreateDemo(() => DemoNow); model.CompleteSetup(); model.SetTheme(theme);
        model.SetAccounts(model.AccountSettings.Select(a => a.Name == "Account three" ? a with { BudgetHours = 1.75, BudgetPeriod = "week" } : a));
        return model;
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void WritePictures(string theme)
    {
        var model = Demo(theme); var pill = new PillWindow(model); pill.Show(); Save(pill, "segment-pill-" + theme); pill.Close();
        var dashboard = new DashboardWindow(model); dashboard.Show(); Save(dashboard, "segment-dashboard-" + theme); dashboard.Close();
    }

    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static double X(Visual v, Visual to) => v.TranslatePoint(default, to)!.Value.X;
    private static Avalonia.Media.Color Fill(Border b) => ((ISolidColorBrush)b.Background!).Color;

    [Fact]
    public void EachDigitLightsTheSegmentsItShouldAndAnyRunIsAsWideAsItsShapeAlone()
    {
        string Lit(char c) => string.Concat(Enumerable.Range(0, 7).Where(s => SegmentShapes.IsLit(c, s)).Select(s => (char)('a' + s)));
        Assert.Equal("abcdef", Lit('0')); Assert.Equal("bc", Lit('1')); Assert.Equal("abc", Lit('7')); Assert.Equal("abcdefg", Lit('8')); Assert.Equal("", Lit(' '));
        Assert.Equal(7, SegmentShapes.For(28).Segments.Count);
        // The width depends on how many digits and colons a run has, never on which, so the clock and the count never move.
        Assert.Equal(SegmentRun.WidthOf("00:00", 28), SegmentRun.WidthOf("88:88", 28), 1e-9);
        Assert.Equal(SegmentRun.WidthOf("00", 15), SegmentRun.WidthOf("  ", 15), 1e-9);
        Assert.True(SegmentClock.Width > 100 && SegmentClock.Width < 130);
    }

    [AvaloniaFact]
    public void ThePillsDisplayIsTheWellsHeightAndKeepsOneWidthWhateverTheTimeAndTheCount()
    {
        var model = Demo("Light"); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var surface = Find<Border>(pill, "PillSurface"); var display = Find<Border>(pill, "PillDisplay"); var well = Find<Border>(pill, "PillWell");
            Assert.Equal(StatusFace.DisplayHeight, display.Bounds.Height, 0.5); Assert.Equal(52, display.Bounds.Height, 0.5); Assert.Equal(14, display.CornerRadius.TopLeft);
            Assert.Equal(StatusFace.DisplayWidth, display.Bounds.Width, 1.0);
            Assert.Equal(PillWindow.InsetLeft, X(display, surface), 3.0); // The display starts at the card's left inset.
            Assert.Equal(12, X(well, surface) - (X(display, surface) + display.Bounds.Width), 1.5); // 12 between the display and the well, and the well and the break (G3).
            var width = display.Bounds.Width; var clockWidth = pill.GetVisualDescendants().OfType<SegmentClock>().Single().Bounds.Width; var countWidth = Find<SegmentRun>(pill, "PillAlertCount").Bounds.Width;
            model.ToggleAccount("Account one"); model.ToggleBreak(); model.Notes.AddNote("Send the budget", null, DemoNow.Date.AddHours(8)); Settle(pill);
            Assert.Equal(width, display.Bounds.Width, 0.5); Assert.Equal(clockWidth, pill.GetVisualDescendants().OfType<SegmentClock>().Single().Bounds.Width, 0.5); Assert.Equal(countWidth, Find<SegmentRun>(pill, "PillAlertCount").Bounds.Width, 0.5);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheCountIsAlwaysTwoDigitCellsAndTakesTheMostUrgentAlertsColour()
    {
        var model = Demo("Dark"); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var count = Find<SegmentRun>(pill, "PillAlertCount");
            Assert.Equal(2, count.Text.Length); Assert.Equal(model.Alerts.Count, int.Parse(count.Text.Trim()));
            Assert.Equal(15, count.DigitHeight);
            var token = model.Alerts[0].LightToken; Assert.True(pill.TryFindResource(token, pill.ActualThemeVariant, out var found)); Assert.Equal(((ISolidColorBrush)found!).Color, count.Colour);
            // Off shows no count at all, and the display keeps its width.
            var width = Find<Border>(pill, "PillDisplay").Bounds.Width; model.SetPillAlertsMode("off"); Settle(pill);
            Assert.Equal("  ", count.Text); Assert.Equal(width, Find<Border>(pill, "PillDisplay").Bounds.Width, 0.5);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheStateLedIsAnEmptyGrooveWhenIdleAndInlaidInTheStateColourOtherwise()
    {
        var model = Demo("Light"); if (model.IsTracking) model.ToggleAccount(model.ActiveAccount!); if (model.IsOnBreak) model.ToggleBreak();
        var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var led = Find<Border>(pill, "PillStateLight"); Assert.Equal(new Size(6, 14), led.Bounds.Size); Assert.Equal(3.5, led.CornerRadius.TopLeft);
            var idle = Fill(led); pill.TryFindResource("DisplayGroove", pill.ActualThemeVariant, out var groove); Assert.Equal(((ISolidColorBrush)groove!).Color, idle);
            model.ToggleAccount("Account one"); Settle(pill); var live = Fill(led); Assert.NotEqual(idle, live);
            model.ToggleBreak(); Settle(pill); var onBreak = Fill(led); Assert.NotEqual(live, onBreak); Assert.NotEqual(idle, onBreak);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheSidebarsDisplayHoldsTheSameParts_FitsItsColumnAndStaysTheSameWidth()
    {
        var model = Demo("Light"); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var sidebar = Find<Border>(window, "Sidebar"); var display = Find<Border>(window, "SidebarDisplay");
            Assert.Equal(52, display.Bounds.Height, 0.5); Assert.True(display.Bounds.Width <= sidebar.Bounds.Width - 24, "the display fits the column with room at each side");
            Assert.NotNull(Find<Border>(window, "SidebarStateLight")); Assert.Equal(2, Find<SegmentRun>(window, "SidebarAlertCount").Text.Length);
            var width = display.Bounds.Width; model.ToggleAccount("Account one"); Settle(window); Assert.Equal(width, display.Bounds.Width, 0.5);
        }
        finally { window.Close(); }
    }
}
