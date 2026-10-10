using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class AccountEditingTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static SessionRow Row(string start, string minutes, string end = "added", string account = "Account one") => new("2026-10-09", account, start, end, minutes);
    private static TrackingEngine Engine(Func<DateTime>? clock = null) => new(clock: clock ?? (() => Day.AddHours(15)));

    [Fact]
    public void RenamingMovesEntriesNotesAndGroupsAndStartsFreshHistory()
    {
        var engine = Engine();
        engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:30:00", "30"), Row("11:00:00", "30", account: "Account two") });
        var editor = new LedgerEditor(engine);
        editor.Edit(engine.Sessions[0], "Account one", Day.AddHours(9), Day.AddHours(9.5), "first note");
        editor.Group(new[] { engine.Sessions[0], engine.Sessions[1] });
        Assert.True(editor.CanUndo);
        var total = engine.Sessions.Count;
        Assert.Equal(2, editor.RenameAccount("Account one", "Client A"));
        Assert.DoesNotContain(engine.Sessions, s => s.Account == "Account one");
        Assert.Equal(2, engine.Sessions.Count(s => s.Account == "Client A")); Assert.Equal(total, engine.Sessions.Count);
        Assert.Equal("first note", editor.Note(engine.Sessions[0]));
        Assert.DoesNotContain("Account one", editor.NotesCsv + editor.GroupsCsv);
        Assert.Contains("Client A", editor.GroupsCsv);
        Assert.Equal("Account two", engine.Sessions[2].Account);
        Assert.False(editor.CanUndo); Assert.Equal(0, editor.UndoCount); // Old steps would restore the old name.
        // The rewritten ledger still edits normally.
        editor.Edit(engine.Sessions[0], "Client A", Day.AddHours(9), Day.AddHours(9.5), "again"); Assert.True(editor.CanUndo);
    }

    [Fact]
    public void RenamingRefusesNamesInTheHistoryAndTheActiveAccount()
    {
        var now = Day.AddHours(10);
        var engine = Engine(() => now); engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("11:00:00", "30", account: "Old client") });
        var editor = new LedgerEditor(engine);
        Assert.Throws<ArgumentException>(() => editor.RenameAccount("Account one", "old CLIENT"));
        Assert.Throws<ArgumentException>(() => editor.RenameAccount("Account one", " padded "));
        Assert.Equal(0, editor.RenameAccount("Never used", "Anything")); Assert.False(editor.Changed);
        editor.RenameAccount("Account one", "ACCOUNT ONE"); Assert.Equal("ACCOUNT ONE", engine.Sessions[0].Account); // Capitals only.
        engine.StartDay(); engine.ToggleAccount("ACCOUNT ONE");
        Assert.Throws<InvalidOperationException>(() => editor.RenameAccount("ACCOUNT ONE", "Elsewhere"));
    }

    [AvaloniaFact]
    public void RenamingAlwaysMovesTheHistoryKeepsTheColourAndSurvivesRestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-rename-" + Guid.NewGuid());
        var now = Day.AddHours(12); string colour;
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => now);
                var model = new TimerModel(engine, store.Load(engine), store);
                colour = model.AccountSettings[0].Colour!;
                model.ToggleAccount("Account one"); now = now.AddMinutes(20);
                Assert.Throws<ArgumentException>(() => model.SetAccounts(model.AccountSettings.Select(a => a.Name == "Account one" ? a with { Name = "internal" } : a),
                    new Dictionary<string, string> { ["Account one"] = "internal" }));
                model.SetAccounts(model.AccountSettings.Select(a => a.Name == "Account one" ? a with { Name = "Client A", BudgetHours = 5 } : a),
                    new Dictionary<string, string> { ["Account one"] = "Client A" });
                Assert.False(model.IsTracking); // The renamed account was running, so it was stopped and saved first.
                Assert.Equal("Client A", model.Accounts[0].Name);
                Assert.Equal(TimeSpan.FromMinutes(20), model.Today(model.Accounts[0]));
                Assert.Single(Directory.GetDirectories(Path.Combine(folder, "Backups"), "before-rename-*"));
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(clock: () => now); var prefs = store.Load(engine);
                Assert.Equal("Client A", prefs.Accounts[0].Name); Assert.Equal(colour, prefs.Accounts[0].Colour);
                var model = new TimerModel(engine, prefs, store);
                Assert.Equal(20, model.Today(model.Accounts[0]).TotalMinutes);
                Assert.DoesNotContain(model.ReviewAccounts, a => a.Name == "Account one"); // No old name is left behind in the history.
                var backup = Directory.GetDirectories(Path.Combine(folder, "Backups"), "before-rename-*").Single();
                Assert.True(File.Exists(Path.Combine(backup, "timer.db")));
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void MoveAccountReordersVisibleAccountsAndLeavesHiddenOnesWhereTheyWere()
    {
        var model = new TimerModel(Engine(), new StoreSettings(new() { new("A"), new("B", Hidden: true), new("C"), new("D") }, false, "System", DayOfWeek.Monday));
        model.MoveAccount("D", 0);
        Assert.Equal(new[] { "D", "B", "A", "C" }, model.AccountSettings.Select(a => a.Name));
        Assert.Equal(new[] { "D", "A", "C", "Break" }, model.Accounts.Select(a => a.Name));
        model.MoveAccount("D", 99); Assert.Equal(new[] { "A", "B", "C", "D" }, model.AccountSettings.Select(a => a.Name));
        model.MoveAccount("B", 0); Assert.Equal(new[] { "A", "B", "C", "D" }, model.AccountSettings.Select(a => a.Name)); // Hidden: ignored.
    }

    [AvaloniaFact]
    public void DraggingARowOnTheDashboardReordersWhileAPlainClickStillTracks()
    {
        var model = new TimerModel(Engine());
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            Button Row(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            var first = Row("Account0"); var height = first.Bounds.Height;
            var from = first.TranslatePoint(new Point(100, height / 2), window)!.Value;
            window.MouseDown(from, MouseButton.Left); window.MouseMove(from + new Vector(0, height * 1.1)); window.MouseUp(from + new Vector(0, height * 1.1), MouseButton.Left);
            Assert.Equal(new[] { "Account two", "Account one", "Internal" }, model.AccountSettings.Select(a => a.Name));
            Assert.False(model.IsTracking); // The drag did not count as a click.
            Dispatcher.UIThread.RunJobs();
            var again = Row("Account0"); var click = again.TranslatePoint(new Point(100, height / 2), window)!.Value;
            window.MouseDown(click, MouseButton.Left); window.MouseUp(click, MouseButton.Left);
            Assert.Equal("Account two", model.ActiveAccount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheRecordingBlockIsMarkedAndExplainsWhyItCannotBeMoved()
    {
        var now = Day.AddHours(10);
        var engine = Engine(() => now); var model = new TimerModel(engine);
        model.ToggleAccount("Account one"); now = now.AddMinutes(45);
        var window = new Window { Width = 600, Height = 500, Content = ReviewViews.Timeline(model) }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs();
            var card = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TimelineRecording");
            Assert.Contains("recording", card.Classes);
            Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "● Recording now");
            var point = card.TranslatePoint(new Point(80, 30), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(0, 40)); window.MouseUp(point + new Vector(0, 40), MouseButton.Left);
            Assert.Contains("still recording", model.EditError);
            Assert.Equal(TimeSpan.FromMinutes(45), engine.Active is { } a ? now - a.Start : TimeSpan.Zero); // Nothing moved.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ADroppedRowGlidesIntoItsNewPlaceAndPassingRowsSlideAside()
    {
        var model = new TimerModel(Engine());
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
            Button Row(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            var first = Row("Account0"); var height = first.Bounds.Height; var second = Row("Account1");
            var from = first.TranslatePoint(new Point(100, height / 2), window)!.Value; var to = from + new Vector(0, height * 1.4); // Released a little past the middle of its new slot.
            window.MouseDown(from, MouseButton.Left); window.MouseMove(from + new Vector(0, height * .6)); window.MouseMove(to);
            Assert.NotNull(second.Transitions); Assert.NotEmpty(second.Transitions!); // The row being passed slides, rather than jumps.
            Assert.Null(first.Transitions); // The dragged row follows the pointer exactly.
            window.MouseUp(to, MouseButton.Left);
            Assert.Equal("Account two", model.AccountSettings[0].Name);
            // The list was rebuilt in its new order, and the row that moved starts where the pointer left it, not in its slot.
            var moved = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Account1");
            // The glide begins on a background callback, which a slow machine can run before this line. So the row is either still
            // away from its slot, or already gliding home (its transition is set). Either way it did not snap to its slot.
            var start = moved.RenderTransform as Avalonia.Media.Transformation.TransformOperations;
            var away = start is not null && Math.Abs(start.Value.M32) > 1; // How far depends on the drawing scale, so only "not at rest" is checked.
            Assert.True(away || moved.Transitions is { Count: > 0 }, $"the moved row should start away from its slot or already be gliding, not at {start?.Value.M32}");
            Dispatcher.UIThread.RunJobs();
            Assert.True(moved.Transitions is { Count: > 0 } || moved.RenderTransform is null); // Then it glides to rest (and is cleared once there).
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AccountsKeepTheirColourWhenReorderedHiddenRenamedOrOthersAreAdded()
    {
        var model = new TimerModel(Engine(), new StoreSettings(new() { new("A"), new("B"), new("C"), new("D") }, false, "System", DayOfWeek.Monday));
        string Colour(string name) => model.ReviewAccounts.Single(a => a.Name == name).Colour;
        var first = new[] { "A", "B", "C", "D" }.ToDictionary(n => n, Colour);
        Assert.Equal(TimerModel.Palette.Take(4), first.Values); // New accounts take the first colours nobody is using, in order.
        model.MoveAccount("D", 0); model.MoveAccount("C", 1);
        Assert.All(first, p => Assert.Equal(p.Value, Colour(p.Key))); // Reordering changes nothing about colour.
        model.SetAccounts(model.AccountSettings.Select(a => a.Name == "B" ? a with { Hidden = true } : a));
        Assert.Equal(first["B"], Colour("B")); Assert.Equal(first["A"], Colour("A"));
        model.SetAccounts(model.AccountSettings.Select(a => a.Name == "A" ? a with { Name = "Alpha" } : a), new Dictionary<string, string> { ["A"] = "Alpha" });
        Assert.Equal(first["A"], Colour("Alpha")); // A renamed account keeps its colour.
        model.SetAccounts(model.AccountSettings.Append(new("E")));
        Assert.Equal(TimerModel.Palette[4], Colour("E")); Assert.DoesNotContain(first.Values, v => v == Colour("E")); // A new one gets a colour not already used.
        Assert.All(first.Keys.Where(k => k != "A"), n => Assert.Equal(first[n], Colour(n)));
        for (var i = 0; i < 6; i++) model.SetAccounts(model.AccountSettings.Append(new("X" + i)));
        Assert.Equal(11, model.AccountSettings.Count); // Past eight accounts the colours have to repeat, but never change for the ones already there.
        Assert.Equal(first["D"], Colour("D"));
    }

    [Fact]
    public void ColoursOrderHidingAndBudgetsSurviveARestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-colours-" + Guid.NewGuid());
        try
        {
            string[] before;
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var prefs = store.Load(engine);
                Assert.All(prefs.Accounts, a => Assert.Null(a.Colour)); // A new database has none yet.
                var model = new TimerModel(engine, prefs, store);
                before = model.AccountSettings.Select(a => a.Colour!).ToArray();
                Assert.Equal(TimerModel.Palette.Take(3), before); // Given in order, once.
                model.SetAccounts(model.AccountSettings.Select(a => a.Name == "Account two" ? a with { Hidden = true, BudgetHours = 3, BudgetPeriod = "quarter" } : a));
                model.MoveAccount("Internal", 0); model.Save();
            }
            using (var store = new TimerStore(folder))
            {
                var prefs = store.Load(new TrackingEngine());
                Assert.Equal(new[] { "Internal", "Account two", "Account one" }, prefs.Accounts.Select(a => a.Name)); // The hidden account keeps its slot.
                Assert.Equal(new[] { before[2], before[1], before[0] }, prefs.Accounts.Select(a => a.Colour));
                Assert.True(prefs.Accounts[1].Hidden); Assert.Equal(3, prefs.Accounts[1].BudgetHours); Assert.Equal("quarter", prefs.Accounts[1].BudgetPeriod);
            }
        }
        finally { Directory.Delete(folder, true); }
    }
}
