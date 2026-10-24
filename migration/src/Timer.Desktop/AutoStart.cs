namespace Timer.Desktop;

/// <summary>The "Start with Windows" switch, however the platform keeps it: the sign-in registry entry for the portable build, or the manifest's startup task for a Store package.</summary>
public interface IAutoStart
{
    bool Available { get; }
    bool IsEnabled { get; }
    void Set(bool enabled);
}

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
public sealed class AutoStartSetting : IAutoStart
{
    // Development channels can start independently without overwriting one another's Run-key entry.
#if ALPHA_BUILD
    public const string ValueName = "BasicsAdvancedTimerAlpha";
#elif EXPERIMENTAL_BUILD
    public const string ValueName = "BasicsAdvancedTimerExperimental";
#else
    public const string ValueName = "BasicsAdvancedTimer";
#endif
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

// What Windows says about a packaged app's startup task. A person can switch the task off in Windows Settings, and then the app may not switch it back on.
public enum StartupState { Disabled, DisabledByUser, DisabledByPolicy, Enabled, EnabledByPolicy }

public interface IStartupTaskHost
{
    StartupState GetState();
    StartupState RequestEnable();
    void Disable();
}

// Start with Windows for the Store package, where the registry entry would be virtualised and never run. The task itself is declared in the manifest.
public sealed class StartupTaskSetting : IAutoStart
{
    private readonly IStartupTaskHost host;
    public StartupTaskSetting(IStartupTaskHost host) { this.host = host; }
    public bool Available => true;
    public bool IsEnabled => host.GetState() is StartupState.Enabled or StartupState.EnabledByPolicy;
    public void Set(bool enabled)
    {
        var state = host.GetState();
        if (enabled)
        {
            if (state is StartupState.Enabled or StartupState.EnabledByPolicy) return;
            if (state == StartupState.DisabledByPolicy) throw new InvalidOperationException("Blocked by your organisation");
            if (state == StartupState.DisabledByUser) throw new InvalidOperationException("It is switched off in Windows. Turn it on under Settings, Apps, Startup.");
            if (host.RequestEnable() is not (StartupState.Enabled or StartupState.EnabledByPolicy))
                throw new InvalidOperationException("Windows did not allow it. Turn it on under Settings, Apps, Startup.");
        }
        else
        {
            if (state == StartupState.EnabledByPolicy) throw new InvalidOperationException("Set by your organisation");
            if (state == StartupState.Enabled) host.Disable();
        }
    }
}

public static class PackagedStartup
{
    /// <summary>The Windows startup task for the Store package, or null in builds that are not Store packages.</summary>
    public static IAutoStart? TryCreate()
#if STORE_PACKAGE
        => new StartupTaskSetting(new WinRtStartupTaskHost());
#else
        => null;
#endif
}

#if STORE_PACKAGE
// Talks to the Windows startup task named in AppxManifest.template.xml. The calls are quick, so they wait for the answer instead of making the menu asynchronous.
internal sealed class WinRtStartupTaskHost : IStartupTaskHost
{
    private const string TaskId = "BasicsAdvancedTimerStartup";
    private static global::Windows.ApplicationModel.StartupTask Task()
        => global::Windows.ApplicationModel.StartupTask.GetAsync(TaskId).AsTask().GetAwaiter().GetResult();
    private static StartupState Map(global::Windows.ApplicationModel.StartupTaskState state) => state switch
    {
        global::Windows.ApplicationModel.StartupTaskState.Enabled => StartupState.Enabled,
        global::Windows.ApplicationModel.StartupTaskState.EnabledByPolicy => StartupState.EnabledByPolicy,
        global::Windows.ApplicationModel.StartupTaskState.DisabledByUser => StartupState.DisabledByUser,
        global::Windows.ApplicationModel.StartupTaskState.DisabledByPolicy => StartupState.DisabledByPolicy,
        _ => StartupState.Disabled
    };
    public StartupState GetState() => Map(Task().State);
    public StartupState RequestEnable() => Map(Task().RequestEnableAsync().AsTask().GetAwaiter().GetResult());
    public void Disable() => Task().Disable();
}
#endif
