using System.IO;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace ScreenDemoRecorder.Capture;

internal sealed class CpuIntermediateRecording
{
    private readonly object sync = new();
    private readonly GraphicsCaptureItem item;
    private readonly PixelRect area;
    private readonly string outputPath;
    private readonly double frameRate;
    private readonly bool showCursor;
    private readonly Func<string?>? validateSource;
    private readonly RecordingTimelineClock clock = new();
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource changed = NewSignal();
    private Direct3D11CaptureFrame? latest;
    private Exception? failure;
    private bool stopped;
    private bool paused;

    public Task<bool> Ready => ready.Task;

    public Task Completion { get; }

    public TimeSpan Elapsed => clock.Elapsed;

    public bool IsPaused { get { lock (sync) return paused; } }

    public CpuIntermediateRecording(GraphicsCaptureItem captureItem, PixelRect region, string cleanVideoPath,
        double fps, bool captureCursor, Func<string?>? sourceValidation = null)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows screen capture is unavailable on this device.");
        ArgumentNullException.ThrowIfNull(captureItem);
        ArgumentException.ThrowIfNullOrWhiteSpace(cleanVideoPath);
        if (!double.IsFinite(fps) || fps is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(fps));
        if (region.X < 0 || region.Y < 0 || region.Width < 1 || region.Height < 1 ||
            region.Right > captureItem.Size.Width || region.Bottom > captureItem.Size.Height)
            throw new ArgumentException("The capture region no longer fits. Select the area again.");
        item = captureItem;
        area = region;
        outputPath = Path.GetFullPath(cleanVideoPath);
        frameRate = fps;
        showCursor = captureCursor;
        validateSource = sourceValidation;
        Completion = Task.Run(RecordAsync);
    }

    public void TogglePause()
    {
        lock (sync)
        {
            if (stopped || !ready.Task.IsCompletedSuccessfully || !ready.Task.Result) return;
            paused = !paused;
            if (paused) clock.Pause(); else clock.Resume();
            Pulse();
        }
    }

    public void Stop()
    {
        lock (sync)
        {
            if (stopped) return;
            stopped = true;
            clock.Stop();
            ready.TrySetResult(false);
            Pulse();
        }
    }

    private async Task RecordAsync()
    {
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;
        try
        {
            CreateGraphicsDevice(out device, out context);
            using var multithread = device.QueryInterface<ID3D11Multithread>();
            multithread.SetMultithreadProtected(true);
            using var captureDevice = GraphicsInterop.Wrap(device);
            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(captureDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            using var session = pool.CreateCaptureSession(item);
            using var reader = new StagingFrameReader(device, context);
            session.IsCursorCaptureEnabled = showCursor;
            pool.FrameArrived += FrameArrived;
            item.Closed += ItemClosed;
            try
            {
                session.StartCapture();
                if (!await WaitForFirstFrameAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false)) return;
                await using var encoder = new FfmpegLosslessEncoder(FfmpegRuntime.RequireExecutable(), outputPath,
                    area.Width, area.Height, frameRate);
                lock (sync)
                {
                    if (stopped) return;
                    clock.Start();
                    ready.TrySetResult(true);
                }
                await SampleFramesAsync(reader, encoder).ConfigureAwait(false);
                await encoder.CompleteAsync().ConfigureAwait(false);
            }
            finally
            {
                pool.FrameArrived -= FrameArrived;
                item.Closed -= ItemClosed;
            }
            if (failure is not null) throw new InvalidOperationException(failure.Message, failure);
        }
        finally
        {
            Stop();
            lock (sync)
            {
                latest?.Dispose();
                latest = null;
            }
            context?.Dispose();
            device?.Dispose();
        }
    }

    private async Task SampleFramesAsync(StagingFrameReader reader, FfmpegLosslessEncoder encoder)
    {
        var period = (long)Math.Round(TimeSpan.TicksPerSecond / frameRate);
        long nextFrameTicks = 0;
        while (true)
        {
            if (validateSource?.Invoke() is { } problem) throw new InvalidOperationException(problem);
            Task signal;
            TimeSpan delay;
            lock (sync)
            {
                if (stopped) return;
                if (failure is not null) throw new InvalidOperationException(failure.Message, failure);
                signal = changed.Task;
                delay = paused ? Timeout.InfiniteTimeSpan : TimeSpan.FromTicks(Math.Max(0, nextFrameTicks - clock.Elapsed.Ticks));
            }
            if (delay == Timeout.InfiniteTimeSpan)
            {
                await signal.ConfigureAwait(false);
                continue;
            }
            if (delay > TimeSpan.Zero)
            {
                await Task.WhenAny(signal, Task.Delay(delay)).ConfigureAwait(false);
                continue;
            }

            lock (sync)
            {
                if (stopped) return;
                if (latest is null) continue;
                using var input = GraphicsInterop.Unwrap(latest.Surface);
                reader.CopyFrom(input, area);
            }
            var frame = reader.ReadMapped(TimeSpan.FromTicks(nextFrameTicks));
            await encoder.WriteAsync(frame).ConfigureAwait(false);
            nextFrameTicks = checked(nextFrameTicks + period);
        }
    }

    private async Task<bool> WaitForFirstFrameAsync()
    {
        while (true)
        {
            Task signal;
            lock (sync)
            {
                if (stopped) return false;
                if (failure is not null) throw new InvalidOperationException(failure.Message, failure);
                if (latest is not null) return true;
                signal = changed.Task;
            }
            await signal.ConfigureAwait(false);
        }
    }

    private void FrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        lock (sync)
        {
            if (stopped) return;
            try
            {
                var frame = sender.TryGetNextFrame();
                if (frame is null) return;
                if (frame.ContentSize.Width != item.Size.Width || frame.ContentSize.Height != item.Size.Height)
                {
                    frame.Dispose();
                    throw new InvalidOperationException("The capture source changed size. Start a new recording.");
                }
                latest?.Dispose();
                latest = frame;
                Pulse();
            }
            catch (Exception error)
            {
                failure ??= error;
                Pulse();
            }
        }
    }

    private void ItemClosed(GraphicsCaptureItem sender, object args)
    {
        lock (sync)
        {
            failure ??= new InvalidOperationException("The capture source was closed or disconnected.");
            Pulse();
        }
    }

    private static void CreateGraphicsDevice(out ID3D11Device device, out ID3D11DeviceContext context)
    {
        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];
        var result = D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            levels, out device, out context);
        if (result.Failure)
            result = D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport,
                levels, out device, out context);
        result.CheckError();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Pulse()
    {
        var previous = changed;
        changed = NewSignal();
        previous.TrySetResult();
    }
}
