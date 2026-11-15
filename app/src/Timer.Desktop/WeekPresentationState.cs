namespace Timer.Desktop;

/// <summary>Session presentation only; rebuilt Week instruments retain the trace and account window.</summary>
public sealed class WeekPresentationState
{
    public string? TracedAccount { get; set; }
    public int Scroll { get; set; }
    public bool ShowQuiet { get; set; }
}
