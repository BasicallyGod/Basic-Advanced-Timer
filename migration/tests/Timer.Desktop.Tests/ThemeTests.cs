using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class ThemeTests
{
    [Fact]
    public void EveryBuiltInThemeSetsEveryPartWithValidColoursAndAUniqueName()
    {
        Assert.Equal(ThemeLibrary.BuiltIn.Count, ThemeLibrary.BuiltIn.Select(t => t.Name.ToUpperInvariant()).Distinct().Count());
        foreach (var theme in ThemeLibrary.BuiltIn)
        {
            Assert.True(theme.BuiltIn);
            foreach (var token in ThemeTokens.Names) Assert.True(ThemeTokens.IsValid(token, theme.Colours.GetValueOrDefault(token)), $"{theme.Name} {token}");
        }
        Assert.Contains(ThemeLibrary.BuiltIn, t => t.Name == "Light" && !t.Dark); Assert.Contains(ThemeLibrary.BuiltIn, t => t.Name == "Dark" && t.Dark);
    }

    [Fact]
    public void TextIsReadableOnTheBackgroundInEveryThemeAndEveryStateTint()
    {
        foreach (var theme in ThemeLibrary.BuiltIn)
        {
            Assert.True(ThemeColours.Contrast(theme.Colour("Ink"), theme.Colour("Surface")) >= 7, $"{theme.Name} text on surface");
            Assert.True(ThemeColours.Contrast(theme.Colour("Ink"), theme.Colour("Panel")) >= 7, $"{theme.Name} text on panel");
            Assert.True(ThemeColours.Contrast(theme.Colour("Muted"), theme.Colour("Surface")) >= 3.5, $"{theme.Name} quiet text");
            foreach (var state in new[] { ThemeDerivation.Break, ThemeDerivation.Overtime })
            {
                var variant = ThemeDerivation.Variant(theme, state);
                Assert.True(ThemeColours.Contrast(variant["Ink"], variant["Surface"]) >= 7, $"{theme.Name} {state} text on surface");
                Assert.True(ThemeColours.Contrast(variant["Ink"], variant["Panel"]) >= 7, $"{theme.Name} {state} text on panel");
                Assert.True(ThemeColours.Contrast(variant["Muted"], variant["Surface"]) >= 3.5, $"{theme.Name} {state} quiet text");
            }
        }
        foreach (var theme in new[] { ThemeLibrary.HighContrastDark, ThemeLibrary.HighContrastLight }) Assert.True(ThemeColours.Contrast(theme.Colour("Ink"), theme.Colour("Surface")) >= 15);
    }

    [Fact]
    public void BreakAndOvertimeTintsTakeTheAccentHueAndKeepTheThemesOwnLightnessAndAccents()
    {
        foreach (var theme in ThemeLibrary.BuiltIn)
        {
            var amber = ThemeDerivation.Variant(theme, ThemeDerivation.Break); var red = ThemeDerivation.Variant(theme, ThemeDerivation.Overtime);
            var hue = ThemeColours.Hsl(amber["Surface"]).H; Assert.InRange(hue, 25, 60);
            Assert.Equal(theme.Dark, ThemeColours.Hsl(amber["Surface"]).L < .5); // A dark theme stays dark, a light one light.
            foreach (var token in new[] { "Live", "Break", "Overtime", "Finish", "BudgetWarning", "ShadowColor" }) { Assert.Equal(theme.Colour(token), amber[token]); Assert.Equal(theme.Colour(token), red[token]); }
            Assert.NotEqual(theme.Colour("Surface"), amber["Surface"]); Assert.NotEqual(amber["Surface"], red["Surface"]);
        }
    }

    [Fact]
    public void ColourMathMixesConvertsAndMeasuresContrast()
    {
        Assert.Equal("#808080", ThemeColours.Mix("#000000", "#FFFFFF", 0.5)); Assert.Equal("#FF0000", ThemeColours.FromHsl(0, 1, .5)); Assert.Equal("#FFFFFF", ThemeColours.FromHsl(0, 0, 1));
        Assert.Equal(21, ThemeColours.Contrast("#000000", "#FFFFFF"), 1); Assert.Equal(1, ThemeColours.Contrast("#336699", "#336699"), 3);
        var (h, s, l) = ThemeColours.Hsl("#3A7CA5"); Assert.Equal("#3A7CA5", ThemeColours.FromHsl(h, s, l));
    }

    [Fact]
    public void YourOwnThemesSurviveSavingAndADamagedOneIsSkippedWithoutLosingTheRest()
    {
        var mineColours = ThemeLibrary.Dark.Colours.ToDictionary(c => c.Key, c => c.Value); mineColours["Surface"] = "#102030";
        var mine = new ThemeDefinition("My blue", true, mineColours, false, ThemeBlockStyle.Softened);
        var lines = new[] { ThemeStorage.ToLine(mine), "this is not json", ThemeStorage.ToLine(ThemeLibrary.Ocean with { Name = "Dark" }), "{\"Name\":\"Plain\",\"Dark\":false,\"Colours\":{\"Surface\":\"nonsense\",\"Ink\":\"#112233\"}}" };
        var loaded = ThemeStorage.FromLines(lines);
        Assert.Equal(new[] { "My blue", "Plain" }, loaded.Select(t => t.Name)); // The broken line and the one reusing a built-in name are left out.
        Assert.Equal("#102030", loaded[0].Colour("Surface")); Assert.Equal(ThemeBlockStyle.Softened, loaded[0].BlockStyle); Assert.True(loaded[0].Dark); Assert.False(loaded[0].BuiltIn);
        Assert.Equal("#112233", loaded[1].Colour("Ink")); Assert.Equal(ThemeLibrary.Light.Colour("Surface"), loaded[1].Colour("Surface")); // A bad colour falls back to the base theme's.
        Assert.All(loaded, t => Assert.All(ThemeTokens.Names, n => Assert.True(ThemeTokens.IsValid(n, t.Colour(n)))));
        Assert.False(ThemeStorage.IsUsableName("System")); Assert.False(ThemeStorage.IsUsableName(" padded ")); Assert.False(ThemeStorage.IsUsableName("")); Assert.True(ThemeStorage.IsUsableName("Evening"));
    }

    [Fact]
    public void SoftenedBlocksMoveTowardsTheSurfaceAndAsChosenBlocksDoNot()
    {
        var soft = ThemeLibrary.Dark with { BlockStyle = ThemeBlockStyle.Softened };
        var colour = "#91B7D5"; var softened = ThemeDerivation.Soften(soft, colour);
        Assert.NotEqual(colour, softened); Assert.True(ThemeColours.Luminance(softened) < ThemeColours.Luminance(colour)); // Towards a dark surface.
        Assert.Equal(colour, ThemeDerivation.Soften(ThemeLibrary.Dark, colour));
        Assert.True(ThemeColours.Luminance(ThemeDerivation.Soften(ThemeLibrary.Light with { BlockStyle = ThemeBlockStyle.Softened }, colour)) > ThemeColours.Luminance(colour));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AUserThemeIsRegisteredSelectedStyledAndRemembered_AndAnUnknownNameFallsBackToSystem()
    {
        var colours = ThemeLibrary.Dark.Colours.ToDictionary(c => c.Key, c => c.Value); colours["Surface"] = "#102030"; colours["Ink"] = "#FAFAFA";
        var mine = new ThemeDefinition("Midnight", true, colours);
        var folder = Path.Combine(Path.GetTempPath(), "timer-theme-" + Guid.NewGuid());
        try
        {
            using (var store = new Timer.Storage.TimerStore(folder))
            {
                var engine = new TrackingEngine(); var prefs = store.Load(engine) with { Themes = new[] { mine } };
                var model = new TimerModel(engine, prefs, store);
                Assert.Contains(model.Themes, t => t.Name == "Midnight"); model.SetTheme("Midnight");
                Assert.Equal(ThemeEngine.VariantFor(mine), Avalonia.Application.Current!.RequestedThemeVariant);
                Assert.True(((Avalonia.Controls.IResourceHost)Avalonia.Application.Current).TryGetResource("Surface", ThemeEngine.VariantFor(mine), out var surface)); Assert.Equal(Avalonia.Media.Color.Parse("#102030"), ((Avalonia.Media.ISolidColorBrush)surface!).Color);
                Assert.NotEqual(ThemeEngine.VariantFor(mine), ThemeEngine.StateVariant(mine, ThemeDerivation.Break)); // The pill's break tint is its own variant.
                Assert.Throws<ArgumentException>(() => model.SetTheme("No such theme"));
                Assert.True(model.Save());
            }
            using (var store = new Timer.Storage.TimerStore(folder))
            {
                var prefs = store.Load(new TrackingEngine());
                Assert.Equal("Midnight", prefs.Theme); Assert.Equal("Midnight", Assert.Single(prefs.Themes!).Name); Assert.Equal("#102030", prefs.Themes![0].Colour("Surface"));
            }
            // A theme name that no longer exists is not left selected.
            File.WriteAllText(Path.Combine(folder, "basics-advanced-timer.settings.json"), "{\"schemaVersion\":1,\"sections\":{\"migrationAppearance\":[\"Gone\",\"shadows=on\"]}}");
            using (var store = new Timer.Storage.TimerStore(folder)) Assert.Equal("System", store.Load(new TrackingEngine()).Theme);
        }
        finally { Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default; if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
