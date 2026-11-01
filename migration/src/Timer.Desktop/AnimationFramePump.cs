namespace Timer.Desktop;

/// <summary>UI-thread animation scheduling with one outstanding native frame request, even across cancellation and watchdog recovery.</summary>
public sealed class AnimationFramePump(Action<Action<TimeSpan>> requestFrame, Func<double> milliseconds)
{
    private bool pending;
    private double started, lastStep, duration;
    private Action<double>? step;
    private Action? completed;
    public bool Running { get; private set; }

    public void Start(double length, Action<double> update, Action finish)
    {
        Stop();
        duration = length; step = update; completed = finish;
        started = lastStep = milliseconds(); Running = true;
        QueueFrame();
    }

    public void Stop() { Running = false; step = null; completed = null; }

    private void QueueFrame()
    {
        if (pending || !Running) return;
        pending = true;
        requestFrame(_ =>
        {
            pending = false;
            Advance();
            QueueFrame();
        });
    }

    /// <summary>Advance a stalled animation without adding another frame request behind the one still waiting.</summary>
    public void Watchdog()
    {
        if (Running && milliseconds() - lastStep >= 50) Advance();
    }

    private void Advance()
    {
        if (!Running) return;
        lastStep = milliseconds();
        var elapsed = lastStep - started;
        var update = step;
        if (elapsed >= duration)
        {
            var finish = completed;
            Stop();
            update?.Invoke(duration);
            finish?.Invoke();
        }
        else update?.Invoke(elapsed);
    }
}
