using Timer.Core;
using Timer.Desktop;
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
}
