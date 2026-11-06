using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class OvernightMeetingTests
{
    private static readonly DateTime Day = new(2026, 11, 6);
    private static TimerModel Model(TimerStore? store = null)
    {
        var engine = new TrackingEngine(clock: () => Day.AddDays(1).AddHours(3));
        if (store is not null) store.Load(engine);
        var model = new TimerModel(engine,
            new StoreSettings(new() { new("Account one") }, false, "Dark", DayOfWeek.Monday), store);
        model.CompleteSetup(); model.SetDayStart(TimeSpan.FromHours(18)); model.GoToDate(Day); return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window) { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window); }
    private static void Commit(Window window, string name, string words)
    {
        var box = Find<TextBox>(window, name); box.Text = words;
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Settle(window);
    }

    [AvaloniaFact]
    public void BothAddFormsAndDetailsAcceptAnEndOnTheFollowingDate()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.Notes.RequestNewMeeting(Day.AddHours(23)); Settle(window);
            Find<TextBox>(window, "NoteField").Text = "Timeline night";
            Find<TextBox>(window, "MeetingEnd").Text = "02:00"; Click(window, "SubmitNote");
            var first = Assert.Single(model.Notes.All); Assert.Equal(TimeSpan.FromHours(26), first.End);
            Assert.Contains("(+1 day)", NoteParts.Span(first));
            model.SelectMeeting(first.Id); Settle(window); Commit(window, "DetailsMeetingEnd", "01:00");
            Assert.Equal(TimeSpan.FromHours(25), model.Notes.Find(first.Id)!.End);
            window.ShowNotes(); Settle(window); var page = window.GetVisualDescendants().OfType<NotesPage>().Single();
            page.SetKind(true); Settle(window); Find<TextBox>(window, "PageNoteField").Text = "Page night";
            Find<TextBox>(window, "PageStart").Text = "22:00"; Find<TextBox>(window, "PageEnd").Text = "03:00";
            Click(window, "PageSubmit"); var added = model.Notes.All.Single(n => model.Notes.Words(n) == "Page night");
            Assert.Equal(TimeSpan.FromHours(27), added.End);
            page.OpenNote(added.Id); Settle(window); Assert.Equal("03:00", Find<TextBox>(window, "PageEnd").Text);
            Find<TextBox>(window, "PageEnd").Text = "04:00"; Click(window, "PageSubmit");
            Assert.Equal(TimeSpan.FromHours(28), model.Notes.Find(added.Id)!.End);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void OvernightRecurrenceAndMovedOccurrenceSurviveReopening()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-night-series-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var model = Model(store);
                Assert.Null(model.Notes.AddMeeting(Day, TimeSpan.FromHours(23), TimeSpan.FromHours(26), "Night series", "Account one", RepeatChoice.Day));
                var first = model.Notes.MeetingsOn(Day).Single();
                Assert.Null(model.Notes.EditMeeting(first.Id, "Later night", "Account one", Day, TimeSpan.FromHours(22), TimeSpan.FromHours(27), RepeatChoice.Day, true));
                Assert.All(model.Notes.All, n => Assert.Equal(TimeSpan.FromHours(27), n.End));
                Assert.True(model.Notes.IsSaved(first.Id)); Assert.False(model.Notes.Unsaved);
            }
            using (var store = new TimerStore(folder))
            {
                var notes = new NotesModel(store, () => Day.AddDays(1), () => new[] { "Account one" }) { DayStart = TimeSpan.FromHours(18) };
                Assert.All(notes.All, n => { Assert.Equal(TimeSpan.FromHours(27), n.End); Assert.Equal("Account one", notes.AccountOf(n)); });
                var first = notes.MeetingsOn(Day).Single();
                Assert.Null(notes.MoveMeeting(first.Id, Day.AddDays(1).AddHours(1), Day.AddDays(1).AddHours(4)));
                Assert.Equal(Day.AddDays(1), notes.Find(first.Id)!.Day); Assert.Single(notes.MeetingsOn(Day));
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void DraggingUsesTheWorkdayWindowAndAllowsMidnightCrossing()
    {
        var moved = MeetingGestures.Resolve(TimeSpan.FromHours(23), TimeSpan.FromHours(26), 0, 60, TimeSpan.FromHours(18), TimeSpan.FromHours(42));
        Assert.Equal((TimeSpan.FromHours(24), TimeSpan.FromHours(27)), moved);
        var model = Model(); model.Notes.AddMeeting(Day, TimeSpan.FromHours(23), TimeSpan.FromHours(26), "Night", null, RepeatChoice.None);
        var first = Assert.Single(model.Notes.All); Assert.Null(model.Notes.MoveMeeting(first.Id, moved.Start, moved.End));
        var saved = model.Notes.Find(first.Id)!; Assert.Equal(Day.AddDays(1), saved.Day); Assert.Equal(TimeSpan.Zero, saved.At); Assert.Equal(TimeSpan.FromHours(3), saved.End);
        Assert.Single(model.Notes.MeetingsOn(Day));
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void MeetingStacksKeepEachCalendarDateAndLateRequestsRoundAcrossMidnight(string theme)
    {
        var model = Model(); model.SetTheme(theme);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(23), TimeSpan.FromHours(26), "Before", null, RepeatChoice.None);
        model.Notes.AddMeeting(Day, TimeSpan.FromHours(23.5), TimeSpan.FromHours(26.5), "Middle", null, RepeatChoice.None);
        model.Notes.AddMeeting(Day.AddDays(1), TimeSpan.FromHours(1), TimeSpan.FromHours(3), "After", null, RepeatChoice.None);
        var before = model.Notes.All.First(); var after = model.Notes.All.Last();
        var window = new DashboardWindow(model); window.Show(); model.SelectMeeting(after.Id); Settle(window);
        try
        {
            var firstBar = Find<Border>(window, "Stack_M" + before.Id); var lastBar = Find<Border>(window, "Stack_M" + after.Id);
            Assert.True(firstBar.Bounds.X < lastBar.Bounds.X);
            Assert.InRange(Math.Abs(firstBar.Bounds.Width / 3 - lastBar.Bounds.Width / 2), 0, 1); // Pixel rounding may differ at each edge.
            model.Notes.RequestNewMeeting(Day.AddHours(23).AddMinutes(58)); Settle(window);
            Assert.Equal("00:00", Find<TextBox>(window, "MeetingStart").Text);
            Find<TextBox>(window, "NoteField").Text = "Rounded night"; Click(window, "SubmitNote");
            var added = model.Notes.All.Last(); Assert.Equal(Day.AddDays(1), added.Day); Assert.Equal(TimeSpan.Zero, added.At);
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null) { Directory.CreateDirectory(folder); using var image = window.CaptureRenderedFrame(); image?.Save(Path.Combine(folder, "overnight-meeting-stack-" + theme + ".png")); }
        }
        finally { window.Close(); model.SetTheme("System"); }
    }

    [AvaloniaFact]
    public void InvalidSeriesDraftCanBeCorrectedOrDiscardedWithoutLosingOtherFields()
    {
        var model = Model(); model.Notes.AddMeeting(Day, TimeSpan.FromHours(23), TimeSpan.FromHours(25), "Night", null, RepeatChoice.Day);
        var first = model.Notes.MeetingsOn(Day).Single(); var window = new DashboardWindow(model); window.Show(); model.SelectMeeting(first.Id); Settle(window);
        try
        {
            Commit(window, "DetailsMeetingName", "Keep this draft"); Commit(window, "DetailsMeetingEnd", "nonsense");
            Click(window, "DetailsSave"); Assert.NotNull(model.EditError);
            Assert.Equal("Keep this draft", Find<TextBox>(window, "DetailsMeetingName").Text); Assert.Equal("nonsense", Find<TextBox>(window, "DetailsMeetingEnd").Text);
            Commit(window, "DetailsMeetingEnd", "03:00"); Click(window, "DetailsSave");
            Assert.Null(model.EditError); Assert.Equal(TimeSpan.FromHours(27), model.Notes.Find(first.Id)!.End);
            Commit(window, "DetailsMeetingName", "Discard me"); Click(window, "DetailsDiscard");
            Assert.Equal("Keep this draft", Find<TextBox>(window, "DetailsMeetingName").Text); Assert.False(Find<Button>(window, "DetailsSave").IsVisible);
        }
        finally { window.Close(); }
    }
}
