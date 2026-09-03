using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder.Capture;

internal static class CpuPipelineSmokeCheck
{
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        CpuFrameReadbackChecks.Run();
        CpuOverlayCompositorChecks.Run();
        var ffmpeg = FfmpegRuntime.RequireExecutable();
        var videoPath = Path.Combine(directory, "cpu-lossless-check.mkv");
        const int width = 64;
        const int height = 48;
        const int frameCount = 5;
        if (File.Exists(videoPath)) File.Delete(videoPath);
        await using (var encoder = new FfmpegLosslessEncoder(ffmpeg, videoPath, width, height, 25))
        {
            for (var index = 0; index < frameCount; index++)
            {
                var frame = new CpuVideoFrame(width, height, TimeSpan.FromMilliseconds(index * 40));
                var pixels = frame.Pixels.Span;
                for (var offset = 0; offset < pixels.Length; offset += 4)
                {
                    pixels[offset] = (byte)(20 + index);
                    pixels[offset + 1] = (byte)(40 + index);
                    pixels[offset + 2] = (byte)(60 + index);
                    pixels[offset + 3] = 255;
                }
                await encoder.WriteAsync(frame);
            }
            await encoder.CompleteAsync();
        }
        Require(File.Exists(videoPath) && new FileInfo(videoPath).Length > 0, "FFV1 did not produce a clean intermediate.");

        var decodedPath = Path.Combine(directory, "cpu-lossless-decoded.bgra");
        var decoded = await DecodeAsync(ffmpeg, videoPath, decodedPath);
        var frameSize = width * height * 4;
        Require(decoded.Length == frameSize * frameCount, "The FFV1 intermediate changed the frame count or geometry.");
        for (var index = 0; index < frameCount; index++)
        {
            for (var offset = index * frameSize; offset < (index + 1) * frameSize; offset += 4)
                Require(decoded[offset] == 20 + index && decoded[offset + 1] == 40 + index &&
                    decoded[offset + 2] == 60 + index && decoded[offset + 3] == 255,
                    "FFV1 did not preserve the CPU BGRA pixels exactly.");
        }

        var finalPath = Path.Combine(directory, "cpu-offline-final.mp4");
        if (File.Exists(finalPath)) File.Delete(finalPath);
        await CpuRecordingRenderer.RenderAsync(ffmpeg, videoPath, finalPath, width, height, 25,
            Mp4OutputPlan.Create(width, height, 0), QualityPreset.Balanced,
            new RecordingOverlays(null, null, null), new OverlaySettings(), []);
        Require(File.Exists(finalPath) && new FileInfo(finalPath).Length > 0,
            "The CPU OpenH264 renderer did not produce a final MP4.");
        var finalPixels = await DecodeAsync(ffmpeg, finalPath, Path.Combine(directory, "cpu-offline-final.bgra"));
        Require(finalPixels.Length == frameSize * frameCount,
            "The final CPU-rendered MP4 changed the frame count or geometry.");

        var cancelledPath = Path.Combine(directory, "cpu-cancelled-final.mp4");
        using (var cancellation = new CancellationTokenSource())
        {
            var cancelled = false;
            try
            {
                await CpuRecordingRenderer.RenderAsync(ffmpeg, videoPath, cancelledPath, width, height, 25,
                    Mp4OutputPlan.Create(width, height, 0), QualityPreset.Balanced,
                    new RecordingOverlays(null, null, null), new OverlaySettings(), [], frameCount,
                    progress =>
                    {
                        if (progress.Frames >= 2) cancellation.Cancel();
                    }, cancellation.Token);
            }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled && File.Exists(videoPath) && !File.Exists(cancelledPath),
                "Cancelling the CPU render did not preserve the clean intermediate or published a partial MP4.");
        }
        await CheckRecoveryAsync(videoPath, directory, width, height, frameCount);

        var captureResult = await CheckWgcCaptureAsync(ffmpeg, directory);
        await GifExportChecks.RunAsync(captureResult.ProductPath, captureResult.ProductArea, directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"),
            $"PASS: GPU staging readback, CPU overlay blending, cancellation/recovery, {frameCount} generated FFV1/OpenH264 frames, " +
            $"{captureResult.FrameCount} real WGC frames, and the normal CPU recording session with shared live/journal events decoded successfully.\n" +
            $"{videoPath}\n{finalPath}\n{captureResult.Path}\n");
    }

    private static async Task CheckRecoveryAsync(string cleanSource, string outputDirectory,
        int width, int height, int frameCount)
    {
        var profile = new RecorderProfile();
        profile.Output.Directory = outputDirectory;
        profile.Output.FilenameTemplate = "cpu-recovery-{counter}";
        profile.Overlays.Keystrokes.Enabled = true;
        var manifest = new RecordingSessionManifest
        {
            SessionId = $"session-recovery-check-{Guid.NewGuid():N}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ApplicationVersion = "test",
            SourceGeometry = new CaptureRegion { Width = width, Height = height },
            FrameRate = 25,
            ActiveDurationTicks = TimeSpan.FromSeconds(frameCount / 25d).Ticks,
            Profile = profile,
        };
        var session = await RecordingSessionStore.CreateAsync(CpuRecordingSession.SessionRootPath, manifest);
        try
        {
            File.Copy(cleanSource, session.CleanVideoPath);
            await using (var journal = session.CreateEventJournal())
            {
                await journal.AppendAsync(new RecordingEvent
                {
                    Kind = RecordingEventKind.Keystroke,
                    TimestampTicks = TimeSpan.FromMilliseconds(40).Ticks,
                    Keys = ["Ctrl", "K"],
                });
            }
            Require(RecordingRecovery.Find().Contains(session.DirectoryPath, StringComparer.OrdinalIgnoreCase),
                "The application did not discover a recoverable CPU session.");
            var recovered = await RecordingRecovery.RenderAsync(session.DirectoryPath);
            Require(File.Exists(recovered) && !Directory.Exists(session.DirectoryPath),
                "The application did not render and retire a recovered CPU session.");
        }
        finally
        {
            if (Directory.Exists(session.DirectoryPath)) CpuRecordingSession.RemoveSession(session.DirectoryPath);
        }
    }

    private static async Task<(string Path, int FrameCount, string ProductPath, PixelRect ProductArea)> CheckWgcCaptureAsync(
        string ffmpeg, string directory)
    {
        var surface = new Border { Background = Brushes.Lime };
        var target = new Window
        {
            Width = 320,
            Height = 180,
            Left = 32,
            Top = 32,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Content = surface,
            Title = "CPU capture verification target",
        };
        CpuIntermediateRecording? recording = null;
        CpuRecordingSession? productRecording = null;
        try
        {
            var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            target.ContentRendered += (_, _) => rendered.TrySetResult();
            target.Show();
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var handle = new WindowInteropHelper(target).Handle;
            Require(NativeDesktop.TryGetWindow(handle, out var window), "The CPU capture test window was not enumerated.");
            var capture = CaptureTargetFactory.Create(new CaptureSettings
            {
                Source = CaptureSource.Window,
                WindowTitle = window.Title,
                WindowProcessName = window.ProcessName,
                WindowClassName = window.ClassName,
            }, [], window);
            Require(capture.Area.X == 0 && capture.Area.Y == 0 && capture.Area.Width >= 2 && capture.Area.Height >= 2,
                "Window capture did not select the complete window surface.");
            Require(capture.MapScreenPoint?.Invoke(new PixelPoint(window.Bounds.X + 12, window.Bounds.Y + 12)) is
                { X: >= 0, Y: >= 0 }, "Window click coordinates were not mapped into the captured surface.");
            Require(capture.MapScreenPoint?.Invoke(new PixelPoint(window.Bounds.X - 20, window.Bounds.Y - 20)) is null,
                "A click outside the captured window was accepted.");
            var path = Path.Combine(directory, "cpu-wgc-clean.mkv");
            if (File.Exists(path)) File.Delete(path);
            recording = new CpuIntermediateRecording(capture.CreateItem, capture.Area, path, 20, false, capture.Validate);
            Require(await recording.Ready.WaitAsync(TimeSpan.FromSeconds(15)), "The clean CPU recorder did not receive its first WGC frame.");
            await Task.Delay(450);
            recording.TogglePause();
            var pausedAt = recording.Elapsed;
            await Task.Delay(300);
            Require(recording.Elapsed == pausedAt, "The clean CPU recorder included paused time.");
            surface.Background = Brushes.Blue;
            recording.TogglePause();
            await Task.Delay(450);
            recording.Stop();
            await recording.Completion.WaitAsync(TimeSpan.FromSeconds(20));
            Require(recording.UsedDedicatedMtaThread,
                "The WGC lifecycle left its dedicated MTA thread.");
            var decodedPath = Path.Combine(directory, "cpu-wgc-clean.bgra");
            var pixels = await DecodeAsync(ffmpeg, path, decodedPath);
            var width = capture.Area.Width;
            var height = capture.Area.Height;
            var frameSize = checked(width * height * 4);
            Require(pixels.Length >= frameSize * 8 && pixels.Length % frameSize == 0,
                "The clean CPU recorder produced an invalid frame stream.");
            var sampleOffset = ((height / 2) * width + width / 2) * 4;
            Require(pixels[sampleOffset + 1] > 220 && pixels[sampleOffset] < 30 && pixels[sampleOffset + 2] < 30,
                "The first clean WGC frame did not preserve the green source pixels.");
            var lastSample = pixels.Length - frameSize + sampleOffset;
            Require(pixels[lastSample] > 220 && pixels[lastSample + 1] < 30 && pixels[lastSample + 2] < 30,
                "The final clean WGC frame did not preserve the blue source pixels.");

            var profile = new RecorderProfile();
            profile.Output.Directory = directory;
            profile.Output.FilenameTemplate = "cpu-product-{counter}";
            profile.Output.Mp4Width = 160;
            profile.Capture.ShowCursor = false;
            profile.Capture.HighlightClicks = true;
            profile.Capture.MaximumDurationSeconds = 5;
            profile.Overlays.Label.Width = 150;
            profile.Overlays.Label.OffsetY = 8;
            profile.Overlays.Label.BackgroundBlur = 4;
            profile.Overlays.Keystrokes.Enabled = true;
            profile.Overlays.Keystrokes.Anchor = OverlayAnchor.TopLeft;
            profile.Overlays.Keystrokes.OffsetX = 8;
            profile.Overlays.Keystrokes.OffsetY = 8;
            profile.Overlays.Desktop.ShowKeystrokes = true;
            profile.Overlays.Desktop.ShowMouseClicks = true;
            var productCapture = CaptureTargetFactory.Create(new CaptureSettings
            {
                Source = CaptureSource.Window,
                WindowTitle = window.Title,
                WindowProcessName = window.ProcessName,
                WindowClassName = window.ClassName,
            }, [], window);
            var overlays = RecordingOverlayPipeline.Create(profile, productCapture.Area.Width, productCapture.Area.Height);
            var liveKeys = 0;
            var liveClicks = 0;
            productRecording = new CpuRecordingSession(productCapture.CreateItem, productCapture.Area, profile, 20, overlays,
                productCapture.MapScreenPoint, productCapture.Validate,
                (_, _) => Interlocked.Increment(ref liveKeys),
                (_, _, _) => Interlocked.Increment(ref liveClicks));
            Require(await productRecording.Ready.WaitAsync(TimeSpan.FromSeconds(15)),
                "The product CPU recording session did not become ready.");
            productRecording.AddKeystrokeForChecks(0x4B, KeyModifiers.Control);
            productRecording.AddMouseClickForChecks(new PixelPoint(productCapture.Area.Width / 2,
                productCapture.Area.Height / 2), MouseClickButton.Left);
            await Task.Delay(500);
            productRecording.TogglePause();
            var productPausedAt = productRecording.Elapsed;
            await Task.Delay(200);
            Require(productRecording.Elapsed == productPausedAt, "The product CPU session included paused time.");
            productRecording.TogglePause();
            await Task.Delay(250);
            productRecording.Stop();
            var productPath = await productRecording.Completion.WaitAsync(TimeSpan.FromSeconds(30));
            Require(productRecording.UsedDedicatedCaptureThread,
                "The product WGC lifecycle left its dedicated MTA thread.");
            Require(productPath is not null && File.Exists(productPath),
                "The product CPU session did not commit its final MP4.");
            Require(liveKeys == 1 && liveClicks == 1,
                "The product CPU session did not fan input events to the live layer exactly once.");
            var productPixels = await DecodeAsync(ffmpeg, productPath!,
                Path.Combine(directory, "cpu-product-final.bgra"));
            var productPlan = Mp4OutputPlan.Create(productCapture.Area.Width, productCapture.Area.Height, 160);
            var productFrameSize = productPlan.Width * productPlan.Height * 4;
            Require(productPixels.Length >= productFrameSize * 8 && productPixels.Length % productFrameSize == 0,
                "The product CPU session produced invalid resized MP4 frames.");
            var overlayPixels = 0;
            for (var offset = 0; offset < productPixels.Length; offset += 4)
                if (productPixels[offset + 1] > 20 || productPixels[offset + 2] > 20) overlayPixels++;
            Require(overlayPixels > 500,
                "The product CPU session did not render its offline overlays into the final MP4.");
            var display = NativeDesktop.Displays().First(candidate =>
                window.Bounds.X >= candidate.Bounds.X && window.Bounds.X < candidate.Bounds.Right &&
                window.Bounds.Y >= candidate.Bounds.Y && window.Bounds.Y < candidate.Bounds.Bottom);
            await LiveOverlayRecordingChecks.RunAsync(display, window, directory);
            return (path, pixels.Length / frameSize, productPath!,
                new PixelRect(0, 0, productPlan.Width, productPlan.Height));
        }
        finally
        {
            productRecording?.Stop(discard: true);
            if (productRecording is not null)
            {
                try { await productRecording.Completion.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch (Exception) when (productRecording.Completion.IsCompleted) { }
            }
            recording?.Stop();
            if (recording is not null)
            {
                try { await recording.Completion.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (Exception) when (recording.Completion.IsCompleted) { }
            }
            target.Close();
        }
    }

    private static async Task<byte[]> DecodeAsync(string ffmpeg, string videoPath, string decodedPath)
    {
        if (File.Exists(decodedPath)) File.Delete(decodedPath);
        var decode = new ProcessStartInfo
        {
            FileName = ffmpeg,
            WorkingDirectory = Path.GetDirectoryName(ffmpeg)!,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        string[] arguments = ["-hide_banner", "-nostdin", "-loglevel", "error", "-i", videoPath,
            "-f", "rawvideo", "-pix_fmt", "bgra", decodedPath];
        foreach (var argument in arguments) decode.ArgumentList.Add(argument);
        using var process = Process.Start(decode) ?? throw new InvalidOperationException("FFmpeg decoder did not start.");
        var diagnostics = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Require(process.ExitCode == 0, $"FFmpeg could not decode its FFV1 intermediate: {await diagnostics}");
        return await File.ReadAllBytesAsync(decodedPath);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
