using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Timer.Core;
using Timer.Storage;
using Xunit;

namespace Timer.Desktop.Tests;

// Sunset to sunrise: the sun's times, the theme that follows them, and its choices in Settings.
public sealed class SunThemeTests
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.CreateCustomTimeZone("L", TimeSpan.FromHours(1), "L", "L"); // British summer time, fixed so the tests do not depend on this computer.
    private static readonly TimeZoneInfo LondonWinter = TimeZoneInfo.CreateCustomTimeZone("LW", TimeSpan.Zero, "LW", "LW");
    private static readonly TimeZoneInfo Sydney = TimeZoneInfo.CreateCustomTimeZone("S", TimeSpan.FromHours(11), "S", "S");

    private static void Near(TimeSpan expected, DateTime? actual) { Assert.NotNull(actual); Assert.True(Math.Abs((actual!.Value.TimeOfDay - expected).TotalMinutes) <= 4, $"{actual:HH:mm} is not within four minutes of {expected:hh\\:mm}"); }

    [Fact]
    public void SunriseAndSunsetMatchTheAlmanacToWithinAFewMinutes()
    {
        var london = SunTimes.Find("London")!;
        var summer = SunTimes.On(new DateTime(2026, 6, 21), london, London); Near(new TimeSpan(4, 43, 0), summer.Rise); Near(new TimeSpan(21, 21, 0), summer.Set);
        var winter = SunTimes.On(new DateTime(2026, 12, 21), london, LondonWinter); Near(new TimeSpan(8, 4, 0), winter.Rise); Near(new TimeSpan(15, 53, 0), winter.Set);
        var sydney = SunTimes.On(new DateTime(2026, 12, 21), SunTimes.Find("Sydney")!, Sydney); Near(new TimeSpan(5, 41, 0), sydney.Rise); Near(new TimeSpan(20, 5, 0), sydney.Set); // The far side of the date line's zone: the event is on the previous UTC day.
    }

    [Fact]
    public void WhereTheSunDoesNotRiseOrSetTheDayHasNoTimes()
    {
        var pole = new SunCity("Pole", 85, 0, 0);
        Assert.Equal((null, null), SunTimes.On(new DateTime(2026, 6, 21), pole, London)); // Midnight sun.
        Assert.Equal((null, null), SunTimes.On(new DateTime(2026, 12, 21), pole, London)); // Polar night.
    }

    [Fact]
    public void EveryCityIsOnEarthAndNamedOnce()
    {
        Assert.Equal(SunTimes.Cities.Count, SunTimes.Cities.Select(c => c.Name).Distinct().Count());
        Assert.All(SunTimes.Cities, c => { Assert.InRange(c.Latitude, -66, 66); Assert.InRange(c.Longitude, -180, 180); });
    }

    [Fact]
    public void ThePlaceIsGuessedFromTheTimeZoneUntilOneIsChosen()
    {
        Assert.Equal("London", SunTimes.Guess(TimeZoneInfo.CreateCustomTimeZone("UTC", TimeSpan.Zero, "UTC", "UTC")).Name);
        Assert.Equal(9, SunTimes.Guess(TimeZoneInfo.CreateCustomTimeZone("JST", TimeSpan.FromHours(9), "JST", "JST")).StandardOffset);
    }

    private static TimerModel Model(DateTime now, TimeZoneInfo zone)
    {
        var model = new TimerModel(new TrackingEngine(clock: () => now), new StoreSettings(new() { new("Account one") }, false, "Light", DayOfWeek.Monday)); model.CompleteSetup();
        model.SunZone = zone; return model;
    }

    [Fact]
    public void SunsetIsAChoiceThatSurvivesResolvingASavedTheme() => Assert.Equal(AppThemes.Sun, AppThemes.Resolve("Sun", null));

    [Fact]
    public void TheThemeIsLightByDayAndDarkByNightFollowingTheSun()
    {
        var day = new DateTime(2026, 6, 21);
        foreach (var (hour, dark) in new[] { (3.0, true), (5.5, false), (12.0, false), (20.5, false), (22.0, true), (23.5, true) })
        {
            var model = Model(day.AddHours(hour), London); model.SetSunPlace("London"); model.SetTheme(AppThemes.Sun);
            Assert.Equal(dark, model.CurrentTheme.Dark); Assert.Equal(dark, model.SunIsDark);
        }
        var winter = Model(new DateTime(2026, 12, 21, 16, 30, 0), LondonWinter); winter.SetSunPlace("London"); winter.SetTheme(AppThemes.Sun);
        Assert.True(winter.CurrentTheme.Dark); // Dark by half past four in December, though the same hour is bright in June.
    }

    [Fact]
    public void HandSetTimesDecideWhenTheSunHasNoSayOrIsNotWanted()
    {
        var day = new DateTime(2026, 6, 21);
        var model = Model(day.AddHours(19.5), London); model.SetSunPlace(SunTimes.ByHand); model.SetTheme(AppThemes.Sun);
        Assert.Equal((new TimeSpan(19, 0, 0), new TimeSpan(7, 0, 0)), model.SunHandTimes); Assert.True(model.CurrentTheme.Dark);
        model.SetSunHandTimes(new TimeSpan(21, 0, 0), new TimeSpan(6, 30, 0));
        Assert.Equal((new TimeSpan(21, 0, 0), new TimeSpan(6, 30, 0)), model.SunHandTimes); Assert.False(model.CurrentTheme.Dark);
        Assert.Equal((new TimeSpan(6, 30, 0), new TimeSpan(21, 0, 0)), model.SunDay(day));
    }

    [Fact]
    public void TheChoiceIsKeptAmongTheAppearanceLines()
    {
        var model = Model(new DateTime(2026, 6, 21, 12, 0, 0), London); model.SetTheme(AppThemes.Sun); model.SetSunPlace("Sydney"); model.SetSunHandTimes(new TimeSpan(20, 15, 0), new TimeSpan(6, 45, 0));
        Assert.Equal("Sydney", model.SunPlace); Assert.Equal(AppThemes.Sun, model.Theme);
        model.SetSunPlace("Nowhere"); Assert.NotEqual("Nowhere", model.SunPlace); // A name the list does not have is ignored, not trusted.
    }

    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Settle(Window w) { for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); using var frame = w.CaptureRenderedFrame(); } Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, string name) { Find<Button>(window, name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle(window); }

    [AvaloniaFact]
    public void SettingsOffersSunsetWithAPlaceThatShowsTodaysTimes()
    {
        var model = Model(new DateTime(2026, 6, 21, 12, 0, 0), London); var window = new DashboardWindow(model); window.Show(); Settle(window);
        try
        {
            Click(window, "Settings");
            Assert.False(Find<StackPanel>(window, "SunOptions").IsVisible); // Nothing extra until Sunset is chosen.
            Click(window, "ThemeSunset");
            Assert.Equal(AppThemes.Sun, model.Theme); Assert.True(Find<StackPanel>(window, "SunOptions").IsVisible);
            var place = Find<ComboBox>(window, "SunPlace"); place.SelectedItem = "London"; Settle(window);
            Assert.Equal("London", model.SunPlace); Assert.Contains("light from 04:", Find<TextBlock>(window, "SunSummary").Text);
            Assert.False(Find<StackPanel>(window, "SunHandTimes").IsVisible);
            place.SelectedItem = "Set times by hand"; Settle(window);
            Assert.Equal(SunTimes.ByHand, model.SunPlace); Assert.True(Find<StackPanel>(window, "SunHandTimes").IsVisible);
            var dark = Find<TextBox>(window, "SunDarkFrom"); dark.Text = "20:30"; dark.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(InputElement.LostFocusEvent)); Settle(window);
            Assert.Equal(new TimeSpan(20, 30, 0), model.SunHandTimes.DarkFrom);
            Click(window, "ThemeLight"); Assert.False(Find<StackPanel>(window, "SunOptions").IsVisible);
        }
        finally { window.Close(); }
    }
}
