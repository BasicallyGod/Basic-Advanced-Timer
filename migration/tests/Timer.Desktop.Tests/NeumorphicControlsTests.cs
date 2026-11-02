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
    public void GalleryKeepsStarsOutlinedAndNeutralWithLegibleGoldInBothThemes()
    {
        var gallery = new NeumorphicGalleryWindow { Height = 1000 }; gallery.Show(); Settle(gallery);
        try
        {
            foreach (var theme in new[] { "Light", "Dark" })
            {
                var ready = Find<ToggleButton>(gallery, theme + "_Important_Ready");
                var on = Find<ToggleButton>(gallery, theme + "_Important_On");
                var pressed = Find<ToggleButton>(gallery, theme + "_Important_Pressed");
                Assert.Equal(Colour(Surface(ready).Background), Colour(Surface(on).Background));
                Assert.NotEqual(Colour(Surface(pressed).Background), Colour(Surface(on).Background));
                var fill = Colour(Surface(on).Background);
                Assert.Equal(IconKind.Star, Assert.IsType<IconGlyph>(on.Content).Kind);
                Assert.Equal("ImportantStar", Assert.IsType<IconGlyph>(on.Content).TintKey);
                var gold = Colour((IBrush)on.FindResource(on.ActualThemeVariant, "ImportantStar")!);
                Assert.True(ThemeColours.Contrast(Hex(gold), Hex(fill)) >= 3, theme + " star contrast");
                Assert.Equal(ready.Bounds.Size, on.Bounds.Size);
                Assert.True(Surface(on).BoxShadow.Count > 0);
                for (var i = 0; i < Surface(on).BoxShadow.Count; i++) { Assert.True(Surface(on).BoxShadow[i].IsInset); Assert.True(Surface(on).BoxShadow[i].Blur <= 2); }
                Assert.True(Find<Border>(Find<Button>(gallery, theme + "_Important_Focus"), "PART_NeumorphicFocus").IsVisible);
                Assert.False(Find<Border>(ready, "PART_NeumorphicFocus").IsVisible);
                Assert.Equal(0, Surface(Find<Button>(gallery, theme + "_Important_Disabled")).BoxShadow.Count);
                var disabled = Find<Button>(gallery, theme + "_Primary_Disabled");
                Assert.Equal(Colour(Surface(ready).Background), Colour(Surface(disabled).Background));
                Assert.Equal(Colour((IBrush)disabled.FindResource(disabled.ActualThemeVariant, "Muted")!), Colour(disabled.Foreground));
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
    public void GallerySectionsShowExistingFamiliesAndKeepNavigationLocal()
    {
        var gallery = new NeumorphicGalleryWindow { Height = 1000 }; gallery.Show(); Settle(gallery);
        try
        {
            var folder = Environment.GetEnvironmentVariable("TIMER_PREVIEW_DIR");
            foreach (var id in new[] { "buttons", "surfaces", "navigation", "inputs", "colours" })
            {
                Find<Button>(gallery, "GallerySection_" + id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(gallery);
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    var section = Find<Control>(gallery, theme + "_Section_" + id); Assert.True(section.IsEffectivelyVisible);
                    Assert.False(Find<Control>(gallery, theme + "_Section_states").IsEffectivelyVisible);
                    Assert.InRange(section.Bounds.Width, 450, 520); // Captions must wrap within each half, not widen the horizontally scrollable comparison.
                    if (folder is not null)
                    {
                        Directory.CreateDirectory(folder);
                        using var picture = WindowMorph.CaptureVisual(section, 1.5);
                        picture.Save(Path.Combine(folder, "neumorphic-" + id + "-" + theme + ".png"));
                    }
                }
            }
            gallery.ShowSection("buttons"); Settle(gallery);
            foreach (var theme in new[] { "Light", "Dark" })
            {
                var disabled = Find<Button>(gallery, theme + "_CurrentSaveDisabled");
                var presenter = Find<Avalonia.Controls.Presenters.ContentPresenter>(disabled, "PART_ContentPresenter");
                Assert.Equal(Colour((IBrush)disabled.FindResource(disabled.ActualThemeVariant, "Panel")!), Colour(presenter.Background));
            }
            gallery.ShowSection("navigation"); Settle(gallery);
            var notes = Find<Button>(gallery, "Light_ViewNotes"); notes.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("selected", notes.Classes); Assert.DoesNotContain("selected", Find<Button>(gallery, "Light_ViewTimeline").Classes);
            Assert.Contains("selected", Find<Button>(gallery, "Dark_ViewTimeline").Classes);
            var behaviour = Find<Button>(gallery, "Light_MenuSettingsBehaviour"); behaviour.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("selected", behaviour.Classes); Assert.DoesNotContain("selected", Find<Button>(gallery, "Light_MenuSettingsAppearance").Classes);
            foreach (var theme in new[] { "Light", "Dark" })
            {
                var menuButton = Find<Button>(gallery, theme + "_OpenContextMenu"); menuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(gallery);
                var menu = menuButton.ContextMenu!; Assert.True(menu.IsOpen); Assert.Equal(menuButton.ActualThemeVariant, menu.ActualThemeVariant);
                if (folder is not null)
                {
                    using var picture = WindowMorph.CaptureVisual(menu, 1.5);
                    picture.Save(Path.Combine(folder, "neumorphic-context-menu-" + theme + ".png"));
                }
                menu.Close();
            }
        }
        finally { gallery.Close(); }
    }

    [AvaloniaFact]
    public void CircularCheckboxesKeepNativeKeyboardMixedDisabledAndFocusBehaviour()
    {
        var gallery = new NeumorphicGalleryWindow(); gallery.Show(); gallery.ShowSection("inputs"); Settle(gallery);
        try
        {
            foreach (var theme in new[] { "Light", "Dark" })
            {
                var off = Find<CheckBox>(gallery, theme + "_TickOff"); var ring = Find<Border>(off, "PART_CheckRing");
                Assert.Equal(new CornerRadius(10), ring.CornerRadius); Assert.Equal(new Size(20, 20), ring.Bounds.Size);
                var before = off.Bounds.Size; off.Focus(NavigationMethod.Tab); Settle(gallery);
                Assert.True(Find<Border>(off, "PART_CheckFocus").IsVisible); Assert.Equal(before, off.Bounds.Size);
                gallery.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                gallery.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); Settle(gallery);
                Assert.True(off.IsChecked); Assert.True(Find<Avalonia.Controls.Shapes.Path>(off, "PART_CheckMark").IsVisible);
                var mixed = Find<CheckBox>(gallery, theme + "_TickMixed"); Assert.Null(mixed.IsChecked);
                Assert.True(Find<Avalonia.Controls.Shapes.Path>(mixed, "PART_MixedMark").IsVisible);
                Assert.False(Find<Avalonia.Controls.Shapes.Path>(mixed, "PART_CheckMark").IsVisible);
                var disabled = Find<CheckBox>(gallery, theme + "_TickDisabled"); Assert.True(disabled.IsChecked); Assert.False(disabled.IsEnabled);
                Assert.False(disabled.Focus()); Assert.Equal(0, Find<Border>(disabled, "PART_CheckRing").BoxShadow.Count);
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
