using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class SlimColumnTests
{
    private static void Click(Visual root, string name) { root.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static DashboardWindow Open(TimerModel model) { var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs(); return window; }
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    [AvaloniaFact]
    public void NotesUsesASeparateCompactColumnBesideTheUnchangedNavigationRail()
    {
        var model = TimerModel.CreateDemo(); var window = Open(model);
        try
        {
            var body = window.GetVisualDescendants().OfType<Grid>().First(g => g.ColumnDefinitions.Count == 3 && g.ColumnDefinitions[1].Width.Value == 1);
            var rail = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("panel") && b.Width == 60);
            var slim = window.GetVisualDescendants().OfType<CompactColumn>().Single();
            Assert.False(slim.IsVisible); Assert.Equal(340, body.ColumnDefinitions[0].Width.Value);
            Click(window, "NotesTab");
            Assert.True(slim.IsVisible); Assert.Equal(CompactColumn.ColumnWidth, body.ColumnDefinitions[0].Width.Value);
            Assert.Equal(60, rail.Width); Assert.False(rail.IsVisualAncestorOf(slim));
            Assert.False(window.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "AccountScroll").IsEffectivelyVisible);
            Assert.True(Find<ScrollViewer>(slim, "CompactAccountScroll").IsEffectivelyVisible);
            Click(window, "TimelineTab"); Assert.False(slim.IsVisible); Assert.Equal(340, body.ColumnDefinitions[0].Width.Value);
            Click(window, "NotesTab"); Click(window, "Settings"); Assert.False(slim.IsVisible); Assert.Equal(60, rail.Width);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SlimControlsKeepTheClockAndFinishDayVisibleWithScrollableAccounts()
    {
        var model = TimerModel.CreateDemo(); var window = Open(model);
        try
        {
            Click(window, "NotesTab"); var slim = window.GetVisualDescendants().OfType<CompactColumn>().Single();
            var list = Find<ScrollViewer>(slim, "CompactAccountScroll");
            Assert.Equal(model.AccountNames.Count, slim.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("CompactDot_") == true));
            Assert.Contains(slim.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "A1");
            Assert.Contains(slim.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == model.FormatCompact(model.Today(model.Accounts.First(a => a.Name == "Account one"))));
            Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, list.VerticalScrollBarVisibility);
            Click(slim, "CompactDot_" + Ui.NameKey("Account one")); Assert.Equal("Account one", model.ActiveAccount);
            Assert.Contains(slim.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Tracking");
            Click(slim, "CompactPause"); Assert.False(model.IsTracking);
            Click(slim, "CompactBreak"); Assert.True(model.IsOnBreak);
            Click(slim, "CompactBreak"); Assert.False(model.IsOnBreak);
            var started = model.DayStarted; Click(slim, "CompactDay"); Assert.NotEqual(started, model.DayStarted);
            // The utility strip and the navigation rail are distinct, so even long account lists cannot push out navigation.
            var rail = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("panel") && b.Width == 60);
            foreach (var name in new[] { "Settings", "QuitTimer", "NotesTab" })
            {
                var button = Find<Button>(window, name);
                Assert.True(button.TranslatePoint(new Point(0, button.Bounds.Height), rail)!.Value.Y <= rail.Bounds.Height + 1, name + " falls below the rail");
            }
            var day = Find<Button>(slim, "CompactDay");
            Assert.True(day.TranslatePoint(new Point(0, day.Bounds.Height), slim)!.Value.Y <= slim.Bounds.Height + 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ManyAccountsScrollWithoutHidingTheTimerOrFinishDay()
    {
        var accounts = Enumerable.Range(1, 22).Select(n => new Timer.Storage.AccountSettings("Client " + n, null, null)).ToList();
        var model = new TimerModel(new TrackingEngine(), new Timer.Storage.StoreSettings(accounts, false, "Light", DayOfWeek.Monday));
        model.CompleteSetup(); var window = Open(model);
        try
        {
            Click(window, "NotesTab"); var slim = window.GetVisualDescendants().OfType<CompactColumn>().Single();
            var scroll = Find<ScrollViewer>(slim, "CompactAccountScroll");
            window.UpdateLayout();
            Assert.Equal(22, slim.GetVisualDescendants().OfType<Button>().Count(b => b.Name?.StartsWith("CompactDot_") == true));
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            var day = Find<Button>(slim, "CompactDay");
            Assert.True(day.TranslatePoint(new Point(0, day.Bounds.Height), slim)!.Value.Y <= slim.Bounds.Height + 1);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void AccountLabelsAreShortAndNeverShared()
    {
        var labels = CompactColumn.Short(new[] { ("Account one", "Account one"), ("Account two", "Account two"), ("Account three", "Account three"), ("Account four", "Account four"), ("Account five", "Account five"), ("Internal", "Internal") });
        Assert.Equal(6, labels.Values.Distinct().Count()); Assert.Equal("A1", labels["Account one"]); Assert.Equal("A2", labels["Account two"]); Assert.Equal("A3", labels["Account three"]); Assert.All(labels.Values, l => Assert.InRange(l.Length, 1, 2));
        var numbered = CompactColumn.Short(new[] { ("Client 1", "Client 1"), ("Client 2", "Client 2"), ("Client 3", "Client 3") });
        Assert.Equal(new[] { "C1", "C2", "C3" }, numbered.Values);
        var repeated = CompactColumn.Short(new[] { ("X one", "X first"), ("X two", "X second"), ("X three", "X third"), ("Internal", "Internal") });
        Assert.Equal(4, repeated.Values.Distinct().Count()); Assert.All(repeated.Values, label => Assert.InRange(label.Length, 1, 2));
    }

    [AvaloniaFact]
    public void CompactAccountTooltipsIncludeBudgetInformation()
    {
        var model = TimerModel.CreateDemo(); var window = Open(model);
        try
        {
            Click(window, "NotesTab"); var slim = window.GetVisualDescendants().OfType<CompactColumn>().Single();
            var row = Find<Button>(slim, "CompactDot_" + Ui.NameKey("Account one"));
            Assert.Contains("Account one", ToolTip.GetTip(row)?.ToString());
            Assert.Contains("today", ToolTip.GetTip(row)?.ToString());
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
            int Rows() => window.GetVisualDescendants().OfType<Button>().Count(t => t.Name?.StartsWith("NoteText_") == true);
            var all = Rows(); Assert.True(all >= 5);
            Click(window, "NotesFilter_tasks"); var tasks = Rows(); Assert.True(tasks > 0 && tasks < all);
            Assert.All(window.GetVisualDescendants().OfType<Button>().Where(t => t.Name?.StartsWith("NoteText_") == true), t => Assert.True(DayNotes.IsTask(model.AllDayNotes.Single(n => "NoteText_" + n.Id == t.Name))));
            Click(window, "NotesFilter_deadlines"); Assert.All(window.GetVisualDescendants().OfType<Button>().Where(t => t.Name?.StartsWith("NoteText_") == true), t => Assert.True(DayNotes.IsDeadline(model.AllDayNotes.Single(n => "NoteText_" + n.Id == t.Name))));
            Click(window, "NotesFilter_events"); Assert.All(window.GetVisualDescendants().OfType<Button>().Where(t => t.Name?.StartsWith("NoteText_") == true), t => Assert.NotNull(model.AllDayNotes.Single(n => "NoteText_" + n.Id == t.Name).At));
            Click(window, "NotesFilter_all"); Assert.Equal(all, Rows());
            Assert.Equal("Add item", Find<Button>(window, "NoteAddButton").Content);
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
}
