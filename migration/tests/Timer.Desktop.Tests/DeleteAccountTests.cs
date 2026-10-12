using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class DeleteAccountTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 15, 0, 0);
    // Account one has two entries, Account two has one, and Internal has none.
    private static TimerModel Model()
    {
        var engine = new TrackingEngine(clock: () => Now);
        engine.AddSession("Account one", Now.Date.AddHours(9), Now.Date.AddHours(10));
        engine.AddSession("Account one", Now.Date.AddHours(11), Now.Date.AddHours(11.5));
        engine.AddSession("Account two", Now.Date.AddHours(12), Now.Date.AddHours(13));
        return new TimerModel(engine);
    }
    // A few passes, because a new page's visuals appear only after a layout and render pass, and a slow Windows runner has been seen to need more than one.
    private static void Settle(Control control) { var window = (Window)TopLevel.GetTopLevel(control)!; for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static T Find<T>(Control w, string name) where T : Control => w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Press(Control w, string name) { Find<Button>(w, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [Fact]
    public void DeletingAnAccountRemovesItsHistoryAndNothingElse()
    {
        var model = Model();
        Assert.Equal((2, TimeSpan.FromHours(1.5)), model.AccountHistory("Account one"));
        model.SetAccounts(model.AccountSettings.Where(a => a.Name != "Account one"), null, new[] { "Account one" });
        Assert.Equal(new[] { "Account two", "Internal" }, model.AccountSettings.Select(a => a.Name));
        Assert.DoesNotContain(model.Engine.Sessions, s => s.Account == "Account one"); Assert.Single(model.Engine.Sessions); // Account two's hour stays.
        Assert.DoesNotContain(model.ReviewAccounts, a => a.Name == "Account one");
        Assert.False(model.Editor.CanUndo);
    }

    [Fact]
    public void AnAccountCannotBeDroppedFromTheListWithoutBeingDeletedOrBothKeptAndDeleted()
    {
        var model = Model();
        Assert.Throws<ArgumentException>(() => model.SetAccounts(model.AccountSettings.Where(a => a.Name != "Internal"))); // Hide instead.
        Assert.Throws<ArgumentException>(() => model.SetAccounts(model.AccountSettings, null, new[] { "Internal" })); // Kept and deleted.
        Assert.Throws<ArgumentException>(() => model.SetAccounts(model.AccountSettings, null, new[] { "Nobody" }));
        Assert.Equal(3, model.AccountSettings.Count);
    }

    [Fact]
    public void DeletingTheRunningAccountStopsItFirstAndTheNotesAndGroupsGoToo()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-delete-" + Guid.NewGuid());
        try
        {
            using var store = new TimerStore(folder);
            var engine = new TrackingEngine(clock: () => Now); var prefs = store.Load(engine);
            engine.AddSession("Account one", Now.Date.AddHours(9), Now.Date.AddHours(10)); engine.AddSession("Account two", Now.Date.AddHours(12), Now.Date.AddHours(13));
            var model = new TimerModel(engine, prefs, store);
            model.Editor.Edit(engine.Sessions[0], "Account one", Now.Date.AddHours(9), Now.Date.AddHours(10), "a note");
            model.ToggleAccount("Account one");
            Assert.Equal("Account one", model.ActiveAccount);
            model.SetAccounts(model.AccountSettings.Where(a => a.Name != "Account one"), null, new[] { "Account one" });
            Assert.Null(model.ActiveAccount); Assert.DoesNotContain("Account one", model.Editor.NotesCsv);
            Assert.Contains(Directory.GetDirectories(Path.Combine(folder, "Backups")), d => Path.GetFileName(d).StartsWith("before-delete")); // A copy was kept first.
            Assert.DoesNotContain(store.Load(new TrackingEngine()).Accounts, a => a.Name == "Account one"); // And it stays gone after a restart.
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public void AnUnusedAccountIsDeletedAtOnceAndOneWithTimeAsksFirst()
    {
        var model = Model(); var window = new AccountsDialog(model).Open(model);
        try
        {
            Settle(window);
            var bins = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("DeleteAccount") == true).ToList();
            Assert.Equal(3, bins.Count);
            // Internal has nothing recorded, so marking it and saving just deletes it.
            Find<Button>(window, "DeleteAccount2").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(.4, window.GetVisualDescendants().OfType<TextBox>().First(t => t.Text == "Internal").Opacity, 2); // The row dims.
            Press(window, "SaveAccounts");
            Assert.DoesNotContain(model.AccountSettings, a => a.Name == "Internal"); Assert.Equal(2, model.AccountSettings.Count);
        }
        finally { window.Close(); }
        var second = Model(); var again = new AccountsDialog(second).Open(second);
        try
        {
            Settle(again);
            Find<Button>(again, "DeleteAccount0").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Press(again, "SaveAccounts");
            var confirm = Find<StackPanel>(again, "DeleteConfirm"); Assert.True(confirm.IsVisible);
            Assert.Contains(confirm.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { } x && x.Contains("Account one") && x.Contains("2 recorded entries") && x.Contains("1h 30m"));
            Assert.Equal(3, second.AccountSettings.Count); // Nothing has gone yet.
            Press(again, "CancelDeleteAccounts"); Assert.False(confirm.IsVisible); Assert.Equal(3, second.AccountSettings.Count);
            Press(again, "SaveAccounts"); Press(again, "ConfirmDeleteAccounts");
            Assert.DoesNotContain(second.AccountSettings, a => a.Name == "Account one"); Assert.DoesNotContain(second.Engine.Sessions, s => s.Account == "Account one");
        }
        finally { again.Close(); }
    }

    [AvaloniaFact]
    public void TheMarkCanBeTakenOffNewRowsJustVanishAndTheLastAccountCannotGo()
    {
        var model = Model(); var window = new AccountsDialog(model).Open(model);
        try
        {
            Settle(window);
            Press(window, "DeleteAccount2"); Press(window, "DeleteAccount2"); // Marked, then kept after all.
            Press(window, "SaveAccounts");
            Assert.Equal(3, model.AccountSettings.Count);
        }
        finally { window.Close(); }
        var second = Model(); var again = new AccountsDialog(second).Open(second);
        try
        {
            Settle(again);
            var input = again.GetVisualDescendants().OfType<TextBox>().Single(t => t.Watermark == "New account name"); input.Text = "Temporary";
            foreach (var button in again.GetVisualDescendants().OfType<Button>().Where(b => (b.Content as string) == "Add account")) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); Settle(again);
            var rows = again.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("DeleteAccount") == true); Assert.Equal(4, rows);
            Press(again, "DeleteAccount3"); // The new row simply goes.
            Assert.Equal(3, again.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("DeleteAccount") == true));
            foreach (var name in new[] { "DeleteAccount0", "DeleteAccount1", "DeleteAccount2" }) Press(again, name);
            Press(again, "SaveAccounts");
            Assert.Equal(3, second.AccountSettings.Count); Assert.Contains(again.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Keep at least one account.");
        }
        finally { again.Close(); }
    }
}
