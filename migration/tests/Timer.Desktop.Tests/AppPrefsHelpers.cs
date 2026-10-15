using Timer.Desk;
using Timer.Storage;

namespace Timer.Desktop.Tests;

/// <summary>The desk app's settings live apart from the library's, so tests set and read them through <see cref="AppPrefs"/>.</summary>
internal static class AppPrefsHelpers
{
    public static StoreSettings WithApp(this StoreSettings settings, AppPrefs app) => app.Into(settings);
    public static AppPrefs App(this StoreSettings settings) => AppPrefs.From(settings);
    public static StoreSettings WithApp(this StoreSettings settings, Func<AppPrefs, AppPrefs> change) => change(AppPrefs.From(settings)).Into(settings);
}
