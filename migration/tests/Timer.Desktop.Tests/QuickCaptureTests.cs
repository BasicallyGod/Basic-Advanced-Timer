using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Desk;
using Timer.Desktop;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class QuickCaptureTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 10, 0, 0);
    private static TimerModel Model() { var m = new TimerModel(new TrackingEngine(clock: () => Now)); m.CompleteSetup(); return m; }

    [Fact]
    public void TheBoxSaysHowTheWordsWereRead()
    {
        var accounts = new[] { "Account one", "Account two" };
        Assert.Contains("#Account", QuickCaptureWindow.Describe("", Now, accounts));
        Assert.Equal("Today", QuickCaptureWindow.Describe("ring back", Now, accounts));
        Assert.Equal("Today · 14:30", QuickCaptureWindow.Describe("14:30 call", Now, accounts));
        Assert.Equal("Friday 16 October · 09:30 · deadline · Account one", QuickCaptureWindow.Describe("! invoice @fri 09:30 #AccountOne", Now, accounts));
    }

    [AvaloniaFact]
    public void EnterSavesTheNoteThroughTheNotesAndClosesTheBox()
    {
        var model = Model(); var window = new QuickCaptureWindow(model); window.Show();
        try
        {
            var text = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "CaptureText"); text.Text = "14:30 Call back @tomorrow";
            Assert.Equal("Thursday 15 October · 14:30", window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CaptureReading").Text);
            text.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Dispatcher.UIThread.RunJobs();
            var note = Assert.Single(model.NotesOn(new DateTime(2026, 10, 15))); Assert.Equal("Call back", note.Text); Assert.Equal(new TimeSpan(14, 30, 0), note.At);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EscapeOrAnEmptyBoxSavesNothing()
    {
        var model = Model(); var window = new QuickCaptureWindow(model); window.Show();
        try
        {
            var text = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "CaptureText"); text.Text = "never saved";
            text.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible); Assert.Empty(model.NotesOn(Now.Date));
        }
        finally { window.Close(); }
        var again = new QuickCaptureWindow(model); again.Show(); again.Save(); Assert.Empty(model.NotesOn(Now.Date)); Assert.False(again.IsVisible);
    }

    [Fact]
    public void KeyPressesMakeShortcutsOnlyWithAModifierAndAnAllowedKey()
    {
        Assert.Equal("Ctrl+Alt+N", GlobalHotkey.FromKey(Key.N, KeyModifiers.Control | KeyModifiers.Alt)!.ToString());
        Assert.Equal("Ctrl+5", GlobalHotkey.FromKey(Key.D5, KeyModifiers.Control)!.ToString());
        Assert.Equal("Alt+F2", GlobalHotkey.FromKey(Key.F2, KeyModifiers.Alt)!.ToString());
        Assert.Null(GlobalHotkey.FromKey(Key.N, KeyModifiers.None)); Assert.Null(GlobalHotkey.FromKey(Key.N, KeyModifiers.Shift));
        Assert.Null(GlobalHotkey.FromKey(Key.Enter, KeyModifiers.Control)); Assert.Null(GlobalHotkey.FromKey(Key.LeftCtrl, KeyModifiers.Control));
    }

    [AvaloniaFact]
    public void SettingsSetsAndClearsTheShortcutAndTheOpenButtonAsksForTheBox()
    {
        var model = Model(); var dashboard = new DashboardWindow(model); dashboard.Show();
        try
        {
            dashboard.OpenCategory("general");
            var box = dashboard.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "CaptureShortcut");
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.N, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt });
            Assert.Equal("Ctrl+Alt+N", model.CaptureShortcut!.ToString()); Assert.Equal("Ctrl+Alt+N", box.Text);
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.N }); Assert.Equal("Ctrl+Alt+N", model.CaptureShortcut!.ToString()); // No modifier: ignored.
            var asked = 0; model.QuickCaptureRequested += () => asked++;
            dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CaptureOpen").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, asked);
            dashboard.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CaptureShortcutClear").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(model.CaptureShortcut); Assert.Equal("", box.Text);
        }
        finally { dashboard.Close(); }
    }

    [Fact]
    public void TheShortcutIsKeptInTheStoreAndABadOneReadsAsNone()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-capture-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store); model.SetCaptureShortcut(HotkeySpec.Parse("Ctrl+Alt+N")); }
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); var model = new TimerModel(engine, store.Load(engine), store); Assert.Equal("Ctrl+Alt+N", model.CaptureShortcut!.ToString()); model.SetCaptureShortcut(null); }
            using (var store = new TimerStore(folder)) { var engine = new TrackingEngine(); Assert.Null(new TimerModel(engine, store.Load(engine), store).CaptureShortcut); }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}
