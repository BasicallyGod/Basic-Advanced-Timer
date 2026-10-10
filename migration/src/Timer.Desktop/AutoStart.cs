namespace Timer.Desktop;

// Where "start with Windows" is kept. The per-user Run key needs no administrator rights and no installer.
public interface IStartupRegistry
{
    string? Get(string name);
    void Set(string name, string command);
    void Remove(string name);
}

public sealed class WindowsRunKey : IStartupRegistry
{
    private const string Path = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool Available => OperatingSystem.IsWindows();
    public string? Get(string name)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Path);
        return key?.GetValue(name) as string;
    }
    public void Set(string name, string command)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Starting with Windows is only available on Windows.");
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Path);
        key.SetValue(name, command);
    }
    public void Remove(string name)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Path, true);
        key?.DeleteValue(name, false);
    }
}

// Starts the timer when the user signs in. It remembers an explicit data folder, so a copied-data test setup restarts on
// the same data. It counts as on only while the saved command is for the program that is running now, so moving the
// folder shows it as off until it is switched on again.
public sealed class AutoStartSetting
{
    public const string ValueName = "BasicsAdvancedTimer";
    private readonly IStartupRegistry registry;
    private readonly string? executable, dataFolder, updaterScript;
    // updaterScript is set when the updater started this build. Each update lives in its own folder, so the sign-in entry points at the
    // updater's launch mode instead, which always starts whichever build is current.
    public AutoStartSetting(IStartupRegistry registry, string? executable, string? dataFolder, string? updaterScript = null)
    { this.registry = registry; this.executable = executable; this.dataFolder = dataFolder; this.updaterScript = updaterScript; }
    // Not offered when run through the dotnet host (a development run), because that would start a different program.
    public bool Available => updaterScript is not null || (executable is not null
        && !System.IO.Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase));
    public string? Command => updaterScript is not null ? BuildUpdaterLaunch(updaterScript) : Available ? Build(executable!, dataFolder) : null;
    public static string BuildUpdaterLaunch(string script)
        => "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + System.IO.Path.GetFullPath(script) + "\" -Mode Launch";
    /// <summary>The updater passes its own script path; anything that is not an existing .ps1 file without quotes is ignored.</summary>
    public static string? AcceptUpdaterScript(string? path)
        => path is not null && !path.Contains('"') && path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(path) ? path : null;
    public static string Build(string executable, string? dataFolder)
        => "\"" + executable + "\"" + (dataFolder is null ? "" : " --data-dir \"" + System.IO.Path.GetFullPath(dataFolder) + "\"");
    public bool IsEnabled => Command is { } expected && string.Equals(registry.Get(ValueName), expected, StringComparison.OrdinalIgnoreCase);
    public void Set(bool enabled)
    {
        if (!Available) throw new InvalidOperationException("Starting with Windows is not available in this run.");
        if (enabled) registry.Set(ValueName, Command!); else registry.Remove(ValueName);
    }
}
