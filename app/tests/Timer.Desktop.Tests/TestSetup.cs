using System.Runtime.CompilerServices;

namespace Timer.Desktop.Tests;

internal static class TestSetup
{
    // Nothing ticks in the headless tests, so the pill's drawer must not wait on its slide to reach its place.
    [ModuleInitializer]
    internal static void Init() { WindowMorph.Enabled = false; }
}
