using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class NeumorphicControlsTests
{
    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++) { Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
    }
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static Border Surface(Control button) => Find<Border>(button, "PART_NeumorphicSurface");
    private static Color Colour(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static string Hex(Color colour) => $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";

    [AvaloniaFact]
    public void GalleryResolvesDistinctStatesAndLegibleWarmStarsInBothThemes()
    {
        var gallery = new NeumorphicGalleryWindow { Height = 1000 }; gallery.Show(); Settle(gallery);
        try
        {
            foreach (var theme in new[] { "Light", "Dark" })
            {
                var ready = Find<ToggleButton>(gallery, theme + "_Important_Ready");
                var on = Find<ToggleButton>(gallery, theme + "_Important_On");
                var pressed = Find<ToggleButton>(gallery, theme + "_Important_Pressed");
                Assert.NotEqual(Colour(Surface(ready).Background), Colour(Surface(on).Background));
                Assert.NotEqual(Colour(Surface(pressed).Background), Colour(Surface(on).Background));
                var fill = Colour(Surface(on).Background); Assert.True(fill.R > fill.B);
                var gold = Colour((IBrush)on.FindResource(on.ActualThemeVariant, "ImportantStar")!);
                Assert.True(ThemeColours.Contrast(Hex(gold), Hex(fill)) >= 3, theme + " star contrast");
                Assert.Equal(ready.Bounds.Size, on.Bounds.Size);
                Assert.True(Surface(on).BoxShadow.Count > 0);
                for (var i = 0; i < Surface(on).BoxShadow.Count; i++) { Assert.True(Surface(on).BoxShadow[i].IsInset); Assert.True(Surface(on).BoxShadow[i].Blur <= 2); }
                Assert.True(Find<Border>(Find<Button>(gallery, theme + "_Important_Focus"), "PART_NeumorphicFocus").IsVisible);
                Assert.False(Find<Border>(ready, "PART_NeumorphicFocus").IsVisible);
                Assert.Equal(0, Surface(Find<Button>(gallery, theme + "_Important_Disabled")).BoxShadow.Count);
                var primary = Find<Button>(gallery, theme + "_Primary_Hover");
                Assert.Equal(Colour((IBrush)primary.FindResource(primary.ActualThemeVariant, "AccentInk")!), Colour(primary.GetVisualDescendants().OfType<TextBlock>().Single().Foreground));
                foreach (var button in Find<Border>(gallery, theme + "_StateMatrix").GetVisualDescendants().OfType<Button>())
                {
                    var cell = Assert.IsType<Border>(button.GetVisualParent());
                    Assert.True(button.Bounds.Width <= cell.Bounds.Width - cell.Padding.Left - cell.Padding.Right + 1, button.Name);
                }
            }
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            if (folder is not null)
            {
                Directory.CreateDirectory(folder);
                using (var picture = gallery.CaptureRenderedFrame()) picture?.Save(Path.Combine(folder, "neumorphic-gallery.png"));
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    using var picture = WindowMorph.CaptureVisual(Find<Border>(gallery, theme + "_StateMatrix"), 2);
                    picture.Save(Path.Combine(folder, "neumorphic-states-" + theme + ".png"));
                }
            }
        }
        finally { gallery.Close(); }
    }

    [AvaloniaFact]
    public void RealImportanceToggleWorksOncePerKeyboardActivationAndKeepsItsBounds()
    {
        var model = new TimerModel(new TrackingEngine(clock: () => new DateTime(2026, 11, 2, 12, 0, 0))); model.CompleteSetup();
        model.Notes.AddNote("Pilot note", null, null); var note = Assert.Single(model.Notes.All);
        var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            for (var i = 0; i < 2; i++)
            {
                var button = Find<ToggleButton>(window, "NoteImportant_" + note.Id); var before = button.Bounds.Size;
                button.Focus(NavigationMethod.Tab); Settle(window);
                Assert.True(Find<Border>(button, "PART_NeumorphicFocus").IsVisible);
                Assert.Equal(before, button.Bounds.Size);
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); Settle(window);
                Assert.Equal(i == 0, NotesModel.IsImportant(model.Notes.Find(note.Id)!));
                Assert.Equal(i == 0, Find<ToggleButton>(window, "NoteImportant_" + note.Id).IsChecked);
                Assert.Single(model.Notes.All);
            }
            window.ShowNotes(); Settle(window);
            Assert.IsType<ToggleButton>(Find<Button>(window, "PageNoteImportant_" + note.Id));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OwnedGalleryIsReusedAndItsLiveSamplesDoNotChangeTimerDataOrTheme()
    {
        var model = TimerModel.CreateDemo(); model.CompleteSetup();
        var owner = new DashboardWindow(model); owner.Show(); Settle(owner);
        var notes = model.Notes.All.ToArray(); var sessions = model.Engine.Sessions.ToArray(); var theme = model.Theme; var tracking = model.IsTracking;
        try
        {
            NeumorphicGalleryWindow.Open(owner); var gallery = Assert.Single(owner.OwnedWindows.OfType<NeumorphicGalleryWindow>()); Settle(gallery);
            NeumorphicGalleryWindow.Open(owner); Assert.Same(gallery, Assert.Single(owner.OwnedWindows.OfType<NeumorphicGalleryWindow>()));
            var button = Find<ToggleButton>(gallery, "Light_LiveImportant"); button.Focus(NavigationMethod.Tab);
            gallery.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            gallery.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); Settle(gallery);
            Assert.False(button.IsChecked); Assert.Contains("changed", Find<TextBlock>(gallery, "Light_GalleryStatus").Text);
            Assert.Equal(notes, model.Notes.All); Assert.Equal(sessions, model.Engine.Sessions); Assert.Equal(theme, model.Theme); Assert.Equal(tracking, model.IsTracking);
            owner.Close(); Assert.False(gallery.IsVisible);
        }
        finally { if (owner.IsVisible) owner.Close(); }
    }

    [AvaloniaFact]
    public void GalleryCannotBeOpenedWhenDeveloperFeaturesAreUnavailable()
    {
        var previous = BuildInfo.DeveloperFeaturesOverride; BuildInfo.DeveloperFeaturesOverride = false;
        DashboardWindow? owner = null;
        try
        {
            var model = TimerModel.CreateDemo(); owner = new DashboardWindow(model); owner.Show(); Settle(owner);
            owner.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(owner);
            owner.GetVisualDescendants().OfType<SettingsView>().Single().ShowSection("about"); Settle(owner);
            var entry = Find<Button>(owner, "OpenControlsGallery"); Assert.False(entry.IsEffectivelyVisible);
            entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Empty(owner.OwnedWindows.OfType<NeumorphicGalleryWindow>());
        }
        finally { owner?.Close(); BuildInfo.DeveloperFeaturesOverride = previous; }
    }
}
