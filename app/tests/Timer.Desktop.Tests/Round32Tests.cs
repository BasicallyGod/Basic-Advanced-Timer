using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 32: a save that fails while only the pill is up is shown by a toast hung by the pill, not left unsaid.
public sealed class Round32Tests
{
    [AvaloniaFact]
    public void AFailedSaveShowsAToastOnThePillThatStaysUntilASaveWorksAndSaysNothingElse()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-pill-toast-" + Guid.NewGuid());
        var now = new DateTime(2026, 10, 9, 9, 0, 0);
        try
        {
            using var store = new TimerStore(folder);
            var engine = new TrackingEngine(clock: () => now); var prefs = store.Load(engine);
            var model = new TimerModel(engine, prefs, store); model.CompleteSetup();
            var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                Assert.False(pill.ToastOpen);
                model.ToggleAccount("Account one"); now = now.AddMinutes(5);
                model.Tell("1 block copied"); Dispatcher.UIThread.RunJobs();
                Assert.False(pill.ToastOpen); // A note is the dashboard's to show.
                StorageFaults.Block(store);
                Assert.False(model.StopForClose()); Dispatcher.UIThread.RunJobs();
                Assert.NotNull(model.LastError); Assert.True(pill.ToastOpen);
                var bar = pill.PillToast!; Assert.Equal(model.LastError, bar.Text); Assert.True(bar.IsWarning);
                // The cross puts it away until the failure changes.
                Find<Button>(bar, "DismissMessage").Command?.Execute(null);
                StorageFaults.Unblock(store); Assert.True(model.Save()); Dispatcher.UIThread.RunJobs();
                Assert.Null(model.LastError); Assert.False(pill.ToastOpen);
            }
            finally { pill.Close(); }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    private static T Find<T>(Avalonia.Controls.Control root, string name) where T : Avalonia.Controls.Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
}
