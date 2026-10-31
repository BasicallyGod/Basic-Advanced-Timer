using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 30: the pill's clock and the sidebar's write the seconds the same way, with the colon before them.
public sealed class Round30Tests
{
    private static readonly DateTime DemoNow = new(2026, 10, 14, 14, 15, 7);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void TheSecondsOnThePillAndInTheSidebarBothKeepTheirColon()
    {
        var model = TimerModel.CreateDemo(() => DemoNow); model.CompleteSetup();
        var dashboard = new DashboardWindow(model); var pill = new PillWindow(model); dashboard.Show(); pill.Show(); Settle(dashboard); Settle(pill);
        try
        {
            foreach (var host in new Visual[] { dashboard, pill })
            {
                var clock = host.GetVisualDescendants().OfType<StackPanel>().First(p => p is IClockText);
                var parts = clock.Children.OfType<TextBlock>().ToList();
                Assert.Equal(2, parts.Count);
                Assert.Matches(@"^\d\d:\d\d$", parts[0].Text); Assert.Matches(@"^:\d\d$", parts[1].Text); // 04:45 large, then :07 small.
                Assert.Equal(model.ClockText, ((IClockText)clock).Text);
            }
        }
        finally { dashboard.Close(); pill.Close(); }
    }
}
