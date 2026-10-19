using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>Regressions from the second round of Windows screenshots: a settings row running off its card, a heavy calendar, a cluttered Notes page and Feed.</summary>
public sealed class UiPolishTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "ui-polish-" + Guid.NewGuid().ToString("N"));
    public UiPolishTests() => Directory.CreateDirectory(folder);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(folder, true); } catch (IOException) { } }

    private static DashboardWindow Open(TimerModel model, string tab)
    {
        var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == tab).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void NothingInSettingsRunsPastTheEdgeOfItsCard()
    {
        var model = TimerModel.CreateDemo(); model.SetFeedFolder(folder);
        var window = Open(model, "Settings");
        try
        {
            var cards = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("dayCard") && b.IsVisible && b.Bounds.Width > 100).ToList();
            Assert.NotEmpty(cards);
            foreach (var card in cards)
                foreach (var child in card.GetVisualDescendants().OfType<Control>().Where(c => c.IsVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0))
                {
                    if (child.GetVisualAncestors().OfType<ScrollViewer>().Any(s => card.IsVisualAncestorOf(s))) continue; // Inside a box of its own that scrolls.
                    var right = child.TranslatePoint(new Point(child.Bounds.Width, 0), card)?.X ?? 0;
                    Assert.True(right <= card.Bounds.Width + 1, $"{child.GetType().Name} {child.Name} ends at {right} in a card {card.Bounds.Width} wide");
                }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheCalendarWearsTheThemeNotTheComputersGreys()
    {
        var model = TimerModel.CreateDemo(); model.SetTheme("Dark");
        var window = Open(model, "NotesTab");
        try
        {
            Assert.True(window.TryFindResource("CalendarViewBackground", window.ActualThemeVariant, out var background));
            Assert.Equal(Color.Parse(model.CurrentTheme.Colour("Surface")), ((SolidColorBrush)background!).Color);
            Assert.True(window.TryFindResource("CalendarViewCalendarItemRevealBorderBrush", window.ActualThemeVariant, out var gaps));
            Assert.Equal(0, ((SolidColorBrush)gaps!).Color.A); // No grid of grey bars between the days.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NotesHeadingsAreQuietAndTheRowToolsWaitForThePointer()
    {
        var model = TimerModel.CreateDemo();
        var window = Open(model, "NotesTab");
        try
        {
            var page = window.GetVisualDescendants().OfType<NotesPage>().Single();
            foreach (var text in new[] { "Coming up", model.SelectedDate.ToString("dddd d MMMM", System.Globalization.CultureInfo.GetCultureInfo("en-GB")) })
                Assert.False(page.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == text).Classes.Contains("clock"), text + " is in the display face");
            var remove = page.GetVisualDescendants().OfType<Button>().First(b => b.Name?.StartsWith("NoteRemove_") == true);
            Assert.Equal(0, remove.Opacity);
            var row = remove.GetVisualAncestors().OfType<Grid>().First(g => g.Children.Contains(remove.Parent!) || g.Children.OfType<StackPanel>().Any(p => p.Children.Contains(remove)));
            row.Classes.Add("revealing"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, remove.Opacity);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ComingUpGroupsNotesUnderTheirDay()
    {
        var model = TimerModel.CreateDemo(); var view = new ComingUpView(model); var host = new Window { Content = view, Width = 400, Height = 800 }; host.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var words = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Single(words, "Today"); // Said once, as a heading, not on every row.
            Assert.Contains("Tomorrow", words);
        }
        finally { host.Close(); }
    }

    [Fact]
    public void FeedBodiesDropTheirTagLinesButKeepTagsInSentences()
    {
        Assert.Equal("A body.", FeedPage.WithoutTagLines("#Accountone\nA body."));
        Assert.Equal("Worth a call about #Accountone today.", FeedPage.WithoutTagLines("Worth a call about #Accountone today."));
        Assert.Equal("", FeedPage.WithoutTagLines("#Accountone #Accounttwo"));
    }

    [AvaloniaFact]
    public void TheFeedPutsItsControlsInOneSlimRowAndItsEntriesOnPlainRows()
    {
        File.WriteAllText(Path.Combine(folder, "wire.md"),
            "## First\nDate: 2026-10-16 09:00\nID: a\n#AccountOne\nWords.\n\n## Second\nDate: 2026-10-16 08:00\nID: b\n#AccountTwo\nMore words.\n");
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 16, 12, 0, 0))); model.CompleteSetup(); model.SetFeedFolder(folder); model.RefreshFeed();
        var window = Open(model, "FeedTab");
        try
        {
            var page = window.GetVisualDescendants().OfType<FeedPage>().Single();
            Assert.Empty(page.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("FeedFilterAccount_") == true)); // One short list instead of a button per account.
            Assert.Single(page.GetVisualDescendants().OfType<ComboBox>(), c => c.Name == "FeedAccountFilter");
            var entries = page.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("FeedEntry_") == true).ToList();
            Assert.Equal(2, entries.Count);
            Assert.All(entries, e => Assert.Equal(new Thickness(0, 0, 0, 1), e.BorderThickness)); // A hairline under each, no box.
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Inlines?.Any(i => (i as Avalonia.Controls.Documents.Run)?.Text?.Contains("#AccountOne") == true) == true);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressingTheFirstFeedEntryIsNeverTakenForADragOfTheWindow()
    {
        File.WriteAllText(Path.Combine(folder, "wire.md"), "## First\nDate: 2026-10-16 09:00\nID: a\nWords.\n\n## Second\nDate: 2026-10-16 08:00\nID: b\nMore words.\n");
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 10, 16, 12, 0, 0))); model.CompleteSetup(); model.SetFeedFolder(folder); model.RefreshFeed();
        var window = Open(model, "FeedTab"); // The window is shown on the timeline first, so the full column has bounds that it keeps once hidden.
        try
        {
            var first = window.GetVisualDescendants().OfType<Border>().First(b => b.Name?.StartsWith("FeedEntry_") == true);
            foreach (var at in new[] { new Point(10, 10), new Point(60, 25), new Point(first.Bounds.Width / 2, 10) })
                Assert.False(window.InDragZone(first.TranslatePoint(at, window)!.Value));
        }
        finally { window.Close(); }
    }
}
