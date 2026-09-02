using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenDemoRecorder.Core.Models;

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
            new RecordingOverlays(null, null, null), new OverlaySettings(), []);
        Require(File.Exists(finalPath) && new FileInfo(finalPath).Length > 0,
            "The CPU OpenH264 renderer did not produce a final MP4.");
        var finalPixels = await DecodeAsync(ffmpeg, finalPath, Path.Combine(directory, "cpu-offline-final.bgra"));
        Require(finalPixels.Length == frameSize * frameCount,
            "The final CPU-rendered MP4 changed the frame count or geometry.");

        var captureResult = await CheckWgcCaptureAsync(ffmpeg, directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"),
            $"PASS: GPU staging readback, CPU overlay blending, {frameCount} generated FFV1/OpenH264 frames and {captureResult.FrameCount} real WGC frames decoded successfully.\n{videoPath}\n{finalPath}\n{captureResult.Path}\n");
    }

    private static async Task<(string Path, int FrameCount)> CheckWgcCaptureAsync(string ffmpeg, string directory)
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
            var path = Path.Combine(directory, "cpu-wgc-clean.mkv");
            if (File.Exists(path)) File.Delete(path);
            recording = new CpuIntermediateRecording(capture.Item, capture.Area, path, 20, false, capture.Validate);
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
            return (path, pixels.Length / frameSize);
        }
        finally
        {
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
