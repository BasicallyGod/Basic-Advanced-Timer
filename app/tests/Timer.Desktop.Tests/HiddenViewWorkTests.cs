using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class HiddenViewWorkTests
{
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void HiddenNotesCardDefersItsListAndKeepsTheDraftWhenItsAncestorReturns()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup(); model.SetNotesCardShowsMeetings(false);
        var card = new NotesCard(model); var parent = new Border { Child = card };
        var window = new Window { Width = 600, Height = 600, Content = parent }; window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Find<Button>(card, "AddNote").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var field = Find<TextBox>(card, "NoteField"); field.Text = "Unsubmitted draft";
            var list = Find<StackPanel>(card, "NotesList"); var before = list.Children[0]; var count = list.Children.Count;
            parent.IsVisible = false;
            model.Notes.AddNote("Added while hidden", null, null);
            model.Tick();
            Assert.Same(before, list.Children[0]); Assert.Equal(count, list.Children.Count);
            parent.IsVisible = true;
            Assert.Equal(count + 1, list.Children.Count);
            Assert.Same(field, Find<TextBox>(card, "NoteField")); Assert.Equal("Unsubmitted draft", field.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NotesPageDefersHiddenAndMinimisedUpdatesAndRefreshesOnReturn()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        var page = new NotesPage(model); var window = new Window { Width = 1000, Height = 700, Content = page };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var list = (StackPanel)Find<ScrollViewer>(page, "NotesPageScroll").Content!;
            var before = list.Children[0];
            window.Hide(); model.Notes.AddNote("Hidden page addition", null, null);
            Assert.Same(before, list.Children[0]);
            window.Show(); Assert.NotSame(before, list.Children[0]);
            before = list.Children[0]; window.WindowState = WindowState.Minimized;
            model.Notes.AddNote("Minimised page addition", null, null);
            Assert.Same(before, list.Children[0]);
            window.WindowState = WindowState.Normal; Assert.NotSame(before, list.Children[0]);
            var id = model.Notes.All.Single(n => model.Notes.Text(n) == "Minimised page addition").Id;
            Assert.Contains(page.GetVisualDescendants().OfType<Control>(), c => c.Name == "PageRow_" + id);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TimelineClockCatchesUpImmediatelyAfterHideAndMinimise()
    {
        var now = new DateTime(2026, 11, 2, 14, 15, 0);
        var model = TimerModel.CreateDemo(() => now); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var chip = Find<Border>(window, "NowChip"); var text = (TextBlock)chip.Child!;
            Assert.Equal("14:15", text.Text);
            window.Hide(); now = now.AddHours(1); window.Show();
            Assert.Equal("15:15", text.Text);
            window.WindowState = WindowState.Minimized; now = now.AddHours(1); window.WindowState = WindowState.Normal;
            Assert.Equal("16:15", text.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FrameChipResubscribesWhenReattached()
    {
        var chip = new FrameChip(() => "Reattach test"); var window = new Window { Width = 300, Height = 100, Content = chip }; window.Show();
        try
        {
            FrameMonitor.Instance.Start(window); Assert.True(chip.IsVisible);
            FrameMonitor.Instance.Stop(); Assert.False(chip.IsVisible);
            window.Content = null; Dispatcher.UIThread.RunJobs();
            window.Content = chip; Dispatcher.UIThread.RunJobs();
            FrameMonitor.Instance.Start(window); Assert.True(chip.IsVisible);
            FrameMonitor.Instance.Stop(); Assert.False(chip.IsVisible);
        }
        finally { FrameMonitor.Instance.Stop(); window.Close(); }
    }
}
