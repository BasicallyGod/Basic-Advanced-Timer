using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class AppThemesTests
{
    [Fact]
    public void LightIsAQuietNeutralWithTheAccentAsItsOnlyBlue()
    {
        var light = AppThemes.Light;
        Assert.Equal("#F4F4F1", light.Colour("Surface")); Assert.Equal("#FFFFFF", light.Colour("Panel")); Assert.Equal("#E2E2DD", light.Colour("Line"));
        // Ink, the accent and the selection tint are the package's, so only the surfaces moved.
        Assert.Equal(ThemeLibrary.Light.Colour("Ink"), light.Colour("Ink")); Assert.Equal(ThemeLibrary.Light.Colour("Accent"), light.Colour("Accent")); Assert.Equal(ThemeLibrary.Light.Colour("Selected"), light.Colour("Selected"));
        foreach (var token in new[] { "Surface", "Panel", "Line", "Hover" })
        {
            var (r, g, b) = ThemeColours.Rgb(light.Colour(token));
            Assert.True(Math.Abs(r - b) <= 16, token + " is not blue-tinted: " + light.Colour(token)); // Warm or neutral, never the old blue-grey.
        }
        Assert.Same(light, AppThemes.BuiltIn[0]); Assert.Same(ThemeLibrary.Dark, AppThemes.BuiltIn[1]);
        Assert.All(ThemeTokens.Names, token => Assert.True(ThemeTokens.IsValid(token, light.Colour(token)), token));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void TheBuiltInPalettesKeepTheContrastTheAppAsksFor(string name)
    {
        var theme = AppThemes.BuiltIn.Single(t => t.Name == name);
        Assert.True(ThemeColours.Contrast(theme.Colour("Ink"), theme.Colour("Surface")) >= 7);
        Assert.True(ThemeColours.Contrast(theme.Colour("Ink"), theme.Colour("Panel")) >= 7);
        Assert.True(ThemeColours.Contrast(theme.Colour("Muted"), theme.Colour("Surface")) >= 3.5);
        Assert.True(ThemeColours.Contrast(theme.Colour("Muted"), theme.Colour("Panel")) >= 3.5);
        Assert.True(ThemeColours.Contrast(theme.Colour("AccentInk"), theme.Colour("Accent")) >= 4.5);
    }
}
