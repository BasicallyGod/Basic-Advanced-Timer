using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 20: a note box that holds two chips and still shows its words, the week's account list scrolling on its own, a pin's halo and count on the dot's centre,
// a lighter recording row on Light, and drop-downs whose words sit on the middle.
public sealed class Round20Tests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TimerModel Model()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(18)), new StoreSettings(new() { new("Account one"), new("Account two") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window); }

    [AvaloniaFact]
    public void ANoteWithAnAccountAndAReminderKeepsItsWordsClearOfTheChips()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "AddNote");
            Find<TextBox>(window, "NoteField").Text = "Follow #Accountt"; Settle(window);
            Click(window, "Suggest_Accounttwo");
            Click(window, "FormBell"); Click(window, "ReminderSet");
            Find<TextBox>(window, "NoteField").Text = "Follow up on the new brief"; Settle(window);
            var field = Find<TextBox>(window, "NoteField"); var account = Find<Button>(window, "FormAccountChip"); var reminder = Find<Button>(window, "FormReminder");
            Rect In(Control c) => new(c.TranslatePoint(new Point(0, 0), window)!.Value, c.Bounds.Size);
            Assert.True(field.Bounds.Width > 150, "the words have room: " + field.Bounds.Width);
            Assert.False(In(field).Intersects(In(account))); Assert.False(In(field).Intersects(In(reminder))); // Nothing lies over the words.
            Assert.True(In(account).Right <= In(reminder).Left + 0.5 || In(account).Bottom <= In(reminder).Top + 0.5); // And the chips do not overlap each other.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheWeeksAccountListScrollsOnItsOwnAndHasNoLineAboveTheFirstAccount()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "WeekOverviewTab");
            var list = Find<StackPanel>(window, "WeekByAccount");
            Assert.NotNull(list.FindAncestorOfType<ScrollViewer>()); // The list has its own scroll bar...
            Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => ((Visual)s).GetVisualDescendants().Contains(list))); // ...and the page itself is not one.
            var first = list.Children.OfType<Border>().First(); var second = list.Children.OfType<Border>().Skip(1).First();
            Assert.Equal(0, first.BorderThickness.Top); Assert.Equal(1, second.BorderThickness.Top);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AGroupedPinsHaloAndCountSitOnTheDotsCentre()
    {
        var model = Model(); var due = Day.AddHours(14);
        model.Notes.AddNote("One", null, due); model.Notes.AddNote("Two", null, due);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var mark = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name?.StartsWith("TimelinePinMark_") == true);
            var visual = ((Visual)mark.Parent!).GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().ToList();
            var dot = visual.Single(e => e.StrokeThickness == 2.5); var halo = visual.Single(e => e.StrokeThickness == 3);
            double CentreX(Control c) => Canvas.GetLeft(c) + c.Width / 2; double CentreY(Control c) => Canvas.GetTop(c) + c.Height / 2;
            Assert.Equal(CentreX(dot), CentreX(halo), 3); Assert.Equal(CentreY(dot), CentreY(halo), 3); // The outer band is level with the dot.
            Assert.Equal(CentreX(dot), CentreX(mark), 3); Assert.Equal(CentreY(dot), CentreY(mark), 3);
            var digit = (TextBlock)mark.Child!; Assert.Equal(Avalonia.Media.TextAlignment.Center, digit.TextAlignment); // The count is centred in its dot.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheRecordingRowsTintIsGentlerOnLight()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Assert.True(window.TryFindResource("LiveTint", window.ActualThemeVariant, out var tint)); Assert.True(window.TryFindResource("Surface", window.ActualThemeVariant, out var surface));
            var a = (tint as Avalonia.Media.ISolidColorBrush)?.Color ?? (Avalonia.Media.Color)tint!; var b = (surface as Avalonia.Media.ISolidColorBrush)?.Color ?? (Avalonia.Media.Color)surface!;
            Assert.True(Math.Abs(a.G - b.G) <= 30 && Math.Abs(a.R - b.R) <= 30 && Math.Abs(a.B - b.B) <= 30, $"{a} vs {b}"); // Grey bars and ticks keep their contrast on it.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DropDownsHaveEvenPaddingSoTheirWordsSitOnTheMiddle()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "NotesTab"); Click(window, "PageKindMeeting");
            foreach (var name in new[] { "PageAccount", "PageRepeat" }) { var box = Find<ComboBox>(window, name); Assert.Equal(box.Padding.Top, box.Padding.Bottom); Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, box.VerticalContentAlignment); }
        }
        finally { window.Close(); }
    }
}
