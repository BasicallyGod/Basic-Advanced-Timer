using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>Writes pictures of the Notes, Feed and Settings pages and the calendar into TIMER_PREVIEW_DIR when it is set, so layout can be looked at. Does nothing otherwise.</summary>
public sealed class PreviewCaptureTests
{
    private static readonly string[] Entries =
    {
        "## Regulator publishes surprise consultation on stablecoin reserves\nDate: 2026-10-16 09:00\nID: a1\n#Accountone\nA financial regulator has opened a short consultation on how stablecoin reserves should be held and reported.\n",
        "## ! Fintech funding round draws wide trade press coverage\nDate: 2026-10-16 08:45\nID: a2\n#Accounttwo\nA mid-sized payments firm closed a large funding round and the story has been picked up by several outlets.\n",
        "## Asset manager reports growing demand for digital asset products\nDate: 2026-10-16 08:30\nID: a3\n#Accountthree\nA leading asset manager noted rising client interest in digital asset exposure in its latest results.\n",
        "## Exchange outage resolved\nDate: 2026-10-15 17:10\nID: a4\n#Accountfour\nService was restored after about forty minutes.\n",
    };

    [AvaloniaFact]
    public void WritePreviews()
    {
        var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR"); if (folder is null) return;
        Directory.CreateDirectory(folder);
        var feed = Path.Combine(Path.GetTempPath(), "preview-feed-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(feed);
        File.WriteAllText(Path.Combine(feed, "feed-test-news-updates-2026-10-16-0900.md"), string.Join("\n", Entries));
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var model = TimerModel.CreateDemo(); model.SetTheme(theme); model.SetFeedFolder(feed); model.RefreshFeed();
            var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                void Shot(string name) { Dispatcher.UIThread.RunJobs(); using var f = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var g = window.CaptureRenderedFrame(); g?.Save(Path.Combine(folder, theme + "-" + name + ".png")); }
                window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NotesTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Shot("notes");
                window.ShowFeed(); Shot("feed");
                window.GetVisualDescendants().OfType<FeedPage>().Single().Toggle("id:a1"); Shot("feed-selected");
                window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FeedTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var open = window.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(p => p.Name?.StartsWith("FeedOpen_") == true); if (open is not null) { Dispatcher.UIThread.RunJobs(); }
                window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
                window.GetVisualDescendants().OfType<Control>().First(c => c.Name == "FeedFolder").BringIntoView(); Shot("settings-feed");
                window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FeedAddSource").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                window.GetVisualDescendants().OfType<Control>().First(c => c.Name == "FeedSourceSave").BringIntoView(); Shot("settings-feed-source");
                var cal = new Window { Width = 360, Height = 380, Content = new Border { Padding = new Avalonia.Thickness(16), Child = new Calendar { SelectedDate = new DateTime(2026, 10, 16), DisplayDate = new DateTime(2026, 10, 16) } } };
                cal.RequestedThemeVariant = window.RequestedThemeVariant; cal.Show(); Dispatcher.UIThread.RunJobs(); using var c1 = cal.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); using var c2 = cal.CaptureRenderedFrame(); c2?.Save(Path.Combine(folder, theme + "-calendar.png")); cal.Close();
            }
            finally { window.Close(); }
        }
    }
}
