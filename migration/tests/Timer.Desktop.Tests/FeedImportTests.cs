using Timer.Core;
using Xunit;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Timer.Desktop.Tests;

public sealed class FeedImportTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "feed-import-" + Guid.NewGuid());
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0);
    public FeedImportTests() => Directory.CreateDirectory(folder);
    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void ReportsAndStatesSurviveRestartAndSourceRemovalAndEdits()
    {
        var source = new FeedSource("assistant", "Briefing", "paste", "", "Account one");
        var original = "## Update\nID: stable\nDate: 2026-10-18\nSee https://example.com/report";
        string id;
        using (var archive = new FeedArchive(folder))
        {
            archive.Import(new(source, new[] { FeedImporters.TextReport(original, source.Name, Now) }), Now);
            id = Assert.Single(archive.Entries()).Id;
            archive.SaveMarks(new(new HashSet<string> { id }, new HashSet<string> { id }));
            archive.Import(new(source, new[] { FeedImporters.TextReport(original.Replace("Update", "Revised"), source.Name, Now) }), Now);
            Assert.Equal(id, Assert.Single(archive.Entries()).Id);
            Assert.Equal("Revised", archive.Entries()[0].Headline);
            archive.RemoveSource(source.Id);
        }
        using var again = new FeedArchive(folder);
        Assert.Empty(again.Sources()); Assert.Single(again.Entries());
        Assert.Contains(id, again.Marks().Read); Assert.Contains(id, again.Marks().Done);
        Assert.Equal("Account one", again.Account(id)); Assert.Contains("https://example.com/report", again.Original(id));
    }

    [Fact]
    public void IdenticalReportsDeduplicateAndIndependentSourceIdsDoNotCollide()
    {
        using var archive = new FeedArchive(folder);
        var report = FeedImporters.TextReport("## A\nID: 1\nWords", "Briefing", Now);
        var a = new FeedSource("a", "A", "paste", "");
        archive.Import(new(a, new[] { report }), Now); archive.Import(new(a, new[] { report }), Now);
        Assert.Single(archive.Entries());
        archive.Import(new(a with { Id = "b" }, new[] { report }), Now);
        Assert.Equal(2, archive.Entries().Count);
    }

    [Fact]
    public void OriginalOfAnOlderEntryIsNotReplacedByANewerReportAtTheSameLocation()
    {
        using var archive = new FeedArchive(folder); var source = new FeedSource("a", "A", "folder", folder);
        archive.Import(new(source, new[] { FeedImporters.TextReport("## First\nID: first\nBody", "A", Now, "daily.md") }), Now);
        archive.Import(new(source, new[] { FeedImporters.TextReport("## Second\nID: second\nOther", "A", Now, "daily.md") }), Now);
        Assert.Contains("First", archive.Original("a:id:first")); Assert.DoesNotContain("Second", archive.Original("a:id:first"));
        Assert.Equal(2, archive.Entries().Count);
    }

    [Fact]
    public async Task FolderImporterKeepsPlainTextAndReportsOutagesWithoutDroppingTheArchive()
    {
        var source = new FeedSource("a", "Cloud folder", "folder", folder);
        File.WriteAllText(Path.Combine(folder, "plain.txt"), "A plain report with https://example.com/news");
        var imported = await FeedImporters.ReadAsync(source);
        Assert.Null(imported.Error); Assert.Single(Assert.Single(imported.Reports).Entries);
        using var archive = new FeedArchive(null); archive.Import(imported, Now);
        File.Delete(Path.Combine(folder, "plain.txt")); Directory.Delete(folder);
        var unavailable = await FeedImporters.ReadAsync(source); Assert.NotNull(unavailable.Error);
        archive.Import(unavailable, Now); Assert.Single(archive.Entries());
        Directory.CreateDirectory(folder);
    }

    [Fact]
    public async Task LockedOrOversizedFilesAreRetriedAndDoNotBlockOtherFiles()
    {
        File.WriteAllText(Path.Combine(folder, "good.md"), "A complete report");
        File.WriteAllText(Path.Combine(folder, "large.txt"), new string('a', (int)Feed.MaxFileBytes + 1));
        using var locked = new FileStream(Path.Combine(folder, "locked.md"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var result = await FeedImporters.ReadAsync(new("a", "A", "folder", folder));
        Assert.Single(result.Reports); Assert.Contains("large.txt", result.Error); Assert.Contains("locked.md", result.Error);
    }

    [Fact]
    public void RssAndAtomKeepDatesLinksAndStableIdsAndRejectExternalEntities()
    {
        var rss = "<rss><channel><item><guid>one</guid><title>News</title><link>https://example.com/one</link><pubDate>Sun, 18 Oct 2026 09:00:00 GMT</pubDate><description>Words</description></item></channel></rss>";
        var entry = Assert.Single(FeedImporters.ParseRss(rss, "News", Now).Entries);
        Assert.True(entry.HasDate); Assert.Contains("https://example.com/one", entry.Body);
        Assert.Equal(entry.Id, Assert.Single(FeedImporters.ParseRss(rss.Replace("Words", "Updated"), "News", Now).Entries).Id);
        var atom = "<feed xmlns='http://www.w3.org/2005/Atom'><entry><id>one</id><title>News</title><link href='https://example.com/one'/><updated>2026-10-18T09:00:00Z</updated><summary>Words</summary></entry></feed>";
        Assert.Contains("https://example.com/one", Assert.Single(FeedImporters.ParseRss(atom, "News", Now).Entries).Body);
        Assert.Throws<System.Xml.XmlException>(() => FeedImporters.ParseRss("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///secret'>]><rss>&x;</rss>", "X", Now));
        Assert.Throws<InvalidDataException>(() => FeedImporters.ParseRss("<html/>", "X", Now));
    }

    [Fact]
    public void OnlyExplicitWebLinksCanBeOpened()
    {
        Assert.False(FeedImporters.SafeUrl("file:///C:/secret")); Assert.False(FeedImporters.SafeUrl("javascript:alert(1)"));
        Assert.False(FeedImporters.SafeUrl("https://user:password@example.com"));
        Assert.Equal(new[] { "https://example.com/a" }, FeedLinks.Find("[link](https://example.com/a) file:///secret javascript:alert(1)"));
    }

    [Fact]
    public void BodyEditsWithoutIdsRetainAnUnambiguousEntryAndItsState()
    {
        using var archive = new FeedArchive(folder); var source = new FeedSource("a", "A", "folder", folder);
        archive.Import(new(source, new[] { FeedImporters.TextReport("## Item\nFirst body", "A", Now, "daily.md") }), Now);
        var id = Assert.Single(archive.Entries()).Id;
        archive.SaveMarks(new(new HashSet<string> { id }, new HashSet<string>()));
        archive.Import(new(source, new[] { FeedImporters.TextReport("## Item\nChanged body", "A", Now, "daily.md") }), Now);
        Assert.Equal(id, Assert.Single(archive.Entries()).Id); Assert.Contains(id, archive.Marks().Read);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void DefaultAccountsTagsPauseAndRemovalWorkThroughTheModel()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Now));
        var source = new FeedSource("pasted", "Briefing", "paste", "", "Account one");
        model.SaveFeedSource(source); model.ImportFeedText(source.Id, "## General\nID: one\nWords\n## Tagged\nID: two\n#AccountTwo\nOther");
        Assert.Equal("Account one", model.FeedAccountOf(model.FeedEntries.Single(e => e.Headline == "General")));
        Assert.Equal("Account two", model.FeedAccountOf(model.FeedEntries.Single(e => e.Headline == "Tagged")));
        model.SaveFeedSource(source with { Paused = true });
        Assert.Throws<InvalidOperationException>(() => model.ImportFeedText(source.Id, "No import"));
        model.RemoveFeedSource(source.Id); Assert.Empty(model.FeedSources); Assert.Equal(2, model.FeedEntries.Count);
        model.StopForClose();
    }

    [Fact]
    public void FeedDatabaseHandlesAreReleasedBetweenOperations()
    {
        using var archive = new FeedArchive(folder);
        archive.SaveSource(new FeedSource("a", "A", "paste", ""));
        using var exclusive = new FileStream(Path.Combine(folder, "feed.db"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.Length > 0);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AddSourceCanImportPastedTextAndTheFeedShowsItWithoutAFolder()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Now));
        var window = new Window { Width = 640, Height = 650, Content = new SettingsView(model) }; window.Show();
        try
        {
            T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            void Click(string name) { Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
            Click("FeedAddSource"); Find<TextBox>("FeedSourceName").Text = "Daily briefing";
            Find<ComboBox>("FeedSourceType").SelectedIndex = 1;
            Find<ComboBox>("FeedSourceAccount").SelectedIndex = 1;
            Find<TextBox>("FeedImportText").Text = "## Today's report\nUseful words and https://example.com/news";
            Click("FeedSourceSave");
            var entry = Assert.Single(model.FeedEntries); Assert.Null(model.FeedFolder);
            model.AddFeedEntryToNotes(entry.Id); Assert.Contains("#Accountone", Assert.Single(model.NotesOn(Now.Date)).Text);
            window.Content = new FeedPage(model); Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "Today's report");
        }
        finally { window.Close(); model.StopForClose(); }
    }
}
