using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Overlays;
using Windows.Media.Editing;
using Windows.Storage;

namespace ScreenDemoRecorder.Capture;

internal static class RecordingSmokeCheck
{
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var canvas = new Canvas { Background = Brushes.Red };
        var block = new Border { Width = 321, Height = 181, Background = Brushes.Lime };
        Canvas.SetLeft(block, 40); Canvas.SetTop(block, 30); canvas.Children.Add(block);
        var target = new Window
        {
            Width = 480, Height = 300, Left = 32, Top = 32, WindowStyle = WindowStyle.None, Topmost = true,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Content = canvas, Title = "Recording verification target",
        };
        Mp4Recording? recording = null;
        try
        {
            var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            target.ContentRendered += (_, _) => rendered.TrySetResult();
            target.Show();
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Require(NativeDesktop.IsPerMonitorV2(), "The recording process is not using Per-Monitor V2 DPI awareness.");
            var targetHandle = new WindowInteropHelper(target).Handle;
            Require(NativeDesktop.TryGetWindow(targetHandle, out var targetWindow), "The visible test window was not enumerated.");
            Require(NativeDesktop.DpiForWindow(target) >= 96, "The test window reported an invalid physical DPI.");
            var windowSettings = new CaptureSettings
            {
                Source = CaptureSource.Window,
                WindowTitle = targetWindow.Title,
                WindowProcessName = targetWindow.ProcessName,
                WindowClassName = targetWindow.ClassName,
            };
            var windowCapture = CaptureTargetFactory.Create(windowSettings, [], targetWindow);
            var item = windowCapture.Item;
            Require(windowCapture.Area == new PixelRect(0, 0, item.Size.Width, item.Size.Height),
                "Window capture did not select the complete window surface.");
            Require(item.Size.Width == targetWindow.Bounds.Width && item.Size.Height == targetWindow.Bounds.Height,
                "Window capture dimensions disagree with Win32 physical bounds.");
            Require(windowCapture.Validate?.Invoke() is null, "The selected window failed initial validation.");
            Require(windowCapture.MapScreenPoint?.Invoke(new PixelPoint(targetWindow.Bounds.X + 12, targetWindow.Bounds.Y + 12)) is
                { X: >= 0, Y: >= 0 }, "Window click coordinates were not mapped into the captured surface.");
            Require(windowCapture.MapScreenPoint?.Invoke(new PixelPoint(targetWindow.Bounds.X - 20, targetWindow.Bounds.Y - 20)) is null,
                "A click outside the captured window was accepted.");
            Require(windowCapture.MapScreenPoint?.Invoke(new PixelPoint(targetWindow.Bounds.Right - 1, targetWindow.Bounds.Bottom - 1)) ==
                new PixelPoint(item.Size.Width - 1, item.Size.Height - 1),
                "The captured window's bottom-right screen coordinate was mapped incorrectly.");
            var connectedDisplays = NativeDesktop.Displays();
            foreach (var connectedDisplay in connectedDisplays)
            {
                var displayItem = GraphicsInterop.ForMonitor(connectedDisplay.Monitor);
                Require(displayItem.Size.Width == connectedDisplay.Bounds.Width && displayItem.Size.Height == connectedDisplay.Bounds.Height,
                    $"Monitor interop returned incorrect physical dimensions for {connectedDisplay.DeviceName}.");
            }
            var display = connectedDisplays.First();
            var scale = item.Size.Width / canvas.ActualWidth;
            var crop = new PixelRect((int)Math.Round(40 * scale), (int)Math.Round(30 * scale),
                (int)Math.Round(321 * scale), (int)Math.Round(181 * scale));
            var profile = new RecorderProfile();
            profile.Output.Directory = directory;
            profile.Output.FilenameTemplate = "capture-check-{counter}";
            profile.Capture.ShowCursor = false;
            profile.Capture.MaximumDurationSeconds = 0;
            recording = new Mp4Recording(item, crop, profile, 30, null);
            if (!await recording.Ready.WaitAsync(TimeSpan.FromSeconds(15))) await recording.Completion;
            await Task.Delay(700);
            recording.TogglePause();
            var pausedAt = recording.Elapsed;
            await Task.Delay(650);
            Require(recording.Elapsed == pausedAt, "The recording clock continued during pause.");
            block.Background = Brushes.Blue;
            recording.TogglePause();
            await Task.Delay(700);
            recording.Stop();
            var elapsed = recording.Elapsed;
            var path = await recording.Completion.WaitAsync(TimeSpan.FromSeconds(20));
            Require(path is not null, "No MP4 was produced.");
            var file = await StorageFile.GetFileFromPathAsync(path!);
            var video = await file.Properties.GetVideoPropertiesAsync();
            Require(video.Width == ((crop.Width + 1) & ~1) && video.Height == ((crop.Height + 1) & ~1), "MP4 dimensions do not match the cropped region.");
            Require(Math.Abs(video.Duration.TotalSeconds - elapsed.TotalSeconds) < 0.4, "The MP4 duration includes the pause or lost active time.");
            var clip = await MediaClip.CreateFromFileAsync(file);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);
            using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromMilliseconds(350), (int)video.Width, (int)video.Height, VideoFramePrecision.NearestFrame);
            using var input = thumbnail.AsStreamForRead();
            var bitmap = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var pixels = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var color = new byte[4];
            pixels.CopyPixels(new Int32Rect(5, 5, 1, 1), color, 4, 0);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(bitmap);
            using (var preview = File.Create(Path.Combine(directory, "encoded-frame.png"))) encoder.Save(preview);
            Require(color[1] > 170 && color[0] < 80 && color[2] < 80, $"The encoded frame is blank or the region was not cropped correctly. Corner BGRA: {string.Join(',', color)}.");
            using var resumedThumbnail = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(video.Duration.TotalSeconds - 0.2),
                (int)video.Width, (int)video.Height, VideoFramePrecision.NearestFrame);
            using var resumedInput = resumedThumbnail.AsStreamForRead();
            var resumed = new FormatConvertedBitmap(BitmapFrame.Create(resumedInput, BitmapCreateOptions.None, BitmapCacheOption.OnLoad), PixelFormats.Bgra32, null, 0);
            resumed.CopyPixels(new Int32Rect(5, 5, 1, 1), color, 4, 0);
            Require(color[0] > 170 && color[1] < 80 && color[2] < 80, "Resuming did not capture the updated window contents.");

            profile.Output.FilenameTemplate = "cancel-check-{counter}";
            recording = new Mp4Recording(item, crop, profile, 15, null);
            await recording.Ready.WaitAsync(TimeSpan.FromSeconds(15));
            await Task.Delay(250);
            recording.TogglePause();
            recording.Stop(discard: true);
            Require(await recording.Completion.WaitAsync(TimeSpan.FromSeconds(20)) is null, "Cancellation saved a video.");
            recording = new Mp4Recording(item, crop, profile, 15, null);
            recording.Stop(discard: true);
            Require(await recording.Completion.WaitAsync(TimeSpan.FromSeconds(20)) is null, "Immediate cancellation saved a video.");

            profile.Output.FilenameTemplate = "duration-check-{counter}";
            profile.Capture.MaximumDurationSeconds = 1;
            recording = new Mp4Recording(item, crop, profile, 23.976, null);
            var limitedPath = await recording.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            Require(limitedPath is not null, "The duration limit did not save the video.");
            var limited = await (await StorageFile.GetFileFromPathAsync(limitedPath!)).Properties.GetVideoPropertiesAsync();
            Require(Math.Abs(limited.Duration.TotalSeconds - 1) < 0.2, "The maximum duration was not respected.");
            profile.Output.FilenameTemplate = "label-check-{counter}";
            profile.Overlays.Label = LabelRenderChecks.TestLabel();
            profile.Overlays.Label.BackgroundBlur = 8;
            var label = LabelRenderer.Render(profile.Overlays.Label, crop.Width, crop.Height)!;
            var expected = LabelRenderChecks.Compose(label, crop.Width, crop.Height, Colors.Blue);
            LabelRenderChecks.Save(expected, Path.Combine(directory, "label-expected.png"));
            recording = new Mp4Recording(item, crop, profile, 30, label);
            var labelledPath = await recording.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            Require(labelledPath is not null, "The recording with a label was not saved.");
            var labelledComposition = new MediaComposition();
            labelledComposition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(labelledPath!)));
            using var labelledThumbnail = await labelledComposition.GetThumbnailAsync(TimeSpan.FromMilliseconds(350), expected.PixelWidth, expected.PixelHeight, VideoFramePrecision.NearestFrame);
            using var labelledInput = labelledThumbnail.AsStreamForRead();
            var actual = BitmapFrame.Create(labelledInput, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            LabelRenderChecks.Save(actual, Path.Combine(directory, "label-encoded.png"));
            var expectedPixels = LabelRenderChecks.Pixels(expected);
            var actualPixels = LabelRenderChecks.Pixels(actual);
            Require(actualPixels.Length == expectedPixels.Length, "Labelled MP4 dimensions differ from preview.");
            double difference = 0;
            for (var i = 0; i < expectedPixels.Length; i++) difference += Math.Abs(expectedPixels[i] - actualPixels[i]);
            difference /= expectedPixels.Length;
            Require(difference < 6, $"Preview and encoded label differ: mean channel error {difference:F2}.");
            var alphaPixel = ((label.Container.Y + 4) * expected.PixelWidth + label.Container.X + 4) * 4;
            for (var channel = 0; channel < 3; channel++)
                Require(Math.Abs(actualPixels[alphaPixel + channel] - expectedPixels[alphaPixel + channel]) < 20, "The label's transparency or placement changed during GPU composition.");
            Require(!Directory.EnumerateFiles(directory, ".recording-*.partial.mp4").Any(), "Cancellation left a temporary recording.");
            var keysPath = await KeystrokeRecordingChecks.RunAsync(item, crop, profile, directory);
            await GifExportChecks.RunAsync(path!, keysPath, crop, directory);

            profile.Output.FilenameTemplate = "scaled-check-{counter}";
            profile.Output.Mp4Width = 160;
            profile.Capture.MaximumDurationSeconds = 1;
            var scaledPlan = Mp4OutputPlan.Create(crop.Width, crop.Height, profile.Output.Mp4Width);
            recording = new Mp4Recording(item, crop, profile, 15, null);
            var scaledPath = await recording.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            Require(scaledPath is not null, "The scaled MP4 was not saved.");
            var scaled = await (await StorageFile.GetFileFromPathAsync(scaledPath!)).Properties.GetVideoPropertiesAsync();
            Require(scaled.Width == scaledPlan.Width && scaled.Height == scaledPlan.Height,
                $"GPU-scaled MP4 dimensions are {scaled.Width} × {scaled.Height}, expected {scaledPlan.Width} × {scaledPlan.Height}.");
            var scaledComposition = new MediaComposition();
            scaledComposition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(scaledPath!)));
            using (var scaledThumbnail = await scaledComposition.GetThumbnailAsync(TimeSpan.FromMilliseconds(350),
                scaledPlan.Width, scaledPlan.Height, VideoFramePrecision.NearestFrame))
            using (var scaledInput = scaledThumbnail.AsStreamForRead())
            {
                var scaledFrame = new FormatConvertedBitmap(BitmapFrame.Create(scaledInput, BitmapCreateOptions.None, BitmapCacheOption.OnLoad),
                    PixelFormats.Bgra32, null, 0);
                scaledFrame.CopyPixels(new Int32Rect(5, 5, 1, 1), color, 4, 0);
                Require(color[0] > 150 && color[1] < 100 && color[2] < 100, "GPU scaling produced a blank or incorrectly colored frame.");
                LabelRenderChecks.Save(scaledFrame, Path.Combine(directory, "scaled-frame.png"));
            }
            profile.Output.Mp4Width = 0;
            profile.Output.FilenameTemplate = "resize-check-{counter}";
            profile.Capture.MaximumDurationSeconds = 0;
            recording = new Mp4Recording(item, windowCapture.Area, profile, 15, null, sourceValidation: windowCapture.Validate);
            await recording.Ready.WaitAsync(TimeSpan.FromSeconds(15));
            target.Width += 48;
            Exception? resizeFailure = null;
            try { await recording.Completion.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (IOException error) { resizeFailure = error; }
            Require(resizeFailure?.Message.Contains("changed size", StringComparison.OrdinalIgnoreCase) == true,
                "Resizing a captured window did not stop recording with a clear error.");
            await File.WriteAllTextAsync(Path.Combine(directory, "result.txt"),
                $"PASS: Per-Monitor V2 recording context, WPF/Win32 DPI, physical dimensions for every connected monitor, full-window capture target and edge click mapping, window resize safety, Windows Graphics Capture, GPU crop and per-frame completion barrier, encoder-ready timeline start, label background blur and high-quality output scaling, H.264 MP4, decoded frames, pause/resume, stop/save, paused/immediate cancellation, duration limit, fractional FPS, label composition, keyboard/mouse hook start/early-stop/cleanup, keystroke and mouse-click privacy/pause/fade composition and preview parity.\nVideo: {video.Width}x{video.Height}, {video.Duration.TotalSeconds:F3}s; active clock: {elapsed.TotalSeconds:F3}s.\nScaled video: {scaled.Width}x{scaled.Height}.\nLabel mean channel error: {difference:F2}.\n{path}\n{labelledPath}\n{scaledPath}\n");
        }
        finally
        {
            if (recording is not null)
            {
                recording.Stop();
                try { await recording.Completion.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (Exception) when (recording.Completion.IsCompleted) { }
            }
            target.Close();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
