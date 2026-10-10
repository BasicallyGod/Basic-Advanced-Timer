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
    public static string Current => Describe(typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}
