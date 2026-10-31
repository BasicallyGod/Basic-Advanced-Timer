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

// Round 28: the pill's alerts, built as the board "Pill notifications: moving between them" draws them: the light, one card at a time with arrows, dots and a list.
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
    public void TheCardSteps_WithArrowsDotsAndKeys_AndWrapsAround()
    {
        var model = Demo(); var (window, card) = Host(model);
        try
        {
            card.ShowCard(0); Settle(window); var n = card.Items.Count;
            Assert.Equal("card", card.View); Assert.Equal($"1 of {n}", ((TextBlock)window.GetVisualDescendants().First(c => c is TextBlock { Name: "AlertOf" })).Text);
            Assert.Equal(card.Items[0].Words, ((TextBlock)window.GetVisualDescendants().First(c => c is TextBlock { Name: "AlertWords" })).Text);
            Press(window, "AlertNext"); Settle(window); Assert.Equal(1, card.Index); Assert.Equal($"2 of {n}", ((TextBlock)window.GetVisualDescendants().First(c => c is TextBlock { Name: "AlertOf" })).Text);
            Press(window, "AlertPrevious"); Press(window, "AlertPrevious"); Settle(window); Assert.Equal(n - 1, card.Index); // Back from the first goes to the last.
            Press(window, "AlertDotButton_1"); Settle(window); Assert.Equal(1, card.Index);
            // A dot for each, the current one in the accent.
            Assert.Equal(n, window.GetVisualDescendants().OfType<Border>().Count(b => b.Name?.StartsWith("AlertDot_") == true));
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null); Settle(window);
            Assert.Equal(2 % n, card.Index);
            window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null); Settle(window);
            Assert.Equal(1, card.Index);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SeeAllOpensTheListInTheCardsPlace_ARowOpensItsCard_AndBackReturnsToTheList()
    {
        var model = Demo(); var (window, card) = Host(model);
        try
        {
            card.ShowCard(0); Settle(window); Press(window, "AlertSeeAll"); Settle(window);
            Assert.Equal("list", card.View); Assert.Equal(PillAlertsCard.CardWidth, card.Bounds.Width, 0.6);
            Assert.Equal(card.Items.Count, window.GetVisualDescendants().OfType<Border>().Count(b => b.Name?.StartsWith("AlertListRow_") == true));
            Press(window, "AlertRow_1"); Settle(window); Assert.Equal("detail", card.View); Assert.Equal(1, card.Index);
            Assert.Equal(card.Items[1].Words, ((TextBlock)window.GetVisualDescendants().First(c => c is TextBlock { Name: "AlertWords" })).Text);
            Press(window, "AlertBack"); Settle(window); Assert.Equal("list", card.View);
            Press(window, "AlertListBack"); Settle(window); Assert.Equal("card", card.View);
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
            card.ShowCard(0); Settle(window);
            var first = card.Items[0]; Assert.Equal(AlertKind.Reminder, first.Kind); var before = card.Items.Count;
            Press(window, "AlertDone"); Settle(window);
            Assert.Equal(before - 1, card.Items.Count); Assert.DoesNotContain(card.Items, i => i.Key == first.Key);
            Assert.NotEqual(first.Words, ((TextBlock)window.GetVisualDescendants().First(c => c is TextBlock { Name: "AlertWords" })).Text);
            // Deal with everything that is left: OK for a budget, Done or In 10 min for a reminder.
            for (var guard = 0; guard < 30 && card.Items.Count > 0; guard++)
            {
                card.ShowCard(0); Settle(window);
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
            card.ShowCard(0); Settle(window); var reminder = card.Items[0]; Assert.Equal(AlertKind.Reminder, reminder.Kind);
            Press(window, "AlertLater"); Settle(window);
            Assert.DoesNotContain(model.Alerts, i => i.Key == reminder.Key); // Its time is ten minutes on, so it is no longer waiting.
            Assert.Equal(DemoNow.AddMinutes(10), NotesModel.ReminderOf(model.Notes.Find(reminder.NoteId!.Value)!));
            var budget = model.Alerts.First(i => i.Kind is AlertKind.Budget or AlertKind.BudgetSpent);
            card.ShowCard(model.Alerts.ToList().FindIndex(i => i.Key == budget.Key)); Settle(window); Press(window, "AlertOk"); Settle(window);
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
            card.ShowList(); Settle(window); Press(window, "AlertOpenDashboard"); Settle(window);
            Assert.Equal(new[] { "notes" }, pages); Assert.Equal(1, opened); Assert.True(folded >= 1);
            card.ShowCard(model.Alerts.ToList().FindIndex(i => i.Kind is AlertKind.Budget or AlertKind.BudgetSpent)); Settle(window); Press(window, "AlertOpenWeek"); Settle(window);
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
            card.ShowCard(0); Settle(window); Press(window, "AlertStay"); Settle(window); Assert.Empty(model.Alerts);
            now = now.AddMinutes(16); Assert.Single(model.Alerts); // Asked again after fifteen minutes.
            card.ShowCard(0); Settle(window); Press(window, "AlertResume"); Settle(window);
            Assert.False(model.IsOnBreak); Assert.Equal("Account one", model.ActiveAccount); Assert.Empty(model.Alerts);
            card.ShowList(); Settle(window); Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "AlertNothing");
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
            Assert.True(pill.AlertsBadge.IsVisible); Assert.Equal(count > 9 ? "9+" : count.ToString(), ((TextBlock)pill.AlertsBadge.Child!).Text);
            var light = pill.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PillReminderLight");
            Assert.True(light.Background is Avalonia.Media.ISolidColorBrush { Color.A: 255 }, "lit, not the quiet grey");
            Assert.False(pill.AlertsOpen); pill.AlertsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(pill);
            Assert.True(pill.AlertsOpen); Assert.Equal("card", pill.AlertsCard!.View);
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
            model.Notes.AddNote("New one", null, now.AddMinutes(-1)); pill.PeekAtNewAlerts(); Assert.True(pill.AlertsOpen); Assert.Equal("card", pill.AlertsCard!.View);
            pill.FoldAlerts(); pill.PeekAtNewAlerts(); Assert.False(pill.AlertsOpen); // Already shown once.
            model.SetPillAlertsMode("light"); model.Notes.AddNote("Third", null, now.AddMinutes(-1)); pill.PeekAtNewAlerts(); Assert.False(pill.AlertsOpen);
            pill.AlertsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(pill); Assert.Equal("list", pill.AlertsCard!.View); // Light only opens the list when asked.
            pill.FoldAlerts(); model.SetPillAlertsMode("off"); Assert.Equal("off", model.PillAlertsMode);
            Assert.False(pill.AlertsBadge.IsVisible); // Off keeps the pill quiet.
            model.SetPillAlertsMode("peek"); Assert.Equal("peek", model.PillAlertsMode);
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void PillAlertsIsAChoiceInSettingsBehaviour()
    {
        var model = Demo(); var dashboard = new DashboardWindow(model); dashboard.Show(); Settle(dashboard);
        try
        {
            dashboard.OpenSettings(); dashboard.GetVisualDescendants().OfType<SettingsView>().Single().ShowSection("behaviour"); Settle(dashboard);
            var choice = dashboard.GetVisualDescendants().OfType<ComboBox>().First(b => b.Name == "PillAlertsChoice");
            Assert.Equal(0, choice.SelectedIndex); choice.SelectedIndex = 1; Assert.Equal("light", model.PillAlertsMode); choice.SelectedIndex = 2; Assert.Equal("off", model.PillAlertsMode); choice.SelectedIndex = 0; Assert.Equal("peek", model.PillAlertsMode);
        }
        finally { dashboard.Close(); }
    }

    [AvaloniaFact]
    public void NothingInTheCardOverlapsAnythingElse_AndItNeverGrowsPastItsLimit()
    {
        var model = Demo(); var (window, card) = Host(model);
        try
        {
            foreach (var view in new[] { "card0", "card1", "list", "detail1" })
            {
                if (view.StartsWith("card")) card.ShowCard(int.Parse(view[4..])); else if (view == "list") card.ShowList(); else card.ShowDetail(1);
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
            card.ShowCard(0); Save(window, "card"); card.ShowCard(3); Save(window, "budget"); card.ShowList(); Save(window, "list"); card.ShowDetail(1); Save(window, "detail");
        }
        finally { window.Close(); }
    }
}
