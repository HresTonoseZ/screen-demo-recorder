using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Overlays;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

namespace ScreenDemoRecorder.Capture;

internal sealed class Mp4Recording
{
    private sealed record PreparedEncoder(MediaTranscoder Transcoder, PrepareTranscodeResult Preparation);
    private readonly object sync = new();
    private readonly GraphicsCaptureItem item;
    private readonly PixelRect area;
    private readonly int expectedWidth;
    private readonly int expectedHeight;
    private readonly Func<string?>? validateSource;
    private readonly RecorderProfile profile;
    private readonly Mp4OutputPlan outputPlan;
    private readonly double fps;
    private readonly Stopwatch clock = new();
    private readonly TaskCompletionSource<bool> frameReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource changed = NewSignal();
    private readonly CancellationTokenSource abort = new();
    private Direct3D11CaptureFrame? latest;
    private ID3D11Device? device;
    private ID3D11DeviceContext? context;
    private ID3D11Query? frameCompletionQuery;
    private bool stopped, paused, discarded, finished;
    private Exception? failure;
    private long nextFrameTicks;
    private readonly LabelRaster? label;
    private OverlayCompositor? compositor;
    private DynamicOverlayCompositor? dynamicCompositor;
    private FrameScaler? scaler;
    private readonly KeystrokeRenderer? keyRenderer;
    private readonly ClickRenderer? clickRenderer;
    private readonly KeystrokeFilter keyFilter;
    private readonly KeystrokeTimeline keyTimeline;
    private readonly ClickTimeline clickTimeline;
    private readonly bool captureKeyboard;
    private readonly bool captureMouse;
    private readonly Func<PixelPoint, PixelPoint?>? mapScreenPoint;
    private readonly Channel<(KeyChord Chord, TimeSpan Time)> pendingKeys = Channel.CreateBounded<(KeyChord, TimeSpan)>(
        new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Channel<(PixelPoint Position, MouseClickButton Button, TimeSpan Time)> pendingClicks =
        Channel.CreateBounded<(PixelPoint, MouseClickButton, TimeSpan)>(
            new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private KeyboardCapture? keyboard;
    private MouseClickCapture? mouse;
    private volatile bool acceptsKeys;
    private volatile bool acceptsClicks;
    private volatile bool usesSoftwareEncoder;

    public Task<bool> Ready => ready.Task;
    public Task<string?> Completion { get; }
    public TimeSpan Elapsed { get { lock (sync) return clock.Elapsed; } }
    public bool IsPaused { get { lock (sync) return paused; } }
    public bool IsStopped { get { lock (sync) return stopped; } }
    public bool UsesSoftwareEncoder => usesSoftwareEncoder;

    public Mp4Recording(GraphicsCaptureItem captureItem, PixelRect region, RecorderProfile settings, double frameRate, LabelRaster? renderedLabel,
        KeystrokeRenderer? renderedKeys = null, ClickRenderer? renderedClicks = null, bool captureKeyboardInput = true,
        bool captureMouseInput = true, Func<PixelPoint, PixelPoint?>? screenPointMapper = null, Func<string?>? sourceValidation = null)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows screen capture is unavailable on this device.");
        if (!double.IsFinite(frameRate) || frameRate < 1 || frameRate > 120) throw new ArgumentOutOfRangeException(nameof(frameRate));
        if (region.X < 0 || region.Y < 0 || region.Width < 2 || region.Height < 2 || region.Right > captureItem.Size.Width || region.Bottom > captureItem.Size.Height)
            throw new ArgumentException("The capture region no longer fits. Select the area again.");
        item = captureItem;
        area = region;
        expectedWidth = captureItem.Size.Width;
        expectedHeight = captureItem.Size.Height;
        validateSource = sourceValidation;
        profile = settings;
        outputPlan = Mp4OutputPlan.Create(region.Width, region.Height,
            settings.Output.Format == OutputFormat.Mp4 ? settings.Output.Mp4Width : 0);
        fps = frameRate;
        label = renderedLabel;
        keyRenderer = renderedKeys;
        clickRenderer = renderedClicks;
        keyFilter = new(settings.Overlays.Keystrokes, settings.Capture);
        keyTimeline = new(settings.Overlays.Keystrokes);
        clickTimeline = new(settings.Overlays.Clicks);
        captureKeyboard = captureKeyboardInput;
        captureMouse = captureMouseInput;
        mapScreenPoint = screenPointMapper;
        Completion = Task.Run(RecordAsync);
    }

    public void TogglePause()
    {
        lock (sync)
        {
            if (stopped || !ready.Task.IsCompletedSuccessfully || !ready.Task.Result) return;
            paused = !paused;
            acceptsKeys = !paused && keyRenderer is not null;
            acceptsClicks = !paused && clickRenderer is not null;
            if (paused) clock.Stop(); else clock.Start();
            Pulse();
        }
    }

    public void Stop(bool discard = false)
    {
        lock (sync)
        {
            if (finished) return;
            discarded |= discard;
            stopped = true;
            acceptsKeys = false;
            acceptsClicks = false;
            keyboard?.RequestStop();
            mouse?.RequestStop();
            clock.Stop();
            frameReady.TrySetResult(false);
            ready.TrySetResult(false);
            Pulse();
            if (discard) abort.Cancel();
        }
    }

    private async Task<string?> RecordAsync()
    {
        RecordingOutput? output = null;
        try
        {
            abort.Token.ThrowIfCancellationRequested();
            output = new RecordingOutput(profile.Output, profile.Overlays.Label.Lines.FirstOrDefault(l => l.Enabled)?.Text ?? "Recording");
            CreateGraphicsDevice(out device, out context);
            using var multithread = device.QueryInterface<ID3D11Multithread>();
            multithread.SetMultithreadProtected(true);
            frameCompletionQuery = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
            if (label is not null)
                compositor = new OverlayCompositor(device, label, area.Width, area.Height);
            if (keyRenderer is not null || clickRenderer is not null)
                dynamicCompositor = new DynamicOverlayCompositor(device, keyRenderer, clickRenderer, area.Width, area.Height);
            if (outputPlan.IsResized) scaler = new FrameScaler(device);
            using var captureDevice = GraphicsInterop.Wrap(device);
            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(captureDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            using var session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = profile.Capture.ShowCursor;
            var width = (uint)outputPlan.Width;
            var height = (uint)outputPlan.Height;
            var properties = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, width, height);
            SetFrameRate(properties);
            var source = new MediaStreamSource(new VideoStreamDescriptor(properties)) { BufferTime = TimeSpan.Zero };
            source.Starting += SourceStarting;
            source.SampleRequested += SampleRequested;
            pool.FrameArrived += FrameArrived;
            item.Closed += ItemClosed;
            try
            {
                session.StartCapture();
                if (!await frameReady.Task.WaitAsync(TimeSpan.FromSeconds(10)))
                {
                    if (failure is not null) throw failure;
                    output.Discard();
                    return null;
                }
                if (keyRenderer is not null && captureKeyboard)
                {
                    keyboard = new KeyboardCapture(AddKeystroke);
                    await keyboard.Ready.WaitAsync(TimeSpan.FromSeconds(5), abort.Token);
                    if (IsStopped) keyboard.RequestStop();
                }
                if (clickRenderer is not null && captureMouse && mapScreenPoint is not null)
                {
                    mouse = new MouseClickCapture(AddMouseClick);
                    await mouse.Ready.WaitAsync(TimeSpan.FromSeconds(5), abort.Token);
                    if (IsStopped) mouse.RequestStop();
                }
                var encoding = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
                encoding.Audio = null;
                encoding.Video.Width = width;
                encoding.Video.Height = height;
                SetFrameRate(encoding.Video);
                var bitsPerPixel = profile.Output.Quality switch { QualityPreset.Efficient => 0.08, QualityPreset.Crisp => 0.24, _ => 0.14 };
                encoding.Video.Bitrate = (uint)Math.Clamp(width * height * fps * bitsPerPixel, 500_000, 80_000_000);
                using var stream = output.Stream.AsRandomAccessStream();
                var prepared = await EncoderFallback.PrepareAsync(async hardware =>
                {
                    if (!hardware)
                    {
                        stream.Seek(0);
                        stream.Size = 0;
                    }
                    var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = hardware };
                    var preparation = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, encoding)
                        .AsTask(abort.Token).WaitAsync(TimeSpan.FromSeconds(20), abort.Token);
                    return new PreparedEncoder(transcoder, preparation);
                }, attempt => attempt.Preparation.CanTranscode ? null : attempt.Preparation.FailureReason.ToString());
                usesSoftwareEncoder = prepared.UsedSoftware;
                lock (sync)
                {
                    if (stopped)
                    {
                        output.Discard();
                        return null;
                    }
                    clock.Start();
                    acceptsKeys = keyRenderer is not null;
                    acceptsClicks = clickRenderer is not null;
                    ready.TrySetResult(true);
                }
                await prepared.Value.Preparation.TranscodeAsync().AsTask(abort.Token);
                await stream.FlushAsync();
            }
            finally
            {
                Stop();
                source.Starting -= SourceStarting;
                source.SampleRequested -= SampleRequested;
                pool.FrameArrived -= FrameArrived;
                item.Closed -= ItemClosed;
            }
            if (keyboard is not null)
            {
                await keyboard.DisposeAsync();
                if (keyboard.Failure is { } keyboardError) throw new InvalidOperationException("Pressed-key capture failed.", keyboardError);
            }
            if (mouse is not null)
            {
                await mouse.DisposeAsync();
                if (mouse.Failure is { } mouseError) throw new InvalidOperationException("Mouse-click capture failed.", mouseError);
            }
            if (failure is not null) throw new InvalidOperationException(failure.Message, failure);
            lock (sync)
            {
                finished = true;
            }
            if (discarded) { output.Discard(); return null; }
            return output.Commit();
        }
        catch (OperationCanceledException) when (discarded)
        {
            output?.Discard();
            return null;
        }
        catch (Exception error)
        {
            var cause = failure is null ? error : new InvalidOperationException(failure.Message, failure);
            throw new RecordingFailureException(cause.Message, cause, output?.TemporaryPath,
                cause is EncoderPreparationException && profile.Output.Format == OutputFormat.Mp4);
        }
        finally
        {
            Stop();
            if (keyboard is not null) await keyboard.DisposeAsync();
            if (mouse is not null) await mouse.DisposeAsync();
            pendingKeys.Writer.TryComplete();
            while (pendingKeys.Reader.TryRead(out _)) { }
            pendingClicks.Writer.TryComplete();
            while (pendingClicks.Reader.TryRead(out _)) { }
            keyTimeline.Clear();
            clickTimeline.Clear();
            lock (sync) { finished = true; latest?.Dispose(); latest = null; abort.Dispose(); }
            scaler?.Dispose(); dynamicCompositor?.Dispose(); compositor?.Dispose(); frameCompletionQuery?.Dispose(); context?.Dispose(); device?.Dispose(); output?.Dispose();
        }
    }

    private void SetFrameRate(VideoEncodingProperties video)
    {
        video.FrameRate.Numerator = (uint)Math.Round(fps * 1000);
        video.FrameRate.Denominator = 1000;
        video.PixelAspectRatio.Numerator = 1;
        video.PixelAspectRatio.Denominator = 1;
    }

    private static void CreateGraphicsDevice(out ID3D11Device graphicsDevice, out ID3D11DeviceContext graphicsContext)
    {
        FeatureLevel[] levels =
        [
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0,
        ];
        var result = D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            levels, out graphicsDevice, out graphicsContext);
        if (result.Failure)
            result = D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport,
                levels, out graphicsDevice, out graphicsContext);
        result.CheckError();
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
                if (frame.ContentSize.Width != expectedWidth || frame.ContentSize.Height != expectedHeight)
                {
                    frame.Dispose();
                    throw new InvalidOperationException("The capture source changed size. Start a new recording.");
                }
                latest?.Dispose(); latest = frame;
                frameReady.TrySetResult(true);
            }
            catch (Exception error) { Fail(error); }
        }
    }

    private void ItemClosed(GraphicsCaptureItem sender, object args) => Fail(new InvalidOperationException("The capture source was closed or disconnected."));
    private static void SourceStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args) => args.Request.SetActualStartPosition(TimeSpan.Zero);

    private async void SampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        var deferral = args.Request.GetDeferral();
        try
        {
            while (true)
            {
                if (validateSource?.Invoke() is { } problem) throw new InvalidOperationException(problem);
                Task signal;
                TimeSpan delay;
                lock (sync)
                {
                    if (stopped) { args.Request.Sample = null; return; }
                    if (profile.Capture.MaximumDurationSeconds > 0 && clock.Elapsed.TotalSeconds >= profile.Capture.MaximumDurationSeconds)
                    {
                        Stop(); args.Request.Sample = null; return;
                    }
                    delay = TimeSpan.FromTicks(Math.Max(0, nextFrameTicks - clock.Elapsed.Ticks));
                    signal = changed.Task;
                    if (!paused && delay == TimeSpan.Zero)
                    {
                        var period = (long)Math.Round(TimeSpan.TicksPerSecond / fps);
                        var ticks = nextFrameTicks == 0 ? 0 : Math.Max(nextFrameTicks, clock.Elapsed.Ticks / period * period);
                        args.Request.Sample = CopySample(TimeSpan.FromTicks(ticks), TimeSpan.FromTicks(period));
                        nextFrameTicks = ticks + period;
                        return;
                    }
                    if (paused) delay = Timeout.InfiniteTimeSpan;
                }
                if (delay == Timeout.InfiniteTimeSpan) await signal;
                else await Task.WhenAny(signal, Task.Delay(delay));
            }
        }
        catch (Exception error) { Fail(error); args.Request.Sample = null; }
        finally { deferral.Complete(); }
    }

    private MediaStreamSample CopySample(TimeSpan time, TimeSpan duration)
    {
        if (latest is null || device is null || context is null) throw new InvalidOperationException("No captured frame is available.");
        using var input = GraphicsInterop.Unwrap(latest.Surface);
        using var texture = CreateTexture(outputPlan.CaptureWidth, outputPlan.CaptureHeight);
        if ((area.Width & 1) != 0 || (area.Height & 1) != 0)
        {
            using var target = device.CreateRenderTargetView(texture);
            context.ClearRenderTargetView(target, new Color4(0, 0, 0, 1));
        }
        context.CopySubresourceRegion(texture, 0, 0, 0, 0, input, 0, new Box(area.X, area.Y, 0, area.Right, area.Bottom, 1));
        if (keyboard?.Failure is { } keyboardError) throw new InvalidOperationException("Pressed-key capture failed.", keyboardError);
        if (mouse?.Failure is { } mouseError) throw new InvalidOperationException("Mouse-click capture failed.", mouseError);
        while (pendingKeys.Reader.TryPeek(out var pending) && pending.Time <= time)
        {
            if (pendingKeys.Reader.TryRead(out var key)) keyTimeline.Add(key.Chord, key.Time);
        }
        while (pendingClicks.Reader.TryPeek(out var pendingClick) && pendingClick.Time <= time)
        {
            if (pendingClicks.Reader.TryRead(out var click)) clickTimeline.Add(click.Position, click.Button, click.Time);
        }
        compositor?.Draw(texture);
        dynamicCompositor?.Draw(texture, keyTimeline.VisibleAt(time), clickTimeline.VisibleAt(time));
        if (!outputPlan.IsResized)
        {
            WaitForFrameCommands();
            return Sample(texture, time, duration);
        }
        using var resized = CreateTexture(outputPlan.ContentWidth, outputPlan.ContentHeight);
        scaler!.Scale(texture, resized, area.Width, area.Height, outputPlan.ContentWidth, outputPlan.ContentHeight);
        WaitForFrameCommands();
        return Sample(resized, time, duration);
    }

    private void WaitForFrameCommands()
    {
        var rendering = context ?? throw new ObjectDisposedException(nameof(Mp4Recording));
        var completion = frameCompletionQuery ?? throw new ObjectDisposedException(nameof(Mp4Recording));
        rendering.End(completion);
        rendering.Flush();
        var started = Stopwatch.GetTimestamp();
        var spinner = new SpinWait();
        while (!rendering.IsDataAvailable(completion))
        {
            abort.Token.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(5))
                throw new TimeoutException("The GPU did not finish preparing a recording frame within five seconds.");
            spinner.SpinOnce();
        }
    }

    private ID3D11Texture2D CreateTexture(int width, int height)
    {
        return device!.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)((width + 1) & ~1), Height = (uint)((height + 1) & ~1), MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        });
    }

    private static MediaStreamSample Sample(ID3D11Texture2D texture, TimeSpan time, TimeSpan duration)
    {
        using var surface = GraphicsInterop.Wrap(texture);
        var sample = MediaStreamSample.CreateFromDirect3D11Surface(surface, time);
        sample.Duration = duration;
        return sample;
    }

    private void Fail(Exception error)
    {
        lock (sync) { failure ??= error; Stop(); }
    }

    internal void AddKeystroke(int virtualKey, KeyModifiers modifiers, bool altGr = false)
    {
        if (!acceptsKeys) return;
        var time = clock.Elapsed;
        var chord = keyFilter.Filter(virtualKey, modifiers, altGr);
        if (chord is not null && acceptsKeys) pendingKeys.Writer.TryWrite((chord, time));
    }

    internal void AddMouseClick(int screenX, int screenY, MouseClickButton button)
    {
        if (!acceptsClicks || mapScreenPoint?.Invoke(new PixelPoint(screenX, screenY)) is not { } position) return;
        var time = clock.Elapsed;
        if (acceptsClicks) pendingClicks.Writer.TryWrite((position, button, time));
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private void Pulse() { var previous = changed; changed = NewSignal(); previous.TrySetResult(); }
}
