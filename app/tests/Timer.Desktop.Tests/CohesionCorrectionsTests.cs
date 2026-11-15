using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class CohesionCorrectionsTests
{
    private static TimerModel Demo() { var model = TimerModel.CreateDemo(() => new DateTime(2026, 11, 15, 14, 0, 0)); model.CompleteSetup(); return model; }
    private static void Settle(Window window) { for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static T Find<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Press(Visual root, string name) => Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void SettingsHasAnAccurateCloseActionAndReturnsToTheSameNotesQuery()
    {
        var model = Demo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            window.ShowNotes(); Settle(window);
            var search = Find<TextBox>(window, "NotesSearch"); search.Text = "invoice";
            Press(window, "Settings"); Settle(window);
            var action = Find<Button>(window, "Settings");
            Assert.Equal("Close settings", AutomationProperties.GetName(action));
            Assert.Equal(IconKind.Close, Assert.IsType<IconGlyph>(action.Content).Kind);
            Press(window, "Settings"); Settle(window);
            Assert.Equal("Settings", AutomationProperties.GetName(action));
            Assert.Equal(IconKind.Gear, Assert.IsType<IconGlyph>(action.Content).Kind);
            Assert.Equal("invoice", Find<TextBox>(window, "NotesSearch").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyCompletionAndCheckControlsHaveAVisibleEdgeInBothThemes(bool dark)
    {
        var model = Demo(); model.SetTheme(dark ? "Dark" : "Light");
        var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window);
            var tick = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("tick"));
            var edge = Assert.IsAssignableFrom<ISolidColorBrush>(tick.BorderBrush).Color;
            var panel = Ui.ThemeColour(tick, "Panel", Colors.Transparent);
            static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            Assert.Equal((byte)255, edge.A);
            Assert.True(ThemeColours.Contrast(Hex(edge), Hex(panel)) >= 3.3);
            Press(window, "Settings"); Settle(window);
            var check = Find<CheckBox>(window, "SeasonalDecorations");
            var ring = check.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_CheckRing");
            Assert.Equal(edge, Assert.IsAssignableFrom<ISolidColorBrush>(ring.BorderBrush).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReminderOpensWithKeyboardFocusAndEscapeReturnsItWithoutWriting()
    {
        var model = Demo(); var window = new DashboardWindow(model); window.Show();
        try
        {
            Settle(window); var before = model.Notes.Revision;
            var bell = Find<Button>(window, "FormBell"); bell.Focus(); Press(window, "FormBell"); Settle(window);
            var popup = Find<Popup>(window, "ReminderHost");
            var card = Assert.IsType<ReminderPickerCard>(popup.Child);
            Assert.True(card.Picker.Strip.IsFocused);
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(card));
            card.Picker.Strip.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Settle(window);
            Assert.False(popup.IsOpen); Assert.True(bell.IsFocused);
            Assert.Equal(before, model.Notes.Revision);
            // A second opening must not leave another owner handler behind.
            Press(window, "FormBell"); Settle(window);
            Find<Popup>(window, "ReminderHost").IsOpen = false;
            var field = Find<TextBox>(window, "NoteField"); field.Focus(); Settle(window);
            Assert.True(field.IsFocused); Assert.Equal(before, model.Notes.Revision);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PillPreviewsShareTheRealKeysAndCompactOnlyRemovesTheName(bool ink)
    {
        var model = Demo(); model.SetDisplayStyle(ink ? DisplayStyle.Ink : DisplayStyle.Phosphor);
        var window = new Window { Content = new SettingsView(model), Width = 1050, Height = 850 }; window.Show();
        try
        {
            Settle(window); Press(window, "SettingsPill"); Settle(window);
            var full = Find<Button>(window, "StyleFull"); var compact = Find<Button>(window, "StyleCompact");
            foreach (var tile in new[] { full, compact, Find<Button>(window, "StyleTall") })
            {
                var play = Assert.Single(tile.GetVisualDescendants().OfType<TransportGlyph>());
                var pause = Assert.Single(tile.GetVisualDescendants().OfType<BreakBadgeGlyph>());
                Assert.True(play.Bounds.Bottom <= pause.TranslatePoint(default, play)!.Value.Y + .1);
                Assert.DoesNotContain(play.GetVisualAncestors(), c => c is StatusFace);
            }
            Assert.Single(full.GetVisualDescendants().OfType<SegmentName>());
            Assert.Empty(compact.GetVisualDescendants().OfType<SegmentName>());
            Assert.Equal("04:45:00", Assert.Single(full.GetVisualDescendants().OfType<SegmentClock>()).Text);
            Assert.False(model.IsTracking);
        }
        finally { window.Close(); }
    }
}
