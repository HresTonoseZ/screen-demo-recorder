using System.IO;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Overlays;
using Windows.Graphics.Capture;
using Windows.Media.Editing;
using Windows.Storage;

namespace ScreenDemoRecorder.Capture;

internal static class KeystrokeRecordingChecks
{
    public static async Task<string> RunAsync(GraphicsCaptureItem item, PixelRect crop, RecorderProfile profile, string directory)
    {
        // Verify hook ownership without injecting keys into the user's applications or retaining live input.
        var hook = new KeyboardCapture((_, _, _) => { });
        try { await hook.Ready.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { await hook.DisposeAsync(); }
        Require(hook.Failure is null, $"Keyboard hook lifecycle failed: {hook.Failure}");
        var mouseHook = new MouseClickCapture((_, _, _) => { });
        try { await mouseHook.Ready.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { await mouseHook.DisposeAsync(); }
        Require(mouseHook.Failure is null, $"Mouse hook lifecycle failed: {mouseHook.Failure}");
        var stoppedKeyboard = new KeyboardCapture((_, _, _) => { });
        stoppedKeyboard.RequestStop();
        await stoppedKeyboard.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        await stoppedKeyboard.DisposeAsync();
        Require(stoppedKeyboard.Failure is null, $"Early keyboard-hook stop failed: {stoppedKeyboard.Failure}");
        var stoppedMouse = new MouseClickCapture((_, _, _) => { });
        stoppedMouse.RequestStop();
        await stoppedMouse.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        await stoppedMouse.DisposeAsync();
        Require(stoppedMouse.Failure is null, $"Early mouse-hook stop failed: {stoppedMouse.Failure}");
        profile.Output.FilenameTemplate = "keystroke-check-{counter}";
        profile.Capture.MaximumDurationSeconds = 2;
        profile.Capture.HighlightClicks = true;
        profile.Overlays.Keystrokes = new() { Enabled = true, VisibleDurationMilliseconds = 600, FadeDurationMilliseconds = 400 };
        profile.Overlays.Clicks = new() { DurationMilliseconds = 1000 };
        var renderer = new KeystrokeRenderer(profile.Overlays.Keystrokes);
        var clickRenderer = new ClickRenderer(profile.Overlays.Clicks);
        var label = LabelRenderer.Render(profile.Overlays.Label, crop.Width, crop.Height);
        var timeline = new KeystrokeTimeline(profile.Overlays.Keystrokes);
        var clickTimeline = new ClickTimeline(profile.Overlays.Clicks);
        var recording = new Mp4Recording(item, crop, profile, 30, label, renderer, clickRenderer,
            captureKeyboardInput: false, captureMouseInput: false, screenPointMapper: point => point);
        try
        {
            await recording.Ready.WaitAsync(TimeSpan.FromSeconds(10));
            var first = recording.Elapsed;
            recording.AddKeystroke(0x43, KeyModifiers.Control);
            timeline.Add(new(["Ctrl", "C"]), first);
            recording.AddKeystroke(0x53, KeyModifiers.Control | KeyModifiers.Shift);
            timeline.Add(new(["Ctrl", "Shift", "S"]), first);
            recording.AddKeystroke(0x41, KeyModifiers.None);
            var clickPosition = new PixelPoint(crop.Width / 2, crop.Height / 2);
            recording.AddMouseClick(clickPosition.X, clickPosition.Y, MouseClickButton.Left);
            clickTimeline.Add(clickPosition, MouseClickButton.Left, first);
            await Task.Delay(250);
            recording.TogglePause();
            recording.AddKeystroke(0x73, KeyModifiers.Alt);
            recording.AddMouseClick(clickPosition.X + 40, clickPosition.Y, MouseClickButton.Right);
            await Task.Delay(250);
            recording.TogglePause();
            var path = await recording.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            Require(path is not null, "Keyboard overlays did not produce an MP4.");
            var composition = new MediaComposition();
            composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path!)));
            foreach (var (milliseconds, name) in new[] { (400, "visible"), (800, "fading"), (1300, "expired") })
            {
                var time = first + TimeSpan.FromMilliseconds(milliseconds);
                var preview = renderer.RenderPreview(timeline.VisibleAt(time), crop.Width, crop.Height);
                var expected = ClickRenderChecks.Compose(label, preview, clickRenderer, clickTimeline.VisibleAt(time), crop.Width, crop.Height);
                using var thumbnail = await composition.GetThumbnailAsync(time, expected.PixelWidth, expected.PixelHeight, VideoFramePrecision.NearestFrame);
                using var stream = thumbnail.AsStreamForRead();
                var actual = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                LabelRenderChecks.Save(expected, Path.Combine(directory, $"keys-{name}-expected.png"));
                LabelRenderChecks.Save(actual, Path.Combine(directory, $"keys-{name}-encoded.png"));
                var expectedPixels = LabelRenderChecks.Pixels(expected);
                var actualPixels = LabelRenderChecks.Pixels(actual);
                var difference = expectedPixels.Zip(actualPixels, (a, b) => Math.Abs(a - b)).Average();
                Require(difference < 6, $"Keyboard/click {name} frame differs from preview: {difference:F2}.");
            }
            return path!;
        }
        finally
        {
            recording.Stop();
            try { await recording.Completion; }
            catch (Exception) when (recording.Completion.IsCompleted) { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
