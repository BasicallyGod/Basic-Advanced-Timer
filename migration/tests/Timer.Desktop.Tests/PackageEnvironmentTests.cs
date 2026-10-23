using Xunit;

namespace Timer.Desktop.Tests;

public sealed class PackageEnvironmentTests
{
    [Fact]
    public void PortableBuildKeepsExistingDataBesideExecutable()
    {
        var folder = TimerDataFolders.DefaultDirectory(Path.Combine("apps", "timer"), Path.Combine("users", "profile"), false);
        Assert.Equal(Path.Combine("apps", "timer", "migration-data"), folder);
    }

    [Fact]
    public void PackagedBuildNeverWritesInsideItsInstallDirectory()
    {
        var install = Path.Combine("Program Files", "WindowsApps", "BasicsAdvancedTimer");
        var profile = Path.Combine("users", "profile", "AppData", "Local");
        var folder = TimerDataFolders.DefaultDirectory(install, profile, true);
        Assert.Equal(Path.Combine(profile, "BasicsAdvancedTimer"), folder);
        Assert.False(folder.StartsWith(install, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PackagedBuildRequiresAValidProfileDirectory()
    {
        Assert.Throws<InvalidOperationException>(() => TimerDataFolders.DefaultDirectory("installed", "", true));
    }
}
