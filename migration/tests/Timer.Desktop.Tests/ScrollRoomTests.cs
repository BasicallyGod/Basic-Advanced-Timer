using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

// The slim scroll bar lies over the content, so every list that scrolls gives its rows room for it, as the bell's and the pill's lists do.
public sealed class ScrollRoomTests
{
    [AvaloniaFact]
    public void TheNotesCardsRowsLeaveRoomForTheScrollBarWhileTheListScrollsAndUseTheWholeWidthWhenItFits()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
        try
        {
            var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "NotesScroll");
            var content = (Control)scroll.Content!;
            Dispatcher.UIThread.RunJobs(); using var again = window.CaptureRenderedFrame();
            if (scroll.Extent.Height > scroll.Viewport.Height + .5) Assert.True(content.Margin.Right >= 14, $"right margin {content.Margin.Right} while the list scrolls");
            else Assert.Equal(0, content.Margin.Right);
        }
        finally { window.Close(); }
    }
}
