using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The sidebar: the pill drawn out with its accounts shown, the views as tabs in the header, and the Notes page's account filter as tag pills.
public sealed class SidebarTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TimerModel Model()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(10)), new StoreSettings(new() { new("Account one"), new("Account two"), new("Account three") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static bool Has(Control root, string name) => root.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name);
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window); }
    private static Point Centre(Window window, Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;

    [AvaloniaFact]
    public void NothingRunsPastItsCardIsSqueezedOrOverlapsOnAnyPageInEitherTheme()
    {
        var faults = new List<string>();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = TimerModel.CreateDemo(); model.SetTheme(theme);
            var window = new DashboardWindow(model); window.Show(); Settle(window);
            try
            {
                void Audit(string label) { foreach (var f in LayoutAudit.Run(window)) faults.Add($"{theme} [{label}] {f}"); }
                Click(window, "TimelineTab"); Audit("timeline");
                model.SelectBlock(model.Engine.Sessions.Take(1).ToArray(), false); Settle(window); Audit("timeline, one block selected");
                model.SelectBlock(model.Engine.Sessions.Take(3).ToArray(), false); Settle(window); Audit("timeline, three blocks selected");
                model.SelectBlock(Array.Empty<SessionRow>(), false);
                Click(window, "AddMeeting"); Audit("timeline, meeting form"); Click(window, "AddNote"); Audit("timeline, note form");
                Click(window, "NotesTab"); Audit("notes"); Click(window, "PageKindMeeting"); Audit("notes, meeting form");
                Click(window, "WeekOverviewTab"); Audit("week chart"); Click(window, "WeekTable"); Audit("week table");
                Click(window, "Settings");
                foreach (var section in new[] { "Appearance", "Behaviour", "Hours", "Accounts", "Export", "About" }) { Click(window, "Settings" + section); Audit("settings " + section); }
            }
            finally { window.Close(); }
        }
        Assert.True(faults.Count == 0, string.Join("\n", faults.Distinct().Take(25)));
    }

    [AvaloniaFact]
    public void TheNotesPageFiltersByAccountWithTagPillsThatCountSelectManyAndClear()
    {
        var model = Model();
        model.Notes.AddNote("Send the revised budget", "Account one", Day.AddHours(8)); model.Notes.AddNote("Chase the letter", "Account one", Day.AddHours(16)); model.Notes.AddNote("Call the client", "Account two", null); model.Notes.AddNote("Tidy the drive", null, null);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab");
            // The left column is the views alone: no accounts list, so nothing in it can be mistaken for the sidebar's rows.
            Assert.DoesNotContain(Find<Border>(window, "NotesViews").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "ACCOUNTS");
            var one = Find<Button>(window, "NotesAccount_Accountone"); var two = Find<Button>(window, "NotesAccount_Accounttwo");
            Assert.Contains(one.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "#Accountone"); Assert.Contains(one.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "2"); // Tagged as a note carries it, with its count.
            Assert.False(Has(window, "NotesAccountClear")); Assert.Equal(4, window.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name?.StartsWith("PageNoteText_") == true));
            Click(window, "NotesAccount_Accountone"); one = Find<Button>(window, "NotesAccount_Accountone");
            Assert.Contains("on", one.Classes); Assert.Equal(2, window.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name?.StartsWith("PageNoteText_") == true)); Assert.True(Has(window, "NotesAccountClear"));
            Click(window, "NotesAccount_Accounttwo"); // Several at once.
            Assert.Equal(3, window.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name?.StartsWith("PageNoteText_") == true));
            Click(window, "NotesAccount_Accountone"); Assert.Equal(1, window.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name?.StartsWith("PageNoteText_") == true)); // Pressing a chosen one takes it off.
            Click(window, "NotesAccountClear"); Assert.Equal(4, window.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name?.StartsWith("PageNoteText_") == true)); Assert.False(Has(window, "NotesAccountClear"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheTrackingCardNamesTheAccountAndItsStateAndTheClockIsOneCentredLine()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.Equal("Account one", Find<TextBlock>(window, "SidebarWhoName").Text); Assert.Equal("Ready to track", Find<TextBlock>(window, "SidebarWhoStatus").Text);
            model.ToggleAccount("Account two"); Settle(window); Assert.Equal("Account two", Find<TextBlock>(window, "SidebarWhoName").Text); Assert.False(Find<TextBlock>(window, "SidebarWhoStatus").IsVisible); // Tracking says itself, on the tile and the light.
            model.ToggleBreak(); Settle(window); Assert.Equal("Break", Find<TextBlock>(window, "SidebarWhoName").Text); Assert.StartsWith("On break", Find<TextBlock>(window, "SidebarWhoStatus").Text);
            // The clock is seven-segment digits (hours and minutes, then the seconds the same height) inside the display, and the display is centred in the column.
            var display = Find<Border>(window, "SidebarDisplay"); var sidebar = Find<Border>(window, "Sidebar");
            var clock = display.GetVisualDescendants().OfType<SegmentClock>().Single(); var parts = clock.GetVisualDescendants().OfType<SegmentRun>().ToList(); Assert.Equal(2, parts.Count);
            Assert.Equal(parts[0].Bounds.Height, parts[1].Bounds.Height, 0.5); // Seconds are as tall as the hours and minutes.
            Assert.Equal(parts[0].TranslatePoint(default, window)!.Value.Y, parts[1].TranslatePoint(default, window)!.Value.Y, 0.5);
            Assert.True(parts[1].TranslatePoint(default, window)!.Value.X > parts[0].TranslatePoint(default, window)!.Value.X + parts[0].Bounds.Width - 1);
            var middle = sidebar.TranslatePoint(new Point(sidebar.Bounds.Width / 2, 0), window)!.Value.X;
            Assert.Equal(middle, display.TranslatePoint(new Point(display.Bounds.Width / 2, 0), window)!.Value.X, 2.0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheMorphEasesOutQuicklyOpeningAndEndsExactlyAndThePillFadesBeforeTheDashboardIsFullyIn()
    {
        Assert.Equal(0, WindowMorph.Ease(0, false), 3); Assert.Equal(1, WindowMorph.Ease(WindowMorph.OpenMilliseconds, false), 3); Assert.Equal(1, WindowMorph.Ease(WindowMorph.CloseMilliseconds, true), 3);
        Assert.True(WindowMorph.Ease(100, false) > .6); // Most of the growth lands in the first 100 ms.
        for (var t = 0; t < WindowMorph.OpenMilliseconds; t += 20) Assert.True(WindowMorph.Ease(t + 20, false) >= WindowMorph.Ease(t, false) - 1e-9); // Never goes back, never overshoots.
        Assert.All(new[] { 0, 50, 100, 200, 340 }, t => Assert.InRange(WindowMorph.Ease(t, false), 0, 1));
        Assert.Equal(1, WindowMorph.PillOpacity(0)); Assert.Equal(0, WindowMorph.PillOpacity(.4)); Assert.Equal(0, WindowMorph.DashboardOpacity(.18)); Assert.Equal(1, WindowMorph.DashboardOpacity(.6));
        var rect = WindowMorph.Lerp(new Rect(12, 12, 72, 316), new Rect(0, 0, 1272, 720), .5); Assert.Equal(new Rect(6, 6, 672, 518), rect);
        Assert.Equal(new Rect(12, 12, 72, 316), WindowMorph.RoundedRect(new Rect(12, 12, 72, 316), 36).Bounds);
    }

    [AvaloniaFact]
    public void TheDashboardIsClippedToTheGrowingCardWhileItMovesAndIsPlainAgainAtTheEnd()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var surface = Find<Border>(window, "DashboardSurface"); var pill = new Rect(12, 12, 72, 316);
            Assert.Equal(1, window.MorphProgress); Assert.Null(surface.Clip); Assert.False(window.Morphing);
            window.GrowFrom(pill, 34, null, new Rect(-18, -18, 108, 352)); // Not animated in the tests, so it arrives at once.
            Assert.Equal(1, window.MorphProgress); Assert.Null(surface.Clip);
            window.SetMorph(0); Settle(window);
            Assert.True(window.Morphing); Assert.Equal(pill, surface.Clip!.Bounds); Assert.Equal(0, window.ContentOpacity, 3); // The pill's size, and the dashboard is not showing.
            window.SetMorph(.5); Settle(window);
            var half = surface.Clip!.Bounds; Assert.True(half.Width > 72 && half.Width < DashboardWindow.DashboardWidth && half.Height > 316 && half.Height < DashboardWindow.DashboardHeight);
            window.SetMorph(1); Settle(window); Assert.Null(surface.Clip); Assert.False(window.Morphing);
            // Nothing in the window can be pressed while it moves.
            window.SetMorph(.4); Assert.False(window.ContentTakesPresses); window.SetMorph(1); Assert.True(window.ContentTakesPresses); Assert.Equal(1, window.ContentOpacity, 3);
        }
        finally { window.Close(); }
    }

    // Pictures for looking at the move: TIMER_PREVIEW_DIR names the folder.
    [AvaloniaFact]
    public void WriteTransitionPictures()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = TimerModel.CreateDemo(); model.SetTheme(theme); model.ToggleAccount("Account one");
            var pill = new PillWindow(model); pill.Show(); Settle(pill);
            var picture = WindowMorph.Capture(pill); Assert.NotNull(picture);
            var window = new DashboardWindow(model); window.Show(); Settle(window);
            try
            {
                var card = new Rect(12, 12, pill.CardSizeNow.Width, pill.CardSizeNow.Height); var at = new Rect(12 - Ui.ShadowMargin * pill.Scale, 12 - Ui.ShadowMargin * pill.Scale, pill.WindowWidthNow, pill.WindowHeightNow);
                window.GrowFrom(card, pill.CardRadiusNow, picture, at);
                foreach (var (m, label) in new[] { (0.0, "0"), (.15, "15"), (.3, "30"), (.5, "50"), (.8, "80") })
                {
                    window.SetMorph(m); Settle(window); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, $"morph-{theme}-{label}.png"));
                }
                window.SetMorph(1);
            }
            finally { window.Close(); pill.Close(); }
        }
    }
}
