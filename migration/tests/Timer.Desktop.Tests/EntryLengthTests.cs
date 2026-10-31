using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// The length box in Add time and in the Selected block card, and what happens when a longer block runs into the blocks after it.
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
    public void TheLengthAndTheEndFollowEachOtherInAddTime()
    {
        var model = Model(out _);
        var window = new EntryDialog(model, Day.AddHours(11), Day.AddHours(11.5)).Open(model);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("0:30", Find<TextBox>(window, "EntryLength").Text);
            Type(window, "EntryLength", "45"); Assert.Equal("11:45:00", Find<TextBox>(window, "EntryEnd").Text); // Plain numbers are minutes.
            Type(window, "EntryLength", "1:15"); Assert.Equal("12:15:00", Find<TextBox>(window, "EntryEnd").Text);
            Type(window, "EntryLength", "0:20:30"); Assert.Equal("11:20:30", Find<TextBox>(window, "EntryEnd").Text);
            Type(window, "EntryEnd", "11:50:00"); Assert.Equal("0:50", Find<TextBox>(window, "EntryLength").Text);
            Type(window, "EntryStart", "11:10:00"); Assert.Equal("0:40", Find<TextBox>(window, "EntryLength").Text); // Moving the start changes the length, the end stays.
            Type(window, "EntryLength", "twenty"); Assert.Equal("11:50:00", Find<TextBox>(window, "EntryEnd").Text); // Nonsense changes nothing.
            Type(window, "EntryLength", "23:59"); Assert.Contains("midnight", Find<TextBlock>(window, "EntryError").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheSelectedBlockCardKeepsTheLengthAndTheEndInStepAndNeverAsksAboutLaterBlocks()
    {
        var model = Model(out var engine); var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            model.ToggleSelection(new[] { engine.Sessions[0] }); Dispatcher.UIThread.RunJobs();
            Assert.Equal("0:30", Find<TextBox>(window, "SelectedLength").Text);
            // A longer length runs into the next block. There is no question: the blocks overlap and stack on the timeline, and the time counts twice.
            Type(window, "SelectedLength", "0:45"); Find<TextBox>(window, "SelectedLength").RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("09:00:00", engine.Sessions[0].Start); Assert.Equal(45.0, engine.Sessions[0].Duration.TotalMinutes, 0.05);
            Assert.Equal("09:30:00", engine.Sessions[1].Start); // The next block did not move.
            Assert.True(engine.Sessions[0].To > engine.Sessions[1].From);
            Assert.Equal("0:45", Find<TextBox>(window, "SelectedLength").Text); Assert.Equal("09:45:00", Find<TextBox>(window, "SelectedEnd").Text);
            Assert.Equal(TimeSpan.FromMinutes(45 + 30 + 30), engine.Credited("Account one", Day, Day.AddDays(1)) + engine.Credited("Account two", Day, Day.AddDays(1)));
            Assert.True(model.UndoEdit()); Assert.Equal(30.0, engine.Sessions[0].Duration.TotalMinutes, 0.05);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ATimeThatMakesNoSenseIsRefusedWithAShortLineAndTheBoxGoesBack()
    {
        var model = Model(out var engine); var window = new DashboardWindow(model); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            model.ToggleSelection(new[] { engine.Sessions[0] }); Dispatcher.UIThread.RunJobs();
            var end = Find<TextBox>(window, "SelectedEnd");
            Type(window, "SelectedEnd", "08:00:00"); end.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter }); Dispatcher.UIThread.RunJobs();
            Assert.Equal("End must be after start", model.EditError); Assert.Equal("09:30:00", Find<TextBox>(window, "SelectedEnd").Text); Assert.Equal("09:30:00", engine.Sessions[0].To.ToString("HH:mm:ss"));
            Type(window, "SelectedStart", "nine"); Find<TextBox>(window, "SelectedStart").RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter }); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Start: try 09:30", model.EditError);
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
    public void AnOverlapIsAllowedWhenEditingButNeverOntoTheBlockBeingRecorded()
    {
        var model = Model(out var engine); var first = engine.Sessions[0];
        Assert.True(model.EditEntry(first, first.Account, first.From, first.From.AddMinutes(45), ""));
        var now = Day.AddHours(11); var running = new TrackingEngine(clock: () => now); running.ToggleAccount("Account one"); now = now.AddMinutes(10);
        running.Sessions.Add(Row("09:00:00", "60"));
        Assert.Throws<ArgumentException>(() => new LedgerEditor(running).Edit(running.Sessions[0], "Account one", running.Sessions[0].From, Day.AddHours(11).AddMinutes(5), "", allowOverlap: true));
    }
}
