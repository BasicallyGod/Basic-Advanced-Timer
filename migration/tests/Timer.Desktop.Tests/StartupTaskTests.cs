using Timer.Desktop;
using Xunit;

namespace Timer.Desktop.Tests;

public sealed class StartupTaskTests
{
    private sealed class FakeHost : IStartupTaskHost
    {
        public StartupState State; public StartupState AfterRequest = StartupState.Enabled; public int Requests, Disables;
        public StartupState GetState() => State;
        public StartupState RequestEnable() { Requests++; State = AfterRequest; return State; }
        public void Disable() { Disables++; State = StartupState.Disabled; }
    }

    [Fact]
    public void TurnsTheTaskOnAndOffThroughWindows()
    {
        var host = new FakeHost(); var setting = new StartupTaskSetting(host);
        Assert.True(setting.Available); Assert.False(setting.IsEnabled);
        setting.Set(true); Assert.True(setting.IsEnabled); Assert.Equal(1, host.Requests);
        setting.Set(true); Assert.Equal(1, host.Requests); // Already on, so Windows is not asked again.
        setting.Set(false); Assert.False(setting.IsEnabled); Assert.Equal(1, host.Disables);
        setting.Set(false); Assert.Equal(1, host.Disables);
    }

    [Fact]
    public void ExplainsWhenWindowsOrPolicyHasTheFinalSay()
    {
        var host = new FakeHost { State = StartupState.DisabledByUser }; var setting = new StartupTaskSetting(host);
        Assert.Contains("Settings, Apps, Startup", Assert.Throws<InvalidOperationException>(() => setting.Set(true)).Message);
        host.State = StartupState.DisabledByPolicy;
        Assert.Contains("organisation", Assert.Throws<InvalidOperationException>(() => setting.Set(true)).Message);
        host.State = StartupState.EnabledByPolicy;
        Assert.True(setting.IsEnabled); Assert.Throws<InvalidOperationException>(() => setting.Set(false)); Assert.Equal(0, host.Disables);
        host.State = StartupState.Disabled; host.AfterRequest = StartupState.DisabledByUser;
        Assert.Throws<InvalidOperationException>(() => setting.Set(true)); Assert.False(setting.IsEnabled);
    }

    [Fact]
    public void ARefusalShowsAsAMessageInsteadOfCrashing()
    {
        var model = Timer.Desktop.TimerModel.CreateDemo(); model.AutoStart = new StartupTaskSetting(new FakeHost { State = StartupState.DisabledByUser });
        model.SetAutoStart(true);
        Assert.Contains("Could not change starting with Windows", model.LastError); Assert.Contains("Settings, Apps, Startup", model.LastError);
    }

    [Fact]
    public void OrdinaryBuildsDoNotOfferTheStoreStartupTask() => Assert.Null(PackagedStartup.TryCreate());
}
