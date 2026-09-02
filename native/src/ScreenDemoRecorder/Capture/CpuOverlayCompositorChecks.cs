using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Overlays;

namespace ScreenDemoRecorder.Capture;

internal static class CpuOverlayCompositorChecks
{
    public static void Run()
    {
        ExactPremultipliedBlend();
        ClipsAndScalesAtFrameEdges();
        BlursOnlyInsideLabelContainer();
        ComposesRealLabelKeystrokesAndClicks();
    }

    private static void BlursOnlyInsideLabelContainer()
    {
        var pixels = new byte[5 * 3 * 4];
        for (var offset = 3; offset < pixels.Length; offset += 4) pixels[offset] = 255;
        pixels[(1 * 5 + 2) * 4 + 2] = 255;
        CpuOverlayCompositor.Blur(pixels, 20, new PixelRect(1, 0, 3, 3), 1);
        Require(pixels[(1 * 5 + 1) * 4 + 2] > 0 && pixels[(1 * 5 + 2) * 4 + 2] < 255,
            "The separable CPU label blur did not spread the source pixel.");
        Require(pixels[(1 * 5) * 4 + 2] == 0 && pixels[(1 * 5 + 4) * 4 + 2] == 0,
            "The CPU label blur changed pixels outside the label container.");
    }

    private static void ComposesRealLabelKeystrokesAndClicks()
    {
        const int width = 320;
        const int height = 180;
        var profile = new RecorderProfile();
        profile.Overlays.Label.Width = 160;
        profile.Overlays.Label.OffsetY = 8;
        profile.Overlays.Label.BackgroundBlur = 0;
        profile.Overlays.Keystrokes.Enabled = true;
        profile.Overlays.Keystrokes.Anchor = OverlayAnchor.TopLeft;
        profile.Overlays.Keystrokes.OffsetX = 8;
        profile.Overlays.Keystrokes.OffsetY = 8;
        profile.Capture.HighlightClicks = true;
        var overlays = new RecordingOverlays(
            LabelRenderer.Render(profile.Overlays.Label, width, height),
            new KeystrokeRenderer(profile.Overlays.Keystrokes, ["Ctrl", "K"]),
            new ClickRenderer(profile.Overlays.Clicks));
        var frame = new CpuVideoFrame(width, height, TimeSpan.Zero);
        try
        {
            for (var offset = 3; offset < frame.Pixels.Length; offset += 4) frame.Pixels.Span[offset] = 255;
            var compositor = new CpuOverlayCompositor(overlays.Label, overlays.Keystrokes, overlays.Clicks, width, height);
            compositor.Draw(frame, [new VisibleKeystroke(new KeyChord(["Ctrl", "K"]), 1)],
                [new VisibleClick(new PixelPoint(width / 2, height / 2), MouseClickButton.Left, 0.3, 1)]);
            Require(ChangedPixels(frame, new Rect(0, 0, 150, 70)) > 200,
                "The CPU compositor did not draw the real keystroke rasters.");
            Require(ChangedPixels(frame, new Rect(100, 125, 120, 55)) > 200,
                "The CPU compositor did not draw the real label raster.");
            Require(ChangedPixels(frame, new Rect(125, 55, 70, 70)) > 40,
                "The CPU compositor did not draw the real click raster.");
        }
        finally { frame.Dispose(); }
    }

    private static int ChangedPixels(CpuVideoFrame frame, Rect bounds)
    {
        var changed = 0;
        for (var y = (int)bounds.Top; y < (int)bounds.Bottom; y++)
        for (var x = (int)bounds.Left; x < (int)bounds.Right; x++)
        {
            var offset = y * frame.Stride + x * 4;
            if (frame.Pixels.Span[offset] != 0 || frame.Pixels.Span[offset + 1] != 0 || frame.Pixels.Span[offset + 2] != 0)
                changed++;
        }
        return changed;
    }

    private static void ExactPremultipliedBlend()
    {
        var destination = new byte[] { 10, 20, 30, 255 };
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Pbgra32, null,
            new byte[] { 100, 50, 25, 128 }, 4);
        CpuOverlayCompositor.Blend(destination, 4, source, new Rect(0, 0, 1, 1), 0.5);
        Require(destination.SequenceEqual(new byte[] { 57, 40, 35, 255 }),
            $"Premultiplied CPU alpha blending is wrong: {string.Join(',', destination)}.");
    }

    private static void ClipsAndScalesAtFrameEdges()
    {
        var destination = new byte[2 * 2 * 4];
        for (var offset = 3; offset < destination.Length; offset += 4) destination[offset] = 255;
        var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Pbgra32, null,
            new byte[] { 0, 0, 255, 255 }, 4);
        CpuOverlayCompositor.Blend(destination, 8, source, new Rect(-1, -1, 2, 2), 1);
        Require(destination[2] == 255 && destination[3] == 255,
            "The clipped CPU overlay did not reach the visible corner.");
        Require(destination[6] == 0 && destination[10] == 0 && destination[14] == 0,
            "The clipped CPU overlay changed pixels outside its bounds.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
