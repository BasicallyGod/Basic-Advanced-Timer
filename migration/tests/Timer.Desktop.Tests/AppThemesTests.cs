using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class AppThemesTests
{
    [Fact]
    public void LightIsWarmGreyWithTheAccentTheOnlyStrongBlue()
    {
        var light = AppThemes.Light;
        Assert.Equal("#E9E9E7", light.Colour("Surface")); Assert.Equal("#F3F3F1", light.Colour("Panel")); Assert.Equal("#D9D9D5", light.Colour("Line")); Assert.Equal("#E3E3E0", light.Colour("Hover"));
        Assert.Equal("#1D222B", light.Colour("Ink")); Assert.Equal("#596070", light.Colour("Muted")); Assert.Equal("#2F5BE0", light.Colour("Accent")); Assert.Equal("#D9E2FA", light.Colour("Selected"));
        // Blocks take most of their account's colour, so they stand clear of the grey tiles behind them.
        Assert.Equal(0.78, light.BlockTint);
        Assert.Same(light, AppThemes.BuiltIn[0]); Assert.Same(AppThemes.Dark, AppThemes.BuiltIn[1]);
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
