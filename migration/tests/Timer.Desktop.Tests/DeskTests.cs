using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
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
    public void TheRailBecomesATopBarWhenTheDeskIsOn()
    {
        var off = Open(Model(false)); var on = Open(Model());
        try
        {
            Point At(Window w, string name) => Named<Button>(w, name).TranslatePoint(new Point(), w)!.Value;
            // A rail stacks its buttons down the side; the bar lays the same buttons along the top.
            Assert.True(Math.Abs(At(off, "TimelineTab").X - At(off, "WeekOverviewTab").X) < 1 && At(off, "WeekOverviewTab").Y > At(off, "TimelineTab").Y + 20);
            Assert.True(Math.Abs(At(on, "TimelineTab").Y - At(on, "WeekOverviewTab").Y) < 1 && At(on, "WeekOverviewTab").X > At(on, "TimelineTab").X + 20);
            Assert.True(At(on, "QuitTimer").X > At(on, "Settings").X); Assert.True(Math.Abs(At(on, "QuitTimer").Y - At(on, "Settings").Y) < 1);
            Assert.True(At(on, "DeskTab").X < At(on, "TimelineTab").X);
        }
        finally { off.Close(); on.Close(); }
    }

    [AvaloniaFact]
    public void TheDateArrowsLiveInTheBarAndMoveEveryDayTile()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = OpenDesk(window);
            Point At(string name) => Named<Control>(window, name).TranslatePoint(new Point(), window)!.Value;
            Assert.True(Math.Abs(At("NextPeriod").Y - At("TimelineTab").Y) < 12); // On the bar's line, beside the view buttons.
            Assert.True(At("PreviousPeriod").X > At("WeekEntriesTab").X);
            var today = model.SelectedDate; Press(window, "PreviousPeriod");
            Assert.Equal(today.AddDays(-1), model.SelectedDate); Assert.Same(desk, window.GetVisualDescendants().OfType<DeskView>().Single());
            Assert.Contains(today.AddDays(-1).ToString("dddd d MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Press(window, "GoToToday"); Assert.Equal(today, model.SelectedDate);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheTrackerTilePausesAndTakesABreak()
    {
        var model = Model(); var window = Open(model);
        try
        {
            OpenDesk(window); var account = model.Accounts.First(a => !BreakAccount.Is(a.Name)).Name;
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Paused");
            model.ToggleAccount(account); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Tracking");
            Press(window, "TrackerPause"); Assert.False(model.IsTracking); Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Paused");
            Press(window, "TrackerPause"); Assert.True(model.IsTracking);
            Press(window, "TrackerBreak"); Assert.True(model.IsOnBreak); Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "On break");
            Press(window, "TrackerBreak"); Assert.False(model.IsOnBreak);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheWeekTileChartsTheWorkingDaysAndTotalsTheWeek()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); model.SetDesk(true); var window = Open(model);
        try
        {
            OpenDesk(window); var chart = Named<WeekChart>(window, "WeekChart"); var days = chart.Days();
            Assert.InRange(days.Count, 5, 7); Assert.Contains(days, d => d.Day == model.SelectedDate.Date && d.Allocated > TimeSpan.Zero);
            Assert.Equal(model.Format(chart.Total()), Named<TextBlock>(window, "TileMeta_week").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ADeadlineNoteStartsTheCountdownAndTickingItOffClearsIt()
    {
        var model = Model(); var window = Open(model);
        try
        {
            OpenDesk(window); Assert.Equal("--:--", Named<TextBlock>(window, "DeadlineLeft").Text);
            var due = model.Engine.Now.Date.AddDays(1); // Tomorrow, at five.
            model.AddDayNote(due, "!Timesheet due"); window.UpdateLayout();
            var expected = due.AddHours(17) - model.Engine.Now; var left = Named<TextBlock>(window, "DeadlineLeft").Text;
            Assert.Equal($"{(int)expected.TotalHours:00}:{expected.Minutes:00}", left);
            Assert.Equal("Timesheet due", Named<TextBlock>(window, "DeadlineWhat").Text); Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Due in");
            model.ToggleDayNote(model.AllDayNotes[0].Id); window.UpdateLayout(); Assert.Equal("--:--", Named<TextBlock>(window, "DeadlineLeft").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EveryTilesHeaderContentIsCentredInItsBand()
    {
        var window = Open(Model()); 
        try
        {
            var desk = OpenDesk(window); desk.Lively = false;
            foreach (var placement in desk.Placements.Where(p => !p.Id.StartsWith("postit"))) // Post-its are informal: no header band.
            {
                var tile = (Control)desk.TileControl(placement.Id)!; var handle = Named<Control>(window, "TileHandle_" + placement.Id);
                var band = (Control)handle.Parent!; Assert.Equal(52, band.Bounds.Height);
                var top = handle.TranslatePoint(new Point(), band)!.Value.Y; var bottom = band.Bounds.Height - (top + handle.Bounds.Height);
                Assert.True(Math.Abs(top - bottom) < 1.5, $"{placement.Id}: {top} above, {bottom} below"); // As much room above the badge as below it.
                Assert.True(top >= 4, $"{placement.Id} header touches the top of its band");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ATileCanBeMadeLargerOrSmallerWithinItsLimitsAndTheSizeIsKept()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false;
            Assert.Equal((3, 4), desk.SizeOf("timeline"));
            desk.Resize("timeline", 3, 6); window.UpdateLayout();
            Assert.Equal((3, 6), desk.SizeOf("timeline")); Assert.Equal((3, 6), model.DeskSizes["timeline"]);
            var placed = desk.Placements.Single(p => p.Id == "timeline"); Assert.Equal((3, 6), (placed.ColumnSpan, placed.RowSpan));
            desk.Resize("timeline", 1, 1); Assert.Equal((2, 3), desk.SizeOf("timeline")); // Held to what a timeline needs.
            desk.Resize("clock", 9, 9); Assert.Equal((4, 8), desk.SizeOf("clock"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ATileOpensInPlaceToFillTheDeskAndGoesBackWithEscape()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false;
            Press(window, "TileExpand_notes"); window.UpdateLayout();
            Assert.Equal("notes", desk.Expanded); Assert.Equal(TileForm.Roomy, Assert.Single(desk.Placements).Form);
            Assert.True(desk.TileControl("notes")!.IsVisible); Assert.False(desk.TileControl("clock")!.IsVisible);
            Assert.True(desk.TileControl("notes")!.Bounds.Width > desk.Bounds.Width - 40);
            window.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Null(desk.Expanded); Assert.True(desk.TileControl("clock")!.IsVisible); Assert.True(desk.Placements.Count > 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheTimelineTileOpensInPlaceWithTheDayArrowsAndEditingButtons()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); model.SetDesk(true); var window = Open(model);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false;
            Assert.True(Named<Button>(window, "NextPeriod").IsEffectivelyVisible); Assert.False(Named<Button>(window, "AddTime").IsEffectivelyVisible); // The date is in the bar always; editing buttons wait for the timeline to open.
            Press(window, "TileExpand_timeline"); window.UpdateLayout();
            Assert.True(Named<Button>(window, "NextPeriod").IsEffectivelyVisible); Assert.True(Named<Button>(window, "AddTime").IsEffectivelyVisible);
            Assert.Same(desk, window.GetVisualDescendants().OfType<DeskView>().Single());
            Press(window, "TileExpand_timeline"); Assert.False(Named<Button>(window, "AddTime").IsEffectivelyVisible); // The same button puts it back.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheTimelineTileShowsTheDaysBlocks()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0)); model.SetDesk(true); var window = Open(model);
        try { var desk = OpenDesk(window); desk.Lively = false; Assert.Contains(desk.Placements, p => p.Id == "timeline"); Assert.NotEmpty(window.GetVisualDescendants().OfType<Control>().Where(c => c.Name == "TimelineSpine")); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSparePostItsStartInTheDrawerAndKeepTheirOwnWords()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false;
            Assert.Equal(new[] { "postit2", "postit3" }, desk.Hidden);
            desk.Show("postit2"); window.UpdateLayout();
            Named<TextBox>(window, "PostItText2").Text = "Second note";
            Assert.Equal("Second note", model.Trinkets.PostIts[1].Text); Assert.Equal("", model.Trinkets.PostIts[0].Text);
            Press(window, "PostItColour2"); Assert.Equal("rose", model.Trinkets.PostIts[1].Colour); Assert.Equal("lemon", model.Trinkets.PostIts[0].Colour);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheDrawerButtonSitsInTheHeaderAndOpensAndClosesTheDrawer()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false;
            var button = Named<Button>(window, "DrawerToggle");
            Assert.False(button.IsEffectivelyVisible); // Putting away is arranging, so the Drawer button waits for Arrange.
            Press(window, "ArrangeToggle"); Assert.True(desk.Arranging); Assert.True(button.IsEffectivelyVisible);
            // It shares a line with the title instead of taking a row above the tiles.
            Assert.True(button.TranslatePoint(new Point(), window)!.Value.Y < desk.TranslatePoint(new Point(), window)!.Value.Y);
            Press(window, "DrawerToggle"); Assert.True(desk.DrawerOpen); Assert.True(Named<Border>(window, "Drawer").IsVisible);
            Press(window, "DrawerToggle"); Assert.False(desk.DrawerOpen); Assert.False(Named<Border>(window, "Drawer").IsVisible);
            window.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null); Dispatcher.UIThread.RunJobs();
            Assert.False(desk.Arranging); Assert.False(button.IsEffectivelyVisible); // Escape leaves arrange mode, and the drawer goes with it.
            Press(window, "TileExpand_notes"); Assert.False(button.IsEffectivelyVisible); // Nothing to put away or bring back while a tile fills the desk.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AtRestTilesShowNoEditingControlsAndArrangeModeBringsThemOut()
    {
        var window = Open(Model());
        try
        {
            var desk = OpenDesk(window); desk.Lively = false;
            Assert.False(Named<Control>(window, "TileResize_todo").IsVisible); Assert.False(Named<Button>(window, "TileHide_todo").IsVisible);
            Assert.Equal("Arrange", Named<Button>(window, "ArrangeToggle").Content);
            Press(window, "ArrangeToggle"); Assert.Equal("Done", Named<Button>(window, "ArrangeToggle").Content);
            Assert.True(Named<Control>(window, "TileResize_todo").IsVisible); Assert.True(Named<Button>(window, "TileHide_todo").IsVisible);
            Press(window, "ArrangeToggle"); Assert.False(Named<Control>(window, "TileResize_todo").IsVisible); Assert.False(Named<Button>(window, "TileHide_todo").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PostItsAreInformalWithATiltWhileTilesStayStraight()
    {
        var window = Open(Model());
        try
        {
            var desk = OpenDesk(window); desk.Lively = false; desk.Show("postit2"); window.UpdateLayout();
            Assert.Equal(-1.6, Assert.IsType<RotateTransform>(desk.TileControl("postit")!.RenderTransform).Angle, 3);
            Assert.Equal(1.3, Assert.IsType<RotateTransform>(desk.TileControl("postit2")!.RenderTransform).Angle, 3);
            Assert.Null(desk.TileControl("todo")!.RenderTransform);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePomodoroTileStartsPausesAndResets()
    {
        var model = Model(); var window = Open(model);
        try
        {
            OpenDesk(window);
            Assert.Equal("25:00", Named<TextBlock>(window, "PomodoroTime").Text); Assert.Equal("Start", Named<Button>(window, "PomodoroToggle").Content);
            Press(window, "PomodoroToggle"); Assert.True(model.Pomodoro.Running); Assert.Equal("Pause", Named<Button>(window, "PomodoroToggle").Content);
            Press(window, "PomodoroToggle"); Assert.False(model.Pomodoro.Running); Assert.Equal("Resume", Named<Button>(window, "PomodoroToggle").Content);
            Press(window, "PomodoroReset"); Assert.Equal(PomodoroPhase.Idle, model.Pomodoro.Phase); Assert.Equal("Start", Named<Button>(window, "PomodoroToggle").Content);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ANoteWithATimeIsMarkedOnTheTimeline()
    {
        var model = Model(); var window = Open(model);
        try
        {
            model.AddDayNote(model.Engine.Now.Date, "Call the client 10:30"); model.AddDayNote(model.Engine.Now.Date, "No time on this one");
            window.UpdateLayout(); Press(window, "TimelineTab"); window.UpdateLayout();
            var marks = window.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("TimelineNote") == true).ToList();
            var mark = Assert.Single(marks);
            Assert.Equal(10.5 * model.TimelinePixelsPerHour + 2 - 6, Canvas.GetTop(mark), 1);
        }
        finally { window.Close(); }
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
            Assert.Contains("day", desk.Hidden); Assert.DoesNotContain(desk.Placements, p => p.Id == "day"); Assert.False(desk.TileControl("day")!.IsVisible);
            Assert.Contains("day", model.DeskHidden); Assert.DoesNotContain("day", model.DeskOrder);
            Press(window, "DrawerToggle"); Assert.True(Named<Border>(window, "Drawer").IsVisible);
            var chip = Named<Border>(window, "DrawerChip_day"); Assert.NotNull(chip);
            desk.Show("day", 0); window.UpdateLayout();
            Assert.DoesNotContain("day", desk.Hidden); Assert.Equal("day", desk.Order[0]); Assert.True(desk.TileControl("day")!.IsVisible); Assert.Equal(desk.Order.Count, desk.Placements.Count);
            Assert.DoesNotContain("day", model.DeskHidden);
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
        model.AddDayNote(model.Engine.Now.Date, "Team stand-up 09:30"); model.AddDayNote(model.Engine.Now.Date, "Send timesheet"); model.AddDayNote(model.Engine.Now.Date.AddDays(1), "Client review 11:00"); model.AddDayNote(model.Engine.Now.Date.AddDays(3), "Quarter end"); model.AddDayNote(model.Engine.Now.Date.AddDays(1), "!Timesheet due 17:00");
        var window = Open(model, width, height);
        try
        {
            Press(window, "DeskTab"); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var shown = window.GetVisualDescendants().OfType<DeskView>().Single(); Assert.Equal(new[] { "postit2", "postit3" }, shown.Hidden);
            Assert.DoesNotContain("selected", Named<Button>(window, "TimelineTab").Classes);
            if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is { } openFolder && width == 1080)
            {
                var desk = window.GetVisualDescendants().OfType<DeskView>().Single(); desk.Lively = false; desk.Expand("timeline"); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Directory.CreateDirectory(openFolder); using (window.CaptureRenderedFrame()) { } using (var open = window.CaptureRenderedFrame()) open?.Save(Path.Combine(openFolder, "desk-timeline-open.png"));
                desk.Collapse(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            }
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
