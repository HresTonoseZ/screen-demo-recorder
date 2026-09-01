using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder.Capture;

internal static class LiveOverlayRecordingChecks
{
    public static async Task RunAsync(DisplayInfo display, DesktopWindowInfo target, string directory)
    {
        var item = GraphicsInterop.ForMonitor(display.Monitor);
        var crop = new PixelRect(target.Bounds.X - display.Bounds.X, target.Bounds.Y - display.Bounds.Y,
            target.Bounds.Width, target.Bounds.Height);
        Require(RegionGeometry.Fit(crop, item.Size.Width, item.Size.Height, 2) == crop,
            "The live-overlay test window does not fit the monitor capture item.");
        var profile = new RecorderProfile();
        profile.Output.Directory = directory;
        profile.Output.FilenameTemplate = "live-overlay-check-{counter}";
        profile.Output.Width = crop.Width;
        profile.Output.GifPaletteColors = 256;
        profile.Output.GifFrameStep = 1;
        profile.Output.GifDither = false;
        profile.Capture.Source = CaptureSource.Region;
        profile.Capture.ShowCursor = false;
        profile.Capture.MaximumDurationSeconds = 2;
        profile.Capture.GifFps = 60;
        profile.Capture.HighlightClicks = true;
        profile.Overlays.Label.Enabled = false;
        profile.Overlays.Keystrokes.Enabled = true;
        profile.Overlays.Keystrokes.DisplayMode = KeystrokeDisplayMode.AllKeys;
        profile.Overlays.Keystrokes.HideNormalTyping = false;
        profile.Overlays.Desktop.ShowKeystrokes = true;
        profile.Overlays.Desktop.ShowMouseClicks = true;

        using var boundary = new RegionBoundary(target.Bounds, "#EE4B5FFF", 3);
        using var overlay = new DesktopOverlayWindow(target.Bounds, profile.Overlays, profile.Capture);
        Require(!overlay.IsExcludedFromCapture, "The live overlay unexpectedly uses capture-affinity.");
        Require(!overlay.HasCaptureSizedSurface, "The live overlay created a capture-sized layered surface.");
        Require(boundary.IsVisible && boundary.IsPassive && !boundary.IsExcluded,
            "The recording boundary is not visible, passive or capture-affinity free.");
        NativeDesktop.FlushComposition();
        var recording = new Mp4Recording(item, crop, profile, 60, null,
            captureKeyboardInput: false, captureMouseInput: false);
        try
        {
            await recording.Ready.WaitAsync(TimeSpan.FromSeconds(15));
            for (var index = 0; index < 6; index++)
            {
                overlay.AddKeystrokeForChecks(0x41 + index);
                overlay.AddMouseClickForChecks(
                    target.Bounds.X + target.Bounds.Width / 3 + index * 12,
                    target.Bounds.Y + target.Bounds.Height / 2,
                    index % 2 == 0 ? MouseClickButton.Left : MouseClickButton.Right);
                await Task.Delay(180);
            }
            Require(boundary.IsVisible && boundary.HasExpectedBounds,
                "The recording boundary disappeared or moved while overlays were updating.");
            Require(overlay.VisibleSurfaceCount > 0 && !overlay.HasCaptureSizedSurface,
                "Dynamic live overlays were not split into small visible surfaces.");
            var mp4Path = await recording.Completion.WaitAsync(TimeSpan.FromSeconds(20));
            Require(mp4Path is not null, "The live-overlay monitor recording was not saved.");
            var gifPath = await GifExport.RunAsync(mp4Path!, new PixelRect(0, 0, crop.Width, crop.Height), profile);
            using var stream = File.OpenRead(gifPath);
            var gif = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Require(gif.Frames.Count > 1, "The live-overlay recording contains too few frames.");
            foreach (var (frame, index) in gif.Frames.Select((frame, index) => (frame, index)))
            {
                var color = new byte[4];
                new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0)
                    .CopyPixels(new System.Windows.Int32Rect(5, 5, 1, 1), color, 4, 0);
                Require(color[2] >= 80 && color[2] >= color[1] + 20 && color[2] >= color[0] + 20,
                    $"Live overlay produced a black or unexpected frame at sample {index}: {string.Join(',', color)}.");
            }
        }
        finally
        {
            recording.Stop();
            try { await recording.Completion.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (Exception) when (recording.Completion.IsCompleted) { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
