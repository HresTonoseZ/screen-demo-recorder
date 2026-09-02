using System.Reflection;
using System.Threading.Channels;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using Windows.Graphics.Capture;

namespace ScreenDemoRecorder.Capture;

internal enum CpuRecordingStage { Starting, Capturing, Finalizing, Rendering, Completed }

internal sealed class CpuRecordingSession
{
    private readonly object sync = new();
    private readonly object eventSync = new();
    private readonly GraphicsCaptureItem item;
    private readonly PixelRect area;
    private readonly RecorderProfile profile;
    private readonly double frameRate;
    private readonly RecordingOverlays overlays;
    private readonly Func<PixelPoint, PixelPoint?>? mapScreenPoint;
    private readonly Func<string?>? validateSource;
    private readonly Action<KeyChord, TimeSpan>? showKeystroke;
    private readonly Action<PixelPoint, MouseClickButton, TimeSpan>? showClick;
    private readonly KeystrokeFilter keyFilter;
    private readonly Channel<RecordingEvent> pendingEvents = Channel.CreateUnbounded<RecordingEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly List<RecordingEvent> events = [];
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource renderCancellation = new();
    private CpuIntermediateRecording? cleanRecording;
    private KeyboardCapture? keyboard;
    private MouseClickCapture? mouse;
    private long lastEventTicks;
    private bool acceptsInput;
    private bool stopped;
    private bool discarded;
    private bool finished;
    private bool renderCancelled;
    private volatile CpuRecordingStage stage = CpuRecordingStage.Starting;
    private int renderedFrames;
    private int totalRenderFrames;
    private int renderPercent;

    public Task<bool> Ready => ready.Task;

    public Task<string?> Completion { get; }

    public TimeSpan Elapsed => cleanRecording?.Elapsed ?? TimeSpan.Zero;

    public bool IsPaused => cleanRecording?.IsPaused ?? false;

    public bool IsStopped { get { lock (sync) return stopped; } }

    public bool UsesSoftwareEncoder => true;

    public CpuRecordingStage Stage => stage;

    public CpuRenderProgress RenderProgress => new(
        Volatile.Read(ref renderedFrames), Volatile.Read(ref totalRenderFrames), Volatile.Read(ref renderPercent));

    public bool WasRenderCancelled => Volatile.Read(ref renderCancelled);

    public string? RecoveryPath { get; private set; }

    public CpuRecordingSession(GraphicsCaptureItem captureItem, PixelRect region, RecorderProfile settings,
        double fps, RecordingOverlays renderedOverlays, Func<PixelPoint, PixelPoint?>? screenPointMapper = null,
        Func<string?>? sourceValidation = null, Action<KeyChord, TimeSpan>? liveKeystroke = null,
        Action<PixelPoint, MouseClickButton, TimeSpan>? liveClick = null)
    {
        ArgumentNullException.ThrowIfNull(captureItem);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(renderedOverlays);
        item = captureItem;
        area = region;
        profile = settings;
        frameRate = fps;
        overlays = renderedOverlays;
        mapScreenPoint = screenPointMapper;
        validateSource = sourceValidation;
        showKeystroke = liveKeystroke;
        showClick = liveClick;
        keyFilter = new KeystrokeFilter(profile.Overlays.Keystrokes, profile.Capture);
        Completion = Task.Run(RunAsync);
    }

    public void TogglePause()
    {
        RecordingEventKind kind;
        lock (sync)
        {
            if (stopped || cleanRecording is null || !ready.Task.IsCompletedSuccessfully || !ready.Task.Result) return;
            acceptsInput = false;
            cleanRecording.TogglePause();
            kind = cleanRecording.IsPaused ? RecordingEventKind.Paused : RecordingEventKind.Resumed;
            acceptsInput = !cleanRecording.IsPaused;
        }
        QueueEvent(new RecordingEvent { Kind = kind });
    }

    internal void AddKeystrokeForChecks(int virtualKey, KeyModifiers modifiers = KeyModifiers.None, bool altGr = false) =>
        OnKeyPressed(virtualKey, modifiers, altGr);

    internal void AddMouseClickForChecks(PixelPoint position, MouseClickButton button) =>
        AcceptMouseClick(position, button);

    public void Stop(bool discard = false)
    {
        lock (sync)
        {
            if (finished) return;
            discarded |= discard;
            stopped = true;
            if (stage == CpuRecordingStage.Capturing) stage = CpuRecordingStage.Finalizing;
            acceptsInput = false;
            keyboard?.RequestStop();
            mouse?.RequestStop();
            cleanRecording?.Stop();
            ready.TrySetResult(false);
        }
    }

    public bool CancelRendering()
    {
        lock (sync)
        {
            if (finished || stage is not (CpuRecordingStage.Finalizing or CpuRecordingStage.Rendering)) return false;
            renderCancelled = true;
            renderCancellation.Cancel();
            return true;
        }
    }

    private async Task<string?> RunAsync()
    {
        RecordingSessionStore? session = null;
        RecordingOutput? output = null;
        RecordingEventJournal? journal = null;
        Task? journalWorker = null;
        try
        {
            var manifest = new RecordingSessionManifest
            {
                SessionId = $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
                SourceGeometry = new CaptureRegion { X = area.X, Y = area.Y, Width = area.Width, Height = area.Height },
                FrameRate = frameRate,
                Profile = profile,
            };
            var sessionRoot = SessionRootPath;
            session = await RecordingSessionStore.CreateAsync(sessionRoot, manifest).ConfigureAwait(false);
            journal = session.CreateEventJournal();
            journalWorker = WriteJournalAsync(journal);
            cleanRecording = new CpuIntermediateRecording(item, area, session.CleanVideoPath, frameRate,
                profile.Capture.ShowCursor, validateSource);
            var startup = await Task.WhenAny(cleanRecording.Ready, cleanRecording.Completion).ConfigureAwait(false);
            var started = false;
            if (startup != cleanRecording.Completion && await cleanRecording.Ready.ConfigureAwait(false))
            {
                await StartInputCaptureAsync().ConfigureAwait(false);
                lock (sync)
                {
                    if (!stopped)
                    {
                        acceptsInput = true;
                        stage = CpuRecordingStage.Capturing;
                        ready.TrySetResult(true);
                        started = true;
                    }
                }
            }
            if (!started) cleanRecording.Stop();
            var durationLimit = EnforceDurationLimitAsync(cleanRecording);
            await cleanRecording.Completion.ConfigureAwait(false);
            await durationLimit.ConfigureAwait(false);
            await StopInputCaptureAsync().ConfigureAwait(false);
            pendingEvents.Writer.TryComplete();
            await journalWorker.ConfigureAwait(false);
            await journal.DisposeAsync().ConfigureAwait(false);
            journal = null;
            if (!started)
            {
                RemoveSession(session.DirectoryPath);
                return null;
            }
            manifest.ActiveDurationTicks = cleanRecording.Elapsed.Ticks;
            await session.WriteManifestAsync(manifest).ConfigureAwait(false);
            if (discarded)
            {
                RemoveSession(session.DirectoryPath);
                return null;
            }

            var renderPath = Path.Combine(session.DirectoryPath, "composed.mp4");
            stage = CpuRecordingStage.Rendering;
            renderCancellation.Token.ThrowIfCancellationRequested();
            var outputPlan = Mp4OutputPlan.Create(area.Width, area.Height,
                profile.Output.Format == OutputFormat.Mp4 ? profile.Output.Mp4Width : 0);
            var expectedFrames = Math.Max(1, (int)Math.Ceiling(cleanRecording.Elapsed.TotalSeconds * frameRate));
            Volatile.Write(ref totalRenderFrames, expectedFrames);
            await CpuRecordingRenderer.RenderAsync(FfmpegRuntime.RequireExecutable(), session.CleanVideoPath,
                renderPath, area.Width, area.Height, frameRate, outputPlan, profile.Output.Quality,
                overlays, profile.Overlays, events, expectedFrames, UpdateRenderProgress,
                renderCancellation.Token).ConfigureAwait(false);
            output = new RecordingOutput(profile.Output,
                profile.Overlays.Label.Lines.FirstOrDefault(line => line.Enabled)?.Text ?? "Recording");
            var temporaryOutput = output.PrepareForExternalWriter();
            File.Move(renderPath, temporaryOutput);
            var destination = output.Commit();
            RemoveSession(session.DirectoryPath);
            stage = CpuRecordingStage.Completed;
            return destination;
        }
        catch (OperationCanceledException) when (renderCancelled)
        {
            RecoveryPath = session?.DirectoryPath;
            output?.Discard();
            return null;
        }
        catch (Exception error)
        {
            if (discarded)
            {
                if (session is not null) RemoveSession(session.DirectoryPath);
                output?.Discard();
                return null;
            }
            var recovery = session?.CleanVideoPath;
            throw new RecordingFailureException(error.Message, error, recovery, false);
        }
        finally
        {
            Stop();
            try { await StopInputCaptureAsync().ConfigureAwait(false); }
            finally
            {
                pendingEvents.Writer.TryComplete();
                try
                {
                    if (journalWorker is not null) await journalWorker.ConfigureAwait(false);
                }
                finally
                {
                    if (journal is not null) await journal.DisposeAsync().ConfigureAwait(false);
                    lock (sync) finished = true;
                    renderCancellation.Dispose();
                    output?.Dispose();
                }
            }
        }
    }

    private async Task StartInputCaptureAsync()
    {
        if (overlays.Keystrokes is not null || profile.Overlays.Desktop.ShowKeystrokes)
        {
            keyboard = new KeyboardCapture(OnKeyPressed);
            await keyboard.Ready.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        if ((overlays.Clicks is not null || profile.Overlays.Desktop.ShowMouseClicks) && mapScreenPoint is not null)
        {
            mouse = new MouseClickCapture(OnMouseClicked);
            await mouse.Ready.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }

    private async Task StopInputCaptureAsync()
    {
        var activeKeyboard = Interlocked.Exchange(ref keyboard, null);
        var activeMouse = Interlocked.Exchange(ref mouse, null);
        if (activeKeyboard is not null)
        {
            await activeKeyboard.DisposeAsync().ConfigureAwait(false);
            if (activeKeyboard.Failure is { } error)
                throw new InvalidOperationException("Pressed-key capture failed.", error);
        }
        if (activeMouse is not null)
        {
            await activeMouse.DisposeAsync().ConfigureAwait(false);
            if (activeMouse.Failure is { } error)
                throw new InvalidOperationException("Mouse-click capture failed.", error);
        }
    }

    private void OnKeyPressed(int virtualKey, KeyModifiers modifiers, bool altGr)
    {
        if (!Volatile.Read(ref acceptsInput)) return;
        var chord = keyFilter.Filter(virtualKey, modifiers, altGr);
        if (chord is null || !Volatile.Read(ref acceptsInput)) return;
        var time = QueueEvent(new RecordingEvent
        {
            Kind = RecordingEventKind.Keystroke,
            Keys = chord.Keys,
            DisplayText = string.Join(" + ", chord.Keys),
        });
        showKeystroke?.Invoke(chord, time);
    }

    private void OnMouseClicked(int screenX, int screenY, MouseClickButton button)
    {
        if (!Volatile.Read(ref acceptsInput) || mapScreenPoint?.Invoke(new PixelPoint(screenX, screenY)) is not { } position)
            return;
        AcceptMouseClick(position, button);
    }

    private void AcceptMouseClick(PixelPoint position, MouseClickButton button)
    {
        if (!Volatile.Read(ref acceptsInput)) return;
        var time = QueueEvent(new RecordingEvent
        {
            Kind = RecordingEventKind.MouseClick,
            MouseButton = button,
            Position = position,
        });
        showClick?.Invoke(position, button, time);
    }

    private TimeSpan QueueEvent(RecordingEvent entry)
    {
        lock (eventSync)
        {
            var timestamp = cleanRecording?.Elapsed.Ticks ?? 0;
            lastEventTicks = Math.Max(lastEventTicks, timestamp);
            entry.TimestampTicks = lastEventTicks;
            if (!pendingEvents.Writer.TryWrite(entry))
                throw new InvalidOperationException("The recording event journal is no longer accepting events.");
            return TimeSpan.FromTicks(lastEventTicks);
        }
    }

    private async Task EnforceDurationLimitAsync(CpuIntermediateRecording recording)
    {
        if (profile.Capture.MaximumDurationSeconds <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        var limit = TimeSpan.FromSeconds(profile.Capture.MaximumDurationSeconds);
        while (!recording.Completion.IsCompleted && await timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            if (recording.Elapsed < limit) continue;
            Stop();
            return;
        }
    }

    private async Task WriteJournalAsync(RecordingEventJournal journal)
    {
        await foreach (var entry in pendingEvents.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var stored = await journal.AppendAsync(entry).ConfigureAwait(false);
            lock (events) events.Add(stored);
        }
    }

    private void UpdateRenderProgress(CpuRenderProgress progress)
    {
        Volatile.Write(ref renderedFrames, progress.Frames);
        Volatile.Write(ref totalRenderFrames, progress.TotalFrames);
        Volatile.Write(ref renderPercent, (int)Math.Round(progress.Percent));
    }

    internal static void RemoveSession(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = SessionRootPath + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The recording session is outside the managed session directory.");
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
    }

    internal static string SessionRootPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Screen Demo Recorder", "Sessions");
}
