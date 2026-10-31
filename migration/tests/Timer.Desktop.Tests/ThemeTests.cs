using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class ThemeTests
{
    [Fact]
    public void EveryBuiltInThemeSetsEveryPartWithValidColoursAndAUniqueName()
    {
        Assert.Equal(AppThemes.BuiltIn.Count, AppThemes.BuiltIn.Select(t => t.Name.ToUpperInvariant()).Distinct().Count());
        foreach (var theme in AppThemes.BuiltIn)
        {
            Assert.True(theme.BuiltIn);
            foreach (var token in ThemeTokens.Names) Assert.True(ThemeTokens.IsValid(token, theme.Colours.GetValueOrDefault(token)), $"{theme.Name} {token}");
        }
        Assert.Contains(AppThemes.BuiltIn, t => t.Name == "Light" && !t.Dark); Assert.Contains(AppThemes.BuiltIn, t => t.Name == "Dark" && t.Dark);
    }

    [Fact]
    public void TextIsReadableOnTheBackgroundInEveryThemeAndEveryStateTint()
    {
        foreach (var theme in AppThemes.BuiltIn)
        {
            Assert.True(ThemeColours.Contrast(theme.Colour("Ink"), theme.Colour("Surface")) >= 7, $"{theme.Name} text on surface");
            Assert.True(ThemeColours.Contrast(theme.Colour("Ink"), theme.Colour("Panel")) >= 7, $"{theme.Name} text on panel");
            Assert.True(ThemeColours.Contrast(theme.Colour("Muted"), theme.Colour("Surface")) >= 3.5, $"{theme.Name} quiet text");
            Assert.True(ThemeColours.Contrast(theme.Colour("AccentInk"), theme.Colour("Accent")) >= 4.5, $"{theme.Name} text on the accent");
            foreach (var state in new[] { ThemeDerivation.Break, ThemeDerivation.Overtime })
            {
                var variant = ThemeDerivation.Variant(theme, state);
                // The capsule is a strong colour with large, heavy text on it, so the bar is the standard 4.5 to 1 for readable text, not the 7 of the calm surfaces.
                Assert.True(ThemeColours.Contrast(variant["Ink"], variant["Surface"]) >= 4.5, $"{theme.Name} {state} text on surface");
                Assert.True(ThemeColours.Contrast(variant["Ink"], variant["Panel"]) >= 4.0, $"{theme.Name} {state} text on panel");
                Assert.True(ThemeColours.Contrast(variant["Muted"], variant["Surface"]) >= 3.0, $"{theme.Name} {state} quiet text");
            }
        }
    }

    [Fact]
    public void ThePillWearsItsStateColourWithPlainTextOnItAndLeavesTheThemesOtherColoursAlone()
    {
        foreach (var theme in AppThemes.BuiltIn)
        {
            var recording = ThemeDerivation.Variant(theme, ThemeDerivation.Recording); var amber = ThemeDerivation.Variant(theme, ThemeDerivation.Break); var red = ThemeDerivation.Variant(theme, ThemeDerivation.Overtime);
            Assert.Equal(theme.Colour("PillSurface"), recording["Surface"]); Assert.Equal(theme.Colour("PillInk"), recording["Ink"]);
            Assert.Equal(theme.Colour("PillBreak"), amber["Surface"]); Assert.Equal(theme.Colour("PillOvertime"), red["Surface"]);
            // The colours that normally mean something become the pill's text colour, since the whole pill already says it.
            foreach (var variant in new[] { recording, amber, red }) foreach (var token in new[] { "Break", "Overtime" }) Assert.Equal(variant["Ink"], variant[token]);
            foreach (var token in new[] { "Accent", "AccentInk", "BudgetWarning", "ShadowColor" }) { Assert.Equal(theme.Colour(token), amber[token]); Assert.Equal(theme.Colour(token), red[token]); }
            Assert.NotEqual(amber["Surface"], red["Surface"]); Assert.NotEqual(recording["Surface"], amber["Surface"]);
            // A dark theme's overtime colour is made for text, so the pill deepens it: white text on it must still read.
            if (theme.Dark) Assert.True(ThemeColours.Hsl(red["Surface"]).L < .45, theme.Name);
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
        var mineColours = AppThemes.Dark.Colours.ToDictionary(c => c.Key, c => c.Value); mineColours["Surface"] = "#102030";
        var mine = new ThemeDefinition("My blue", true, mineColours, false, ThemeBlockStyle.Softened);
        var lines = new[] { ThemeStorage.ToLine(mine), "this is not json", ThemeStorage.ToLine(AppThemes.Dark with { Name = "Dark" }), "{\"Name\":\"Plain\",\"Dark\":false,\"Colours\":{\"Surface\":\"nonsense\",\"Ink\":\"#112233\"}}" };
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
        var soft = AppThemes.Dark with { BlockStyle = ThemeBlockStyle.Softened };
        var colour = "#91B7D5"; var softened = ThemeDerivation.Soften(soft, colour);
        Assert.NotEqual(colour, softened); Assert.True(ThemeColours.Luminance(softened) < ThemeColours.Luminance(colour)); // Towards a dark surface.
        Assert.Equal(colour, ThemeDerivation.Soften(AppThemes.Dark, colour));
        Assert.True(ThemeColours.Luminance(ThemeDerivation.Soften(AppThemes.Light with { BlockStyle = ThemeBlockStyle.Softened }, colour)) > ThemeColours.Luminance(colour));
    }

    private static T Find<T>(Avalonia.Controls.Control window, string name) where T : Avalonia.Controls.Control
        => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<T>().Single(c => c.Name == name);

    [Fact]
    public void TintedBlocksKeepTheirTextReadableForEveryPresetColourAndTheEdgeKeepsTheAccountColour()
    {
        foreach (var colour in TimerModel.Palette)
        {
            var look = ThemeDerivation.Block(AppThemes.Light, colour);
            Assert.True(ThemeColours.Contrast(look.Text, look.Fill) >= 4.5, $"{colour}: text {look.Text} on fill {look.Fill}");
            Assert.Equal(colour.ToUpperInvariant(), look.Edge.ToUpperInvariant()); // The light theme leaves the edge as the account's own colour.
            Assert.NotEqual(colour.ToUpperInvariant(), look.Fill.ToUpperInvariant()); // The fill is a tint, not the colour itself.
        }
        // A theme can push the edge towards its text colour, and take more or less of the account colour into the fill.
        var stronger = AppThemes.Light with { BlockEdge = .5, BlockTint = .9 };
        var a = ThemeDerivation.Block(AppThemes.Light, "#91B7D5"); var b = ThemeDerivation.Block(stronger, "#91B7D5");
        Assert.NotEqual(a.Edge, b.Edge); Assert.True(ThemeColours.Luminance(b.Edge) < ThemeColours.Luminance(a.Edge)); Assert.True(ThemeColours.Luminance(b.Fill) < ThemeColours.Luminance(a.Fill));
        // The older styles still work: solid blocks keep the account colour, softened ones blend towards the surface.
        Assert.Equal("#91B7D5", ThemeDerivation.Block(AppThemes.Light with { BlockStyle = ThemeBlockStyle.AsChosen }, "#91B7D5").Fill);
        Assert.NotEqual("#91B7D5", ThemeDerivation.Block(AppThemes.Light with { BlockStyle = ThemeBlockStyle.Softened }, "#91B7D5").Fill);
    }

    [Fact]
    public void BlockRulesSurviveSavingAndAnOlderThemeLineGetsTheDefaults()
    {
        var mine = AppThemes.Light with { Name = "Mine", BuiltIn = false, BlockStyle = ThemeBlockStyle.Tinted, BlockTint = .45, BlockEdge = .1 };
        var back = ThemeStorage.FromLines(new[] { ThemeStorage.ToLine(mine) }).Single();
        Assert.Equal(ThemeBlockStyle.Tinted, back.BlockStyle); Assert.Equal(.45, back.BlockTint); Assert.Equal(.1, back.BlockEdge);
        var old = ThemeStorage.FromLines(new[] { "{\"Name\":\"Older\",\"Dark\":false,\"Colours\":{},\"BlockStyle\":\"softened\"}" }).Single();
        Assert.Equal(ThemeBlockStyle.DefaultTint, old.BlockTint); Assert.Equal(ThemeBlockStyle.DefaultEdge, old.BlockEdge); Assert.Equal(ThemeBlockStyle.Softened, old.BlockStyle);
        var wild = ThemeStorage.FromLines(new[] { ThemeStorage.ToLine(mine with { Name = "Wild", BlockTint = 9, BlockEdge = -3 }) }).Single();
        Assert.Equal(1, wild.BlockTint); Assert.Equal(0, wild.BlockEdge); // Kept within sensible limits.
    }

    [Fact]
    public void OnlyLightAndDarkShipAndAnythingRetiredLandsOnTheNearestOfThem()
    {
        Assert.Equal(new[] { "Light", "Dark" }, AppThemes.BuiltIn.Select(t => t.Name)); Assert.Equal(new[] { false, true }, AppThemes.BuiltIn.Select(t => t.Dark));
        Assert.Equal("Light", ThemeLibrary.Renamed("Time Deck")); Assert.Equal("Dark", ThemeLibrary.Renamed("Time Deck Dark")); Assert.Equal("My own", ThemeLibrary.Renamed("My own"));
        // A saved choice of a retired study becomes Light or Dark by its base, and a name that is nothing falls back to System.
        Assert.Equal("Dark", AppThemes.Resolve("After Hours", null)); Assert.Equal("Light", AppThemes.Resolve("Botanical", null)); Assert.Equal("Dark", AppThemes.Resolve("hawthorn", null));
        Assert.Equal("Dark", AppThemes.Resolve("Sage", null)); Assert.Equal("Light", AppThemes.Resolve("Studio", null));
        Assert.Equal("System", AppThemes.Resolve("Gone", null)); Assert.Equal("System", AppThemes.Resolve(null, null)); Assert.Equal("System", AppThemes.Resolve("system", null));
        Assert.Equal("Light", AppThemes.Resolve("light", null)); Assert.Equal("Dark", AppThemes.Resolve("DARK", null));
        // A user's own theme goes by its own base.
        var mine = new ThemeDefinition("Moonlit", true, AppThemes.Dark.Colours); var daylight = new ThemeDefinition("Daylight", false, AppThemes.Light.Colours);
        Assert.Equal("Dark", AppThemes.Resolve("Moonlit", new[] { mine, daylight })); Assert.Equal("Light", AppThemes.Resolve("Daylight", new[] { mine, daylight }));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ASavedOwnThemeIsKeptInTheFileButTheChoiceBecomesItsBase()
    {
        var colours = AppThemes.Dark.Colours.ToDictionary(c => c.Key, c => c.Value); colours["Surface"] = "#102030";
        var mine = new ThemeDefinition("Moonlit", true, colours);
        var folder = Path.Combine(Path.GetTempPath(), "timer-theme-" + Guid.NewGuid());
        try
        {
            using (var store = new Timer.Storage.TimerStore(folder))
            {
                var engine = new TrackingEngine(); var prefs = store.Load(engine) with { Themes = new[] { mine }, Theme = "Moonlit" };
                var model = new TimerModel(engine, prefs, store);
                Assert.Equal("Dark", model.Theme); Assert.Equal(new[] { "Light", "Dark" }, model.Themes.Select(t => t.Name)); // Only the two are offered.
                Assert.Throws<ArgumentException>(() => model.SetTheme("Moonlit"));
                Assert.True(model.Save());
            }
            using (var store = new Timer.Storage.TimerStore(folder))
            {
                var prefs = store.Load(new TrackingEngine());
                Assert.Equal("Dark", prefs.Theme); Assert.Equal("Moonlit", Assert.Single(prefs.Themes!).Name); // The theme is left alone in the settings file.
            }
        }
        finally { Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default; if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
