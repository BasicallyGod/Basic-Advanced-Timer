using Timer.Core;
using Timer.Desk;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

/// <summary>Timer.Core and Timer.Storage are meant to be shared with other apps, so they must stand on their own: nothing in them may reach up to the desk app or to a UI library.</summary>
public sealed class SharedLibraryBoundaryTests
{
    [Fact]
    public void TheSharedLibrariesKnowNothingOfTheDeskAppOrAnyUiLibrary()
    {
        foreach (var assembly in new[] { typeof(TrackingEngine).Assembly, typeof(TimerStore).Assembly })
        {
            var references = assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
            Assert.DoesNotContain(references, name => name.StartsWith("Timer.Desk", StringComparison.Ordinal) || name.StartsWith("Avalonia", StringComparison.Ordinal) || name.StartsWith("BasicsAdvancedTimer", StringComparison.Ordinal));
        }
        Assert.Contains(typeof(TimerStore).Assembly.GetReferencedAssemblies().Select(a => a.Name), n => n == "Timer.Core"); // And Storage builds on Core, not the other way round.
        Assert.DoesNotContain(typeof(TrackingEngine).Assembly.GetReferencedAssemblies().Select(a => a.Name), n => n == "Timer.Storage");
    }

    [Fact]
    public void SettingsAnAppAddsForItselfSurviveWithoutTheLibraryKnowingWhatTheyAre()
    {
        var folder = Path.Combine(Path.GetTempPath(), "timer-extras-" + Guid.NewGuid());
        try
        {
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var settings = store.Load(engine);
                var extras = new SettingsExtras(new[] { "colour-blind=on", "future-flag=7" }, new Dictionary<string, IReadOnlyList<string>> { ["someOtherApp"] = new[] { "{\"a\":1}" } });
                store.Save(engine, settings with { Extras = extras });
            }
            using (var store = new TimerStore(folder))
            {
                var engine = new TrackingEngine(); var settings = store.Load(engine);
                Assert.Contains("colour-blind=on", settings.Extras!.AppearanceLines); Assert.Contains("future-flag=7", settings.Extras.AppearanceLines);
                Assert.Equal(new[] { "{\"a\":1}" }, settings.Extras.Sections["someOtherApp"]);
                Assert.DoesNotContain(settings.Extras.AppearanceLines, l => l.StartsWith("shadows=", StringComparison.Ordinal)); // The library's own lines are not handed over.
                Assert.DoesNotContain("migrationAppearance", settings.Extras.Sections.Keys); Assert.DoesNotContain("workingHours", settings.Extras.Sections.Keys);
            }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    [Fact]
    public void TheDeskAppsSettingsRoundTripAndLeaveAnotherAppsSettingsAlone()
    {
        var other = new SettingsExtras(new[] { "colour-blind=on" }, new Dictionary<string, IReadOnlyList<string>> { ["someOtherApp"] = new[] { "x" } });
        var start = new StoreSettings(new() { new("A") }, false, "Light", DayOfWeek.Monday, Extras: other);
        var app = new AppPrefs(DashboardWidth: 900, DashboardHeight: 620, DeskOrder: new[] { "day" }, Calm: true, DeskHidden: new[] { "clock" }, DeskSizes: new[] { "todo:2x4" }, PillStyle: "compact",
            DeskPositions: new[] { "day:0+1" }, CaptureKey: "Ctrl+Alt+N", FloatOnTop: false, Floating: new[] { new FloatingWidget("todo", 5, 6, 2, 3) },
            DeskLayouts: new[] { new SavedLayout("Focus", new[] { "todo" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()) });
        var saved = app.Into(start); var back = AppPrefs.From(saved);
        Assert.Equal(900, back.DashboardWidth); Assert.Equal(new[] { "day" }, back.DeskOrder); Assert.True(back.Calm); Assert.Equal(new[] { "clock" }, back.DeskHidden); Assert.Equal("compact", back.PillStyle);
        Assert.Equal("Ctrl+Alt+N", back.CaptureKey); Assert.False(back.FloatOnTop); Assert.Equal(new FloatingWidget("todo", 5, 6, 2, 3), Assert.Single(back.Floating!)); Assert.Equal("Focus", Assert.Single(back.DeskLayouts!).Name);
        Assert.Contains("colour-blind=on", saved.Extras!.AppearanceLines); Assert.Equal(new[] { "x" }, saved.Extras.Sections["someOtherApp"]); // Another app's settings were kept.
        Assert.Equal(1, saved.Extras.AppearanceLines.Count(l => l.StartsWith("desk-order=", StringComparison.Ordinal))); // Saving again does not stack the lines.
        Assert.Equal(1, AppPrefs.From(back.Into(saved)).DeskOrder!.Count); Assert.Equal(1, back.Into(saved).Extras!.AppearanceLines.Count(l => l.StartsWith("desk-order=", StringComparison.Ordinal)));
        var plain = AppPrefs.From(new StoreSettings(new() { new("A") }, false, "Light", DayOfWeek.Monday)); // An older file with none of them gives the defaults.
        Assert.Equal("full", plain.PillStyle); Assert.True(plain.FloatOnTop); Assert.False(plain.Calm); Assert.Null(plain.DashboardWidth); Assert.Empty(plain.Floating!);
    }
}
