using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The length box in the entry window, and what happens when a longer entry runs into the blocks after it.
public sealed class EntryLengthTests
{
    private static DateTime Day => new(2026, 10, 9);
    private static SessionRow Row(string start, string minutes, string account = "Account one") => new(Day.ToString("yyyy-MM-dd"), account, start, "added", minutes);
    private static TimerModel Model(out TrackingEngine engine)
    {
        engine = new TrackingEngine(clock: () => Day.AddHours(16));
        engine.Sessions.AddRange(new[] { Row("09:00:00", "30"), Row("09:30:00", "30", "Account two"), Row("10:00:00", "30") });
        return new TimerModel(engine, new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday));
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Type(Control root, string name, string text) { Find<TextBox>(root, name).Text = text; Dispatcher.UIThread.RunJobs(); }
    private static void Click(Control root, string name) { Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void TheLengthAndTheEndFollowEachOther()
    {
        var model = Model(out var engine); var row = engine.Sessions[0];
        var window = new EntryDialog(model, row, row.From, row.To).Open(model);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("0:30", Find<TextBox>(window, "EntryLength").Text);
            Type(window, "EntryLength", "45"); Assert.Equal("09:45:00", Find<TextBox>(window, "EntryEnd").Text); // Plain numbers are minutes.
            Type(window, "EntryLength", "1:15"); Assert.Equal("10:15:00", Find<TextBox>(window, "EntryEnd").Text);
            Type(window, "EntryLength", "0:20:30"); Assert.Equal("09:20:30", Find<TextBox>(window, "EntryEnd").Text);
            Type(window, "EntryEnd", "09:50:00"); Assert.Equal("0:50", Find<TextBox>(window, "EntryLength").Text);
            Type(window, "EntryStart", "09:10:00"); Assert.Equal("0:40", Find<TextBox>(window, "EntryLength").Text); // Moving the start changes the length, the end stays.
            Type(window, "EntryLength", "twenty"); Assert.Equal("09:50:00", Find<TextBox>(window, "EntryEnd").Text); // Nonsense changes nothing.
            Type(window, "EntryLength", "23:59"); Assert.Contains("midnight", Find<TextBlock>(window, "EntryError").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ALongerLengthOffersToMoveLaterBlocksOrAllowTheOverlap_AndTheOverlapIsKept()
    {
        var model = Model(out var engine); var row = engine.Sessions[0];
        var window = new EntryDialog(model, row, row.From, row.To).Open(model);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Type(window, "EntryLength", "0:45"); Click(window, "SaveEntry");
            Assert.True(Find<Border>(window, "ShovePrompt").IsVisible); Assert.Equal("30", engine.Sessions[0].Minutes); // Asked first.
            Click(window, "ShoveOverlap");
            Assert.False(window.IsVisible);
            Assert.Equal("09:00:00", engine.Sessions[0].Start); Assert.Equal(45.0, engine.Sessions[0].Duration.TotalMinutes, 0.05);
            Assert.Equal("09:30:00", engine.Sessions[1].Start); // The next block did not move, so they overlap for fifteen minutes.
            Assert.True(engine.Sessions[0].To > engine.Sessions[1].From);
            // The hours in the overlap count twice, as chosen: forty-five, thirty and thirty minutes make an hour and three quarters.
            Assert.Equal(TimeSpan.FromMinutes(45 + 30 + 30), engine.Credited("Account one", Day, Day.AddDays(1)) + engine.Credited("Account two", Day, Day.AddDays(1)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ALongerLengthAsksEveryTimeAndMovingTheBlocksChainsThemLater()
    {
        var model = Model(out var engine);
        var window = new EntryDialog(model, engine.Sessions[0], engine.Sessions[0].From, engine.Sessions[0].To).Open(model);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Type(window, "EntryLength", "0:45"); Click(window, "SaveEntry");
            Assert.True(Find<Border>(window, "ShovePrompt").IsVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<CheckBox>(), c => c.Name == "AlwaysShove"); // There is no way to stop being asked.
            Click(window, "ShoveConfirm");
            Assert.Equal("09:45:00", engine.Sessions[1].Start); Assert.Equal("10:15:00", engine.Sessions[2].Start);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SettingsHasNoSwitchForMovingLaterBlocks()
    {
        var model = Model(out _);
        var dashboard = new DashboardWindow(model); dashboard.Show(); Dispatcher.UIThread.RunJobs();
        Click(dashboard, "Settings"); Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(dashboard.GetVisualDescendants().OfType<CheckBox>(), c => c.Name == "MoveLaterAlways");
        dashboard.Close();
    }

    [Fact]
    public void AnOverlapIsAllowedOnlyWhenAskedForAndNeverOntoTheBlockBeingRecorded()
    {
        var model = Model(out var engine); var first = engine.Sessions[0];
        Assert.False(model.EditEntry(first, first.Account, first.From, first.From.AddMinutes(45), "")); // Refused without the permission.
        Assert.Contains("overlaps", model.EditError);
        Assert.True(model.EditEntry(first, first.Account, first.From, first.From.AddMinutes(45), "", allowOverlap: true));
        var now = Day.AddHours(11); var running = new TrackingEngine(clock: () => now); running.ToggleAccount("Account one"); now = now.AddMinutes(10);
        running.Sessions.Add(Row("09:00:00", "60"));
        Assert.Throws<ArgumentException>(() => new LedgerEditor(running).Edit(running.Sessions[0], "Account one", running.Sessions[0].From, Day.AddHours(11).AddMinutes(5), "", allowOverlap: true));
    }
}
