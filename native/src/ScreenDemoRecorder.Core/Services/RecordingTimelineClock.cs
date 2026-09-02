namespace ScreenDemoRecorder.Core.Services;

public sealed class RecordingTimelineClock(TimeProvider? timeProvider = null)
{
    private readonly object sync = new();
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private long activeTicks;
    private long startedAt;
    private bool running;
    private bool stopped;

    public TimeSpan Elapsed
    {
        get
        {
            lock (sync)
            {
                return TimeSpan.FromTicks(CurrentTicks());
            }
        }
    }

    public bool IsRunning
    {
        get { lock (sync) return running; }
    }

    public TimeSpan Start()
    {
        lock (sync)
        {
            if (stopped) throw new InvalidOperationException("A stopped recording clock cannot be restarted.");
            if (!running)
            {
                startedAt = timeProvider.GetTimestamp();
                running = true;
            }
            return TimeSpan.FromTicks(CurrentTicks());
        }
    }

    public TimeSpan Pause()
    {
        lock (sync)
        {
            if (running)
            {
                activeTicks = CurrentTicks();
                running = false;
            }
            return TimeSpan.FromTicks(activeTicks);
        }
    }

    public TimeSpan Resume() => Start();

    public TimeSpan Stop()
    {
        lock (sync)
        {
            if (running) activeTicks = CurrentTicks();
            running = false;
            stopped = true;
            return TimeSpan.FromTicks(activeTicks);
        }
    }

    private long CurrentTicks()
    {
        if (!running) return activeTicks;
        return checked(activeTicks + timeProvider.GetElapsedTime(startedAt, timeProvider.GetTimestamp()).Ticks);
    }
}
