using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 28: the pill's alerts, built as the board "Pill notifications: moving between them" draws them: the light, and the card as one short list with one row open (the board "Notifications: calmer at a glance", option B).
public sealed class PillAlertsTests
{
    private static readonly DateTime DemoNow = new(2026, 10, 14, 14, 15, 0);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static Button Find(Visual root, string name) => root.GetSelfAndVisualDescendants().OfType<Button>().First(b => b.Name == name);
    private static void Press(Visual root, string name) => Find(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    // The demo with two budgets turned up so there is one at 80 per cent (Account three: 01:25 of 01:45) and one spent (Account four).
    private static TimerModel Demo()
    {
        var model = TimerModel.CreateDemo(() => DemoNow); model.CompleteSetup();
        model.SetAccounts(model.AccountSettings.Select(a => a.Name == "Account three" ? a with { BudgetHours = 1.75, BudgetPeriod = "week" } : a.Name == "Account four" ? a with { BudgetHours = 5, BudgetPeriod = "week" } : a));
        return model;
    }
    // The card laid out in a window of its own, since a pop-up has no window headless.
    private static (Window Window, PillAlertsCard Card) Host(TimerModel model, Action? fold = null, Action? open = null)
    {
        var card = new PillAlertsCard(model, fold ?? (() => { }), open ?? (() => { })) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 420, Height = 520, Content = card, SystemDecorations = SystemDecorations.None }; window.Show(); Settle(window); return (window, card);
    }

    [AvaloniaFact]
    public void WhatIsWaitingIsMostUrgentFirstAndTheKeysStayTheSame()
    {
        var model = Demo(); var items = model.Alerts;
        Assert.True(items.Count >= 3, "the demo has late reminders and budgets");
        Assert.Equal(items.OrderBy(i => i.Rank).ThenBy(i => i.Since).ThenBy(i => i.Key, StringComparer.Ordinal).Select(i => i.Key), items.Select(i => i.Key));
        Assert.Equal(items.Select(i => i.Key), model.Alerts.Select(i => i.Key));
        Assert.Equal(items.Count, items.Select(i => i.Key).Distinct().Count());
        // A reminder a day or more late outranks a spent budget, which outranks one that passed today, then a budget at 80 per cent.
        var ranks = items.Select(i => i.Rank).ToList(); Assert.Equal(ranks.OrderBy(r => r), ranks);
        Assert.All(items.Where(i => i.Kind == AlertKind.Reminder), i => Assert.Contains(i.Rank, new[] { 0, 2 }));
        Assert.All(items.Where(i => i.Kind == AlertKind.Budget), i => Assert.Equal(3, i.Rank)); Assert.All(items.Where(i => i.Kind == AlertKind.BudgetSpent), i => Assert.Equal(1, i.Rank));
        Assert.Equal("3 min late", PillAlertItems.LateBy(TimeSpan.FromMinutes(3))); Assert.Equal("2 h late", PillAlertItems.LateBy(TimeSpan.FromMinutes(130))); Assert.Equal("2 days late", PillAlertItems.LateBy(TimeSpan.FromDays(2.2))); Assert.Equal("1 day late", PillAlertItems.LateBy(TimeSpan.FromHours(30)));
    }

    [AvaloniaFact]
    public void TheCardHasItsOwnFill_SoNothingBehindItShowsThrough()
    {
        // A style that names Border alone skips the card (a subclass), which left it see-through over whatever was under it.
        var model = Demo(); var (window, card) = Host(model);
        try
        {
            foreach (var view in new[] { "first", "second" })
            {
                card.Open(); if (view == "second") card.OpenRow(1);
                Settle(window);
                var fill = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(card.Background);
                Assert.True(fill.Color.A == 255, view + " is opaque");
                Assert.True(card.TryFindResource("PillBody", card.ActualThemeVariant, out var body) && body is Avalonia.Media.ISolidColorBrush solid && solid.Color == fill.Color, view + " wears the pill's own colour");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePrimaryActionsOfAReminderStayOnOneLineWithRoomToSpare()
    {
        // On Windows the type is a little wider than here, so the two keys and the link keep at least 12 pixels between them.
        var model = Demo(); var (window, card) = Host(model);
        try
        {
            card.Open(); Settle(window);
            var buttons = new[] { "AlertDone", "AlertLater", "AlertReschedule" }.Select(n => Find(window, n)).ToList();
            Assert.Single(buttons.Select(b => Math.Round(b.TranslatePoint(new Point(0, b.Bounds.Height / 2), card)!.Value.Y / 2)).Distinct()); // On one line: their middles agree (the link is shorter than the keys).
            // The two keys sit at the left and Reschedule is words at the right edge of the open row, so the room that matters is the gap between them.
            var later = buttons[1]; var link = buttons[2];
            var keysEnd = later.TranslatePoint(new Point(later.Bounds.Width, 0), card)!.Value.X; var linkStart = link.TranslatePoint(new Point(), card)!.Value.X;
            Assert.True(linkStart - keysEnd >= 12, $"the keys end at {keysEnd} and Reschedule starts at {linkStart}");
            var linkEnd = link.TranslatePoint(new Point(link.Bounds.Width, 0), card)!.Value.X; Assert.True(linkEnd <= card.Bounds.Width - card.Padding.Right + .5, $"Reschedule ends at {linkEnd} in a card {card.Bounds.Width} wide");
        }
        finally { window.Close(); }
    }

    private static string OpenWords(Window window) => ((TextBlock)window.GetVisualDescendants().First(c => c is TextBlock { Name: "AlertWords" })).Text!;
    private static int ClosedRows(Window window) => window.GetVisualDescendants().OfType<Border>().Count(b => b.Name?.StartsWith("AlertListRow_") == true);

    [AvaloniaFact]
    public void TheCardIsOneListWithOneRowOpen_PressingAnotherOpensItAndFoldsTheFirst_AndUpAndDownMoveIt()
    {
        var model = Demo(); var (window, card) = Host(model);
        try
        {
            card.Open(); Settle(window); var n = card.Items.Count;
            Assert.Equal(PillAlertsCard.CardWidth, card.Bounds.Width, 0.6);
            Assert.Equal($"{n} waiting", ((TextBlock)window.GetVisualDescendants().First(c => c is TextBlock { Name: "AlertTitle" })).Text);
            Assert.Equal(0, card.Index); Assert.Equal(card.Items[0].Words, OpenWords(window));
            // Exactly one row is open; every other alert is one line.
            Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "AlertOpenRow"); Assert.Equal(n - 1, ClosedRows(window));
            Press(window, "AlertRow_2"); Settle(window); Assert.Equal(2, card.Index); Assert.Equal(card.Items[2].Words, OpenWords(window));
            Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "AlertOpenRow"); Assert.Equal(n - 1, ClosedRows(window)); Assert.Equal(n, card.Items.Count);
            // The old arrows, dots, count and See all are gone.
            foreach (var gone in new[] { "AlertNext", "AlertPrevious", "AlertSeeAll", "AlertBack", "AlertListBack", "AlertDotButton_0" }) Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Name == gone);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); Settle(window); Assert.Equal(3 % n, card.Index);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null); Settle(window); Assert.Equal(2, card.Index);
            Assert.True(card.HandleKey(Key.Up)); Assert.True(card.HandleKey(Key.Up)); Assert.True(card.HandleKey(Key.Up)); Assert.Equal(n - 1, card.Index); // Up from the first goes to the last.
            Assert.False(card.HandleKey(Key.Left)); Assert.False(card.HandleKey(Key.Right)); // Left and Right no longer step.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AReminderRowShowsItsLatenessUntilThePointerIsOnIt_ThenATickAndAClock()
    {
        var model = Demo(); var (window, card) = Host(model);
        try
        {
            card.Open(); Settle(window);
            var row = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "AlertListRow_1"); Assert.Equal(AlertKind.Reminder, card.Items[1].Kind);
            var late = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "AlertRowLate_1"); Assert.Equal(1, late.Opacity); Assert.Equal(card.Items[1].Detail, late.Text);
            Assert.Equal(0, ((Control)Find(window, "AlertRowDone_1").Parent!).Opacity); // The tick and clock wait, unseen but measured, beside the lateness.
            var key = card.Items[1].Key;
            window.MouseMove(row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value, RawInputModifiers.None);
            Settle(window); Assert.Equal(1, ((Control)Find(window, "AlertRowDone_1").Parent!).Opacity); Assert.True(((Control)Find(window, "AlertRowDone_1").Parent!).IsHitTestVisible); Assert.Equal(0, late.Opacity);
            Press(window, "AlertRowDone_1"); Settle(window); Assert.DoesNotContain(model.Alerts, i => i.Key == key);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NothingComingUpIsListedAndTheListHasNoScrollBar()
    {
        var model = Demo(); var (window, card) = Host(model);
        try
        {
            card.Open(); Settle(window);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name == "AlertComingToggle" || c.Name?.StartsWith("AlertComing_") == true); // Open Notes shows what is coming.
            Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, window.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Name == "AlertListScroll").VerticalScrollBarVisibility);
            Assert.NotNull(Find(window, "AlertOpenDashboard"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void APressAnywhereOnAClosedRowOpensItNotOnlyOnItsWords()
    {
        var model = Demo(); var (window, card) = Host(model);
        try
        {
            card.Open(); Settle(window); Assert.Equal(0, card.Index);
            var row = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "AlertListRow_1");
            // The far right of the row, past the words, is on the row's padding and the lateness text, not on the button that holds the words.
            var at = row.TranslatePoint(new Point(row.Bounds.Width - 3, row.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, Avalonia.Input.MouseButton.Left, RawInputModifiers.None); window.MouseUp(at, Avalonia.Input.MouseButton.Left, RawInputModifiers.None); Settle(window);
            Assert.Equal(1, card.Index);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DoneOnAReminder_TakesItAway_AndTheNextSlidesIn_AndTheLastOneEmptiesTheCard()
    {
        var model = Demo(); var (window, card) = Host(model);
        var emptied = false; card.Emptied += () => emptied = true;
        try
        {
            card.Open(); Settle(window);
            var first = card.Items[0]; Assert.Equal(AlertKind.Reminder, first.Kind); var before = card.Items.Count;
            Press(window, "AlertDone"); Settle(window);
            Assert.Equal(before - 1, card.Items.Count); Assert.DoesNotContain(card.Items, i => i.Key == first.Key);
            Assert.NotEqual(first.Words, ((TextBlock)window.GetVisualDescendants().First(c => c is TextBlock { Name: "AlertWords" })).Text);
            // Deal with everything that is left: OK for a budget, Done or In 10 min for a reminder.
            for (var guard = 0; guard < 30 && card.Items.Count > 0; guard++)
            {
                card.Open(); Settle(window);
                var item = card.Items[0];
                Press(window, item.Kind == AlertKind.Reminder ? "AlertDone" : item.Kind == AlertKind.Break ? "AlertStay" : "AlertOk"); Settle(window);
            }
            Assert.Empty(card.Items); Assert.True(emptied);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void InTenMinutesMovesTheReminder_AndABudgetIsPutAwayWithOk()
    {
        var model = Demo(); var (window, card) = Host(model);
        try
        {
            card.Open(); Settle(window); var reminder = card.Items[0]; Assert.Equal(AlertKind.Reminder, reminder.Kind);
            Press(window, "AlertLater"); Settle(window);
            Assert.DoesNotContain(model.Alerts, i => i.Key == reminder.Key); // Its time is ten minutes on, so it is no longer waiting.
            Assert.Equal(DemoNow.AddMinutes(10), NotesModel.ReminderOf(model.Notes.Find(reminder.NoteId!.Value)!));
            var budget = model.Alerts.First(i => i.Kind is AlertKind.Budget or AlertKind.BudgetSpent);
            card.Open(); card.OpenRow(model.Alerts.ToList().FindIndex(i => i.Key == budget.Key)); Settle(window); Press(window, "AlertOk"); Settle(window);
            Assert.DoesNotContain(model.Alerts, i => i.Key == budget.Key);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OpenInTheDashboardAsksForTheNotesPageAndOpensTheDashboard()
    {
        var model = Demo(); var pages = new List<string>(); model.PageRequested += pages.Add; var opened = 0; var folded = 0;
        var (window, card) = Host(model, () => folded++, () => opened++);
        try
        {
            card.Open(); Settle(window); Press(window, "AlertOpenDashboard"); Settle(window);
            Assert.Equal(new[] { "notes" }, pages); Assert.Equal(1, opened); Assert.True(folded >= 1);
            card.Open(); card.OpenRow(model.Alerts.ToList().FindIndex(i => i.Kind is AlertKind.Budget or AlertKind.BudgetSpent)); Settle(window); Press(window, "AlertOpenWeek"); Settle(window);
            Assert.Equal(new[] { "notes", "week" }, pages);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AnEmptyPillShowsNothingWaiting_AndALongBreakAsksToResumeAndStayOnBreakAsksAgainLater()
    {
        var now = new DateTime(2026, 10, 21, 13, 0, 0);
        var model = new TimerModel(new TrackingEngine(clock: () => now), new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        Assert.Empty(model.Alerts); model.SetBreakAllowance(TimeSpan.FromMinutes(30));
        model.Engine.ToggleAccount("Account one"); now = now.AddMinutes(30); model.ToggleBreak(); Assert.True(model.IsOnBreak);
        Assert.Empty(model.Alerts); // Inside the allowance.
        now = now.AddMinutes(45); var item = Assert.Single(model.Alerts);
        Assert.Equal(AlertKind.Break, item.Kind); Assert.Equal("Back to Account one?", item.Words); Assert.Equal("45 min so far", item.Detail);
        var (window, card) = Host(model);
        try
        {
            card.Open(); Settle(window); Press(window, "AlertStay"); Settle(window); Assert.Empty(model.Alerts);
            now = now.AddMinutes(16); Assert.Single(model.Alerts); // Asked again after fifteen minutes.
            card.Open(); Settle(window); Press(window, "AlertResume"); Settle(window);
            Assert.False(model.IsOnBreak); Assert.Equal("Account one", model.ActiveAccount); Assert.Empty(model.Alerts);
            card.Open(); Settle(window); Assert.Equal("Nothing waiting", ((TextBlock)window.GetVisualDescendants().First(c => c is TextBlock { Name: "AlertTitle" })).Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePillsLightTakesTheColourOfWhatIsWaiting_ShowsACountAndOpensTheCard()
    {
        var model = Demo(); var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var count = model.Alerts.Count; Assert.True(count > 1);
            // The count sits in the display, always two digits (the leading one blank under ten), in the colour of the most urgent thing waiting.
            var digits = pill.GetVisualDescendants().OfType<SegmentRun>().First(r => r.Name == "PillAlertCount");
            Assert.Equal(count < 10 ? " " + count : count.ToString(), digits.Text); Assert.NotNull(digits.Colour);
            Assert.False(pill.AlertsOpen); pill.AlertsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(pill);
            Assert.True(pill.AlertsOpen); Assert.Equal(0, pill.AlertsCard!.Index);
            pill.AlertsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(pill); Assert.False(pill.AlertsOpen); // Pressing the light again folds it.
            pill.ShowAlerts(); pill.ShowPanel(); Assert.False(pill.AlertsOpen); Assert.True(pill.PanelOpen); // One list at a time.
            pill.ShowAlerts(); Assert.True(pill.AlertsOpen); Assert.False(pill.PanelOpen);
            pill.CloseFlyouts(); Assert.False(pill.AlertsOpen);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void ACardOpensByItselfForSomethingNew_OnlyUnderPeek_AndNotForWhatWasAlreadyWaiting()
    {
        var now = new DateTime(2026, 10, 21, 13, 0, 0);
        var model = new TimerModel(new TrackingEngine(clock: () => now), new StoreSettings(new() { new("Account one") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        model.Notes.AddNote("Old one", null, now.AddMinutes(-5));
        var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            pill.PeekAtNewAlerts(); Assert.False(pill.AlertsOpen); // It was waiting before the pill came up.
            model.Notes.AddNote("New one", null, now.AddMinutes(-1)); pill.PeekAtNewAlerts(); Assert.True(pill.AlertsOpen); Assert.Equal(0, pill.AlertsCard!.Index);
            pill.FoldAlerts(); pill.PeekAtNewAlerts(); Assert.False(pill.AlertsOpen); // Already shown once.
            model.SetPillAlertsMode("light"); model.Notes.AddNote("Third", null, now.AddMinutes(-1)); pill.PeekAtNewAlerts(); Assert.False(pill.AlertsOpen);
            pill.AlertsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(pill); Assert.True(pill.AlertsOpen); // Light only opens it when asked.
            pill.FoldAlerts(); model.SetPillAlertsMode("off"); Assert.Equal("off", model.PillAlertsMode);
            Assert.Equal("  ", pill.GetVisualDescendants().OfType<SegmentRun>().First(r => r.Name == "PillAlertCount").Text); // Off keeps the pill quiet.
            model.SetPillAlertsMode("peek"); Assert.Equal("peek", model.PillAlertsMode);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void PillAlertsIsAChoiceOfThreePicturesInSettingsAlerts()
    {
        var model = Demo(); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        try
        {
            dashboard.OpenSettings(); dashboard.GetVisualDescendants().OfType<SettingsView>().Single().ShowSection("alerts"); Settle(dashboard);
            Button Tile(string name) => dashboard.GetVisualDescendants().OfType<Button>().First(b => b.Name == name);
            Assert.Contains("selected", Tile("AlertsPeek").Classes);
            Tile("AlertsLight").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal("light", model.PillAlertsMode); Assert.Contains("selected", Tile("AlertsLight").Classes);
            Tile("AlertsOff").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal("off", model.PillAlertsMode);
            Tile("AlertsPeek").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal("peek", model.PillAlertsMode);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void NothingInTheCardOverlapsAnythingElse_AndItNeverGrowsPastItsLimit()
    {
        var model = Demo(); var (window, card) = Host(model);
        try
        {
            foreach (var view in new[] { "row0", "row1", "row3" })
            {
                card.Open(); card.OpenRow(int.Parse(view[3..]));
                Settle(window);
                Assert.True(card.Bounds.Height <= card.MaxHeight + .5, view + " is within its limit");
                var parts = card.GetVisualDescendants().OfType<Control>().Where(c => c.IsVisible && c.Bounds.Width > 0 && (c is Button || c is TextBlock { Text.Length: > 0 })).ToList();
                foreach (var part in parts)
                {
                    var box = new Rect(part.TranslatePoint(new Point(), card)!.Value, part.Bounds.Size);
                    if (card.GetVisualDescendants().OfType<ScrollViewer>().Any()) continue; // A scrolling list may run past its edge by design.
                    Assert.True(box.Right <= card.Bounds.Width + .5 && box.Bottom <= card.Bounds.Height + .5, $"{view}: {(part as TextBlock)?.Text ?? part.Name} stays inside the card");
                }
            }
        }
        finally { window.Close(); }
    }

    // Pictures for looking at the layout (written only when TIMER_PREVIEW_DIR is set): the pill with its light and count, then the card, the list and a row opened from it, in both themes.
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void WritePictures(string theme)
    {
        if (Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR") is not { } folder) return;
        Directory.CreateDirectory(folder);
        var model = Demo(); model.SetTheme(theme);
        void Save(Window w, string name) { Settle(w); using var shot = w.CaptureRenderedFrame(); shot?.Save(Path.Combine(folder, $"alerts-{name}-{theme}.png")); }
        var pill = new PillWindow(model); pill.Show(); Save(pill, "pill"); pill.Close();
        var (window, card) = Host(model);
        try
        {
            card.Open(); Save(window, "first"); card.OpenRow(3); Save(window, "budget"); card.OpenRow(1); Save(window, "second"); card.OpenRow(card.Items.ToList().FindIndex(i => i.Kind is AlertKind.Budget or AlertKind.BudgetSpent)); Save(window, "budgetopen");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EveryPointOfTheListBelongsToARow_SoNoPressLandsInADeadStrip()
    {
        var model = Demo(); var (window, card) = Host(model);
        try
        {
            card.Open(); Settle(window);
            var rows = window.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("AlertListRow_") == true || b.Name == "AlertOpenRow").OrderBy(b => b.TranslatePoint(default, window)!.Value.Y).ToList();
            for (var i = 1; i < rows.Count; i++)
            {
                var above = rows[i - 1]; var below = rows[i];
                var gap = below.TranslatePoint(default, window)!.Value.Y - (above.TranslatePoint(default, window)!.Value.Y + above.Bounds.Height);
                Assert.True(gap <= 4, $"{above.Name} to {below.Name}: {gap}"); // The open row's own margin is the most between two.
            }
            // A press at the very foot of a closed row opens it.
            var closed = rows.First(r => r.Name == "AlertListRow_1"); var spot = closed.TranslatePoint(new Point(closed.Bounds.Width - 6, closed.Bounds.Height - 2), window)!.Value;
            window.MouseDown(spot, MouseButton.Left); window.MouseUp(spot, MouseButton.Left); Settle(window);
            Assert.Equal(1, card.Index);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WithNothingWaitingTheCardIsOneShortLineWithOpenNotesBeside()
    {
        var model = TimerModel.CreateDemo(() => DemoNow); model.CompleteSetup(); model.Notes.All.ToList().ForEach(n => { });
        var empty = new TimerModel(new TrackingEngine(clock: () => DemoNow), new StoreSettings(new List<AccountSettings> { new("Account one") }, false, "Light", DayOfWeek.Monday)); empty.CompleteSetup();
        var (window, card) = Host(empty);
        try
        {
            card.Open(); Settle(window);
            Assert.Equal(0, card.Items.Count); Assert.True(card.Bounds.Height < 70, $"card is {card.Bounds.Height} tall");
            var title = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "AlertTitle"); var link = Find(window, "AlertOpenDashboard");
            Assert.Equal(title.TranslatePoint(default, window)!.Value.Y + title.Bounds.Height / 2, link.TranslatePoint(default, window)!.Value.Y + link.Bounds.Height / 2, 4); // On one line.
        }
        finally { window.Close(); }
    }
}
