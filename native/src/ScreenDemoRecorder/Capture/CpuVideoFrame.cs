using System.Buffers;

namespace ScreenDemoRecorder.Capture;

internal sealed class CpuVideoFrame : IDisposable
{
    private byte[]? buffer;

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public TimeSpan Timestamp { get; }

    public Memory<byte> Pixels => (buffer ?? throw new ObjectDisposedException(nameof(CpuVideoFrame))).AsMemory(0, Stride * Height);

    internal byte[] Buffer => buffer ?? throw new ObjectDisposedException(nameof(CpuVideoFrame));

    public CpuVideoFrame(int width, int height, TimeSpan timestamp)
    {
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        Stride = checked(width * 4);
        Timestamp = timestamp;
        buffer = ArrayPool<byte>.Shared.Rent(checked(Stride * height));
    }

    public void Dispose()
    {
        var rented = Interlocked.Exchange(ref buffer, null);
        if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
    }
}
