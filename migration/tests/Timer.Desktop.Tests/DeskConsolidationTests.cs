using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desk;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class DeskConsolidationTests
{
    private static void Settle(Window w) { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var f = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }

    // A scrollable list inside a taller scroll viewer, with the list already at its end.
    private static (Window Window, ScrollViewer Outer, ScrollViewer Inner) Nested(bool own)
    {
        var innerRows = new StackPanel(); for (var i = 0; i < 30; i++) innerRows.Children.Add(new TextBlock { Text = "row " + i, Height = 30 });
        var inner = new ScrollViewer { Content = innerRows, Height = 200, Name = "Inner" };
        var outerRows = new StackPanel { Children = { new TextBlock { Text = "above", Height = 100 }, inner } };
        for (var i = 0; i < 20; i++) outerRows.Children.Add(new TextBlock { Text = "below " + i, Height = 40 });
        var outer = new ScrollViewer { Content = outerRows, Name = "Outer" };
        var window = new Window { Width = 400, Height = 400, Content = outer }; window.Show();
        if (own) Ui.OwnWheel(outer);
        Settle(window);
        outer.Offset = new Vector(0, 60); Settle(window); // The list is under the pointer, whatever the desk has scrolled to.
        inner.Offset = new Vector(0, 100000); Settle(window);
        return (window, outer, inner);
    }

    [AvaloniaFact]
    public void AListAtItsEndKeepsTheWheelSoTheSurroundingScrollDoesNotMove()
    {
        // Control: left alone, the wheel passes from the list at its end to the scroll viewer around it, so this check can fail.
        var (plain, plainOuter, plainInner) = Nested(false);
        try
        {
            var before = plainOuter.Offset.Y; var at = plainInner.TranslatePoint(new Point(50, 100), plain)!.Value;
            plain.MouseWheel(at, new Vector(0, -3)); Settle(plain);
            Assert.True(plainOuter.Offset.Y > before, "Without the fix the surrounding scroll should have moved.");
        }
        finally { plain.Close(); }
        var (window, outer, inner) = Nested(true);
        try
        {
            var before = outer.Offset.Y; var innerEnd = inner.Offset.Y; var at = inner.TranslatePoint(new Point(50, 100), window)!.Value;
            window.MouseWheel(at, new Vector(0, -3)); Settle(window);
            Assert.Equal(before, outer.Offset.Y); Assert.Equal(innerEnd, inner.Offset.Y); Assert.True(Ui.OwnsWheel(inner));
            // The list still scrolls itself in the other direction.
            window.MouseWheel(at, new Vector(0, 3)); Settle(window);
            Assert.True(inner.Offset.Y < innerEnd); Assert.Equal(before, outer.Offset.Y);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AListWithNothingToScrollLetsTheWheelThrough()
    {
        var rows = new StackPanel(); for (var i = 0; i < 3; i++) rows.Children.Add(new TextBlock { Text = "row " + i, Height = 30 });
        var inner = new ScrollViewer { Content = rows, Height = 200 };
        var outerRows = new StackPanel { Children = { inner } }; for (var i = 0; i < 20; i++) outerRows.Children.Add(new TextBlock { Text = "below " + i, Height = 40 });
        var outer = new ScrollViewer { Content = outerRows }; var window = new Window { Width = 400, Height = 400, Content = outer }; window.Show();
        try
        {
            Ui.OwnWheel(outer); Settle(window);
            window.MouseWheel(inner.TranslatePoint(new Point(50, 50), window)!.Value, new Vector(0, -3)); Settle(window);
            Assert.True(outer.Offset.Y > 0); // Nothing in the list to scroll, so the desk takes it.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheDesksListsAndTimelineKeepTheWheel()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0))); model.CompleteSetup();
        var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        try
        {
            dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DeskTab").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(dashboard);
            var inside = dashboard.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.GetVisualAncestors().OfType<ScrollViewer>().Any() && !s.GetVisualAncestors().OfType<TextBox>().Any()).ToList(); // A text box has a scroll viewer of its own inside.
            Assert.NotEmpty(inside);
            Assert.All(inside, s => Assert.True(Ui.OwnsWheel(s), $"{s.Name ?? s.Content?.GetType().Name} chains the wheel to the desk."));
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void AnOpenPillOnAShortScreenScrollsItsAccountsAndKeepsFinishDayInView()
    {
        var accounts = Enumerable.Range(1, 40).Select(i => new AccountSettings("Client " + i, null, null)).ToList();
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0)), new StoreSettings(accounts, false, "Light", DayOfWeek.Monday));
        var pill = new PillWindow(model); pill.Show(); pill.ToggleExpansion(); Settle(pill);
        try
        {
            var screen = pill.Screens.ScreenFromWindow(pill); var screenHeight = screen is null ? 1080.0 : screen.WorkingArea.Height / screen.Scaling;
            Assert.True(pill.Height <= screenHeight * PillWindow.OpenShareOfScreen + 2 * Ui.ShadowMargin + 2, $"The open pill is {pill.Height} high on a {screenHeight} screen.");
            var list = pill.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "PillAccountScroll");
            Assert.True(list.Extent.Height > list.Viewport.Height + 10, "The list should scroll inside its box.");
            var day = pill.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PillDay");
            var bottom = day.TranslatePoint(new Point(0, day.Bounds.Height), pill)!.Value.Y;
            Assert.True(bottom <= pill.Bounds.Height + 1, "The Finish day button runs off the bottom of the pill.");
            // Quick switching still works from the list.
            var first = pill.GetVisualDescendants().OfType<Button>().First(b => b.Name == "Account0");
            first.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Client 1", model.ActiveAccount);
        }
        finally { pill.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, TileForm.Normal)]
    [InlineData(false, TileForm.Roomy)]
    [InlineData(true, TileForm.Normal)]
    [InlineData(true, TileForm.Tiny)]
    public void TheDayTileStartsAndFinishesTheDayInEveryForm(bool slim, TileForm form)
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0)));
        var view = new DayView(model, form, slim); var window = new Window { Width = 500, Height = 300, Content = view }; window.Show();
        try
        {
            Settle(window);
            var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DayStartFinish"); Assert.True(button.IsVisible);
            var before = model.DayStarted;
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(before, model.DayStarted);
            view.Refresh(); // The tile is refreshed by the desk on every change.
            Assert.Equal(model.DayStarted ? "Finish day" : "Start day", ToolTip.GetTip(button));
            Assert.Equal(model.DayStarted, button.Classes.Contains("finishDay"));
            // The words show where there is room for them, and only the flag where there is not.
            Assert.Equal(!slim, view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsVisible && t.Text is "Start day" or "Finish day"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheDesksAccountListCanBeReorderedLikeTheDashboards()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0))); model.CompleteSetup();
        var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        try
        {
            dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DeskTab").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(dashboard);
            model.SetDeskHidden(Array.Empty<string>()); Settle(dashboard);
            var grips = dashboard.GetVisualDescendants().OfType<IconGlyph>().Where(g => g.Kind == IconKind.Grip && g.IsVisible).ToList();
            Assert.NotEmpty(grips); // The Accounts tile's list has a grip on each row, so the order can be changed without leaving the desk.
        }
        finally { dashboard.Close(); }
    }

    private static void Press(Window w, string name) => w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void TheWeekIsOneTimesheetButtonWithTabsOnItsPage()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0))); model.CompleteSetup();
        var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        try
        {
            // The bar has Timesheet and no separate week buttons.
            var buttons = dashboard.GetVisualDescendants().OfType<Button>().Select(b => b.Name).ToList();
            Assert.Contains("TimesheetTab", buttons); Assert.DoesNotContain("WeekOverviewTab", buttons); Assert.DoesNotContain("WeekHoursTab", buttons); Assert.DoesNotContain("WeekEntriesTab", buttons);
            Press(dashboard, "TimesheetTab"); Settle(dashboard);
            Assert.Contains("selected", dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TimesheetTab").Classes);
            Assert.Equal("Timesheet", dashboard.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == null && t.Text == "Timesheet" && t.Classes.Contains("clock")).Text);
            // The three sections are tabs on the page, with their own tooltips, and the chosen one is marked.
            foreach (var (name, section, tip) in new[] { ("WeekOverviewTab", "overview", "Week overview"), ("WeekHoursTab", "hours", "Hours table"), ("WeekEntriesTab", "entries", "Entries") })
            {
                Press(dashboard, name); Settle(dashboard);
                Assert.Equal(section, model.WeekSection);
                var tab = dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
                Assert.Contains("selected", tab.Classes); Assert.Equal(tip, ToolTip.GetTip(tab));
                Assert.Single(dashboard.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("tab") && b.Classes.Contains("selected")));
            }
            // Coming back to the Timesheet from another page returns to the section last used.
            Press(dashboard, "TimelineTab"); Settle(dashboard); Press(dashboard, "TimesheetTab"); Settle(dashboard);
            Assert.Contains("selected", dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WeekEntriesTab").Classes);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheDashboardOpensOnTheDeskAndGoHomeReturnsToItFromAnyPage()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0))); model.CompleteSetup();
        var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        try
        {
            Assert.Contains("selected", dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DeskTab").Classes); // It opens on the desk.
            Press(dashboard, "TimesheetTab"); Settle(dashboard);
            Assert.DoesNotContain("selected", dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DeskTab").Classes);
            dashboard.GoHome(); Settle(dashboard);
            Assert.Contains("selected", dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DeskTab").Classes);
            Assert.DoesNotContain("selected", dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TimesheetTab").Classes);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(TileForm.Normal)]
    [InlineData(TileForm.Roomy)]
    public void AnEmptyDeadlineSaysNothingDueOnceAndOffersToAddOne(TileForm form)
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0))); model.CompleteSetup();
        var view = new DeadlineView(model, form); var window = new Window { Width = 500, Height = 500, Content = view }; window.Show();
        try
        {
            Settle(window);
            var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text).ToList();
            Assert.Equal(new[] { "Nothing due" }, texts.Where(t => t is "Nothing due" or "No deadlines" or "None").ToArray()); // Said once, with no big "None" beside it.
            string? asked = null; model.NoteRequested += p => asked = p;
            var add = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DeadlineAdd"); Assert.True(add.IsVisible);
            add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.Equal("! ", asked); // The dialog opens ready for a deadline.
            model.AddDayNote(model.Engine.Now.Date.AddDays(1), "!report"); view.Refresh(); Settle(window);
            Assert.False(view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DeadlineAdd").IsVisible); // With one due the button is gone.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AnEmptyComingUpIsOneQuietLineWithAButtonThatOpensTheNoteDialog()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0))); model.CompleteSetup();
        var view = new ComingUpView(model); var window = new Window { Width = 400, Height = 300, Content = view }; window.Show();
        try
        {
            Settle(window);
            Assert.Equal("Nothing coming up", Assert.Single(view.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text) && t.Text.StartsWith("Nothing"))).Text);
            var asked = false; model.NoteRequested += _ => asked = true;
            view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ComingAdd").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.True(asked);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheNoteDialogOpensPreparedForADeadlineWhenAskedTo()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 10, 0, 0))); model.CompleteSetup();
        var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        try
        {
            model.RequestNote("! "); Settle(dashboard);
            Assert.Equal("Add deadline", dashboard.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DialogTitle").Text);
            var box = dashboard.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "NoteText"); Assert.Equal("! ", box.Text);
            box.Text = "! file report @fri 17:00"; Press(dashboard, "SaveNote"); Settle(dashboard);
            var note = Assert.Single(model.AllDayNotes); Assert.True(DayNotes.IsDeadline(note)); Assert.Equal(new TimeSpan(17, 0, 0), note.At);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void TheRoomyDayTileAddsTheWorkingTimeLeftAndTheTrackerKeepsToItsOwnFigures()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 14, 11, 0, 0))); model.CompleteSetup();
        var day = new DayView(model, TileForm.Roomy); var tracker = new TrackerView(model, TileForm.Roomy); var window = new Window { Width = 600, Height = 700, Content = new StackPanel { Children = { day, tracker } } }; window.Show();
        try
        {
            Settle(window);
            Assert.Contains(day.GetVisualDescendants().OfType<Grid>(), g => g.Name == "DayWorkLeft");
            foreach (var name in new[] { "DayUnallocated", "DayBreaks", "DayOvertime" }) Assert.Contains(day.GetVisualDescendants().OfType<Grid>(), g => g.Name == name);
            // The tracker does not repeat any of them: its big figures are the block and the account's day.
            Assert.DoesNotContain(tracker.GetVisualDescendants().OfType<Control>(), c => c.Name is "DayUnallocated" or "DayWorkLeft");
            Assert.Contains(tracker.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "TrackerBlock"); Assert.Contains(tracker.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "TrackerAccountToday");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePomodoroCanBeForAnAccountWithoutTouchingRecordedTime()
    {
        var now = new DateTime(2026, 10, 14, 10, 0, 0); var model = new TimerModel(new TrackingEngine(clock: () => now)); model.CompleteSetup();
        var sessionsBefore = model.Engine.Sessions.Count; var active = model.ActiveAccount;
        var view = new PomodoroView(model, false); var window = new Window { Width = 400, Height = 400, Content = view }; window.Show();
        try
        {
            Settle(window);
            var button = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PomodoroAccount"); Assert.Equal("No account", button.Content);
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Assert.Equal("Account one", model.Pomodoro.Account); Assert.Equal("#Account one", button.Content);
            model.Pomodoro.Start(now); now = now.AddMinutes(26); model.Pomodoro.Update(now); // A whole focus block for that account.
            Assert.Equal(1, model.Pomodoro.CompletedFor["Account one"]); Assert.Equal(1, model.Pomodoro.Completed);
            Assert.Equal(sessionsBefore, model.Engine.Sessions.Count); Assert.Equal(active, model.ActiveAccount); Assert.False(model.IsTracking); // Nothing was recorded or started.
            for (var i = 0; i < 3; i++) button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); // Through the other two accounts, then back to none.
            Assert.Null(model.Pomodoro.Account);
            model.Pomodoro.Reset(); Assert.Empty(model.Pomodoro.CompletedFor);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PreviewPictures()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return; Directory.CreateDirectory(folder);
        var model = TimerModel.CreateDemo();var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        try
        {
            void Shot(string name) { Settle(dashboard); using var frame = dashboard.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, name + ".png")); }
            Shot("desk-home"); dashboard.SelectTab(false); Shot("timeline"); dashboard.GoHome(); Press(dashboard, "TimesheetTab"); Shot("timesheet-overview"); Press(dashboard, "WeekHoursTab"); Shot("timesheet-hours"); Press(dashboard, "WeekEntriesTab"); Shot("timesheet-entries");
        }
        finally { dashboard.Close(); }
    }
}
