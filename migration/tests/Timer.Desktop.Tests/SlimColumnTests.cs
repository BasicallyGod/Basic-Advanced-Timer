using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class SlimColumnTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "slim-" + Guid.NewGuid().ToString("N"));
    public SlimColumnTests() => Directory.CreateDirectory(folder);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(folder, true); } catch (IOException) { } }

    private static void Click(Visual root, string name) { root.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static DashboardWindow Open(TimerModel model) { var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs(); return window; }
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private const string Wire = "## Fintech round\nDate: 2026-10-16 08:45\nID: a2\n#Accounttwo\nA payments firm closed a round.\n\n## Regulator consultation\nDate: 2026-10-16 09:00\nID: a1\n#Accountone\nA regulator opened a consultation.\n";
    private TimerModel Demo()
    {
        File.WriteAllText(Path.Combine(folder, "wire.md"), Wire);
        var model = TimerModel.CreateDemo(); model.SetFeedFolder(folder); model.RefreshFeed(); return model;
    }

    [AvaloniaFact]
    public void NotesAndFeedGiveUpTheLeftColumnAndFoldItsControlsIntoAWiderRail()
    {
        var model = Demo(); var window = Open(model);
        try
        {
            var body = window.GetVisualDescendants().OfType<Grid>().First(g => g.ColumnDefinitions.Count == 3 && g.ColumnDefinitions[1].Width.Value == 1);
            var rail = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("panel") && b.Width == 60);
            var slim = window.GetVisualDescendants().OfType<CompactColumn>().Single();
            Assert.False(slim.IsVisible); Assert.Equal(340, body.ColumnDefinitions[0].Width.Value); Assert.Equal(60, rail.Width);
            Click(window, "NotesTab"); Assert.True(slim.IsVisible); Assert.Equal(0, body.ColumnDefinitions[0].Width.Value); Assert.Equal(CompactColumn.RailWidth, rail.Width);
            Assert.False(window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "AccountScroll").IsEffectivelyVisible);
            Assert.True(rail.IsVisualAncestorOf(slim)); // It is part of the rail, not a column of its own.
            Click(window, "FeedTab"); Assert.True(slim.IsVisible);
            Click(window, "TimelineTab"); Assert.False(slim.IsVisible); Assert.Equal(340, body.ColumnDefinitions[0].Width.Value); Assert.Equal(60, rail.Width);
            Click(window, "NotesTab"); Click(window, "Settings"); Assert.False(slim.IsVisible); // Settings keeps the full column.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheRailBlockHoldsTheClockTheControlsAndAnAvatarPerAccount()
    {
        var model = Demo(); var window = Open(model);
        try
        {
            Click(window, "NotesTab"); var slim = window.GetVisualDescendants().OfType<CompactColumn>().Single();
            Assert.Equal(model.AccountNames.Count, slim.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("CompactDot_") == true));
            Assert.Contains(slim.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "AO"); // Account one's initials.
            Assert.Equal(model.AccountNames.Count, slim.GetVisualDescendants().OfType<Border>().Where(b => b.Child is TextBlock { FontSize: 8.5 } t).Select(b => ((TextBlock)b.Child!).Text).Distinct().Count()); // No two avatars say the same.
            Click(slim, "CompactDot_" + FeedPage.Key("Account one")); Assert.Equal("Account one", model.ActiveAccount);
            Assert.Contains(slim.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Working");
            Click(slim, "CompactPause"); Assert.False(model.IsTracking);
            Click(slim, "CompactBreak"); Assert.True(model.IsOnBreak); Assert.Contains(slim.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Break");
            Click(slim, "CompactBreak"); Assert.False(model.IsOnBreak);
            var started = model.DayStarted; Click(slim, "CompactDay"); Assert.NotEqual(started, model.DayStarted);
            // Everything fits the rail's height: nothing is cut off at the bottom.
            var rail = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("panel") && b.Width == CompactColumn.RailWidth);
            foreach (var name in new[] { "Settings", "QuitTimer", "FeedTab", "NotesTab" })
            { var button = Find<Button>(window, name); Assert.True(button.TranslatePoint(new Point(0, button.Bounds.Height), rail)!.Value.Y <= rail.Bounds.Height + 1, name + " is below the rail"); }
        }
        finally { window.Close(); }
    }

    [Fact]
    public void AvatarLabelsAreShortAndNeverShared()
    {
        var labels = CompactColumn.Short(new[] { ("Account one", "Account one"), ("Account two", "Account two"), ("Account three", "Account three"), ("Account four", "Account four"), ("Account five", "Account five"), ("Internal", "Internal") });
        Assert.Equal(6, labels.Values.Distinct().Count()); Assert.Equal("AO", labels["Account one"]); Assert.All(labels.Values, l => Assert.InRange(l.Length, 1, 3));
        var numbered = CompactColumn.Short(new[] { ("Client 1", "Client 1"), ("Client 2", "Client 2"), ("Client 3", "Client 3") });
        Assert.Equal(new[] { "C1", "C2", "C3" }, numbered.Values);
        Assert.Equal(new[] { "1", "2" }, CompactColumn.Short(new[] { ("", ""), (" ", " ") }).Values); // Nothing to go on: places in the list.
    }

    [AvaloniaFact]
    public void AnAvatarShowsADotWhileTheFeedHasUnreadItemsForThatAccount()
    {
        var model = Demo(); var window = Open(model);
        try
        {
            Click(window, "NotesTab"); var slim = window.GetVisualDescendants().OfType<CompactColumn>().Single();
            Assert.Contains(slim.GetVisualDescendants().OfType<Border>(), b => b.Name == "CompactUnread_" + FeedPage.Key("Account one"));
            Assert.Contains(slim.GetVisualDescendants().OfType<Border>(), b => b.Name == "CompactUnread_" + FeedPage.Key("Account two"));
            Assert.DoesNotContain(slim.GetVisualDescendants().OfType<Border>(), b => b.Name == "CompactUnread_" + FeedPage.Key("Account three"));
            model.SetFeedRead("id:a1", true); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(slim.GetVisualDescendants().OfType<Border>(), b => b.Name == "CompactUnread_" + FeedPage.Key("Account one"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NotesTabsNarrowTheDayToOneKindOfNote()
    {
        var model = TimerModel.CreateDemo(); var window = Open(model);
        try
        {
            Click(window, "NotesTab");
            int Rows() => window.GetVisualDescendants().OfType<TextBox>().Count(t => t.Name?.StartsWith("NoteText_") == true);
            var all = Rows(); Assert.True(all >= 5);
            Click(window, "NotesFilter_tasks"); var tasks = Rows(); Assert.True(tasks > 0 && tasks < all);
            Assert.All(window.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name?.StartsWith("NoteText_") == true), t => Assert.True(DayNotes.IsTask(model.AllDayNotes.Single(n => "NoteText_" + n.Id == t.Name))));
            Click(window, "NotesFilter_deadlines"); Assert.All(window.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name?.StartsWith("NoteText_") == true), t => Assert.True(DayNotes.IsDeadline(model.AllDayNotes.Single(n => "NoteText_" + n.Id == t.Name))));
            Click(window, "NotesFilter_events"); Assert.All(window.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name?.StartsWith("NoteText_") == true), t => Assert.NotNull(model.AllDayNotes.Single(n => "NoteText_" + n.Id == t.Name).At));
            Click(window, "NotesFilter_all"); Assert.Equal(all, Rows());
            Assert.Equal("Add note", Find<Button>(window, "NoteAddButton").Content);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ComingUpChoosesHowFarAhead()
    {
        var model = TimerModel.CreateDemo(); model.AddDayNote(model.Engine.Now.Date, "far away @" + model.Engine.Now.Date.AddDays(100).ToString("yyyy-MM-dd")); var window = Open(model);
        try
        {
            Click(window, "NotesTab"); var range = Find<ComboBox>(window, "ComingRange");
            bool Shows() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "far away");
            Assert.Equal(1, range.SelectedIndex); Assert.False(Shows()); // Next 30 days.
            range.SelectedIndex = 2; Dispatcher.UIThread.RunJobs(); Assert.True(Shows());
            range.SelectedIndex = 0; Dispatcher.UIThread.RunJobs(); Assert.False(Shows());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FeedTabsSearchAndDetailPane()
    {
        var model = Demo(); var window = Open(model);
        try
        {
            Click(window, "FeedTab"); var page = window.GetVisualDescendants().OfType<FeedPage>().Single();
            int Entries() => page.GetVisualDescendants().OfType<Border>().Count(b => b.Name?.StartsWith("FeedEntry_") == true);
            Assert.False(Find<Border>(page, "FeedDetail").IsVisible); Assert.Equal(2, Entries());
            Find<TextBox>(page, "FeedSearch").Text = "fintech"; Dispatcher.UIThread.RunJobs(); Assert.Equal(1, Entries());
            Find<TextBox>(page, "FeedSearch").Text = ""; Dispatcher.UIThread.RunJobs(); Assert.Equal(2, Entries());
            page.Toggle("id:a1"); Dispatcher.UIThread.RunJobs();
            Assert.True(Find<Border>(page, "FeedDetail").IsVisible); Assert.True(Find<Border>(page, "FeedEntry_" + FeedPage.Key("id:a1")).Classes.Contains("feedSelected"));
            Click(page, "FeedDone_" + FeedPage.Key("id:a1")); Assert.True(model.FeedIsDone("id:a1")); Assert.Equal(1, Entries()); // Done leaves All.
            Click(page, "FeedFilterDone"); Assert.Equal(1, Entries()); Assert.True(model.FeedIsDone("id:a1"));
            Click(page, "FeedFilterUnread"); Assert.Equal(1, Entries()); Assert.False(model.FeedIsDone("id:a2"));
        }
        finally { window.Close(); }
    }
}
