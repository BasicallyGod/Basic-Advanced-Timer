using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class SharedControlContractTests
{
    [Theory]
    [InlineData("45", DurationUnit.Minutes, "en-GB", .75)]
    [InlineData("45", DurationUnit.Hours, "en-GB", 45)]
    [InlineData("1.30", DurationUnit.Hours, "en-GB", 1.3)]
    [InlineData("1,5", DurationUnit.Hours, "de-DE", 1.5)]
    [InlineData("1.5", DurationUnit.Hours, "de-DE", 1.5)]
    [InlineData("1,5h", DurationUnit.Minutes, "de-DE", 1.5)]
    [InlineData("01:30", DurationUnit.Minutes, "en-GB", 1.5)]
    [InlineData("01:30", DurationUnit.Hours, "de-DE", 1.5)]
    [InlineData("1h 30m", DurationUnit.Hours, "en-GB", 1.5)]
    [InlineData("90 minutes", DurationUnit.Hours, "en-GB", 1.5)]
    [InlineData("120:30", DurationUnit.Hours, "en-GB", 120.5)]
    public void DurationSyntaxKeepsTheExplicitBareUnit(string text, DurationUnit unit, string locale, double expected)
        => Assert.Equal(expected, DurationText.Hours(text, unit, CultureInfo.GetCultureInfo(locale))!.Value, 10);

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("1:60")]
    [InlineData("1:30:60")]
    [InlineData("1:-30")]
    [InlineData("1h -30m")]
    [InlineData("1,234")]
    public void InvalidDurationsAreRefusedWithoutThousandsGuessing(string text)
        => Assert.Null(DurationText.Hours(text, DurationUnit.Hours, CultureInfo.GetCultureInfo("en-GB")));

    [Fact]
    public void BlockLengthsKeepTheDayLimitButBudgetHoursDoNotAcquireIt()
    {
        Assert.Equal(TimeSpan.FromDays(1), DurationText.Span("24:00", DurationUnit.Minutes));
        Assert.Null(DurationText.Span("24:00:01", DurationUnit.Minutes));
        Assert.Equal(80, DurationText.Hours("80", DurationUnit.Hours));
    }

    [AvaloniaFact]
    public void FormatChangesDoNotRoundAnUntouchedValueOrChangeBareNumberUnits()
    {
        var precise = TimeSpan.FromTicks(123456789);
        foreach (var dec in new[] { false, true })
        {
            var length = new DurationBox(DurationUnit.Minutes, dec, "Length"); length.SetDuration(precise);
            Assert.Equal(precise, length.ReadDuration());
            length.Text = "1.5"; Assert.Equal(TimeSpan.FromSeconds(90), length.ReadDuration());
            length.Text = "1.5h"; Assert.Equal(TimeSpan.FromMinutes(90), length.ReadDuration());
            var budget = new DurationBox(DurationUnit.Hours, dec, "Budget"); budget.SetHours(1.23456789);
            Assert.Equal(1.23456789, budget.ReadHours());
            budget.Text = "1.5"; Assert.Equal(1.5, budget.ReadHours());
            budget.Text = ""; Assert.Null(budget.ReadHours());
        }
    }

    private static TimerModel Model()
    {
        var day = new DateTime(2026, 11, 15);
        var model = new TimerModel(new TrackingEngine(clock: () => day.AddHours(14)),
            new StoreSettings(new() { new("Account one", 1.23456789), new("Account two") }, false, "Light", DayOfWeek.Monday));
        model.CompleteSetup(); return model;
    }
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window window) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Dispatcher.UIThread.RunJobs(); }
    private static void Key(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    [AvaloniaFact]
    public void SavingAccountsUnchangedPreservesFractionalHoursInBothFormats()
    {
        foreach (var dec in new[] { false, true })
        {
            var model = Model(); model.SetDecimalHours(dec);
            var window = new DashboardWindow(model); window.Show();
            var dialog = new AccountsDialog(model).Open(model);
            try
            {
                Settle(window);
                var budget = dialog.GetVisualDescendants().OfType<DurationBox>().First();
                Assert.Equal(dec ? "1.23h" : "01:14", budget.Text);
                Find<Button>(dialog, "SaveAccounts").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1.23456789, model.AccountSettings[0].BudgetHours);
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void NativeDropdownKeepsArrowSelectionAndEscapeInsideTheChoiceWrapper()
    {
        var choice = new ChoiceStrip { Name = "EntryAccount", Look = ChoiceLook.Dropdown, ItemsSource = new[] { "Alpha", "Beta", "Gamma" }, SelectedItem = "Alpha" };
        var window = new Window { Content = choice, Width = 300, Height = 180 }; window.Show();
        try
        {
            Settle(window); var box = choice.GetVisualDescendants().OfType<ComboBox>().Single();
            Assert.Equal("Account", AutomationProperties.GetName(box)); box.Focus();
            Key(window, Avalonia.Input.Key.F4); Settle(window); Assert.True(box.IsDropDownOpen);
            Key(window, Avalonia.Input.Key.Down); Key(window, Avalonia.Input.Key.Enter); Settle(window);
            Assert.False(box.IsDropDownOpen); Assert.Equal("Beta", choice.SelectedItem);
            Key(window, Avalonia.Input.Key.F4); Key(window, Avalonia.Input.Key.Down); Key(window, Avalonia.Input.Key.Escape); Settle(window);
            Assert.False(box.IsDropDownOpen); Assert.Equal("Beta", choice.SelectedItem);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SegmentsExposeRadioStateAndArrowsChooseOnceWithOneTabStop()
    {
        var choice = new ChoiceStrip { Name = "Kind", Look = ChoiceLook.Segments, ItemsSource = new[] { "Note", "Meeting" }, SelectedItem = "Note" };
        var window = new Window { Content = choice, Width = 300, Height = 180 }; window.Show();
        try
        {
            Settle(window); var options = choice.Options.Cast<RadioButton>().ToArray();
            Assert.True(options[0].IsChecked); Assert.False(options[1].IsChecked);
            var provider = Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(options[0]));
            Assert.True(provider.IsSelected);
            var changes = 0; choice.SelectionChanged += (_, _) => changes++;
            options[0].Focus(); Key(window, Avalonia.Input.Key.Right); Settle(window);
            Assert.Equal("Meeting", choice.SelectedItem); Assert.Equal(1, changes);
            Assert.False(provider.IsSelected); Assert.True(options[1].IsChecked);
            Assert.False(options[0].IsTabStop); Assert.True(options[1].IsTabStop);
            provider.Select(); Settle(window);
            Assert.Equal("Note", choice.SelectedItem); Assert.Equal(2, changes); Assert.True(provider.IsSelected);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CompletionExposesItsCheckedStateAndSpaceTogglesTheNoteOnce()
    {
        var model = Model(); model.Notes.AddNote("A sample note", null, null); var note = model.Notes.All.Single();
        var tick = NoteParts.Tick(model.Notes, note); var window = new Window { Content = tick, Width = 100, Height = 100 }; window.Show();
        try
        {
            Settle(window);
            var provider = Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(tick));
            Assert.Equal(ToggleState.Off, provider.ToggleState);
            tick.Focus(); Key(window, Avalonia.Input.Key.Space); Settle(window);
            Assert.True(model.Notes.All.Single().Done); Assert.Equal(ToggleState.On, provider.ToggleState);
            provider.Toggle(); Settle(window);
            Assert.False(model.Notes.All.Single().Done); Assert.Equal(ToggleState.Off, provider.ToggleState);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LiveFormatChangesPreservePrecisionAndLeaveAnUnfinishedEditAlone()
    {
        var model = Model(); var value = TimeSpan.FromTicks(123456789);
        var box = new DurationBox(DurationUnit.Minutes, model, "Length"); box.SetDuration(value);
        var window = new Window { Content = box, Width = 200, Height = 100 }; window.Show();
        try
        {
            Settle(window); model.SetDecimalHours(true); Settle(window);
            Assert.EndsWith("h", box.Text); Assert.Equal(value, box.ReadDuration());
            box.Text = "unfinished"; model.SetDecimalHours(false); Settle(window);
            Assert.Equal("unfinished", box.Text); Assert.Null(box.ReadDuration());
        }
        finally { window.Close(); }
        Assert.Null(DurationText.Span("0h", DurationUnit.Minutes));
        Assert.Equal(TimeSpan.Zero, DurationText.Span("0h", DurationUnit.Minutes, allowZero: true));
    }

    [AvaloniaFact]
    public void InvalidAddTimeLengthCannotSaveAStaleEndAndFractionalMinutesSurvive()
    {
        var model = Model(); model.SetDecimalHours(true);
        var window = new DashboardWindow(model); window.Show();
        var day = model.Engine.Now.Date;
        var dialog = new EntryDialog(model, day.AddHours(9), day.AddHours(9.5)).Open(model);
        try
        {
            Settle(window); var length = Find<DurationBox>(dialog, "EntryLength");
            Assert.Equal("0.50h", length.Text);
            length.Text = "invalid"; Settle(window);
            Find<Button>(dialog, "SaveEntry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(dialog.IsVisible); Assert.Empty(model.Engine.Sessions);
            Assert.Contains("Length:", Find<TextBlock>(dialog, "EntryError").Text);
            length.Text = "7.51"; Settle(window);
            Assert.Equal("09:07:30.6", Find<TextBox>(dialog, "EntryEnd").Text);
            Find<Button>(dialog, "SaveEntry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(7.51, Assert.Single(model.Engine.Sessions).Duration.TotalMinutes, 6);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ClockRoundTripsRetainFractionalSecondsAndTheMidnightBoundary()
    {
        var day = new DateTime(2026, 11, 15);
        foreach (var ticks in new[] { 1L, 6000000L, 1234567L })
        {
            var time = day.AddHours(9).AddTicks(ticks);
            Assert.Equal(time, TimeText.ParseClock(TimeText.ShowClock(time, day), day, false));
        }
        Assert.Equal(day.AddDays(1), TimeText.ParseClock(TimeText.ShowClock(day.AddDays(1), day), day, true));
    }

    [AvaloniaFact]
    public void AccountFilterExposesMultipleChecksKeepsFocusAndEscapeReturnsToItsField()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); window.ShowNotes();
        try
        {
            Settle(window); var anchor = Find<Button>(window, "NotesAccountFilter"); anchor.Focus();
            anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
            var host = Find<Popup>(window, "NotesAccountHost"); Assert.True(host.IsOpen);
            var card = Assert.IsType<Border>(host.Child);
            var first = card.GetVisualDescendants().OfType<ToggleButton>().First();
            Assert.True(first.IsFocused); Key(window, Avalonia.Input.Key.Space); Settle(window);
            var rows = card.GetVisualDescendants().OfType<ToggleButton>().ToArray();
            Assert.True(rows[0].IsChecked, "First filter remains checked after rebuilding");
            Assert.True(rows[0].IsFocused, "Focus follows the replacement filter row");
            Assert.False(rows[1].IsChecked);
            rows[1].Focus(); Key(window, Avalonia.Input.Key.Space); Settle(window);
            Assert.All(card.GetVisualDescendants().OfType<ToggleButton>(), b => Assert.True(b.IsChecked));
            var clear = Find<Button>(card, "NotesAccountClear"); clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(window);
            Assert.All(card.GetVisualDescendants().OfType<ToggleButton>(), b => Assert.False(b.IsChecked));
            Assert.True(card.GetVisualDescendants().OfType<ToggleButton>().First().IsFocused);
            Key(window, Avalonia.Input.Key.Escape); Settle(window);
            Assert.False(host.IsOpen); Assert.True(anchor.IsFocused);
        }
        finally { window.Close(); }
    }
}
