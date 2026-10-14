using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class DeskTests
{
    private static TimerModel Model(bool desk = true, params string[] order)
        => new(new TrackingEngine(), new StoreSettings(new() { new("Account one", null, null), new("Account two", null, null) }, false, "Light", DayOfWeek.Monday, Desk: desk, DeskOrder: order));

    private static DashboardWindow Open(TimerModel model, int width = 1080, int height = 680)
    {
        var window = new DashboardWindow(model); window.Width = width + 2 * Ui.ShadowMargin; window.Height = height + 2 * Ui.ShadowMargin; window.Show();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); return window;
    }
    private static T Named<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Press(Control root, string name) { Named<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void TheDeskTabAppearsOnlyWhenThePreviewIsOn()
    {
        var off = Open(Model(false)); var on = Open(Model());
        try { Assert.False(Named<Button>(off, "DeskTab").IsVisible); Assert.True(Named<Button>(on, "DeskTab").IsVisible); }
        finally { off.Close(); on.Close(); }
    }

    [AvaloniaFact]
    public void TheDeskShowsItsTilesInTheSavedOrderAndHidesTheAccountsColumn()
    {
        var window = Open(Model(true, "day", "working", "accounts"));
        try
        {
            Press(window, "DeskTab"); window.UpdateLayout();
            var desk = window.GetVisualDescendants().OfType<DeskView>().Single();
            Assert.Equal(new[] { "day", "working", "accounts" }, desk.Order.Take(3)); // Tiles added since the order was saved follow it.
            Assert.Equal(DeskView.Kinds.Count, desk.Placements.Count); Assert.Equal("day", desk.Placements[0].Id);
            Assert.False(Named<Control>(window, "SidebarPanel").IsVisible);
            Assert.Equal(TileForm.Normal, desk.Placements.Single(p => p.Id == "working").Form);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DroppingATileMovesItAndTheOrderIsKept()
    {
        var model = Model(); var window = Open(model);
        try
        {
            Press(window, "DeskTab");
            var desk = window.GetVisualDescendants().OfType<DeskView>().Single();
            desk.Drop("accounts", 0); window.UpdateLayout();
            Assert.Equal("accounts", desk.Order[0]);
            Assert.Equal("accounts", model.DeskOrder[0]);
            Assert.Equal(0, desk.Placements.Single(p => p.Id == "accounts").Row);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ACalmDeskMovesTilesStraightToTheirPlaces()
    {
        var window = Open(Model());
        try
        {
            Press(window, "DeskTab");
            var desk = window.GetVisualDescendants().OfType<DeskView>().Single(); desk.Lively = false;
            desk.Drop("accounts", 0); window.UpdateLayout();
            var accounts = (Control)desk.TileControl("accounts")!;
            Assert.Equal(DeskView.Gap / 2, accounts.Bounds.X, 1); Assert.Equal(DeskView.Gap / 2, accounts.Bounds.Y, 1);
        }
        finally { window.Close(); }
    }

    private static DeskView OpenDesk(DashboardWindow window) { Press(window, "DeskTab"); window.UpdateLayout(); return window.GetVisualDescendants().OfType<DeskView>().Single(); }

    [AvaloniaFact]
    public void AToDoWithATagLinksTheAccountAndTicksOffAndSurvivesTheDeskBeingKept()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = OpenDesk(window);
            var input = Named<TextBox>(window, "TodoInput"); input.Text = "Send invoice #AccountOne";
            input.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter }); Dispatcher.UIThread.RunJobs();
            Assert.Same(desk, window.GetVisualDescendants().OfType<DeskView>().Single()); // The desk is not rebuilt, so the box keeps its focus.
            var item = Assert.Single(model.Trinkets.Todos); Assert.Equal("Send invoice", item.Text); Assert.Equal("Account one", item.Account);
            Assert.Equal("", input.Text); Assert.NotNull(Named<Control>(window, "TodoTag_0"));
            Named<CheckBox>(window, "TodoTick_0").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.True(model.Trinkets.Todos[0].Done);
            Press(window, "TodoRemove_0"); Assert.Empty(model.Trinkets.Todos);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void APostItKeepsWhatIsTypedAndCyclesItsColour()
    {
        var model = Model(); var window = Open(model);
        try
        {
            OpenDesk(window);
            Named<TextBox>(window, "PostItText").Text = "Ring the printer people";
            Assert.Equal("Ring the printer people", model.Trinkets.PostIts[0].Text);
            Press(window, "PostItColour"); Assert.Equal("lilac", model.Trinkets.PostIts[0].Colour);
            Press(window, "PostItColour"); Press(window, "PostItColour"); Assert.Equal("lemon", model.Trinkets.PostIts[0].Colour);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheClockTileDrawsAnAnalogueFace()
    {
        var window = Open(Model());
        try { OpenDesk(window); Assert.True(Named<AnalogueClock>(window, "AnalogueClock").Bounds.Width > 40); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheDayStripShowsTheDaysBlocksAndCoversTheWorkingHours()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); model.SetDesk(true); var window = Open(model);
        try
        {
            OpenDesk(window); var strip = Named<DayStrip>(window, "DayStrip");
            var blocks = strip.Blocks(); Assert.NotEmpty(blocks); Assert.All(blocks, b => Assert.True(b.To >= b.From));
            var (from, to) = strip.Window(blocks); Assert.True(from <= 8 && to >= 18); Assert.True(strip.Bounds.Width > 100);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ANoteWithATimeAppearsInComingUpAndEditsAreKept()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = OpenDesk(window); var today = model.Engine.Now.Date;
            var input = Named<TextBox>(window, "NoteInput"); input.Text = "Ring the client 23:30";
            input.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter }); Dispatcher.UIThread.RunJobs();
            Assert.Same(desk, window.GetVisualDescendants().OfType<DeskView>().Single());
            var note = Assert.Single(model.NotesOn(today)); Assert.Equal("Ring the client", note.Text); Assert.Equal(new TimeSpan(23, 30, 0), note.At);
            Assert.Contains(model.ComingUp(), n => n.Id == note.Id);
            Assert.Contains("Today 23:30", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Named<TextBox>(window, "NoteText_" + note.Id).Text = "Ring the client back";
            Assert.Equal("Ring the client back", model.NotesOn(today)[0].Text);
            Named<CheckBox>(window, "ComingTick_" + note.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Empty(model.ComingUp()); Assert.True(model.NotesOn(today)[0].Done);
            Press(window, "NoteRemove_" + note.Id); Assert.Empty(model.NotesOn(today));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RenamingAnAccountKeepsItsToDosLinked()
    {
        var model = Model(); model.AddTodo("Call #AccountTwo");
        Assert.Equal("Account two", model.Trinkets.Todos[0].Account);
        model.SetAccounts(model.Accounts.Where(a => !BreakAccount.Is(a.Name)).Select(a => a.Name == "Account two" ? new AccountSettings("Beta", null, "week") : new AccountSettings(a.Name, null, "week")).ToList(), new Dictionary<string, string> { ["Account two"] = "Beta" });
        Assert.Equal("Beta", model.Trinkets.Todos[0].Account);
    }

    [AvaloniaFact]
    public void ATilePutAwayLeavesTheDeskAndSitsInTheDrawerUntilBroughtBack()
    {
        var model = Model(); var window = Open(model);
        try
        {
            Press(window, "DeskTab");
            var desk = window.GetVisualDescendants().OfType<DeskView>().Single(); desk.Lively = false;
            Press(window, "TileHide_day"); window.UpdateLayout();
            Assert.Equal(new[] { "day" }, desk.Hidden); Assert.DoesNotContain(desk.Placements, p => p.Id == "day"); Assert.False(desk.TileControl("day")!.IsVisible);
            Assert.Equal(new[] { "day" }, model.DeskHidden); Assert.DoesNotContain("day", model.DeskOrder);
            Press(window, "DrawerToggle"); Assert.True(Named<Border>(window, "Drawer").IsVisible);
            var chip = Named<Border>(window, "DrawerChip_day"); Assert.NotNull(chip);
            desk.Show("day", 0); window.UpdateLayout();
            Assert.Empty(desk.Hidden); Assert.Equal("day", desk.Order[0]); Assert.True(desk.TileControl("day")!.IsVisible); Assert.Equal(desk.Order.Count, desk.Placements.Count);
            Assert.Empty(model.DeskHidden);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ASavedDrawerIsRestoredWhenTheDeskOpens()
    {
        var window = Open(new TimerModel(new TrackingEngine(), new StoreSettings(new() { new("A", null, null) }, false, "Light", DayOfWeek.Monday, Desk: true, DeskOrder: new[] { "working", "accounts" }, DeskHidden: new[] { "day" })));
        try
        {
            Press(window, "DeskTab");
            var desk = window.GetVisualDescendants().OfType<DeskView>().Single();
            Assert.Equal(new[] { "day" }, desk.Hidden); Assert.Equal(DeskView.Kinds.Count - 1, desk.Placements.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ATileOpensItsFullPage()
    {
        var window = Open(Model());
        try
        {
            Press(window, "DeskTab");
            Press(window, "TileOpen_day");
            Assert.Empty(window.GetVisualDescendants().OfType<DeskView>());
            Assert.True(Named<Button>(window, "WeekOverviewTab").Classes.Contains("selected"));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void TheDeskSwitchAndOrderSurviveSavingAndLoading()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-desk-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var prefs = store.Load(engine); Assert.False(prefs.Desk); store.Save(engine, prefs with { Desk = true, Calm = true, DeskOrder = new[] { "day", "accounts" }, DeskHidden = new[] { "working" } }); }
            using (var store = new TimerStore(folder)) { var prefs = store.Load(new TrackingEngine()); Assert.True(prefs.Desk); Assert.True(prefs.Calm); Assert.Equal(new[] { "working" }, prefs.DeskHidden); Assert.Equal(new[] { "day", "accounts" }, prefs.DeskOrder); }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    // Pictures of the desk for looking at when TIMER_PREVIEW_DIR is set.
    [AvaloniaTheory]
    [InlineData(640, 480)]
    [InlineData(1080, 680)]
    [InlineData(1500, 900)]
    public void TheDeskDrawsAtEachSize(int width, int height)
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); model.SetDesk(true);
        model.AddTodo("Send the invoice #AccountOne"); model.AddTodo("Call back about the budget #AccountThree"); model.AddTodo("Order more tea"); model.ToggleTodo(2);
        model.SetPostItText(0, "Ask about the printer. Book the room for Friday.");
        model.AddDayNote(model.Engine.Now.Date, "Team stand-up 09:30"); model.AddDayNote(model.Engine.Now.Date, "Send timesheet"); model.AddDayNote(model.Engine.Now.Date.AddDays(1), "Client review 11:00"); model.AddDayNote(model.Engine.Now.Date.AddDays(3), "Quarter end");
        var window = Open(model, width, height);
        try
        {
            Press(window, "DeskTab"); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var shown = window.GetVisualDescendants().OfType<DeskView>().Single(); Assert.Empty(shown.Hidden);
            Assert.DoesNotContain("selected", Named<Button>(window, "TimelineTab").Classes); Assert.Contains("selected", Named<Button>(window, "DeskTab").Classes);
            if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is { } folder)
            {
                Directory.CreateDirectory(folder); using (window.CaptureRenderedFrame()) { } // The first capture brings the picture up to date.
                using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, $"desk-{width}x{height}.png"));
            }
        }
        finally { window.Close(); }
    }
}
