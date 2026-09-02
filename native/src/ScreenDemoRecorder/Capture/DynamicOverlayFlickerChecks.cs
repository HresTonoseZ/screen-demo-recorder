using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using Windows.Graphics.Capture;

namespace ScreenDemoRecorder.Capture;

internal static class DynamicOverlayFlickerChecks
{
    public static async Task RunAsync(GraphicsCaptureItem item, PixelRect crop, string directory)
    {
        foreach (var scenario in new[]
                 {
                     new Scenario("none", false, false),
                     new Scenario("keys", true, false),
                     new Scenario("clicks", false, true),
                     new Scenario("keys-clicks", true, true),
                 })
            await RunScenarioAsync(item, crop, directory, scenario);

        await File.WriteAllTextAsync(Path.Combine(directory, "dynamic-overlay-result.txt"),
            "PASS: no-overlay, keystroke-only, click-only and combined MP4/GIF recordings retained a stable background and uninterrupted active overlays across every decoded sample.\n");
    }

    private static async Task RunScenarioAsync(GraphicsCaptureItem item, PixelRect crop, string directory, Scenario scenario)
    {
        var profile = new RecorderProfile();
        profile.Output.Directory = directory;
        profile.Output.FilenameTemplate = $"dynamic-{scenario.Name}-{{counter}}";
        profile.Output.Width = crop.Width;
        profile.Output.GifPaletteColors = 256;
        profile.Output.GifDither = false;
        profile.Capture.ShowCursor = false;
        profile.Capture.MaximumDurationSeconds = 1;
        profile.Capture.GifFps = 60;
        profile.Capture.HighlightClicks = scenario.Clicks;
        profile.Overlays.Label.Enabled = false;
        profile.Overlays.Keystrokes.Enabled = scenario.Keys;
        profile.Overlays.Keystrokes.DisplayMode = KeystrokeDisplayMode.AllKeys;
        profile.Overlays.Keystrokes.HideNormalTyping = false;
        profile.Overlays.Keystrokes.VisibleDurationMilliseconds = 700;
        profile.Overlays.Keystrokes.FadeDurationMilliseconds = 200;
        profile.Overlays.Clicks.LeftColor = "#FF4000FF";
        profile.Overlays.Clicks.DurationMilliseconds = 900;

        var overlays = RecordingOverlayPipeline.Create(profile, crop.Width, crop.Height);
        var recording = new Mp4Recording(item, crop, profile, 30,
            overlays.Label, overlays.Keystrokes, overlays.Clicks,
            captureKeyboardInput: false, captureMouseInput: false,
            screenPointMapper: point => point);
        string? mp4Path;
        try
        {
            await recording.Ready.WaitAsync(TimeSpan.FromSeconds(10));
            if (scenario.Keys) recording.AddKeystroke(0x43, KeyModifiers.Control);
            if (scenario.Clicks) recording.AddMouseClick(crop.Width / 2, crop.Height / 2, MouseClickButton.Left);
            mp4Path = await recording.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            recording.Stop();
            try { await recording.Completion; }
            catch (Exception) when (recording.Completion.IsCompleted) { }
        }

        Require(mp4Path is not null, $"The {scenario.Name} scenario did not produce an MP4.");
        var gifPath = await GifExport.RunAsync(mp4Path!, new PixelRect(0, 0, crop.Width, crop.Height), profile);
        using var stream = File.OpenRead(gifPath);
        var gif = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Require(gif.Frames.Count >= 45, $"The {scenario.Name} scenario produced too few decoded samples: {gif.Frames.Count}.");

        for (var index = 0; index < gif.Frames.Count; index++)
        {
            var (blue, other, total) = CountColors(gif.Frames[index]);
            Require(blue >= total * 0.80,
                $"The {scenario.Name} scenario flickered at decoded sample {index}: only {blue} of {total} pixels retained the blue source.");
            if (!scenario.HasOverlay)
                Require(other <= total * 0.01,
                    $"The no-overlay scenario changed unexpectedly at decoded sample {index}: {other} non-blue pixels.");
            else if (index is >= 4 and <= 12)
                Require(other >= 80,
                    $"The {scenario.Name} overlay disappeared at decoded sample {index}: only {other} overlay pixels remained.");
        }
    }

    private static (int Blue, int Other, int Total) CountColors(BitmapSource frame)
    {
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        var blue = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
            if (pixels[offset] >= 80 && pixels[offset] >= pixels[offset + 1] + 20 && pixels[offset] >= pixels[offset + 2] + 20)
                blue++;
        var total = pixels.Length / 4;
        return (blue, total - blue, total);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Scenario(string Name, bool Keys, bool Clicks)
    {
        public bool HasOverlay => Keys || Clicks;
    }
}
