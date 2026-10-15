using Timer.Core;
using Timer.Desk;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class DeskHueTests
{
    public static IEnumerable<object[]> Tiles() => DeskHues.Ids.Where(id => !id.StartsWith("postit")).Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(Tiles))]
    public void TextReadsOnEveryTilesWashAndBandInTheLightTheme(string id)
    {
        var theme = ThemeLibrary.Light; var (body, band) = DeskHues.Composite(id, false, theme.Colours["Surface"]);
        Assert.True(ThemeColours.Contrast(theme.Colours["Ink"], body) >= 7, $"{id} body {ThemeColours.Contrast(theme.Colours["Ink"], body):0.0}");
        Assert.True(ThemeColours.Contrast(theme.Colours["Ink"], band) >= 7, $"{id} band");
        Assert.True(ThemeColours.Contrast(theme.Colours["Muted"], body) >= 3.5, $"{id} muted on body {ThemeColours.Contrast(theme.Colours["Muted"], body):0.0}");
    }

    [Theory]
    [MemberData(nameof(Tiles))]
    public void TextReadsOnEveryTilesWashAndBandInTheDarkTheme(string id)
    {
        var theme = ThemeLibrary.Dark; var (body, band) = DeskHues.Composite(id, true, theme.Colours["Surface"]);
        Assert.True(ThemeColours.Contrast(theme.Colours["Ink"], body) >= 7, $"{id} body {ThemeColours.Contrast(theme.Colours["Ink"], body):0.0}");
        Assert.True(ThemeColours.Contrast(theme.Colours["Ink"], band) >= 7, $"{id} band {ThemeColours.Contrast(theme.Colours["Ink"], band):0.0}");
        Assert.True(ThemeColours.Contrast(theme.Colours["Muted"], body) >= 3.5, $"{id} muted on body {ThemeColours.Contrast(theme.Colours["Muted"], body):0.0}");
    }

    [Theory]
    [MemberData(nameof(Tiles))]
    public void ADarkTilesWashIsDeeperThanItsLightOneButStillItsOwnHue(string id)
    {
        var light = ThemeColours.Rgb(DeskHues.Wash(id, false)); var dark = ThemeColours.Rgb(DeskHues.Wash(id, true));
        Assert.True(dark.R + dark.G + dark.B < light.R + light.G + light.B - 60, id); // Deeper.
        Assert.True(Math.Abs(Math.Max(dark.R, Math.Max(dark.G, dark.B)) - Math.Min(dark.R, Math.Min(dark.G, dark.B))) >= 40, id); // Still coloured, not grey.
    }

    [Fact]
    public void EveryDeskTileHasAHueAndThePostItsKeepTheirPaper()
    {
        foreach (var kind in Timer.Desktop.DeskView.Kinds) Assert.Contains(kind.Id, DeskHues.Ids);
        Assert.Equal(DeskHues.Wash("postit", false), DeskHues.Wash("postit", true));
        Assert.Equal(DeskHues.Badge("working"), DeskHues.Wash("working", false)); // The badge is the pastel.
    }
}
