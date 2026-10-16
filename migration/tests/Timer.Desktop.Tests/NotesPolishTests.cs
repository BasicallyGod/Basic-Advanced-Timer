using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class NotesPolishTests
{
    private static readonly string[] Accounts = { "Account one", "Account two", "Internal", "Break" };

    [Fact]
    public void TheTagBeingTypedIsFoundBeforeTheCaretAndOnlyThere()
    {
        Assert.Equal((5, "Acc"), TagSuggestions.Current("call #Acc", 9)); Assert.Equal((0, ""), TagSuggestions.Current("#", 1));
        Assert.Equal((5, "A"), TagSuggestions.Current("call #Acme later", 7)); // The caret inside the tag counts, what follows it does not.
        Assert.Null(TagSuggestions.Current("call Sam", 8)); Assert.Null(TagSuggestions.Current("call #Acme later", 16)); Assert.Null(TagSuggestions.Current("a#b", 3)); // A # inside a word is not a tag.
    }

    [Fact]
    public void OnlyAccountsThatStartThatWayAreOfferedAndNeverTheBreak()
    {
        Assert.Equal(new[] { "Account one", "Account two" }, TagSuggestions.Matches("acc", Accounts));
        Assert.Equal(new[] { "Account one", "Account two", "Internal" }, TagSuggestions.Matches("", Accounts)); // Nothing typed yet offers them all, but not Break.
        Assert.Equal(new[] { "Account two" }, TagSuggestions.Matches("accounttw", Accounts)); Assert.Empty(TagSuggestions.Matches("zzz", Accounts)); Assert.Empty(TagSuggestions.Matches("br", Accounts));
        Assert.True(TagSuggestions.IsComplete("Internal", TagSuggestions.Matches("Internal", Accounts))); Assert.False(TagSuggestions.IsComplete("Acc", TagSuggestions.Matches("Acc", Accounts)));
    }

    [Fact]
    public void ChoosingAnAccountWritesItsWholeTagAndMovesTheCaretOn()
    {
        var (text, caret) = TagSuggestions.Accept("call #Acc about it", 5, 9, "Account two");
        Assert.Equal("call #Accounttwo about it", text); Assert.Equal(5 + "#Accounttwo ".Length, caret);
        Assert.Equal(("#Internal ", 10), TagSuggestions.Accept("#Int", 0, 4, "Internal"));
    }

    private static (TimerModel Model, DashboardWindow Window, TextBox Input) Open()
    {
        var model = new TimerModel(); var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NotesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        return (model, window, window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "NoteInput"));
    }
    private static void Press(TextBox box, Key key) { box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key }); Dispatcher.UIThread.RunJobs(); }
    private static Popup Picker(Window window) => window.GetLogicalDescendants().OfType<Popup>().Single(p => p.Name == "TagPicker");
    private static void Type(TextBox box, string text) { box.Focus(); box.Text = text; box.CaretIndex = text.Length; Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void TypingAHashOffersTheAccountsAndTabOrEnterCompletesTheChosenOneBeforeAnythingIsSaved()
    {
        var (model, window, input) = Open();
        try
        {
            Type(input, "call #Acc"); var popup = Picker(window); Assert.True(popup.IsOpen);
            var rows = ((StackPanel)((Border)popup.Child!).Child!).Children.OfType<Button>().Select(b => b.Name).ToList();
            Assert.Equal(new[] { "TagPickerRow_Account_one", "TagPickerRow_Account_two" }, rows);
            Press(input, Key.Down); Press(input, Key.Tab);
            Assert.Equal("call #Accounttwo ", input.Text); Assert.False(popup.IsOpen); Assert.Empty(model.AllDayNotes);
            // Enter while the list is open completes the tag instead of saving the note; the next Enter saves.
            Type(input, "ring #Int"); Assert.True(popup.IsOpen); Press(input, Key.Enter);
            Assert.Equal("ring #Internal ", input.Text); Assert.Empty(model.AllDayNotes);
            Press(input, Key.Enter); var note = Assert.Single(model.AllDayNotes); Assert.Equal("ring #Internal", note.Text); Assert.Equal("Internal", NoteShorthand.AccountOf(note.Text, model.AccountNames));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EscapeAndAFinishedTagPutTheListAwayAndAClickTakesAnAccount()
    {
        var (model, window, input) = Open();
        try
        {
            var popup = Picker(window);
            Type(input, "#Acc"); Assert.True(popup.IsOpen); Press(input, Key.Escape); Assert.False(popup.IsOpen); Assert.Equal("#Acc", input.Text);
            Type(input, "#Internal"); Assert.False(popup.IsOpen); // Already whole, so there is nothing to add.
            Type(input, "no tag here"); Assert.False(popup.IsOpen);
            Type(input, "#Acc"); var second = ((StackPanel)((Border)popup.Child!).Child!).Children.OfType<Button>().Last();
            second.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); Assert.Equal("#Accounttwo ", input.Text); Assert.False(popup.IsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TypingNeverMovesOrResizesTheBox()
    {
        var (_, window, input) = Open();
        try
        {
            window.UpdateLayout(); var before = input.Bounds; var top = input.TranslatePoint(new Point(0, 0), window)!.Value;
            input.Focus(); input.Text = "a long note that goes on and on and on about the supplier and the board pack and everything else under the sun"; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(before.Height, input.Bounds.Height); Assert.Equal(before.Width, input.Bounds.Width); Assert.Equal(top, input.TranslatePoint(new Point(0, 0), window)!.Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ANoteCanBePutOnAnyDayWithTheCalendarBesideTheBoxAndComingUpShowsItMonthsAhead()
    {
        var (model, window, input) = Open();
        try
        {
            var far = model.SelectedDate.AddDays(75);
            var pick = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NoteDayPick");
            var calendar = (Calendar)((Flyout)pick.Flyout!).Content!; ((Flyout)pick.Flyout!).ShowAt(pick); Dispatcher.UIThread.RunJobs();
            calendar.SelectedDate = far; Dispatcher.UIThread.RunJobs();
            var chip = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NoteDayChip"); Assert.True(chip.IsVisible);
            Assert.Contains("For " + far.ToString("ddd d MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), chip.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Type(input, "board pack"); Press(input, Key.Enter);
            var note = Assert.Single(model.AllDayNotes); Assert.Equal(far, note.Day); Assert.False(chip.IsVisible); // One note only: the next goes on the day showing again.
            Assert.Contains(window.GetVisualDescendants().OfType<CheckBox>(), c => c.Name == "ComingTick_" + note.Id); // Coming up reaches months ahead.
            Type(input, "today note"); Press(input, Key.Enter); Assert.Equal(model.SelectedDate, model.AllDayNotes.Last().Day);
            // A day typed in the words still wins over the picked one.
            Type(input, "later @tomorrow"); Press(input, Key.Enter); Assert.Equal(model.SelectedDate.AddDays(1), model.AllDayNotes.Last().Day);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressingTheDateInTheHeaderOpensACalendarThatJumpsToAnyDay()
    {
        var (model, window, _) = Open();
        try
        {
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DatePick");
            var calendar = (Calendar)((Flyout)button.Flyout!).Content!; ((Flyout)button.Flyout!).ShowAt(button); Dispatcher.UIThread.RunJobs();
            var far = model.SelectedDate.AddDays(40); calendar.SelectedDate = far; Dispatcher.UIThread.RunJobs();
            Assert.Equal(far, model.SelectedDate);
            Assert.Contains(far.ToString("ddd d MMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void TheWeekViewsStillStopAtTheCurrentWeekWhenJumping()
    {
        var model = new TimerModel(); var today = model.Engine.Now.Date; model.GoToDate(today.AddDays(30), weeks: true); Assert.Equal(today, model.SelectedDate);
        model.GoToDate(today.AddDays(30)); Assert.Equal(today.AddDays(30), model.SelectedDate); // Days and notes can look ahead.
        model.GoToDate(today.AddDays(-20), weeks: true); Assert.Equal(today.AddDays(-20), model.SelectedDate);
    }

    [AvaloniaFact]
    public void QuickCaptureCompletesATagToo()
    {
        var model = new TimerModel(); var window = new QuickCaptureWindow(model); window.Show();
        try
        {
            var box = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "CaptureText"); box.Focus(); box.Text = "#Int"; box.CaretIndex = 4; Dispatcher.UIThread.RunJobs();
            var popup = window.GetLogicalDescendants().OfType<Popup>().Single(p => p.Name == "TagPicker"); Assert.True(popup.IsOpen);
            Press(box, Key.Tab); Assert.Equal("#Internal ", box.Text); Assert.True(window.IsVisible); // Tab completes it and the box stays.
        }
        finally { window.Close(); }
    }
}
