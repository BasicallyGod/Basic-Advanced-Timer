using System.Reflection;

namespace Timer.Desktop;

// Which build is running, so you can tell at a glance whether an update arrived. The workflow stamps each published build with
// its run number and commit, as 0.1.<run>+<commit>; a build made on a developer's machine carries no stamp and says so.
public static class BuildInfo
{
    public static string Describe(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return "Version: development build";
        var parts = informationalVersion.Split('+', 2);
        var number = parts[0].Trim();
        // 1.0.0 is what a build gets when nobody stamped it.
        if (number is "" or "1.0.0" or "1.0.0.0") return "Version: development build";
        var commit = parts.Length == 2 && parts[1].Trim().Length >= 7 ? " (" + parts[1].Trim()[..7] + ")" : "";
        return "Version " + number + commit;
    }
    /// <summary>
    /// Lets a test behave as the Store build does, since the tests themselves are built without the Store flag. Nothing in the app sets it.
    /// </summary>
    public static bool? DeveloperFeaturesOverride { get; set; }
    /// <summary>Whether this build offers the developer features at all. A Store package never does, whatever the settings file says.</summary>
    public static bool DeveloperFeatures => DeveloperFeaturesOverride ?? CompiledDeveloperFeatures;
    private static bool CompiledDeveloperFeatures =>
#if STORE_PACKAGE
        false;
#else
        true;
#endif

    public static string Current
    {
        get
        {
            var version = Describe(typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
#if ALPHA_BUILD
            return version + " — Alpha";
#elif EXPERIMENTAL_BUILD
            return version + " — Experimental";
#else
            return version;
#endif
        }
    }
}
