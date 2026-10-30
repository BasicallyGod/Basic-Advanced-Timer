using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

// The seventh round of feedback on the experimental build: the pill's colour and slide, the bell and the calendar, the week overview, the notes rows and the day bar.
public sealed class FeedbackRound7Tests
{
    private static DateTime Day => new(2026, 10, 9);
    private static SessionRow Row(string start, string minutes, string account = "Account one") => new(Day.ToString("yyyy-MM-dd"), account, start, "added", minutes);
    private static TimerModel Model(string theme = "Light", bool meeting = false)
    {
        var engine = new TrackingEngine(clock: () => Day.AddHours(16));
        engine.Sessions.AddRange(new[] { Row("09:00:00", "60"), Row("10:00:00", "60", "Account two") });
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, theme, DayOfWeek.Monday)); model.CompleteSetup();
        if (meeting) model.Notes.AddMeeting(Day, TimeSpan.FromHours(14), TimeSpan.FromHours(15), "A meeting with a name far too long to fit in its card", null, RepeatChoice.None);
        return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window); }
    private static Point Centre(Window window, Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
    private static void Press(Window window, Point at) { window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Settle(window); }

    [AvaloniaFact]
    public void ThePillWearsTheSidebarsColourOnDark()
    {
        var model = Model("Dark"); var pill = new PillWindow(model); pill.Show(); var dash = new DashboardWindow(model); dash.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Avalonia.Media.Color Of(Border b) => ((Avalonia.Media.ISolidColorBrush)b.Background!).Color;
            Assert.True(dash.TryFindResource("Panel", dash.ActualThemeVariant, out var panel)); Assert.Equal(((Avalonia.Media.ISolidColorBrush)panel!).Color, Of(Find<Border>(pill, "PillSurface"))); // The sidebar is on the panel colour the pill wears.
            Assert.Contains("panel", Find<Border>(dash, "Sidebar").Classes);
        }
        finally { pill.Close(); dash.Close(); }
    }

    [AvaloniaFact]
    public void PressingTheBellOrTheDateAgainClosesItsCardAndAPressOutsideDoesToo()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var bell = Find<Popup>(window, "NotificationsPopup"); var calendar = Find<Popup>(window, "DateCalendarPopup");
            // As a person does it: a real press and release on the button, twice.
            Press(window, Centre(window, Find<Button>(window, "Notifications"))); Assert.True(bell.IsOpen);
            Press(window, Centre(window, Find<Button>(window, "Notifications"))); Assert.False(bell.IsOpen);
            Press(window, Centre(window, Find<Button>(window, "DatePick"))); Assert.True(calendar.IsOpen);
            Press(window, Centre(window, Find<Button>(window, "DatePick"))); Assert.False(calendar.IsOpen);
            // A press somewhere else closes them, and so does Escape.
            Press(window, Centre(window, Find<Button>(window, "Notifications"))); Assert.True(bell.IsOpen);
            Press(window, new Point(300, 640)); Assert.False(bell.IsOpen);
            Press(window, Centre(window, Find<Button>(window, "DatePick"))); Assert.True(calendar.IsOpen);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Settle(window); Assert.False(calendar.IsOpen);
            // The calendar hangs from the date button by a point, like the bell's card.
            Press(window, Centre(window, Find<Button>(window, "DatePick"))); Assert.NotEmpty(window.GetVisualDescendants().OfType<Canvas>().Where(c => c.Name == "NotificationsPoint"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheNotesCardHasNoCountAndItsRowsAreCentredOnTheirTick()
    {
        var model = Model(); model.Notes.AddNote("Tidy the shared drive", null, null); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.Equal("Notes", Find<Button>(window, "CardKindNotes").Content); // The title is the switch between notes and meetings, with no count.
            var note = model.Notes.All.Single(); var tick = Find<Button>(window, "NoteDone_" + note.Id); var text = Find<TextBlock>(window, "NoteText_" + note.Id); var remove = Find<Button>(window, "NoteRemove_" + note.Id);
            double MidY(Control c) => c.TranslatePoint(new Point(0, c.Bounds.Height / 2), window)!.Value.Y;
            Assert.InRange(Math.Abs(MidY(text) - MidY(tick)), 0, 1.5); Assert.InRange(Math.Abs(MidY(remove) - MidY(tick)), 0, 1.5);
            // Ticked, the tick mark sits in the middle of its circle.
            model.Notes.Toggle(note.Id); Settle(window);
            var ring = Find<Button>(window, "NoteDone_" + note.Id).GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("tick"));
            var mark = ring.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "TickMark"); var centre = mark.Data!.Bounds.Center; var inner = ring.Bounds.Width - 2 * ring.BorderThickness.Left;
            Assert.InRange(centre.X - inner / 2, -.4, .4); Assert.InRange(centre.Y - inner / 2, -.4, .4);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSelectedBlockTitleStaysWhereItIsWhetherOrNotABlockIsSelected()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            double Y() => Find<TextBlock>(window, "SelectedTitle").TranslatePoint(default, window)!.Value.Y;
            var none = Y(); model.SelectBlock(new[] { model.Engine.Sessions[0] }, false); Settle(window); var one = Y();
            model.SelectBlock(new[] { model.Engine.Sessions[1] }, true); Settle(window); var two = Y();
            model.ClearSelection(); Settle(window);
            Assert.Equal(none, one, 0.5); Assert.Equal(none, two, 0.5); Assert.Equal(none, Y(), 0.5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AMeetingsNameSlidesOnHoverAndTheGapsAboveAndBelowTheMeetingRowAreEqual()
    {
        var model = Model(meeting: true); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var card = Find<Border>(window, "TimelineMeeting" + model.Notes.All.Single().Id); var name = card.GetVisualDescendants().OfType<MarqueeText>().Single();
            Assert.False(name.Scrolling);
            var axis = Find<Border>(window, "TimelineAxis"); var block = Find<Border>(window, "TimelineEntry0");
            Assert.Equal(Canvas.GetTop(card) - Canvas.GetTop(axis), Canvas.GetTop(block) - (Canvas.GetTop(card) + card.Height), 0.5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheHalfHoursAreAsClearAsTheHours()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var ticks = window.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is "Tick").ToList();
            var tall = ticks.Max(t => t.Height); var halves = ticks.Where(t => t.Height is > 5.5 and < 7).ToList();
            Assert.NotEmpty(halves); Assert.True(halves.All(h => h.Opacity >= 1)); Assert.True(tall - halves[0].Height <= 3.5);
            Assert.NotEmpty(window.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is "HalfGuide"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheWeekTableTotalsUseTheOrdinaryTypefaceAndTheWorkingTimeRowHasRoom()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window); model.SetWeekAsTable(true); Settle(window);
        try
        {
            var totals = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "WeekTableTotal").ToList();
            Assert.NotEmpty(totals); Assert.All(totals, t => Assert.False(t.Classes.Contains("figures")));
            var working = Find<Grid>(window, "WeekTableRow_Workingtime"); var account = Find<Grid>(window, "WeekTableRow_Accountone");
            Assert.True(working.Bounds.Height >= account.Bounds.Height + 8);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheWeekChartKeepsTheScheduleWordsApartFromTheColumnsAndCarriesNoMeetingOrNoteCounts()
    {
        var model = Model(); model.Notes.AddMeeting(Day, TimeSpan.FromHours(14), TimeSpan.FromHours(15), "Review", null, RepeatChoice.None); model.Notes.AddNote("Chase", null, Day.AddHours(18));
        var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window);
        try
        {
            var chart = Find<Grid>(window, "WeekChart_");
            // The day's meetings and open notes are not counted under its column (they are on the timeline and the Notes page), and the day's name is not cut off.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name is "WeekDayMeetings" or "WeekDayTasks");
            var dayLabel = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "WeekDayLabel"); var labelBottom = dayLabel.TranslatePoint(new Point(0, dayLabel.Bounds.Height), chart)!.Value.Y; Assert.True(labelBottom <= chart.Bounds.Height, $"day names end at {labelBottom} of {chart.Bounds.Height}");
            var label = Find<TextBlock>(window, "WeekScheduleLabel"); var last = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("WeekDay_") == true).OrderBy(b => b.TranslatePoint(default, window)!.Value.X).Last();
            var lastRight = last.TranslatePoint(new Point(last.Bounds.Width, 0), window)!.Value.X; Assert.True(label.TranslatePoint(default, window)!.Value.X >= lastRight);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheReminderLightPulsesARingWhileAReminderIsDueAndIsStillWhenNot()
    {
        var model = Model(); var pill = new PillWindow(model); pill.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var light = Find<Border>(pill, "PillReminderLight"); Assert.False(Ui.IsPulsing(light));
            model.Notes.AddNote("Send the budget", null, Day.AddHours(8)); Dispatcher.UIThread.RunJobs(); Assert.True(Ui.IsPulsing(light));
            Assert.Equal(1.0, light.Opacity); // The light itself stays steady; the ring swells round it.
        }
        finally { pill.Close(); }
    }

    [AvaloniaFact]
    public void TheHeaderTabsListTimelineThenNotesThenTheWeekOverviewLeftToRight()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            double X(string name) => Find<Button>(window, name).TranslatePoint(default, window)!.Value.X;
            Assert.True(X("TimelineTab") < X("NotesTab") && X("NotesTab") < X("WeekOverviewTab"));
        }
        finally { window.Close(); }
    }

    private static byte[] Pixels(Control control)
    {
        var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(72, 72)); bitmap.Render(control);
        using var stream = new MemoryStream(); bitmap.Save(stream); return stream.ToArray();
    }

    [AvaloniaFact]
    public void TheRailIconsPlayALoopThatStartsAndEndsAtRest()
    {
        foreach (var (kind, seconds) in new[] { (IconKind.Timeline, 0.55), (IconKind.Week, 0.2), (IconKind.NotesList, 1.3) })
        {
            var glyph = new IconGlyph(kind); var window = new Window { Content = glyph, Width = 72, Height = 72 }; window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs(); var rest = Pixels(glyph);
                glyph.PlaySeconds = seconds; Dispatcher.UIThread.RunJobs(); Assert.NotEqual(rest, Pixels(glyph));
                glyph.PlaySeconds = 0; Dispatcher.UIThread.RunJobs(); Assert.Equal(rest, Pixels(glyph));
            }
            finally { window.Close(); }
        }
        for (var bar = 0; bar < 3; bar++) { Assert.Equal(0, RailMotion.BarOffset(bar, 0), 6); Assert.Equal(0, RailMotion.BarOffset(bar, RailMotion.BarSeconds[bar]), 6); }
        for (var column = 0; column < 3; column++) { Assert.Equal(1, RailMotion.ColumnScale(column, 0), 6); Assert.Equal(1, RailMotion.ColumnScale(column, RailMotion.ColumnSeconds * 2), 6); }
        Assert.Equal(1, RailMotion.TickProgress(0), 6); Assert.Equal(1, RailMotion.TickProgress(RailMotion.NotesSeconds), 6); Assert.Equal(0, RailMotion.CrossProgress(0), 6); Assert.Equal(0, RailMotion.CrossProgress(RailMotion.NotesSeconds), 6);
        // The tick and the cross take turns: never both whole at once.
        for (var s = 0.0; s < RailMotion.NotesSeconds; s += .05) Assert.True(RailMotion.TickProgress(s) < .5 || RailMotion.CrossProgress(s) < .5);
        // The bars never move in step, and the columns spring a beat apart.
        Assert.NotEqual(RailMotion.BarOffset(0, 1) / 1.6, RailMotion.BarOffset(1, 1) / -1.6, 3);
        Assert.NotEqual(RailMotion.ColumnScale(0, .3), RailMotion.ColumnScale(1, .3), 3);
    }

    [AvaloniaFact]
    public void AButtonWithWordsHasNoTooltipExceptToShowItsShortcut()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.Null(ToolTip.GetTip(Find<Button>(window, "AddTime"))); // It says Add time already.
            Assert.Null(ToolTip.GetTip(Find<Button>(window, "GoToToday")));
            model.SelectBlock(new[] { model.Engine.Sessions[0] }, false); Settle(window);
            var copy = ToolTip.GetTip(Find<Button>(window, "CopySelected")); Assert.IsType<StackPanel>(copy); // "Copy" and its key.
            Assert.Contains(((StackPanel)copy!).GetVisualDescendants().Concat(((StackPanel)copy).Children).OfType<TextBlock>(), x => x.Text == "Ctrl C");
            Assert.NotNull(ToolTip.GetTip(Find<Button>(window, "TimelineZoomOut"))); // An icon is named.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AMessageIsAFewWordsWithAQuietDetailAndTheSharedPackagesSentencesAreShortened()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.Tell("Merged · 00:05 gap added"); Settle(window);
            Assert.Equal("Merged", Find<TextBlock>(window, "MessageText").Text); Assert.Equal("00:05 gap added", Find<TextBlock>(window, "MessageDetail").Text);
            model.Tell("Undone"); Settle(window); Assert.False(Find<TextBlock>(window, "MessageDetail").IsVisible);
            model.Refuse("This interval overlaps recorded or running time. Choose a free gap."); Settle(window);
            Assert.Equal("Overlaps another block", Find<TextBlock>(window, "MessageText").Text);
            model.Refuse("Merge blocks with only blank gaps between them. Overlapping or intervening time cannot be merged."); Assert.Equal("Time in between: can’t merge", model.EditError);
        }
        finally { window.Close(); }
    }
}
