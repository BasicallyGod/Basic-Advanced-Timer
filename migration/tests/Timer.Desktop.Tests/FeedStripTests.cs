using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class FeedStripTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0);
    private readonly string folder = Path.Combine(Path.GetTempPath(), "feed-strip-" + Guid.NewGuid());
    public FeedStripTests() => Directory.CreateDirectory(folder);
    public void Dispose() { try { Directory.Delete(folder, true); } catch (IOException) { } }

    private const string One =
        "## ! Supplier delay confirmed\nDate: 2026-10-16 08:30\nID: a1\n#AccountOne\nShipment slipped.\n\n" +
        "## Contract renewal draft ready\nDate: 2026-10-15 17:00\nID: a2\n#AccountOne\nFor review.\n\n" +
        "## Invoice query answered\nDate: 2026-10-14 10:00\nID: a3\n#AccountOne\nResolved.\n\n" +
        "## Other client news\nDate: 2026-10-16 09:00\nID: b1\n#AccountTwo\nSomething else.\n\n" +
        "## Stale item\nDate: 2026-08-01 09:00\nID: a4\n#AccountOne\nOld.\n";

    private TimerModel Model()
    {
        File.WriteAllText(Path.Combine(folder, "wire.md"), One);
        var model = new TimerModel(new TrackingEngine(clock: () => Now)); model.CompleteSetup(); model.SetFeedFolder(folder); model.RefreshFeed(); return model;
    }
    private static (DashboardWindow Window, FeedStrip Strip) Open(TimerModel model)
    {
        var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        return (window, window.GetVisualDescendants().OfType<FeedStrip>().Single());
    }
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Visual root, string name) { Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static string Key(string id) => FeedPage.Key(id);

    [Fact]
    public void TheModelListsUnreadEntriesAboutOneAccountAndLeavesOutOldOnes()
    {
        var model = Model();
        Assert.Equal(new[] { "id:a1", "id:a2", "id:a3" }, model.FeedUnreadFor("Account one").Select(e => e.Id)); // The stale one is past the age limit.
        Assert.Equal(new[] { "id:b1" }, model.FeedUnreadFor("Account two").Select(e => e.Id));
        model.SetFeedRead("id:a2", true); Assert.Equal(new[] { "id:a1", "id:a3" }, model.FeedUnreadFor("Account one").Select(e => e.Id));
    }

    [AvaloniaFact]
    public void TheStripIsHiddenUntilAnAccountWithUnreadItemsIsRunningAndGoneOnABreak()
    {
        var model = Model(); var (window, strip) = Open(model);
        try
        {
            Assert.False(strip.IsVisible); // Nothing running.
            model.ToggleAccount("Account one"); Dispatcher.UIThread.RunJobs();
            Assert.True(strip.IsVisible);
            var texts = strip.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("From the feed · Account one", texts); Assert.Contains("Supplier delay confirmed", texts); Assert.Contains("Contract renewal draft ready", texts);
            Assert.DoesNotContain("Invoice query answered", texts); Assert.Contains("+1 more", texts); Assert.DoesNotContain("Other client news", texts);
            Assert.Contains("08:30", texts); Assert.Contains("Yest.", texts);
            model.ToggleBreak(); Dispatcher.UIThread.RunJobs(); Assert.False(strip.IsVisible);
            model.ToggleBreak(); model.ToggleAccount("Account two"); Dispatcher.UIThread.RunJobs();
            Assert.True(strip.IsVisible); Assert.Contains("Other client news", strip.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)); Assert.DoesNotContain(strip.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "+1 more");
            model.ToggleAccount("Internal"); Dispatcher.UIThread.RunJobs(); Assert.False(strip.IsVisible); // No feed items for it.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheCrossMarksAnItemReadAndTheStripClosesWhenNoneAreLeft()
    {
        var model = Model(); model.ToggleAccount("Account two"); var (window, strip) = Open(model);
        try
        {
            Assert.True(strip.IsVisible); Click(strip, "FeedStripRead_" + Key("id:b1"));
            Assert.True(model.FeedIsRead("id:b1")); Assert.False(strip.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClickingAnItemOpensTheFeedFilteredToTheAccountWithThatEntryOpenAndRead()
    {
        var model = Model(); model.ToggleAccount("Account one"); var (window, strip) = Open(model);
        try
        {
            Click(strip, "FeedStripItem_" + Key("id:a2"));
            var page = window.GetVisualDescendants().OfType<FeedPage>().Single();
            Assert.True(model.FeedIsRead("id:a2")); Assert.Equal(0, Find<TextBlock>(page, "FeedBody_" + Key("id:a2")).MaxLines); // Open in full.
            var entries = page.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("FeedEntry_", StringComparison.Ordinal) == true).Select(b => b.Name).ToList();
            Assert.DoesNotContain("FeedEntry_" + Key("id:b1"), entries); Assert.Contains("FeedEntry_" + Key("id:a1"), entries); // Only Account one's entries.
            // Clicking from the feed page itself refocuses it too.
            model.SetFeedRead("id:a1", false); Click(window, "FeedStripItem_" + Key("id:a1")); Assert.True(model.FeedIsRead("id:a1"));
        }
        finally { window.Close(); }
    }
}
