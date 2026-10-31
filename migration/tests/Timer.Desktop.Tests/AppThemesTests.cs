using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class AppThemesTests
{
    [Fact]
    public void LightIsCoolAndCrispWithTheAccentTheOnlyStrongBlue()
    {
        var light = AppThemes.Light;
        Assert.Equal("#EDF0F5", light.Colour("Surface")); Assert.Equal("#FFFFFF", light.Colour("Panel")); Assert.Equal("#D5DBE5", light.Colour("Line")); Assert.Equal("#E4E9F1", light.Colour("Hover"));
        Assert.Equal("#131B2A", light.Colour("Ink")); Assert.Equal("#4E5A6C", light.Colour("Muted")); Assert.Equal("#2D5CCB", light.Colour("Accent")); Assert.Equal("#D6E2FA", light.Colour("Selected"));
        // Blocks take most of their account's colour, so they stand clear of the grey tiles behind them.
        Assert.Equal(0.78, light.BlockTint);
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
