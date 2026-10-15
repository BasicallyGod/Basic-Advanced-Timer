using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desk;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class DeskTests
{
    private static TimerModel Model(params string[] order)
        => new(new TrackingEngine(), new StoreSettings(new() { new("Account one", null, null), new("Account two", null, null) }, false, "Light", DayOfWeek.Monday).WithApp(new AppPrefs(DeskOrder: order)));

    private static DashboardWindow Open(TimerModel model, int width = 1080, int height = 680)
    {
        var window = new DashboardWindow(model); window.Width = width + 2 * Ui.ShadowMargin; window.Height = height + 2 * Ui.ShadowMargin; window.Show();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); return window;
    }
    private static T Named<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static void Press(Control root, string name) { Named<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void TheDeskShowsItsTilesInTheSavedOrder()
    {
        var window = Open(Model("day", "working", "accounts"));
        try
        {
            Press(window, "DeskTab"); window.UpdateLayout();
            var desk = window.GetVisualDescendants().OfType<DeskView>().Single();
            Assert.Equal(new[] { "day", "working", "accounts" }, desk.Order.Take(3)); // Tiles added since the order was saved follow it.
            Assert.Equal(DeskView.Kinds.Count, desk.Placements.Count); Assert.Equal("day", desk.Placements[0].Id);
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
            var item = Assert.Single(model.Todos); Assert.Equal("Send invoice #AccountOne", item.Text); Assert.Equal("Account one", NoteShorthand.AccountOf(item.Text, model.AccountNames));
            Assert.Equal("", input.Text); Assert.NotNull(Named<Control>(window, "TodoTag_" + item.Id));
            Named<CheckBox>(window, "TodoTick_" + item.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.True(model.Todos[0].Done);
            Press(window, "TodoRemove_" + item.Id); Assert.Empty(model.Todos);
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
            Press(window, "PostItColour"); Assert.Equal("rose", model.Trinkets.PostIts[0].Colour); Press(window, "PostItColour"); Assert.Equal("sky", model.Trinkets.PostIts[0].Colour); Press(window, "PostItColour"); Assert.Equal("mint", model.Trinkets.PostIts[0].Colour);
            Press(window, "PostItColour"); Assert.Equal("lemon", model.Trinkets.PostIts[0].Colour); // Five sheets, then round again.
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
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0));var window = Open(model);
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
            Assert.True(Has<Border>(window, "ComingEvent_" + note.Id)); Assert.False(window.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name == "ComingTick_" + note.Id)); // An event is not ticked off; it passes.
            Named<CheckBox>(window, "NoteTick_" + note.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Empty(model.ComingUp()); Assert.True(model.NotesOn(today)[0].Done);
            Press(window, "NoteRemove_" + note.Id); Assert.Empty(model.NotesOn(today));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RenamingAnAccountKeepsItsToDosLinked()
    {
        var model = Model(); model.AddTodo("Call #AccountTwo");
        Assert.Equal("Account two", NoteShorthand.AccountOf(model.Todos[0].Text, model.AccountNames));
        model.SetAccounts(model.Accounts.Where(a => !BreakAccount.Is(a.Name)).Select(a => a.Name == "Account two" ? new AccountSettings("Beta", null, "week") : new AccountSettings(a.Name, null, "week")).ToList(), new Dictionary<string, string> { ["Account two"] = "Beta" });
        Assert.Equal("Beta", NoteShorthand.AccountOf(model.Todos[0].Text, model.AccountNames)); // The tag was rewritten to the new name.
    }

    [AvaloniaFact]
    public void TheViewButtonsAreABarAlongTheTop()
    {
        var on = Open(Model());
        try
        {
            Point At(Window w, string name) => Named<Button>(w, name).TranslatePoint(new Point(), w)!.Value;
            Assert.True(Math.Abs(At(on, "TimelineTab").Y - At(on, "TimesheetTab").Y) < 1 && At(on, "TimesheetTab").X > At(on, "TimelineTab").X + 20);
            Assert.True(At(on, "QuitTimer").X > At(on, "Settings").X); Assert.True(Math.Abs(At(on, "QuitTimer").Y - At(on, "Settings").Y) < 1);
            Assert.True(At(on, "DeskTab").X < At(on, "TimelineTab").X);
        }
        finally { on.Close(); }
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
            Assert.True(At("PreviousPeriod").X > At("TimesheetTab").X);
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
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0));var window = Open(model);
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
            OpenDesk(window); Assert.False(Named<TextBlock>(window, "DeadlineLeft").IsVisible); Assert.Equal("Nothing due", Named<TextBlock>(window, "DeadlineWhat").Text); // No big figure for nothing.
            var due = model.Engine.Now.Date.AddDays(1); // Tomorrow, at five.
            model.AddDayNote(due, "!Timesheet due"); window.UpdateLayout();
            var expected = due.AddHours(17) - model.Engine.Now; var left = Named<TextBlock>(window, "DeadlineLeft").Text;
            Assert.Equal($"{(int)expected.TotalHours}h {expected.Minutes:00}m", left); // Spelled out, so it reads as a duration and not a time of day.
            Assert.Equal("Timesheet due", Named<TextBlock>(window, "DeadlineWhat").Text); Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Due in");
            model.ToggleDayNote(model.AllDayNotes[0].Id); window.UpdateLayout(); Assert.False(Named<TextBlock>(window, "DeadlineLeft").IsVisible); Assert.Equal("Nothing due", Named<TextBlock>(window, "DeadlineWhat").Text);
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
                var band = (Control)handle.Parent!;
                var top = handle.TranslatePoint(new Point(), band)!.Value.Y; var bottom = band.Bounds.Height - (top + handle.Bounds.Height);
                Assert.True(Math.Abs(top - bottom) < 1.5, $"{placement.Id}: {top} above, {bottom} below"); // As much room above the badge as below it.
                Assert.True(top >= 4, $"{placement.Id} header touches the top of its band");
            }
        }
        finally { window.Close(); }
    }

    private static bool Has<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Any(c => c.Name == name);

    [AvaloniaFact]
    public void TheClockChangesWhatItShowsWithItsSizeNotJustHowBigItIs()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false;
            desk.Resize("clock", 2, 1); window.UpdateLayout(); // One row high: a digital strip with the date, and no face.
            Assert.True(Has<TextBlock>(window, "ClockStripTime")); Assert.False(window.GetVisualDescendants().OfType<AnalogueClock>().Any());
            desk.Resize("clock", 1, 2); window.UpdateLayout(); // Two rows: the face, with the time in figures under it.
            Assert.True(window.GetVisualDescendants().OfType<AnalogueClock>().Any()); Assert.False(Has<TextBlock>(window, "ClockStripTime")); Assert.True(Has<TextBlock>(window, "ClockDigital"));
            desk.Resize("clock", 2, 3); window.UpdateLayout(); // A big one adds the date.
            Assert.True(window.GetVisualDescendants().OfType<AnalogueClock>().Any());
            Assert.Contains(model.Engine.Now.ToString("dddd d MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheDeadlineHasATinyStateAStandardOneAndAnExpandedOneWithContext()
    {
        var model = Model(); model.AddDayNote(model.Engine.Now.Date.AddDays(1), "!Timesheet due 17:00"); model.AddDayNote(model.Engine.Now.Date.AddDays(3), "!Quarter end report 09:00"); model.AddDayNote(model.Engine.Now.Date.AddDays(4), "!Budget review");
        var window = Open(model);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false;
            desk.Resize("deadline", 1, 1); window.UpdateLayout(); // Tiny: a countdown and the name, and no date line or extras.
            Assert.True(Has<TextBlock>(window, "DeadlineLeft")); Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Due in"); Assert.False(Has<TextBlock>(window, "DeadlineWorkLeft"));
            desk.Resize("deadline", 2, 2); window.UpdateLayout(); // Standard: the phrase and the date.
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Due in"); Assert.False(Has<TextBlock>(window, "DeadlineWorkLeft"));
            desk.Resize("deadline", 2, 3); window.UpdateLayout(); // Expanded: working time left and what follows.
            var left = Named<TextBlock>(window, "DeadlineWorkLeft").Text; Assert.StartsWith("Working time left: ", left);
            var due = model.Engine.Now.Date.AddDays(1).AddHours(17);
            Assert.StartsWith("Working time left: " + model.Format(model.Engine.Schedule.WorkingTimeBetween(model.Engine.Now, due)) + " over ", left); Assert.Contains("working day", left);
            Assert.False(Has<StackPanel>(window, "DeadlineLater")); // The deadlines after it are Coming up's business.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheWeekChartGainsAnAccountBreakdownAsItGrows()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0));var window = Open(model);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false; var chart = Named<WeekChart>(window, "WeekChart");
            Assert.Equal(TileForm.Normal, chart.Mode);
            desk.Resize("week", 1, 1); window.UpdateLayout(); Assert.Equal(TileForm.Tiny, chart.Mode);
            desk.Resize("week", 3, 2); window.UpdateLayout(); Assert.Equal(TileForm.Roomy, chart.Mode);
            var accounts = chart.AccountsThisWeek(); Assert.NotEmpty(accounts); Assert.Equal(accounts.OrderByDescending(a => a.Time).Select(a => a.Account), accounts.Select(a => a.Account));
            Assert.All(chart.AccountsOn(model.SelectedDate), a => Assert.True(a.Time > TimeSpan.Zero));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheWeekChartKeepsDetailsForTheHoverAndTheFullLegendForTheBiggestSize()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0));var window = Open(model);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false; var chart = Named<WeekChart>(window, "WeekChart");
            desk.Resize("week", 3, 2); window.UpdateLayout(); Assert.False(chart.FullLegend); // Roomy: the biggest few and a count of the rest.
            desk.Resize("week", 3, 3); window.UpdateLayout(); Assert.True(chart.FullLegend); // Large: every account in the week.
            Assert.True(chart.AccountsThisWeek().Count > 5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TrackerAccountsAndComingUpEachHaveASlimAndABigForm()
    {
        var model = Model(); model.AddDayNote(model.Engine.Now.Date, "Call the client 23:00"); var window = Open(model);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false;
            Assert.False(Has<TextBlock>(window, "TrackerBlock")); // Standard: the clock and who is recording, no extra figures.
            desk.Resize("working", 2, 3); window.UpdateLayout(); Assert.True(Has<TextBlock>(window, "TrackerBlock")); Assert.True(Has<TextBlock>(window, "TrackerAccountToday")); // Big: the block and the account's day, not the day's figures.
            desk.Resize("working", 2, 1); window.UpdateLayout(); Assert.True(Has<Button>(window, "TrackerPause")); Assert.False(Has<Button>(window, "TrackerBreak")); // Slim: the clock and one button, beside the title.
            Assert.True(Has<Border>(window, "TileInline_working")); Assert.True(Named<Border>(window, "TileInline_working").IsVisible);
            desk.Resize("working", 1, 1); window.UpdateLayout(); Assert.True(Has<Button>(window, "TrackerPause")); // Slimmer still: one cell wide drops the title, not the content.
            desk.Resize("accounts", 2, 1); window.UpdateLayout(); // Slim: every account with its name and time, nothing to hover for.
            var chips = Named<AccountChips>(window, "AccountChips");
            Assert.Equal(model.Accounts.Count(a => !BreakAccount.Is(a.Name)), chips.Children.Count);
            Assert.All(chips.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Parent == chips || true).Take(1), _ => { });
            Assert.Contains(chips.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Account one"); Assert.Contains(chips.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Account two");
            desk.Resize("accounts", 1, 1); window.UpdateLayout(); // One cell: the account in hand, and how many there are.
            var single = Named<AccountChips>(window, "AccountChips"); Assert.Single(single.Children);
            Assert.Contains(single.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { } x && x.EndsWith("of 2"));
            desk.Resize("accounts", 2, 3); window.UpdateLayout(); Assert.Empty(window.GetVisualDescendants().OfType<AccountChips>());
            desk.Resize("coming", 2, 1); window.UpdateLayout(); Assert.False(window.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name?.StartsWith("ComingTick_") == true)); // One line, no ticks.
            desk.Resize("coming", 2, 2); window.UpdateLayout(); Assert.True(window.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name?.StartsWith("ComingTick_") == true) || window.GetVisualDescendants().OfType<Button>().Any(c => c.Name?.StartsWith("ComingDone_") == true) || Has<Border>(window, "TileInline_coming"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ComingUpTellsTasksEventsAndDeadlinesApart()
    {
        var model = Model(); var today = model.Engine.Now.Date;
        model.AddDayNote(today.AddDays(1), "Update the budget sheet"); model.AddDayNote(today.AddDays(1), "Client review 11:00"); model.AddDayNote(today.AddDays(2), "!Timesheet due 17:00");
        var window = Open(model);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false; desk.Resize("coming", 2, 3); window.UpdateLayout();
            var task = model.AllDayNotes.First(n => n.Text.StartsWith("Update")); var ev = model.AllDayNotes.First(n => n.Text.StartsWith("Client")); var due = model.AllDayNotes.First(n => n.Text.StartsWith("!"));
            Assert.True(window.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name == "ComingTick_" + task.Id)); // A task: a tick box.
            Assert.True(Has<Border>(window, "ComingEvent_" + ev.Id)); Assert.False(window.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name == "ComingTick_" + ev.Id)); // An event: a clock, nothing to tick.
            Press(window, "ComingDone_" + due.Id); Assert.True(model.AllDayNotes.First(n => n.Id == due.Id).Done); // A deadline: a flag that completes it.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheTilesWearTheHuesOfTheThemeInUseAndChangeWhenItChanges()
    {
        var model = Model(); model.SetTheme("Light"); var window = Open(model);
        try
        {
            var desk = OpenDesk(window);
            Color Wash(string id) => ((SolidColorBrush)((Border)desk.TileControl(id)!).Background!).Color;
            var light = Wash("deadline"); Assert.Equal(DeskHues.WashAlpha, light.A); Assert.Equal(ThemeColours.Rgb(DeskHues.Wash("deadline", false)), ((int)light.R, (int)light.G, (int)light.B));
            model.SetTheme("Dark"); Dispatcher.UIThread.RunJobs();
            var dark = Wash("deadline"); Assert.Equal(DeskHues.DarkWashAlpha, dark.A); Assert.Equal(ThemeColours.Rgb(DeskHues.Wash("deadline", true)), ((int)dark.R, (int)dark.G, (int)dark.B));
            Assert.NotEqual(light, dark);
            Assert.Same(desk, window.GetVisualDescendants().OfType<DeskView>().Single()); // Re-dressed in place, not rebuilt.
            Assert.Equal(Color.Parse(DeskHues.Badge("deadline")), ((SolidColorBrush)Named<Border>(window, "Tile_deadline").GetVisualDescendants().OfType<Border>().First(b => b.Width == 30).Background!).Color); // The badge stays its pastel.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheDayTileMeasuresTheWorkingDayAgainstItsTarget()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0));var window = Open(model);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false;
            var f = DayFigures.For(model); Assert.Equal(TimeSpan.FromHours(8), f.Target); // Nine to five.
            Assert.Equal(f.Target - f.Allocated, f.Left); Assert.InRange(f.Fraction!.Value, 0.01, 1);
            Assert.Equal(model.Format(f.Allocated), Named<TextBlock>(window, "DayDone").Text); // Slim by default: one row, the figure beside a bar.
            Assert.Equal($"{f.Fraction * 100:0}%", Named<TextBlock>(window, "TileMeta_day").Text);
            Assert.True(Has<DayProgress>(window, "DayProgress"));
            desk.Resize("day", 2, 3); window.UpdateLayout(); // Roomy: the reconciliation behind the figure.
            Assert.True(Has<Grid>(window, "DayUnallocated")); Assert.True(Has<Grid>(window, "DayBreaks")); Assert.True(Has<Grid>(window, "DayOvertime"));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text is { } x && x.EndsWith("still to put on accounts"));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ADayOffHasNoTargetAndNoFraction()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 10, 11, 0, 0)), new StoreSettings(new() { new("A", null, null) }, false, "Light", DayOfWeek.Monday)); // A Saturday.
        var f = DayFigures.For(model); Assert.Equal(TimeSpan.Zero, f.Target); Assert.Null(f.Fraction); Assert.Equal(TimeSpan.Zero, f.Left);
    }

    [AvaloniaFact]
    public void ATileThatIsResizedBackAndForthNeverTripsOverItsSharedPieces()
    {
        var window = Open(Model());
        try
        {
            var desk = OpenDesk(window); desk.Lively = false;
            foreach (var id in new[] { "clock", "day", "working", "deadline" })
                foreach (var size in new[] { (1, 1), (2, 2), (2, 3), (1, 2), (2, 1), (1, 1) }) { desk.Resize(id, size.Item1, size.Item2); window.UpdateLayout(); }
            Assert.NotEmpty(window.GetVisualDescendants().OfType<AnalogueClock>().Concat<Control>(window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "ClockStripTime")));
        }
        finally { window.Close(); }
    }

    // A picture of the tiles at their small and big forms, for looking at when TIMER_PREVIEW_DIR is set.
    [AvaloniaFact]
    public void TheTilesDrawAtTheirSmallAndBigForms()
    {
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0));
        model.AddDayNote(model.Engine.Now.Date.AddDays(1), "!Timesheet due 17:00"); model.AddDayNote(model.Engine.Now.Date.AddDays(3), "!Quarter end report 09:00"); model.AddDayNote(model.Engine.Now.Date.AddDays(4), "!Budget review"); model.AddDayNote(model.Engine.Now.Date, "Call about scope 15:00");
        model.SetDeskHidden(new[] { "todo", "notes", "timeline", "strip", "postit2", "postit3", "pomodoro" });
        var window = Open(model, 1500, 900);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false;
            desk.Resize("clock", 2, 1); desk.Resize("deadline", 2, 3); desk.Resize("week", 3, 2); desk.Resize("working", 2, 3); desk.Resize("accounts", 1, 1); desk.Resize("day", 2, 2);
            desk.Drop("clock", 0); desk.Drop("accounts", 1); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is { } folder)
            {
                Directory.CreateDirectory(folder); using (window.CaptureRenderedFrame()) { } using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, "desk-forms.png"));
                model.SetTheme("Dark"); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using (window.CaptureRenderedFrame()) { } using (var darkFrame = window.CaptureRenderedFrame()) darkFrame?.Save(Path.Combine(folder, "desk-forms-dark.png")); model.SetTheme("Light");
                desk.SetArranging(true); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using (window.CaptureRenderedFrame()) { } using var arranging = window.CaptureRenderedFrame(); arranging?.Save(Path.Combine(folder, "desk-arranging.png"));
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
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0));var window = Open(model);
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
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0));var window = Open(model);
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
    public void APostItCanLoseItsTapeAndKeepsThatChoice()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = OpenDesk(window); desk.Lively = false; desk.Show("postit2"); window.UpdateLayout();
            Assert.True(model.Trinkets.PostIts[0].Tape); Assert.False(model.Trinkets.PostIts[1].Tape); // The first is taped; the spare starts plain, for some variety.
            Press(window, "PostItTape"); Assert.False(model.Trinkets.PostIts[0].Tape); Assert.True(model.Trinkets.PostIts[1].Tape == false);
            Press(window, "PostItTape2"); Assert.True(model.Trinkets.PostIts[1].Tape); Assert.False(model.Trinkets.PostIts[0].Tape);
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
            Assert.DoesNotContain("day", desk.Hidden); Assert.Contains("day", desk.Order); Assert.True(desk.TileControl("day")!.IsVisible); Assert.Equal(desk.Order.Count, desk.Placements.Count);
            Assert.DoesNotContain("day", model.DeskHidden);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ASavedDrawerIsRestoredWhenTheDeskOpens()
    {
        var window = Open(new TimerModel(new TrackingEngine(), new StoreSettings(new() { new("A", null, null) }, false, "Light", DayOfWeek.Monday).WithApp(new AppPrefs(DeskOrder: new[] { "working", "accounts" }, DeskHidden: new[] { "day" }))));
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
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var prefs = store.Load(engine); store.Save(engine, prefs.WithApp(a => a with { Calm = true, DeskOrder = new[] { "day", "accounts" }, DeskHidden = new[] { "working" } })); }
            using (var store = new TimerStore(folder)) { var prefs = store.Load(new TrackingEngine()); Assert.True(prefs.App().Calm); Assert.Equal(new[] { "working" }, prefs.App().DeskHidden); Assert.Equal(new[] { "day", "accounts" }, prefs.App().DeskOrder); }
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
        var model = TimerModel.CreateDemo(() => new DateTime(2026, 10, 9, 12, 0, 0));
        model.AddTodo("Send the invoice #AccountOne"); model.AddTodo("Call back about the budget #AccountThree"); model.AddTodo("Order more tea"); model.ToggleTodo(model.Todos[2].Id);
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

    private static DeskView Arranged(DashboardWindow window)
    {
        Press(window, "DeskTab");
        var desk = window.GetVisualDescendants().OfType<DeskView>().Single(); desk.Lively = false; Press(window, "ArrangeToggle"); window.UpdateLayout();
        return desk;
    }

    [AvaloniaFact]
    public void ATileDroppedInAnEmptyCellStaysThereAndTheGapIsKept()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = Arranged(window);
            var before = desk.Placements.ToDictionary(p => p.Id);
            var farRow = desk.Placements.Max(p => p.Row + p.RowSpan) + 2;
            desk.DropAt("clock", 0, farRow); window.UpdateLayout();
            var clock = desk.Placements.Single(p => p.Id == "clock");
            Assert.Equal((0, farRow), (clock.Column, clock.Row));
            foreach (var p in desk.Placements.Where(p => p.Id != "clock")) Assert.Equal((before[p.Id].Column, before[p.Id].Row), (p.Column, p.Row)); // Nothing else moved.
            Assert.Equal((0, farRow), model.DeskPositions["clock"]);
            Assert.True(Named<Button>(window, "TidyDesk").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PuttingATileAwayLeavesItsPlaceEmptyUntilTidyUp()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = Arranged(window);
            var next = desk.Placements.First(p => p.Id != "working"); var before = desk.Placements.ToDictionary(p => p.Id);
            Press(window, "TileHide_working"); window.UpdateLayout();
            Assert.DoesNotContain(desk.Placements, p => p.Id == "working");
            foreach (var p in desk.Placements) Assert.Equal((before[p.Id].Column, before[p.Id].Row), (p.Column, p.Row));
            Press(window, "TidyDesk"); window.UpdateLayout();
            Assert.False(desk.HasGaps); Assert.Empty(model.DeskPositions);
            Assert.Equal((0, 0), (desk.Placements[0].Column, desk.Placements[0].Row));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MakingATileBiggerMovesOnlyWhatItLandsOn()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = Arranged(window);
            desk.DropAt("clock", 0, 14); window.UpdateLayout(); // Far below everything, in a gap of its own.
            var before = desk.Placements.ToDictionary(p => p.Id);
            desk.Resize("week", 3, 2); window.UpdateLayout();
            var after = desk.Placements.ToDictionary(p => p.Id);
            Assert.Equal((before["clock"].Column, before["clock"].Row), (after["clock"].Column, after["clock"].Row));
            Assert.Equal((before["week"].Column, before["week"].Row), (after["week"].Column, after["week"].Row)); // The resized tile keeps its corner.
            var list = desk.Placements.ToList();
            for (var i = 0; i < list.Count; i++) for (var j = i + 1; j < list.Count; j++)
                Assert.False(list[i].Column < list[j].Column + list[j].ColumnSpan && list[j].Column < list[i].Column + list[i].ColumnSpan && list[i].Row < list[j].Row + list[j].RowSpan && list[j].Row < list[i].Row + list[i].RowSpan);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ADeskThatIsNeverArrangedKeepsFlowingAndSavedGapsComeBackOnReopening()
    {
        var model = Model(); var window = Open(model);
        try { Press(window, "DeskTab"); Assert.False(window.GetVisualDescendants().OfType<DeskView>().Single().HasGaps); Assert.Empty(model.DeskPositions); }
        finally { window.Close(); }
        var saved = new TimerModel(new TrackingEngine(), new StoreSettings(new() { new("A", null, null) }, false, "Light", DayOfWeek.Monday).WithApp(new AppPrefs(DeskPositions: new[] { "clock:0+9" })));
        var again = Open(saved);
        try
        {
            Press(again, "DeskTab");
            var desk = again.GetVisualDescendants().OfType<DeskView>().Single(); desk.Lively = false; again.UpdateLayout();
            var clock = desk.Placements.Single(p => p.Id == "clock"); Assert.Equal((0, 9), (clock.Column, clock.Row));
        }
        finally { again.Close(); }
    }

    [AvaloniaFact]
    public void ATileBroughtBackFromTheDrawerLandsWhereItWasDropped()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = Arranged(window);
            desk.ShowAt("postit2", 0, 12); window.UpdateLayout();
            var p = desk.Placements.Single(x => x.Id == "postit2"); Assert.Equal((0, 12), (p.Column, p.Row)); Assert.DoesNotContain("postit2", desk.Hidden);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ASavedLayoutBringsBackTheTilesTheirSizesAndTheirCellsAndNothingElse()
    {
        var model = Model(); var window = Open(model);
        try
        {
            var desk = Arranged(window);
            Assert.True(Named<Button>(window, "LayoutsToggle").IsVisible);
            desk.DropAt("clock", 0, 9); desk.Resize("week", 3, 2); Press(window, "TileHide_day"); window.UpdateLayout();
            var panel = (Panel)desk.BuildLayoutsPanel(); Assert.Empty(panel.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("LayoutApply_") == true));
            panel.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "LayoutName").Text = "Focus";
            Click(panel.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "LayoutSave"));
            Assert.Equal("Focus", Assert.Single(model.DeskLayouts).Name);
            var placed = desk.Placements.ToDictionary(p => p.Id);
            // Change everything, then apply the layout.
            desk.TidyUp(); desk.Resize("week", 1, 1); desk.Show("day"); window.UpdateLayout();
            desk.ApplyLayout(model.DeskLayouts[0]); window.UpdateLayout();
            Assert.Contains("day", desk.Hidden); Assert.Equal((3, 2), desk.SizeOf("week")); Assert.Equal((0, 9), desk.PinOf("clock"));
            var after = desk.Placements.ToDictionary(p => p.Id);
            Assert.Equal(placed.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
            foreach (var (id, p) in placed) Assert.Equal((p.Column, p.Row, p.ColumnSpan, p.RowSpan), (after[id].Column, after[id].Row, after[id].ColumnSpan, after[id].RowSpan));
            Assert.Contains("Focus", Assert.Single(desk.BuildLayoutsPanel().GetVisualDescendants().OfType<Button>().Where(b => b.Name == "LayoutApply_Focus")).Content?.ToString());
            Click(desk.BuildLayoutsPanel().GetVisualDescendants().OfType<Button>().Single(b => b.Name == "LayoutDelete_Focus")); Assert.Empty(model.DeskLayouts);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AnOldLayoutNamingAGoneTileStillAppliesAndSavedLayoutsComeBackOnReopening()
    {
        var saved = new SavedLayout("Old", new[] { "ghost", "todo" }, new[] { "clock", "ghost2" }, new[] { "ghost:2x2", "todo:2x4" }, new[] { "ghost:0+0" });
        var model = new TimerModel(new TrackingEngine(), new StoreSettings(new() { new("A", null, null) }, false, "Light", DayOfWeek.Monday).WithApp(new AppPrefs(DeskLayouts: new[] { saved })));
        var window = Open(model);
        try
        {
            Press(window, "DeskTab"); var desk = window.GetVisualDescendants().OfType<DeskView>().Single(); desk.Lively = false;
            desk.ApplyLayout(model.DeskLayouts.Single()); window.UpdateLayout();
            Assert.Equal(new[] { "clock" }, desk.Hidden); Assert.Equal("todo", desk.Order[0]); Assert.DoesNotContain("ghost", desk.Order); Assert.Equal(DeskView.Kinds.Count - 1, desk.Placements.Count);
            Assert.Equal((2, 4), desk.SizeOf("todo"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ListTilesOpenTallEnoughForTheirItemsButNeverOverASizeChosenByHand()
    {
        var model = Model(); for (var i = 0; i < 12; i++) model.AddTodo("Task " + i);
        var window = Open(model);
        try
        {
            Press(window, "DeskTab"); var desk = window.GetVisualDescendants().OfType<DeskView>().Single(); desk.Lively = false;
            Assert.True(desk.SizeOf("todo").Rows > 3); Assert.Equal(3, desk.SizeOf("accounts").Rows); Assert.Equal((2, 2), desk.SizeOf("week")); // Only lists grow, and only when they hold more.
            Assert.False(model.DeskSizes.ContainsKey("todo")); // Worked out, not saved.
            desk.Resize("todo", 2, 3); Assert.Equal((2, 3), desk.SizeOf("todo")); Assert.Equal((2, 3), model.DeskSizes["todo"]); // A size chosen by hand wins.
        }
        finally { window.Close(); }
        var chosen = new TimerModel(new TrackingEngine(), new StoreSettings(new() { new("A", null, null) }, false, "Light", DayOfWeek.Monday).WithApp(new AppPrefs(DeskSizes: new[] { "todo:2x3" })));
        for (var i = 0; i < 12; i++) chosen.AddTodo("Task " + i);
        var again = Open(chosen);
        try { Press(again, "DeskTab"); Assert.Equal((2, 3), again.GetVisualDescendants().OfType<DeskView>().Single().SizeOf("todo")); } // Saved earlier, so left alone.
        finally { again.Close(); }
        var empty = Open(Model());
        try { Press(empty, "DeskTab"); Assert.Equal((2, 3), empty.GetVisualDescendants().OfType<DeskView>().Single().SizeOf("todo")); }
        finally { empty.Close(); }
    }
}
