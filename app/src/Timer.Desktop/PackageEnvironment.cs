using System.Runtime.InteropServices;

namespace Timer.Desktop;

// The portable build remains portable; only an installed MSIX uses per-user app data.
// Do not infer packaging from the build configuration: a published EXE can be run unpackaged.
public static class PackageEnvironment
{
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    public static bool IsPackaged()
    {
        if (!OperatingSystem.IsWindows()) return false;
        uint length = 0;
        var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
        return result switch
        {
            ErrorInsufficientBuffer => true,
            AppModelErrorNoPackage => false,
            _ => false // Treat unexpected package-query failures as the regular desktop process.
        };
    }
}

public static class TimerDataFolders
{
    public static string DefaultDirectory(string executableDirectory, string localAppData, bool packaged)
    {
        if (packaged)
        {
            if (string.IsNullOrWhiteSpace(localAppData))
                throw new InvalidOperationException("Windows did not provide a per-user application data folder.");
            return Path.Combine(localAppData, "BasicsAdvancedTimer");
        }
        return Path.Combine(executableDirectory, "migration-data");
    }
}
