using Timer.Core;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class AutoStartTests
{
    private sealed class FakeRegistry : IStartupRegistry
    {
        public readonly Dictionary<string, string> Values = new();
        public bool Fail;
        public string? Get(string name) => Values.GetValueOrDefault(name);
        public void Set(string name, string command) { if (Fail) throw new UnauthorizedAccessException("Blocked by policy."); Values[name] = command; }
        public void Remove(string name) => Values.Remove(name);
    }
    private static readonly string Exe = Path.Combine(Path.GetTempPath(), "Timer folder", "BasicsAdvancedTimer.Preview.exe");

    [Fact]
    public void TurnsOnAndOffAndQuotesThePath()
    {
        var registry = new FakeRegistry(); var setting = new AutoStartSetting(registry, Exe, null);
        Assert.True(setting.Available); Assert.False(setting.IsEnabled);
        setting.Set(true);
        Assert.Equal("\"" + Exe + "\"", registry.Values[AutoStartSetting.ValueName]); Assert.True(setting.IsEnabled);
        setting.Set(false); Assert.Empty(registry.Values); Assert.False(setting.IsEnabled);
    }

    [Fact]
    public void RemembersAnExplicitDataFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "My data");
        var registry = new FakeRegistry(); var setting = new AutoStartSetting(registry, Exe, folder);
        setting.Set(true);
        Assert.EndsWith($"--data-dir \"{Path.GetFullPath(folder)}\"", registry.Values[AutoStartSetting.ValueName]);
    }

    [Fact]
    public void AMovedProgramOrDifferentFolderCountsAsOffUntilSwitchedOnAgain()
    {
        var registry = new FakeRegistry(); new AutoStartSetting(registry, Exe, null).Set(true);
        var moved = new AutoStartSetting(registry, Path.Combine(Path.GetTempPath(), "Elsewhere", "BasicsAdvancedTimer.Preview.exe"), null);
        Assert.False(moved.IsEnabled); moved.Set(true); Assert.True(moved.IsEnabled);
        Assert.False(new AutoStartSetting(registry, moved.Command!.Trim('"'), "other").IsEnabled);
    }

    [Fact]
    public void IsNotOfferedThroughTheDotnetHostOrWithoutAProgramPath()
    {
        Assert.False(new AutoStartSetting(new FakeRegistry(), "/usr/bin/dotnet", null).Available);
        Assert.False(new AutoStartSetting(new FakeRegistry(), null, null).Available);
        Assert.Throws<InvalidOperationException>(() => new AutoStartSetting(new FakeRegistry(), null, null).Set(true));
    }

    [Fact]
    public void AFailedChangeIsReportedWithoutThrowing()
    {
        var registry = new FakeRegistry { Fail = true };
        var model = new TimerModel(new TrackingEngine()) { AutoStart = new AutoStartSetting(registry, Exe, null) };
        model.SetAutoStart(true);
        Assert.Contains("Blocked by policy", model.LastError); Assert.False(model.AutoStart.IsEnabled);
        registry.Fail = false; model.SetAutoStart(true);
        Assert.Null(model.LastError); Assert.True(model.AutoStart.IsEnabled);
    }

    [Fact]
    public void AnUpdaterInstallStartsThroughTheUpdaterSoItSurvivesUpdates()
    {
        var script = Path.Combine(Path.GetTempPath(), "Updater folder", "Update-TestBuild.ps1");
        var registry = new FakeRegistry();
        var first = new AutoStartSetting(registry, Path.Combine(Path.GetTempPath(), "versions", "run-1", "BasicsAdvancedTimer.Preview.exe"), "data", script);
        Assert.True(first.Available); first.Set(true);
        Assert.Contains("-File \"" + Path.GetFullPath(script) + "\" -Mode Launch", registry.Values[AutoStartSetting.ValueName]);
        // After an update the program lives in a different folder, but the entry still counts as on.
        var updated = new AutoStartSetting(registry, Path.Combine(Path.GetTempPath(), "versions", "run-2", "BasicsAdvancedTimer.Preview.exe"), "data", script);
        Assert.True(updated.IsEnabled);
        // Run from the dotnet host the updater route is still offered, because it does not depend on the running program.
        Assert.True(new AutoStartSetting(registry, "/usr/bin/dotnet", null, script).Available);
    }

    [Fact]
    public void OnlyAnExistingScriptWithoutQuotesIsTrustedFromTheCommandLine()
    {
        var real = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ps1"); File.WriteAllText(real, "");
        try
        {
            Assert.Equal(real, AutoStartSetting.AcceptUpdaterScript(real));
            Assert.Null(AutoStartSetting.AcceptUpdaterScript(real + "\" -Mode Evil"));
            Assert.Null(AutoStartSetting.AcceptUpdaterScript(Path.Combine(Path.GetTempPath(), "missing.ps1")));
            Assert.Null(AutoStartSetting.AcceptUpdaterScript(Path.Combine(Path.GetTempPath(), Path.GetFileName(real).Replace(".ps1", ".cmd"))));
            Assert.Null(AutoStartSetting.AcceptUpdaterScript(null));
        }
        finally { File.Delete(real); }
    }
}
