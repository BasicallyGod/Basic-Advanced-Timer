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

// The eighth round of feedback on the experimental build: the week chart, the Selected block card, the Notes page's search, switch and # offers, and small wording and type changes.
public sealed class FeedbackRound8Tests
{
    private static DateTime Day => new(2026, 10, 9);
    private static SessionRow Row(string start, string minutes, string account = "Account one") => new(Day.ToString("yyyy-MM-dd"), account, start, "added", minutes);
    private static TimerModel Model(string theme = "Light")
    {
        var engine = new TrackingEngine(clock: () => Day.AddHours(16));
        engine.Sessions.AddRange(new[] { Row("09:00:00", "60"), Row("10:00:00", "60", "Account two") });
        var model = new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, theme, DayOfWeek.Monday)); model.CompleteSetup();
        model.Notes.AddNote("Chase the invoice", "Account one", Day.AddHours(18));
        return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window); }
    private static Point Centre(Window window, Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
    private static void Press(Window window, Point at) { window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Settle(window); }

    [AvaloniaFact]
    public void AWeekDayHasNoFillUnderThePointerAndItsCountsAreOnePairEach()
    {
        var model = Model(); model.Notes.AddMeeting(Day, TimeSpan.FromHours(14), TimeSpan.FromHours(15), "Review", null, RepeatChoice.None);
        var window = new DashboardWindow(model); window.Show(); window.SelectTab(true); Settle(window);
        try
        {
            var day = Find<Button>(window, "WeekDay_20261009"); window.MouseMove(Centre(window, day)); Settle(window);
            var presenter = day.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(c => c.Name == "PART_ContentPresenter");
            Assert.True(presenter.Background is null || presenter.Background is Avalonia.Media.ISolidColorBrush { Color.A: 0 }, "The column keeps no fill under the pointer, so the lines behind it show.");
            // The meeting count is an icon and a number on one centre line.
            var pair = Find<Grid>(window, "WeekDayMeetings"); var icon = pair.GetVisualDescendants().OfType<IconGlyph>().Single(); var number = pair.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.True(Math.Abs(Centre(window, icon).Y - Centre(window, number).Y) <= 1.5, "The icon and its number share a centre line.");
            // The total over the column is in the ordinary typeface, and the chart carries no hint line.
            Assert.All(window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "WeekDayTotal"), t => Assert.DoesNotContain("figures", t.Classes));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").StartsWith("Hover a day"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSelectedBlockCardWritesNothingUnderItAndAnImpossibleMergeSaysWhyInTheToast()
    {
        var model = Model(); model.Engine.Sessions.Add(Row("12:00:00", "30", "Account two"));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.SelectBlock(new[] { model.Engine.Sessions[0] }, false); Settle(window);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").StartsWith("Shift-click"));
            model.SelectBlock(new[] { model.Engine.Sessions[1] }, true); Settle(window);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").StartsWith("Shift-click") || t.Name is "SelectedHint" or "SelectedNote");
            var merge = Find<Button>(window, "MergeSelected"); Assert.True(merge.IsEnabled);
            Click(window, "MergeSelected"); Assert.Equal("Same account only", model.EditError); Assert.Equal(3, model.Engine.Sessions.Count); // Nothing was merged.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ZoomOutNamesTheCtrlWheelLikeZoomInAndTheTypefacesAreTheOrdinaryOne()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.IsType<StackPanel>(ToolTip.GetTip(Find<Button>(window, "TimelineZoomOut"))); Assert.IsType<StackPanel>(ToolTip.GetTip(Find<Button>(window, "TimelineZoomIn")));
            Assert.DoesNotContain("figures", Find<Border>(window, "NowChip") is { Child: TextBlock t } ? t.Classes : new Avalonia.Collections.AvaloniaList<string>());
        }
        finally { window.Close(); }
    }

    [Fact]
    public void TheNotesIconStartsToChangeWithinAFewTenthsOfASecond()
    {
        Assert.Equal(1, RailMotion.TickProgress(0), 6);
        Assert.True(RailMotion.TickProgress(.3) < .9, "The tick is already leaving a third of a second in.");
        Assert.True(RailMotion.CrossProgress(.5) > .5);
    }

    [AvaloniaFact]
    public void ThePressOnTheNotesSearchBoxFocusesItInsteadOfDraggingTheWindowAndTypingFiltersTheList()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab");
            var box = Find<TextBox>(window, "NotesSearch");
            Press(window, Centre(window, box)); Assert.True(box.IsFocused, "A real press lands in the box.");
            window.KeyTextInput("zzz"); Settle(window);
            Assert.Equal("zzz", box.Text); Assert.Equal("Nothing matches that search.", Find<TextBlock>(window, "NotesPageEmpty").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NoteAndMeetingAreOneSwitchAndAddHasAccentInk()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab");
            var note = Find<Button>(window, "PageKindNote"); var meeting = Find<Button>(window, "PageKindMeeting");
            Assert.Same(note.Parent, meeting.Parent); Assert.Contains("segTrack", ((Border)note.Parent!.Parent!).Classes);
            Assert.Contains("optOn", note.Classes); Assert.DoesNotContain("optOn", meeting.Classes);
            Click(window, "PageKindMeeting"); Assert.Contains("optOn", Find<Button>(window, "PageKindMeeting").Classes); Assert.DoesNotContain("optOn", Find<Button>(window, "PageKindNote").Classes);
            var add = Find<Button>(window, "PageSubmit"); Assert.Contains("primary", add.Classes);
            Assert.Equal("#FFFFFF", AppThemes.Light.Colour("AccentInk"), ignoreCase: true);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AHashInTheNotesPagesBoxOffersTheAccountsAndChoosingOneSetsItAndTakesTheWordOut()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab");
            var field = Find<TextBox>(window, "PageNoteField"); field.Text = "Ring them #Account t"; Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("PageSuggest_") == true && b.IsEffectivelyVisible));
            field.Text = "Ring them #acc"; Dispatcher.UIThread.RunJobs(); Settle(window);
            Assert.Equal(2, window.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("PageSuggest_") == true));
            Click(window, "PageSuggest_Accounttwo");
            Assert.Equal("Ring them", Find<TextBox>(window, "PageNoteField").Text); Assert.Equal("Account two", Find<ComboBox>(window, "PageAccount").SelectedItem);
            Assert.Empty(window.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("PageSuggest_") == true));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheBellCardHasNoSubtitle()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "Notifications");
            var popup = Find<Avalonia.Controls.Primitives.Popup>(window, "NotificationsPopup");
            Assert.DoesNotContain(((Visual)popup.Child!).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Reminders from your notes");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheRailIconsAreASofterGreyOnLightButStillReadAgainstThePageAndTheChosenTile()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Avalonia.Media.Color Resource(string key) { Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var v)); return ((Avalonia.Media.ISolidColorBrush)v!).Color; }
            double Ratio(Avalonia.Media.Color a, Avalonia.Media.Color b) { double L(Avalonia.Media.Color c) { double F(byte x) { var v = x / 255.0; return v <= .03928 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); } return .2126 * F(c.R) + .7152 * F(c.G) + .0722 * F(c.B); } var (hi, lo) = (Math.Max(L(a), L(b)), Math.Min(L(a), L(b))); return (hi + .05) / (lo + .05); }
            var icon = Resource("RailIcon"); var muted = Resource("Muted");
            Assert.True(Ratio(icon, Resource("Surface")) >= 3, "Readable on the page.");
            Assert.True(Ratio(icon, Resource("Surface")) < Ratio(muted, Resource("Surface")), "Lighter than the quiet text colour.");
            Assert.True(Ratio(Resource("RailIconOn"), Resource("Selected")) >= 3, "The chosen icon reads on its tint.");
            Assert.Equal("RailIconOn", Find<Button>(window, "TimelineTab").GetVisualDescendants().OfType<IconGlyph>().Single().TintKey);
            Assert.Equal("RailIcon", Find<Button>(window, "NotesTab").GetVisualDescendants().OfType<IconGlyph>().Single().TintKey);
        }
        finally { window.Close(); }
    }
}
