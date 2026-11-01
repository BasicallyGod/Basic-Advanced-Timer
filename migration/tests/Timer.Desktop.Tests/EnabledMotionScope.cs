namespace Timer.Desktop.Tests;

// A visual animation test must choose its preference rather than inherit the runner's Windows setting.
internal sealed class EnabledMotionScope : IDisposable
{
    private readonly bool? previous = Motion.Override;
    public EnabledMotionScope() => Motion.Override = false;
    public void Dispose() => Motion.Override = previous;
}
