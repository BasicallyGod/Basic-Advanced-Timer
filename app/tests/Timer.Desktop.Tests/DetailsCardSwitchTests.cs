using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

// Typing a note in the Details card for one block and then selecting another used to leave the typed words in the box (a box with the focus keeps what is typed, and a click on
// the timeline does not move the focus), and they were written onto the other block when the box was next left.
public sealed class DetailsCardSwitchTests
{
    private static void Settle(Window w) { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var f = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void SelectingAnotherBlockShowsItsOwnNoteAndKeepsWhatWasTypedForTheFirst()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 14, 14, 15, 7)); model.CompleteSetup();
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var rows = model.Engine.Sessions.Where(r => r.Duration > TimeSpan.Zero && r.Day == model.SelectedDate.Date).ToList();
            var first = rows.First(); var second = rows.First(r => r.Account != first.Account);
            model.SelectBlock(new[] { first }, false); Settle(window);
            var note = window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "SelectedNote");
            note.Focus(); Dispatcher.UIThread.RunJobs(); Assert.True(note.IsFocused);
            note.Text = "Press Office";
            model.SelectBlock(new[] { second }, false); Settle(window);
            var after = window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "SelectedNote");
            var shown = model.SelectedBlocks.Single();
            Assert.Equal(second.Account, shown.Account);
            Assert.NotEqual("Press Office", shown.Note); // The words were not carried onto the second block.
            Assert.Equal(shown.Note, after.Text ?? "");   // The box shows the block that is selected.
            model.SelectBlock(new[] { model.Engine.Sessions.First(r => r.Account == first.Account && r.From == first.From) }, false); Settle(window);
            Assert.Equal("Press Office", model.SelectedBlocks.Single().Note); // What was typed was kept for the first block.
        }
        finally { window.Close(); }
    }
}
