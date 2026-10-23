using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The Selected block card under the timeline, the floating message with its Undo, and the look of the timeline's blocks and the dashboard's buttons.
public sealed class SelectedBlockCardTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static SessionRow Row(string start, string minutes, string account = "Account one") => new(Day.ToString("yyyy-MM-dd"), account, start, "added", minutes);
    private static TimerModel Model(out TrackingEngine engine)
    {
        engine = new TrackingEngine(clock: () => Day.AddHours(16));
        engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:30:00", "30"), Row("11:00:00", "30", "Account two") });
        return new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday));
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Enter(Control box) => box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
    private static void Click(Control root, string name) { Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void ChangingTheStartKeepsTheEndAndTheBlockStaysSelected()
    {
        var model = Model(out var engine); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.SelectBlock(new[] { engine.Sessions[2] }, false); Settle(window);
            Find<TextBox>(window, "SelectedStart").Text = "10:45"; Enter(Find<TextBox>(window, "SelectedStart")); Settle(window);
            Assert.Equal("10:45:00", engine.Sessions[2].Start); Assert.Equal("11:30:00", Find<TextBox>(window, "SelectedEnd").Text); Assert.Equal("0:45", Find<TextBox>(window, "SelectedLength").Text);
            Assert.Same(engine.Sessions[2], Assert.Single(model.SelectedRows)); // Still selected, so the card still shows it.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheAccountListMovesTheBlockAndItsCreditedTimeStaysTheSame()
    {
        var model = Model(out var engine); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var row = engine.Sessions[0]; model.SelectBlock(new[] { row }, false); Settle(window);
            var account = Find<ComboBox>(window, "SelectedAccount"); Assert.Contains("Break", account.ItemsSource!.Cast<string>());
            account.SelectedItem = "Account two"; Settle(window);
            Assert.Equal("Account two", engine.Sessions[0].Account); Assert.Equal("30", engine.Sessions[0].Minutes); Assert.Equal("09:00:00", engine.Sessions[0].Start);
            Assert.True(model.UndoEdit()); Assert.Equal("Account one", engine.Sessions[0].Account);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheNoteIsEditedInTheCardAndSavedWhenEnterIsPressed()
    {
        var model = Model(out var engine); model.SetTimelineScale(320, 0, 0); model.TimelineOffset = null; var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.SelectBlock(new[] { engine.Sessions[0] }, false); Settle(window);
            var note = Find<TextBox>(window, "SelectedNote"); note.Text = "Drafting"; Enter(note); Settle(window);
            Assert.Equal("Drafting", model.Editor.Note(engine.Sessions[0])); Assert.Equal("30", engine.Sessions[0].Minutes);
            Assert.Contains("• Drafting", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)); // The block on the timeline shows the first line.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DeleteTakesTheBlockOutSaysSoWithAnUndoAndTheUndoBringsItBack()
    {
        var model = Model(out var engine); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.SelectBlock(new[] { engine.Sessions[2] }, false); Settle(window);
            Click(window, "DeleteSelected"); Settle(window);
            Assert.Equal(2, engine.Sessions.Count); Assert.Empty(model.SelectedRows);
            var bar = Find<Border>(window, "MessageBar"); Assert.True(bar.IsVisible); Assert.Equal("Deleted.", Find<TextBlock>(window, "MessageText").Text);
            Assert.True(Find<Button>(window, "UndoMessage").IsVisible);
            Click(window, "UndoMessage"); Settle(window);
            Assert.Equal(3, engine.Sessions.Count); Assert.Equal("Undone.", model.Notice); Assert.False(Find<Button>(window, "UndoMessage").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AMergedBlockHasReadOnlyTimesChipsForWhatItJoinsAndAnUnmergeButton()
    {
        var model = Model(out var engine); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.ToggleSelection(new[] { engine.Sessions[0], engine.Sessions[1] }); Assert.True(model.MergeSelection()); Settle(window);
            Assert.Equal("Merged.", Find<TextBlock>(window, "MessageText").Text);
            var group = model.Editor.Entries(Day, Day.AddDays(1)).Single(e => e.GroupId is not null);
            model.SelectBlock(group.Sources, false); Settle(window);
            Assert.Equal("Merged block", Find<TextBlock>(window, "SelectedTitle").Text);
            Assert.False(Find<TextBox>(window, "SelectedStart").IsEnabled); Assert.False(Find<ComboBox>(window, "SelectedAccount").IsEnabled);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "09:00–09:30"); Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "09:30–10:00");
            Click(window, "UnmergeSelected"); Settle(window);
            Assert.DoesNotContain(model.Editor.Entries(Day, Day.AddDays(1)), e => e.GroupId is not null); Assert.Contains("Unmerged", model.Notice);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SeveralBlocksShowARowEachAndDeleteRemovesThemAllAsOneStep()
    {
        var model = Model(out var engine); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.ToggleSelection(engine.Sessions.ToArray()); Settle(window);
            Assert.Equal("3 blocks selected", Find<TextBlock>(window, "SelectedTitle").Text);
            Assert.Equal(3, Find<ScrollViewer>(window, "SelectedRows").GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("selectedRow")));
            Assert.Equal("Shift-click a selected block to take it out of the selection.", Find<TextBlock>(window, "SelectedHint").Text);
            Click(window, "DeleteSelected"); Settle(window);
            Assert.Empty(engine.Sessions); Assert.Equal("Deleted 3 blocks.", model.Notice);
            Assert.True(model.UndoEdit()); Assert.Equal(3, engine.Sessions.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ARefusalIsAWarningWithoutAnUndoAndANoteOfSomethingDoneIsATick()
    {
        var model = Model(out _); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.Refuse("End has to be after the start."); Settle(window);
            var bar = Find<MessageBar>(window, "MessageBar"); Assert.True(bar.IsWarning); Assert.Contains("warn", bar.Classes); Assert.False(Find<Button>(window, "UndoMessage").IsVisible);
            model.ExpireMessages(); Settle(window);
            Assert.True(model.CopyBlocks(model.Engine.Sessions.Take(1).ToList())); Settle(window);
            Assert.False(bar.IsWarning); Assert.DoesNotContain("warn", bar.Classes); Assert.Equal("Copied 1 block.", Find<TextBlock>(window, "MessageText").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSelectionRingIsNotClippedAndTheBlocksTimeIsInTheOrdinaryTypeface()
    {
        var model = Model(out var engine); model.SetTimelineScale(320, 0, 0); model.TimelineOffset = null; var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            model.SelectBlock(new[] { engine.Sessions[0] }, false); Settle(window);
            var card = Find<Border>(window, "TimelineEntry0");
            Assert.False(card.ClipToBounds); // The card is not clipped, or the ring round it would be cut off.
            Assert.Equal(1, card.BoxShadow.Count); Assert.Equal(3, card.BoxShadow[0].Spread);
            var time = card.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "BlockDuration");
            Assert.DoesNotContain("clock", time.Classes); Assert.Equal(FontWeight.SemiBold, time.FontWeight);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheMeetingRowsRoomIsKeptAboveTheBlocksAndTheDayBarsUnallocatedStripesCanBeSeen()
    {
        var model = TimerModel.CreateDemo(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var card = window.GetVisualDescendants().OfType<Border>().First(b => b.Name?.StartsWith("TimelineEntry") == true);
            Assert.True(Canvas.GetTop(card) >= 92); // A row above the blocks for planned meetings, which arrive with Notes.
            var stripes = Find<Border>(window, "DayBarUnallocated"); Assert.IsType<LinearGradientBrush>(stripes.Background);
            Assert.Equal(GradientSpreadMethod.Repeat, ((LinearGradientBrush)stripes.Background!).SpreadMethod);
            Assert.Contains(((LinearGradientBrush)stripes.Background).GradientStops, g => g.Color == Colors.Transparent); // Stripes on nothing: they show against the track.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheRightClickMenuIsDrawnInTheThemesColours()
    {
        var model = Model(out _); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            foreach (var theme in new[] { AppThemes.Light, AppThemes.Dark })
            {
                Assert.True(Avalonia.Application.Current!.Resources.ThemeDictionaries.TryGetValue(ThemeEngine.VariantFor(theme), out var found));
                var dictionary = (ResourceDictionary)found!;
                Assert.Equal(Color.Parse(theme.Colour("Panel")), ((SolidColorBrush)dictionary["MenuFlyoutPresenterBackground"]!).Color);
                Assert.Equal(Color.Parse(theme.Colour("Hover")), ((SolidColorBrush)dictionary["MenuFlyoutItemBackgroundPointerOver"]!).Color);
                Assert.Equal(Color.Parse(theme.Colour("Ink")), ((SolidColorBrush)dictionary["MenuFlyoutItemForeground"]!).Color);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheHeaderButtonsAreOutlinedCardsWithTheDateInsideACalendarButton()
    {
        var model = Model(out _); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            foreach (var name in new[] { "PreviousPeriod", "DatePick", "NextPeriod", "GoToToday", "Settings" })
            {
                var button = Find<Button>(window, name); Assert.Contains("ctl", button.Classes); Assert.Equal(38, button.Height); Assert.Equal(new CornerRadius(12), button.CornerRadius);
            }
            Assert.NotEmpty(Find<Button>(window, "DatePick").GetVisualDescendants().OfType<IconGlyph>()); // The calendar icon beside the date.
            Assert.Equal(IconKind.Plus, Find<Button>(window, "AddTime").GetVisualDescendants().OfType<IconGlyph>().Single().Kind); // Add time keeps its plus.
        }
        finally { window.Close(); }
    }
}
