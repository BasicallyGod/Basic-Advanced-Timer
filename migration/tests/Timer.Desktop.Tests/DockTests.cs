using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>The dashboard's docked account panel: its motion as numbers, and how each page gives way to it at none of it, half of it and all of it.</summary>
public sealed class DockTests
{
    private static T Find<T>(Window w, string name) where T : Control => w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static bool Has(Window w, string name) => w.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name);
    private static void Settle(Window w) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); }
    private static void Click(Control c) => c.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    private static TimerModel Demo() { var m = TimerModel.CreateDemo(); m.CompleteSetup(); return m; }
    private static DockMotion Eased(bool open = false, bool pinned = true) { Motion.Override = false; return new DockMotion(open, pinned) { Animated = true }; }
    private static void Run(DockMotion motion, double milliseconds) { for (var t = 0.0; t < milliseconds - .001; t += 4) motion.Advance(TimeSpan.FromMilliseconds(Math.Min(4, milliseconds - t))); }

    // ---- The motion ----

    [Fact]
    public void OpeningEasesOutQuicklyAndArrivesInTwoHundredAndEightyMilliseconds()
    {
        var motion = Eased(); motion.SetOpen(true);
        Assert.Equal(0, motion.P); Assert.True(motion.Moving);
        Run(motion, 100); Assert.InRange(motion.P, .6, .95); // Most of the width arrives in the first 100 ms.
        Run(motion, 170); Assert.InRange(motion.P, .97, 1);
        Run(motion, 20); Assert.Equal(1, motion.P); Assert.False(motion.Moving);
    }

    [Fact]
    public void ClosingTakesTwoHundredAndTwentyMillisecondsAndNeverOvershoots()
    {
        var motion = Eased(open: true); motion.SetOpen(false);
        var last = 1.0;
        for (var t = 0; t < 230; t += 4) { motion.Advance(TimeSpan.FromMilliseconds(4)); Assert.InRange(motion.P, 0, last + 1e-9); last = motion.P; }
        Assert.Equal(0, motion.P); Assert.False(motion.Moving);
    }

    [Fact]
    public void TurningRoundHalfWayCarriesOnFromWhereItIsWithNoJump()
    {
        var motion = Eased(); motion.SetOpen(true); Run(motion, 80);
        var before = motion.P; Assert.InRange(before, .2, .9);
        motion.SetOpen(false);
        Assert.Equal(before, motion.P, 6); // The value does not move when the direction does.
        Run(motion, 4); Assert.True(motion.P <= before); // And it heads back from there.
        Run(motion, 300); Assert.Equal(0, motion.P);
    }

    [Fact]
    public void ThePageGivesUpRoomOnlyWhileDockedAndPinningEasesIt()
    {
        var motion = Eased(open: true, pinned: false);
        Assert.Equal(0, motion.Room); // Floating over the page, it takes none.
        motion.SetPinned(true); Run(motion, 130); Assert.InRange(motion.Room, .2, .9);
        Run(motion, 150); Assert.Equal(1, motion.Room);
        motion.SetOpen(false); Run(motion, 300); Assert.Equal(0, motion.Room);
    }

    [Fact]
    public void LessMotionAskedForJumpsStraightToTheEnd()
    {
        Motion.Override = true;
        try { var motion = new DockMotion(false, true) { Animated = true }; motion.SetOpen(true); Assert.Equal(1, motion.P); Assert.False(motion.Moving); }
        finally { Motion.Override = null; }
    }

    [Fact]
    public void ARepeatedAskForWhereItIsAlreadyHeadingChangesNothing()
    {
        var motion = Eased(); motion.SetOpen(true); Run(motion, 60); var at = motion.P;
        motion.SetOpen(true); Assert.Equal(at, motion.P); Run(motion, 4); Assert.True(motion.P > at); // Not set off again from the start.
        Assert.Equal(.5, DockMotion.Window(.5, 0, 1)); Assert.Equal(0, DockMotion.Window(-1, 0, 1)); Assert.Equal(1, DockMotion.Window(2, 0, 1));
    }

    // ---- The panel and the page ----

    [AvaloniaFact]
    public void TheOpenAndPinnedChoicesAreRememberedAndAFreshDashboardStartsWhereTheLastLeftOff()
    {
        var model = Demo(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.False(model.AccountsDockOpen); Assert.True(model.AccountsDocked); Assert.Equal(0, window.Dock.P);
            window.ShowAccountsPanel(); Click(Find<Button>(window, "RailPinAccounts")); Settle(window);
            Assert.True(model.AccountsDockOpen); Assert.False(model.AccountsDocked);
        }
        finally { window.Close(); }
        var again = new DashboardWindow(model); again.Show(); Settle(again);
        try { Assert.Equal(1, again.Dock.P); Assert.Equal(0, again.Dock.Pin); Assert.True(again.AccountsOpen); Assert.False(again.AccountsDockedNow); }
        finally { again.Close(); }
    }

    [AvaloniaFact]
    public void DockedThePagesRoomIsTheSpacerAndFloatingItIsNotWithADimmedPageBehind()
    {
        var model = Demo(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var room = Find<Border>(window, "DockRoom"); var drawer = Find<Border>(window, "AccountsDrawer"); var scrim = Find<Border>(window, "AccountsScrim");
            Assert.Equal(0, room.Bounds.Width, .5);
            window.ShowAccountsPanel(); Settle(window);
            Assert.Equal(DockMotion.PanelWidth, room.Bounds.Width, .5); Assert.Equal(DockMotion.PanelWidth, drawer.Bounds.Width, .5); // The page makes room, edge to edge with the panel.
            Assert.False(scrim.IsHitTestVisible); Assert.Equal(0, ((Avalonia.Media.SolidColorBrush)scrim.Background!).Color.A);
            Click(Find<Button>(window, "RailPinAccounts")); Settle(window);
            Assert.Equal(0, room.Bounds.Width, .5); Assert.Equal(DockMotion.PanelWidth, drawer.Bounds.Width, .5);
            Assert.True(scrim.IsHitTestVisible); Assert.True(((Avalonia.Media.SolidColorBrush)scrim.Background!).Color.A > 0);
            Assert.NotEqual(default, drawer.BoxShadow); // Floating, it casts its shadow.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheRowsAndTheHeadingComeInInOrderAsThePanelOpensAndAreLeftAloneWhenItIsOpen()
    {
        var model = Demo(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            window.Dock.Jump(.3, 1); Settle(window); // A panel that is shut is not laid out at all, so it has to be out to have rows to look at.
            var rows = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name is { } n && n.StartsWith("Account") && n.Length <= 9 && char.IsDigit(n[^1])).OrderBy(b => b.Name).ToList();
            Assert.True(rows.Count >= 3);
            Assert.True(rows[0].Opacity > rows[1].Opacity && rows[1].Opacity >= rows[2].Opacity, $"{rows[0].Opacity} {rows[1].Opacity} {rows[2].Opacity}");
            Assert.InRange(rows[0].Opacity, .01, .99); Assert.False(rows[0].RenderTransform is null || rows[0].RenderTransform.Value.IsIdentity);
            window.Dock.Jump(1, 1); Settle(window);
            Assert.All(rows, r => { Assert.Equal(1, r.Opacity); Assert.True(r.RenderTransform is null || r.RenderTransform.Value.IsIdentity); });
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheTimelineKeepsEverythingItNeedsAtNoneHalfAndAllOfTheRoomTaken()
    {
        var model = Demo(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var today = Find<Fold>(window, "TodayFold"); var unallocated = Find<Fold>(window, "TotalsUnallocatedFold"); var breaks = Find<Fold>(window, "TotalsBreaksFold");
            var host = Find<Reserve>(window, "SelectedBlockHost"); var notes = Find<NotesCard>(window, "NotesCard");
            Assert.Equal(0, today.Amount); Assert.True(host.IsVisible); Assert.False(Has(window, "CardKindSelected") && Find<Button>(window, "CardKindSelected").IsEffectivelyVisible);
            var widthShut = Find<Border>(window, "TimelineCard").Bounds.Width;

            window.Dock.Jump(.25, 1); Settle(window); // Part way: the folds are part folded, the card is narrowing, nothing overflows the window.
            Assert.InRange(today.Amount, .01, .99); Assert.InRange(host.Bounds.Width, 1, host.Bounds.Width + 1);
            Assert.True(Find<Border>(window, "TimelineCard").Bounds.Right <= Find<Border>(window, "DashboardSurface").Bounds.Right);

            window.Dock.Jump(1, 1); Settle(window);
            Assert.Equal(1, today.Amount); Assert.Equal(1, unallocated.Amount); Assert.Equal(1, breaks.Amount);
            Assert.False(today.IsVisible); Assert.False(Find<StackPanel>(window, "TotalsUnallocated").IsEffectivelyVisible);
            Assert.False(host.IsVisible); Assert.True(notes.HasSelectedTab); Assert.True(Find<Button>(window, "CardKindSelected").IsEffectivelyVisible);
            var strip = Find<StackPanel>(window, "DayTotals"); var tools = strip.Parent!; // The figures, the zoom buttons and Add time all fit inside the card.
            var card = Find<Border>(window, "TimelineCard");
            Assert.True(card.Bounds.Width < widthShut - 200, $"{card.Bounds.Width} vs {widthShut}");
            foreach (var name in new[] { "AddTime", "TimelineZoomOut", "TimelineZoomIn", "RedoEdit" })
            {
                var button = Find<Button>(window, name); var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), card)!.Value.X;
                Assert.InRange(right, 0, card.Bounds.Width + .5);
            }
            Assert.True(strip.TranslatePoint(new Point(strip.Bounds.Width, 0), card)!.Value.X <= Find<Button>(window, "TimelineZoomOut").TranslatePoint(default, card)!.Value.X + .5, "the figures run into the zoom buttons");

            window.Dock.Jump(0, 1); Settle(window); // And everything comes back.
            Assert.Equal(0, today.Amount); Assert.True(host.IsVisible); Assert.Equal(widthShut, Find<Border>(window, "TimelineCard").Bounds.Width, .5); Assert.True(Find<StackPanel>(window, "TotalsUnallocated").IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSelectedBlockMovesIntoATabOfTheNotesCardAndComesBackAndPickingABlockOpensIt()
    {
        var model = Demo(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var card = Find<SelectedBlockCard>(window, "SelectedBlockCard");
            window.Dock.Jump(1, 1); Settle(window);
            Assert.Same(card, Find<ContentControl>(window, "SelectedBlockSlot").Content); // It lives in the Notes card now.
            Assert.False(model.NotesCardShowsSelected); Assert.True(Find<Button>(window, "AddNote").IsEffectivelyVisible);
            model.SelectBlock(model.Engine.Sessions.Take(1).ToArray(), false); Settle(window);
            Assert.True(model.NotesCardShowsSelected); Assert.True(Find<Button>(window, "CardKindSelected").Classes.Contains("optOn"));
            Assert.True(Find<TextBox>(window, "SelectedStart").IsEffectivelyVisible); Assert.False(Find<Button>(window, "AddNote").IsEffectivelyVisible);
            Assert.False(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "SelectedTitle" && t.IsVisible)); // The tab says it already.
            Assert.False(card.Classes.Contains("dayCard")); // Plain inside the tab, with no edge of its own.
            model.ClearSelection(); Settle(window);
            Assert.False(model.NotesCardShowsSelected); Assert.True(Find<Button>(window, "AddNote").IsEffectivelyVisible); // Back to the notes.
            // Folding the panel away again puts the card back under the timeline, with its own edge.
            model.SelectBlock(model.Engine.Sessions.Take(1).ToArray(), false); window.Dock.Jump(0, 1); Settle(window);
            Assert.False(model.NotesCardShowsSelected); Assert.Contains(card.GetVisualAncestors(), a => a is Reserve); Assert.True(card.Classes.Contains("dayCard"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheNotesPageFoldsItsViewsAwayForAChipRowThatHoldsTheSameViewsAndTheAccountFilter()
    {
        var model = Demo(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(Find<Button>(window, "NotesTab")); Settle(window);
            var views = Find<Reserve>(window, "NotesViewsHost"); var chips = Find<Border>(window, "NotesChips");
            Assert.True(views.IsVisible); Assert.False(chips.IsVisible); Assert.Equal(196, views.Bounds.Width, .5);
            window.Dock.Jump(.4, 1); Settle(window);
            Assert.InRange(chips.Height, 1, 49); Assert.InRange(views.Bounds.Width, 1, 195);
            window.Dock.Jump(1, 1); Settle(window);
            Assert.False(views.IsVisible); Assert.True(chips.IsVisible); Assert.Equal(50, chips.Height, .5);
            var page = Find<NotesPage>(window, "NotesPage");
            Click(Find<Button>(window, "NotesChip_Overdue")); Settle(window); Assert.Equal(NoteView.Overdue, page.View); // Each chip is a view.
            Click(Find<Button>(window, "NotesChip_All")); Settle(window);
            Assert.True(Has(window, "NotesChipAccounts"));
            // The search box gives up a little width with the rest.
            Assert.True(Find<TextBox>(window, "NotesSearch").Width < 240);
            window.Dock.Jump(0, 1); Settle(window);
            Assert.True(views.IsVisible); Assert.False(chips.IsVisible); Assert.Equal(240, Find<TextBox>(window, "NotesSearch").Width, .5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheWeekOverviewStillShowsItsFiguresAndEveryDayWithTheDockOpen()
    {
        var model = Demo(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(Find<Button>(window, "WeekOverviewTab")); window.Dock.Jump(1, 1); Settle(window);
            var surface = Find<Border>(window, "DashboardSurface");
            foreach (var name in new[] { "WeekTotals", "WeekByDay", "WeekByAccount" })
            {
                var part = Find<Control>(window, name); var right = part.TranslatePoint(new Point(part.Bounds.Width, 0), surface)!.Value.X;
                Assert.InRange(right, 0, surface.Bounds.Width + .5);
            }
            Assert.Equal(7 - 2, window.GetVisualDescendants().OfType<Button>().Count(b => b.Name is { } n && n.StartsWith("WeekDay_"))); // The five working days are all there.
        }
        finally { window.Close(); }
    }
}
