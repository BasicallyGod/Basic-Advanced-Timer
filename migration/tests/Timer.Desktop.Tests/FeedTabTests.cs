using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class FeedTabTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0);
    private readonly string folder = Path.Combine(Path.GetTempPath(), "feed-tab-" + Guid.NewGuid());
    public FeedTabTests() => Directory.CreateDirectory(folder);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(folder, true); } catch (IOException) { } }

    private const string Wire =
        "## ! Supplier delay confirmed\nDate: 2026-10-16 08:30\nID: acme-delay\n#AccountOne\nShipment slipped to **Tuesday**. Call before 2pm.\n\n" +
        "## Quarterly figures out\nDate: 2026-10-15 17:00\n#AccountTwo\n- revenue up\n- costs flat\n\n" +
        "## Old news\nDate: 2026-08-01 09:00\nNothing to see.\n";

    private TimerModel Model(bool withFolder = true)
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Now)); model.CompleteSetup();
        File.WriteAllText(Path.Combine(folder, "daily.md"), Wire);
        if (withFolder) { model.SetFeedFolder(folder); model.RefreshFeed(); }
        return model;
    }
    private static (DashboardWindow Window, TimerModel Model) Open(TimerModel model)
    {
        var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FeedTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        return (window, model);
    }
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Visual root, string name) { Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static string Key(string id) => FeedPage.Key(id);

    [Fact]
    public void TheModelReadsTheFolderNewestFirstAndHidesWhatIsPastTheAgeLimit()
    {
        var model = Model();
        Assert.Equal(new[] { "Supplier delay confirmed", "Quarterly figures out", "Old news" }, model.FeedEntries.Select(e => e.Headline));
        Assert.Equal(new[] { "Supplier delay confirmed", "Quarterly figures out" }, model.FeedShown.Select(e => e.Headline)); // Old news is 76 days old.
        model.SetFeedLonger(true); Assert.Equal(90, model.FeedRetentionDays); Assert.Equal(3, model.FeedShown.Count);
        Assert.Equal(3, model.FeedUnread); // Nothing has been read, so the third counts once it shows.
        Assert.StartsWith("1 KB in 1 file. 3 entries showing, 0 hidden as older than 90 days.", model.FeedNote);
    }

    [Fact]
    public void ReadAndDoneMarksPersistAndAreDroppedWhenAnEntryLeavesTheFolder()
    {
        var data = Path.Combine(folder, "data"); using var store = new TimerStore(data); var engine = new TrackingEngine(clock: () => Now);
        var model = new TimerModel(engine, store.Load(engine), store); model.SetFeedFolder(folder);
        File.WriteAllText(Path.Combine(folder, "daily.md"), Wire); model.RefreshFeed();
        model.SetFeedRead("id:acme-delay", true); model.SetFeedDone(model.FeedEntries[1].Id, true); model.Save();
        var again = new TimerModel(engine, store.Load(engine), store); again.RefreshFeed();
        Assert.True(again.FeedIsRead("id:acme-delay")); Assert.True(again.FeedIsDone(again.FeedEntries[1].Id)); Assert.Equal(0, again.FeedUnread);
        // The file is rewritten without the first entry: its mark goes with it, the other stays.
        File.WriteAllText(Path.Combine(folder, "daily.md"), Wire[(Wire.IndexOf("## Quarterly", StringComparison.Ordinal))..]); again.RefreshFeed();
        Assert.False(again.FeedIsRead("id:acme-delay")); Assert.True(again.FeedIsDone(again.FeedEntries[0].Id));
        // An empty folder (a cloud drive that is offline) drops nothing.
        File.Delete(Path.Combine(folder, "daily.md")); again.RefreshFeed(); File.WriteAllText(Path.Combine(folder, "daily.md"), Wire[(Wire.IndexOf("## Quarterly", StringComparison.Ordinal))..]); again.RefreshFeed();
        Assert.True(again.FeedIsDone(again.FeedEntries[0].Id));
    }

    [Fact]
    public void AddingAnEntryToNotesKeepsItsUrgencyAndAccount()
    {
        var model = Model(); model.AddFeedEntryToNotes("id:acme-delay");
        var note = Assert.Single(model.NotesOn(Now.Date)); Assert.Equal("! Supplier delay confirmed #Accountone", note.Text); Assert.True(DayNotes.IsDeadline(note));
        Assert.Equal("Account one", NoteShorthand.AccountOf(note.Text, model.AccountNames)); Assert.Equal("Added to today's notes.", model.Notice);
        model.AddFeedEntryToNotes(model.FeedEntries[1].Id); Assert.Equal("Quarterly figures out #Accounttwo", model.NotesOn(Now.Date).Last().Text);
    }

    [AvaloniaFact]
    public void TheFeedRailButtonShowsTheStreamWithDayHeadingsAndAnUnreadBadge()
    {
        var (window, model) = Open(Model());
        try
        {
            Assert.Single(window.GetVisualDescendants().OfType<FeedPage>());
            Assert.True(Find<Border>(window, "FeedBadge").IsVisible); Assert.Equal("Feed (2 unread)", ToolTip.GetTip(Find<Button>(window, "FeedTab")));
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Today", texts); Assert.Contains("Yesterday", texts); Assert.Contains("Supplier delay confirmed", texts); Assert.DoesNotContain("Old news", texts);
            Assert.Contains(window.GetVisualDescendants().OfType<Control>(), c => c.Name == "FeedUnreadDot_" + Key("id:acme-delay"));
            Assert.Contains("2 unread", texts.First(t => t?.Contains("unread", StringComparison.Ordinal) == true && t.Contains('·')));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OpeningAnEntryShowsItInFullAndMarksItReadWhenThatIsOn()
    {
        var (window, model) = Open(Model());
        try
        {
            var page = window.GetVisualDescendants().OfType<FeedPage>().Single();
            Assert.Equal(2, Find<TextBlock>(window, "FeedBody_" + Key("id:acme-delay")).MaxLines);
            page.Toggle("id:acme-delay"); Dispatcher.UIThread.RunJobs();
            Assert.True(model.FeedIsRead("id:acme-delay")); Assert.Equal(0, Find<TextBlock>(window, "FeedDetailBody_" + Key("id:acme-delay")).MaxLines); // In full in the pane beside the list.
            Assert.True(Find<Border>(window, "FeedDetail").IsVisible); Assert.Equal(2, Find<TextBlock>(window, "FeedBody_" + Key("id:acme-delay")).MaxLines); // The row stays short.
            Click(window, "FeedDetailClose"); Assert.False(Find<Border>(window, "FeedDetail").IsVisible); page.Toggle("id:acme-delay"); Dispatcher.UIThread.RunJobs();
            Assert.True(Find<Border>(window, "FeedBadge").IsVisible); // One entry is still unread.
            Assert.Equal(1, model.FeedUnread); Click(window, "FeedDone_" + Key("id:acme-delay")); Assert.True(model.FeedIsDone("id:acme-delay"));
        }
        finally { window.Close(); }
    }

    private static void Pick(Window window, string? tag)
    {
        var box = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "FeedAccountFilter");
        box.SelectedIndex = tag is null ? 0 : ((IEnumerable<string>)box.ItemsSource!).ToList().IndexOf(tag); Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void WithAutoReadOffOpeningDoesNotMarkAndTheFiltersNarrowTheStream()
    {
        var model = Model(); model.SetFeedAutoRead(false); var (window, _) = Open(model);
        try
        {
            window.GetVisualDescendants().OfType<FeedPage>().Single().Toggle("id:acme-delay"); Dispatcher.UIThread.RunJobs();
            Assert.False(model.FeedIsRead("id:acme-delay"));
            Pick(window, "#Accounttwo");
            Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Name?.StartsWith("FeedEntry_", StringComparison.Ordinal) == true);
            Pick(window, null);
            model.SetFeedRead("id:acme-delay", true); Dispatcher.UIThread.RunJobs(); Click(window, "FeedFilterUnread");
            Assert.Single(window.GetVisualDescendants().OfType<Border>(), b => b.Name?.StartsWith("FeedEntry_", StringComparison.Ordinal) == true); // Only the unread one.
            Click(window, "FeedMarkAllRead"); Assert.Equal(0, model.FeedUnread);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Nothing to show");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WithNoFolderThePageOffersToChooseOneAndSettingsHoldsTheFeedControls()
    {
        var model = Model(withFolder: false); var (window, _) = Open(model);
        try
        {
            Assert.True(Find<Border>(window, "FeedEmpty").IsVisible); Find<Button>(window, "FeedChoose");
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var box = Find<TextBox>(window, "FeedFolder"); box.Text = folder; box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter }); Dispatcher.UIThread.RunJobs();
            Assert.Equal(folder, model.FeedFolder); Assert.Equal(2, model.FeedShown.Count);
            Assert.StartsWith("1 KB in 1 file. 2 entries showing, 1 hidden as older than 30 days.", Find<TextBlock>(window, "FeedFolderNote").Text);
            var longer = Find<CheckBox>(window, "FeedLonger"); longer.IsChecked = true; longer.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Assert.True(model.FeedLonger); // A raised click does not flip the box itself.
            Find<Button>(window, "FeedBrowse"); Find<Button>(window, "FeedCopyPrompt");
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ThePromptNamesTheFolderTheTagsTheFormatAndAskForANewFileEachRun()
    {
        var prompt = FeedPrompt.Build(new[] { ("Account one", "Account one"), ("Internal", "Internal") }, @"C:\Users\sam\OneDrive\Feed");
        Assert.Contains(@"in this folder: C:\Users\sam\OneDrive\Feed", prompt);
        Assert.Contains("#Accountone  (Account one)", prompt); Assert.Contains("#Internal  (Internal)", prompt);
        Assert.Contains("WHAT TO LOOK FOR", prompt); Assert.Contains("Replace this line", prompt); // The one part the person fills in.
        Assert.Contains("feed-<short task name>-YYYY-MM-DD-HHMM.md", prompt); Assert.Contains("never change or delete earlier files", prompt);
        Assert.Contains("Date: YYYY-MM-DD HH:MM", prompt); Assert.Contains("ID: ", prompt); Assert.Contains("start it with ! if", prompt); Assert.Contains("put the whole file in your reply", prompt);
        // The prompt it asks for is something the feed reads: its own example entry parses with a date, an ID and an urgent mark.
        var entry = Feed.Parse("## ! Short headline\nDate: 2026-10-16 08:30\nID: a-stable-name\n#Accountone\nOne sentence.", "wire", new DateTime(2026, 10, 16))[0];
        Assert.True(entry.Urgent); Assert.True(entry.HasDate); Assert.Equal("id:a-stable-name", entry.Id);
        Assert.Contains(FeedPrompt.NoFolder, FeedPrompt.Build(Array.Empty<(string, string)>(), null)); // Until a folder is chosen it says where the folder comes from.
        Assert.Contains("#AccountName", FeedPrompt.Build(Array.Empty<(string, string)>(), null));
    }

    [AvaloniaFact]
    public void SettingsShowThePromptInFullAndItFollowsTheFolderAndTheAccounts()
    {
        var model = Model(withFolder: false); var (window, _) = Open(model);
        try
        {
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var box = Find<TextBox>(window, "FeedPromptText"); Assert.True(box.IsReadOnly); Assert.True(box.AcceptsReturn);
            Assert.Contains(FeedPrompt.NoFolder, box.Text); Assert.Contains("#Accountone  (Account one)", box.Text);
            model.SetFeedFolder(folder); Dispatcher.UIThread.RunJobs();
            Assert.Contains("in this folder: " + folder, box.Text); Assert.DoesNotContain(FeedPrompt.NoFolder, box.Text);
            Assert.Equal(model.FeedPromptText, box.Text);
            Assert.Contains("cannot write to this computer", Find<TextBlock>(window, "FeedPromptNote").Text); // Says why a synced folder is needed for a cloud assistant.
            Find<Button>(window, "FeedCopyPrompt");
            // Renaming an account is reflected the next time the page is looked at.
            model.SetAccounts(model.AccountSettings.Select(a => a.Name == "Account one" ? a with { Name = "Client Alpha" } : a), new Dictionary<string, string> { ["Account one"] = "Client Alpha" }); Dispatcher.UIThread.RunJobs();
            Assert.Contains("#ClientAlpha  (Client Alpha)", box.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FeedTextShowsBoldItalicAndBulletsAndNothingElse()
    {
        Assert.Equal("a bold and italic word", FeedText.Plain("a **bold** and *italic* word"));
        Assert.Equal("• one\n• two", FeedText.Plain("- one\n* two"));
        Assert.Equal("<b>not html</b> [not a link](http://x)", FeedText.Plain("<b>not html</b> [not a link](http://x)"));
        var bold = FeedText.Inlines("**strong**").OfType<Run>().Single(); Assert.Equal(Avalonia.Media.FontWeight.Bold, bold.FontWeight);
    }

    [Fact]
    public void TheFeedSettingsKeepOtherSettingsAndSurviveARestart()
    {
        var data = Path.Combine(folder, "data2"); using (var store = new TimerStore(data))
        {
            var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store);
            model.SetCaptureShortcut(HotkeySpec.Parse("Ctrl+Alt+N")); model.SetFeedFolder(folder); model.SetFeedLonger(true); model.SetFeedAutoRead(false); model.SetFeedRead("id:x", true); model.Save();
        }
        using (var store = new TimerStore(data))
        {
            var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store);
            Assert.Equal(folder, model.FeedFolder); Assert.True(model.FeedLonger); Assert.False(model.FeedAutoRead); Assert.True(model.FeedIsRead("id:x"));
            Assert.Equal("Ctrl+Alt+N", model.CaptureShortcut!.ToString()); // The quick capture shortcut is untouched.
        }
    }
}
