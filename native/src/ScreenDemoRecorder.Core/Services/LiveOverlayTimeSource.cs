namespace ScreenDemoRecorder.Core.Services;

public sealed class LiveOverlayTimeSource : IDisposable
{
    private readonly object gate = new();
    private Func<TimeSpan>? source;
    private bool disposed;

    public LiveOverlayTimeSource(Func<TimeSpan> source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public static Func<TimeSpan> Bind<T>(T owner, Func<T, TimeSpan> read)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(read);
        return () => read(owner);
    }

    public bool TryGetCurrent(out TimeSpan current)
    {
        lock (gate)
        {
            if (disposed)
            {
                current = default;
                return false;
            }

            current = source!();
            return true;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            source = null;
        }
    }
}
