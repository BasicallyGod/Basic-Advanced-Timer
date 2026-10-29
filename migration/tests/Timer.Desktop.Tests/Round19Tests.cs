using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 19: meetings as quiet blocks, rings that sit evenly round what they mark, notes at one time, one floating card at a time, editing and starring notes.
public sealed class Round19Tests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TimerModel Model()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(18)), new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window); }
    private static bool Has(Control root, string name) => root.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name);
    private static long IdOf(TimerModel model, string text) => model.Notes.All.Single(n => model.Notes.Text(n) == text).Id;
    private static Point At(DashboardWindow window, TimerModel model, double hour, double y = 100)
    {
        var scroll = Find<ScrollViewer>(window, "TimelineScroll"); var canvas = Find<Canvas>(window, "TimelineCanvas");
        var x = ReviewViews.TimelineGutter + hour * model.TimelinePixelsPerHour;
        scroll.Offset = new Vector(Math.Max(0, x - scroll.Viewport.Width / 2), 0); Settle(window);
        return canvas.TranslatePoint(new Point(x, y), window)!.Value;
    }

    private const double Ruler = 36, Lanes = 100; // Pointer heights on the timeline's canvas: on the ruler and its pins, and among the blocks.

    [AvaloniaFact]
    public void MeetingsAreQuietGreyBlocksWithNoDashedFrame()
    {
        var model = Model(); model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Planning", null, RepeatChoice.None);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var card = Find<Border>(window, "TimelineMeeting" + model.Notes.All.Single().Id);
            Assert.DoesNotContain(card.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Shape>(), s => s.StrokeDashArray is { Count: > 0 }); // No dashes anywhere on it.
            Assert.True(card.TryFindResource("MeetBg", card.ActualThemeVariant, out var grey)); Assert.Equal(((ISolidColorBrush)grey!).Color, ((ISolidColorBrush)card.Background!).Color); // The theme's light grey.
            Assert.Equal(new CornerRadius(12), card.CornerRadius);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AmongTheBlocksTheGuideRingsWhatItCrossesWithTheBlocksOwnShadowAndLeavesItOnTheRuler()
    {
        var model = Model(); Assert.True(model.AddEntry("Account one", Day.AddHours(7), Day.AddHours(8), "")); model.Notes.AddMeeting(Day, TimeSpan.FromHours(7.5), TimeSpan.FromHours(8.5), "Stand-up", null, RepeatChoice.None);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var canvas = Find<Canvas>(window, "TimelineCanvas");
            List<Border> Ringed() => canvas.Children.OfType<Border>().Where(b => b.Classes.Contains("guideOver")).ToList();
            window.MouseMove(At(window, model, 7.75, Lanes)); Settle(window); // Under the block and the meeting.
            var ringed = Ringed(); Assert.Equal(2, ringed.Count);
            // The ring is the card's own shadow, so it is the same width on every side.
            Assert.All(ringed, b => { var shadow = b.BoxShadow[0]; Assert.Equal(0, shadow.OffsetX); Assert.Equal(0, shadow.OffsetY); Assert.Equal(0, shadow.Blur); Assert.Equal(2, shadow.Spread); });
            window.MouseMove(At(window, model, 7.25, Lanes)); Settle(window); // Under the block alone: the meeting's ring goes and its own shadow is as it was.
            Assert.Single(Ringed()); Assert.Equal(0, Find<Border>(window, "TimelineMeeting" + model.Notes.All.Single(n => NotesModel.IsMeeting(n)).Id).BoxShadow.Count);
            window.MouseMove(At(window, model, 7.75, Ruler)); Settle(window); // On the ruler the guide rings nothing.
            Assert.Empty(Ringed());
            window.MouseMove(At(window, model, 7.75, Lanes)); Settle(window); Assert.Equal(2, Ringed().Count);
            window.MouseMove(new Point(2, 2)); Settle(window); Assert.Empty(Ringed()); // Off the timeline, every block is as it was.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ARingNeverReplacesTheSelectionRingOfASelectedBlock()
    {
        var model = Model(); Assert.True(model.AddEntry("Account one", Day.AddHours(7), Day.AddHours(8), ""));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.SelectBlock(new[] { model.Engine.Sessions[0] }, false); Settle(window);
            var block = Find<Border>(window, "TimelineEntry0"); var before = block.BoxShadow[0];
            window.MouseMove(At(window, model, 7.5, Lanes)); Settle(window);
            block = Find<Border>(window, "TimelineEntry0"); Assert.Equal(3, block.BoxShadow[0].Spread); Assert.Equal(before.Color, block.BoxShadow[0].Color); Assert.DoesNotContain("guideOver", block.Classes);
        }
        finally { window.Close(); }
    }

    private static TimerModel ClusterModel()
    {
        var model = Model();
        model.Notes.AddNote("First thing", "Account one", Day.AddHours(8)); model.Notes.AddNote("Second thing", "Account two", Day.AddHours(8)); model.Notes.AddNote("Third thing", null, Day.AddHours(8).AddMinutes(4));
        model.Notes.AddNote("Lunch order", null, Day.AddHours(12));
        return model;
    }

    [AvaloniaFact]
    public void NotesAtOneTimeShareOnePinThatCountsThemAndTheCardListsThemAll()
    {
        var model = ClusterModel(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var first = IdOf(model, "First thing"); var lunch = IdOf(model, "Lunch order");
            // 08:00, 08:00 and 08:04 (a few pixels apart at this zoom) are one pin of three; the lunch reminder has a pin of its own with no count.
            Assert.True(Has(window, "TimelinePin_" + first)); Assert.False(Has(window, "TimelinePin_" + IdOf(model, "Second thing"))); Assert.False(Has(window, "TimelinePin_" + IdOf(model, "Third thing")));
            Assert.Equal("3", ((TextBlock)Find<Border>(window, "TimelinePinMark_" + first).Child!).Text); Assert.False(Has(window, "TimelinePinMark_" + lunch));
            Assert.Contains("First thing", Avalonia.Automation.AutomationProperties.GetName(Find<Canvas>(window, "TimelinePin_" + first)));
            Assert.Contains("Third thing", Avalonia.Automation.AutomationProperties.GetName(Find<Canvas>(window, "TimelinePin_" + first)));
            window.MouseMove(At(window, model, 8.0, Ruler)); Settle(window);
            var words = string.Join(" | ", Find<Border>(window, "TimelineGuideNote").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains("3 reminders", words); Assert.Contains("First thing", words); Assert.Contains("Second thing", words); Assert.Contains("Third thing", words);
            // Pressing the pin edits the first of them in the Notes card.
            var onPin = Find<Canvas>(window, "TimelinePin_" + first).TranslatePoint(new Point(10, 14), window)!.Value; window.MouseDown(onPin, MouseButton.Left); window.MouseUp(onPin, MouseButton.Left); Settle(window);
            Assert.Equal("First thing", Find<TextBox>(window, "NoteField").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OnlyOneCardSpeaksAtATimeThePinsNoteOnTheRulerAndTheBlocksOwnTooltipAmongTheBlocks()
    {
        var model = Model(); model.Notes.AddNote("Send the revised budget", null, Day.AddHours(8)); model.Notes.AddMeeting(Day, TimeSpan.FromHours(7.5), TimeSpan.FromHours(8.5), "Stand-up", null, RepeatChoice.None);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var card = Find<Border>(window, "TimelineGuideNote"); var line = Find<Avalonia.Controls.Shapes.Line>(window, "TimelineGuideLine");
            window.MouseMove(At(window, model, 8.0, Ruler)); Settle(window); Assert.True(card.IsVisible); // On the ruler by the pin: the note's card.
            window.MouseMove(At(window, model, 8.0, Lanes)); Settle(window);
            Assert.False(card.IsVisible); Assert.True(line.IsVisible); // Down among the blocks, at the very same time: no note card, the meeting's own tooltip is the one to read.
            Assert.Contains("Stand-up", ToolTip.GetTip(Find<Border>(window, "TimelineMeeting" + model.Notes.All.Single(n => NotesModel.IsMeeting(n)).Id))!.ToString());
            Assert.Equal("08:00", ((TextBlock)Find<Border>(window, "TimelineGuideChip").Child!).Text); // The chip still follows the pointer.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ANoteIsEditedInTheNotesCardByPressingItsWordsAndKeepsItsAccountAndReminder()
    {
        var model = Model(); model.Notes.AddNote("Ring the bank", "Account one", Day.AddHours(15));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var id = model.Notes.All.Single().Id;
            Click(window, "NoteEdit_" + id);
            var field = Find<TextBox>(window, "NoteField"); Assert.Equal("Ring the bank", field.Text);
            Assert.True(Has(window, "FormAccountChip")); Assert.True(Has(window, "FormReminder"));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Save"); // The form says it is saving an existing note.
            field.Text = "Ring the bank back"; Click(window, "SubmitNote");
            var note = model.Notes.All.Single(); Assert.Equal(id, note.Id);
            Assert.Equal("Ring the bank back", model.Notes.Text(note)); Assert.Equal("Account one", model.Notes.AccountOf(note)); Assert.Equal(Day.AddHours(15), NotesModel.ReminderOf(note));
            Assert.False(Has(window, "NoteField")); // The form closes after saving.
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ANoteIsMarkedImportantWithAMarkAtTheStartOfItsWordsAndComesFirst()
    {
        var model = Model(); model.Notes.AddNote("Plain one", null, null); model.Notes.AddNote("!! Pay the invoice", "Account one", null); model.Notes.AddNote("Plain two", null, null);
        var pay = model.Notes.All.Single(n => model.Notes.Text(n) == "Pay the invoice");
        Assert.True(NotesModel.IsImportant(pay)); Assert.Equal("Pay the invoice", model.Notes.Text(pay)); Assert.Equal("!! Pay the invoice", model.Notes.Words(pay)); Assert.Equal("Account one", model.Notes.AccountOf(pay));
        Assert.Equal(new[] { "Pay the invoice", "Plain one", "Plain two" }, model.Notes.ForDay(Day).Select(model.Notes.Text)); // Important first, the rest in their order.
        var one = IdOf(model, "Plain one"); model.Notes.SetImportant(one, true);
        Assert.True(NotesModel.IsImportant(model.Notes.Find(one)!)); Assert.Equal("Plain one", model.Notes.Text(model.Notes.Find(one)!));
        model.Notes.SetImportant(pay.Id, false);
        Assert.False(NotesModel.IsImportant(model.Notes.Find(pay.Id)!)); Assert.Equal("Account one", model.Notes.AccountOf(model.Notes.Find(pay.Id)!)); // The account stays.
        Assert.Equal(new[] { "Plain one", "Pay the invoice", "Plain two" }, model.Notes.ForDay(Day).Select(model.Notes.Text));
        Assert.Single(model.Notes.Listed().Where(n => model.Notes.InView(n, NoteView.Important))); // Only open important notes are in the view.
        model.Notes.Toggle(one); Assert.Empty(model.Notes.Listed().Where(n => model.Notes.InView(n, NoteView.Important)));
        // A meeting is a plan, not a task: it is never important, and a !! in its name stays in its name.
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "!! Board", null, RepeatChoice.None);
        var board = model.Notes.All.Single(n => NotesModel.IsMeeting(n)); Assert.False(NotesModel.IsImportant(board)); Assert.Equal("!! Board", model.Notes.Text(board)); model.Notes.SetImportant(board.Id, true); Assert.False(NotesModel.IsImportant(board));
        Assert.True(NotesModel.IsMarked("!!")); Assert.True(NotesModel.IsMarked("!! x")); Assert.False(NotesModel.IsMarked("!!x")); Assert.False(NotesModel.IsMarked("Hello !! x")); Assert.Equal("!! x", NotesModel.WithMark("x", true)); Assert.Equal("x", NotesModel.WithMark("!! x", false));
    }

    [AvaloniaFact]
    public void AStarOnTheRowAndInTheFormMarksANoteImportantAndTheTimelinePinShowsItAndTheNotesPageHasAnImportantView()
    {
        var model = Model(); model.Notes.AddNote("Plain one", null, Day.AddHours(15)); model.Notes.AddNote("Pay the invoice", null, Day.AddHours(9));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var plain = IdOf(model, "Plain one"); var pay = IdOf(model, "Pay the invoice");
            Assert.False(Has(window, "TimelinePinMark_" + plain));
            Click(window, "NoteImportant_" + pay); Assert.True(NotesModel.IsImportant(model.Notes.Find(pay)!));
            Assert.Equal("!", ((TextBlock)Find<Border>(window, "TimelinePinMark_" + pay).Child!).Text); // The pin carries a !.
            var names = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name?.StartsWith("NoteText_") == true).Select(t => t.Name!).ToList();
            Assert.Equal("NoteText_" + pay, names[0]); // Important first in the card.
            Click(window, "NoteImportant_" + pay); Assert.False(NotesModel.IsImportant(model.Notes.Find(pay)!)); Click(window, "NoteImportant_" + pay);
            // The form's star puts the mark in its box and takes it off again.
            Click(window, "AddNote"); var field = Find<TextBox>(window, "NoteField"); field.Text = "Call back";
            Click(window, "FormImportant"); Assert.Equal("!! Call back", Find<TextBox>(window, "NoteField").Text); Click(window, "FormImportant"); Assert.Equal("Call back", Find<TextBox>(window, "NoteField").Text);
            Click(window, "FormImportant"); Click(window, "SubmitNote"); Assert.True(NotesModel.IsImportant(model.Notes.All.Single(n => model.Notes.Text(n) == "Call back")));
            // The Notes page lists them as a view with a count, and its rows have the star too.
            Click(window, "NotesTab");
            Assert.Contains("2", Find<Button>(window, "NotesView_Important").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Click(window, "NotesView_Important");
            Assert.True(Has(window, "PageNoteText_" + pay)); Assert.False(Has(window, "PageNoteText_" + plain));
            Click(window, "PageNoteImportant_" + pay); Assert.False(NotesModel.IsImportant(model.Notes.Find(pay)!));
        }
        finally { window.Close(); }
    }

    // Pictures for looking at the layout: TIMER_PREVIEW_DIR names the folder.
    [AvaloniaFact]
    public void WriteRound19Pictures()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = ClusterModel(); model.SetTheme(theme);
            Assert.True(model.AddEntry("Account one", Day.AddHours(7), Day.AddHours(7.75), "")); Assert.True(model.AddEntry("Account two", Day.AddHours(10), Day.AddHours(11), "Planning"));
            model.Notes.AddMeeting(Day, TimeSpan.FromHours(9), TimeSpan.FromHours(10), "Planning call", "Account one", RepeatChoice.None); model.Notes.AddMeeting(Day, TimeSpan.FromHours(13), TimeSpan.FromHours(14.5), "Client review", null, RepeatChoice.Week);
            model.Notes.SetImportant(IdOf(model, "Lunch order"), true); model.Notes.AddNote("Tidy the shared drive", null, null);
            var window = new DashboardWindow(model); window.Show(); Settle(window);
            try
            {
                using (var rest = window.CaptureRenderedFrame()) rest?.Save(Path.Combine(folder, $"r19-rest-{theme}.png"));
                window.MouseMove(At(window, model, 7.4, Lanes)); Settle(window);
                using (var block = window.CaptureRenderedFrame()) block?.Save(Path.Combine(folder, $"r19-hover-block-{theme}.png"));
                window.MouseMove(At(window, model, 9.5, Lanes)); Settle(window);
                using (var meeting = window.CaptureRenderedFrame()) meeting?.Save(Path.Combine(folder, $"r19-hover-meeting-{theme}.png"));
                window.MouseMove(At(window, model, 8.0, Ruler) + new Vector(2, 0)); Settle(window);
                using (var cluster = window.CaptureRenderedFrame()) cluster?.Save(Path.Combine(folder, $"r19-cluster-{theme}.png"));
                window.MouseMove(At(window, model, 12.0, Ruler)); Settle(window);
                using (var star = window.CaptureRenderedFrame()) star?.Save(Path.Combine(folder, $"r19-important-pin-{theme}.png"));
                Click(window, "NoteEdit_" + IdOf(model, "Lunch order"));
                using (var edit = window.CaptureRenderedFrame()) edit?.Save(Path.Combine(folder, $"r19-edit-{theme}.png"));
                window.SelectTab(true); Settle(window);
                using (var week = window.CaptureRenderedFrame()) week?.Save(Path.Combine(folder, $"r19-week-{theme}.png"));
            }
            finally { window.Close(); }
        }
    }
}
