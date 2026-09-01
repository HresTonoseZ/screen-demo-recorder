using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Core.Services;

public sealed class GifExportPlan
{
    private readonly long durationTicks;
    private readonly double periodTicks;
    private readonly int finalHoldMilliseconds;
    public int Width { get; }
    public int Height { get; }
    public int FrameCount { get; }

    public GifExportPlan(int sourceWidth, int sourceHeight, TimeSpan duration, CaptureSettings capture, OutputSettings output)
    {
        if (sourceWidth < 1 || sourceHeight < 1) throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if (!double.IsFinite(capture.GifFps) || capture.GifFps < 1 || capture.GifFps > 60 || output.GifFrameStep is < 1 or > 30)
            throw new ArgumentException("GIF frame rate must be between 1 and 60 fps, with a frame step from 1 to 30.");
        if (output.Width is < 64 or > 7680) throw new ArgumentException("GIF width must be between 64 and 7680 pixels.");
        Width = output.Width;
        Height = (int)Math.Max(1, Math.Round(sourceHeight * (double)Width / sourceWidth));
        if (Height > 7680) throw new ArgumentException("The GIF would be taller than 7680 pixels. Choose a smaller output width.");
        durationTicks = duration.Ticks;
        periodTicks = TimeSpan.TicksPerSecond * (double)output.GifFrameStep / capture.GifFps;
        FrameCount = Math.Max(1, checked((int)Math.Ceiling(durationTicks / periodTicks - 1e-9)));
        finalHoldMilliseconds = Math.Clamp(output.FinalFrameDurationMilliseconds, 0, 60_000);
    }

    public long StartTicks(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, FrameCount);
        return (long)Math.Round(index * periodTicks);
    }

    public ushort DelayCentiseconds(int index)
    {
        var start = StartTicks(index);
        if (index == FrameCount - 1 && finalHoldMilliseconds > 0)
            return (ushort)Math.Max(1, Math.Round(finalHoldMilliseconds / 10.0));
        var end = index == FrameCount - 1 ? durationTicks : StartTicks(index + 1);
        // Round the timeline, not each period, so fractional FPS does not accumulate drift.
        var delay = Math.Round(end / (double)(TimeSpan.TicksPerMillisecond * 10)) -
                    Math.Round(start / (double)(TimeSpan.TicksPerMillisecond * 10));
        return (ushort)Math.Clamp(delay, 1, ushort.MaxValue);
    }
}
