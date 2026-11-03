using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Round 22: the header that stays put, a gold mark for importance, the star that keeps its place, Quit timer, a shadow that waits for the grow, and a dot that stays at one edge.
public sealed class Round22Tests
{
    private static DateTime Day => new(2026, 10, 9);
    private static TimerModel Model(params string[] accounts)
    {
        var names = accounts.Length > 0 ? accounts : new[] { "Account one", "Account two" };
        var model = new TimerModel(new TrackingEngine(clock: () => Day.AddHours(12)), new StoreSettings(names.Select(n => new AccountSettings(n)).ToList(), false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        return model;
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window); }
    private static double TopOf(Window window, Control c) => c.TranslatePoint(new Point(0, 0), window)!.Value.Y;
    private static double LeftOf(Window window, Control c) => c.TranslatePoint(new Point(0, 0), window)!.Value.X;

    [AvaloniaFact]
    public void TheSettingsButtonStaysWhereItIsWhetherSettingsAreOpenOrShut()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var gear = Find<Button>(window, "Settings"); var shut = TopOf(window, gear);
            Click(window, "Settings"); Assert.Equal(shut, TopOf(window, gear), 0.01);
            Click(window, "Settings"); Assert.Equal(shut, TopOf(window, gear), 0.01);
            Click(window, "NotesTab"); Assert.Equal(shut, TopOf(window, gear), 0.01);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AnImportantNoteIsGoldNotTheAccent()
    {
        var model = Model(); model.Notes.AddNote("!! Ring the client", null, null); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var app = Application.Current!;
            Avalonia.Media.Color Colour(string key) => ((Avalonia.Media.SolidColorBrush)app.FindResource(window.ActualThemeVariant, key)!).Color;
            var star = Colour("ImportantStar"); var accent = Colour("Accent");
            Assert.True(star.R > star.B + 80 && star.R >= accent.R); // Warm: far from the accent's blue.
            Assert.NotEqual(Colour("ImportantTint"), Colour("Panel"));
            Assert.True(Hue(Colour("ImportantTint")) < 60 && Hue(Colour("ImportantEdge")) < 60);
        }
        finally { window.Close(); }
    }
    private static double Hue(Avalonia.Media.Color c) { var hsv = c.ToHsv(); return hsv.H; }

    [AvaloniaFact]
    public void TheStarKeepsItsPlaceWhetherOrNotTheBellIsThere()
    {
        var model = Model(); model.Notes.AddNote("!! With a bell", null, null); model.Notes.AddNote("!! Without a bell", null, Day.AddHours(15));
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var withBell = model.Notes.All.Single(n => n.Text.Contains("With a bell")); var without = model.Notes.All.Single(n => n.Text.Contains("Without a bell"));
            var a = Find<Button>(window, "NoteImportant_" + withBell.Id); var b = Find<Button>(window, "NoteImportant_" + without.Id);
            var bell = Find<Button>(window, "NoteAddReminder_" + withBell.Id);
            Assert.Equal(a.Bounds.Width, b.Bounds.Width, 0.01);
            Assert.Equal(LeftOf(window, a) - LeftOf(window, Find<Button>(window, "NoteRemove_" + withBell.Id)), LeftOf(window, b) - LeftOf(window, Find<Button>(window, "NoteRemove_" + without.Id)), 0.01);
            Assert.True(LeftOf(window, bell) < LeftOf(window, a)); // The bell is on the star's left, where it can come and go.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheFootButtonSaysQuitTimer()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var quit = Find<Button>(window, "QuitTimer");
            Assert.Contains(quit.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Quit timer");
            Assert.Equal("Quit timer", Avalonia.Automation.AutomationProperties.GetName(quit));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheCardsShadowAndItsFadeStayOffUntilTheDashboardHasGrown()
    {
        var model = Model(); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            var host = Find<Border>(window, "ShadowHost");
            window.SetMorph(0); Assert.Null(host.Transitions); Assert.Equal(0, host.BoxShadow.Count); // No fade to leave an old shadow showing round a small card.
            window.SetMorph(.9); Assert.Null(host.Transitions); Assert.Equal(0, host.BoxShadow.Count);
            window.SetMorph(1); Assert.NotNull(host.Transitions); // The fade is back for the shadow's own changes.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheTrackingCardsDotKeepsOneLeftEdgeWhateverTheNameIs()
    {
        var model = Model("Al", "A rather long account name that goes on"); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            double Edge() { var dot = Find<Border>(window, "SidebarWell").GetVisualDescendants().OfType<Ellipse>().First(e => e.Width == 9); return LeftOf(window, dot); }
            var idle = Edge(); model.ToggleAccount("Al"); Settle(window); var short1 = Edge();
            model.ToggleAccount("A rather long account name that goes on"); Settle(window); var long1 = Edge();
            Assert.Equal(short1, long1, 0.01); Assert.Equal(idle, short1, 0.01);
            var name = Find<TextBlock>(window, "SidebarWhoName"); Assert.True(name.Bounds.Right <= Find<Border>(window, "SidebarWell").Bounds.Width);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ALongHorizontalDropdownGivesTheScrollTrackThePaddingInsteadOfAddingToIt()
    {
        var names = Enumerable.Range(1, 14).Select(i => "Account " + i).ToArray();
        var model = Model(names);
        var pill = new PillWindow(model); pill.Show(); Settle(pill);
        try
        {
            var card = pill.MakeDropdownCard(); var host = new Window { Width = 320, Height = 600, Content = card }; host.Show(); Settle(host);
            Assert.Equal(12, card.Padding.Left); Assert.Equal(2, card.Padding.Right); // Long enough to scroll: the track sits where the padding was.
            var short1 = Model("Account one", "Account two"); 
            var other = new PillWindow(short1); other.Show(); Settle(other);
            var shortCard = other.MakeDropdownCard(); var host2 = new Window { Width = 320, Height = 600, Content = shortCard }; host2.Show(); Settle(host2);
            Assert.Equal(12, shortCard.Padding.Right); // A short list has no track and keeps the even padding.
            host2.Close(); other.Close(); host.Close();
        }
        finally { pill.Close(); }
    }
}
