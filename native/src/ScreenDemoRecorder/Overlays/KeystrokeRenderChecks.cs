using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder.Overlays;

internal static class KeystrokeRenderChecks
{
    public static void Run(string directory)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(LabelRenderer.Brush("#202A3CFF"), null, new Rect(0, 0, 520, 320));
            var y = 16;
            foreach (var style in new[] { KeystrokeStylePreset.Dark, KeystrokeStylePreset.Light, KeystrokeStylePreset.Accent, KeystrokeStylePreset.Minimal })
            {
                var settings = new KeystrokeOverlaySettings { Enabled = true, Style = style, Anchor = OverlayAnchor.TopLeft, OffsetX = 0, OffsetY = 0 };
                var renderer = new KeystrokeRenderer(settings);
                Require(renderer.Keycaps.Count == 0, "Live keycaps must be created lazily instead of blocking the UI thread.");
                VisibleKeystroke[] entries = [new(new KeyChord(["Ctrl", "Shift", "S"]), 1), new(new KeyChord(["Alt", "Tab"]), 0.5)];
                foreach (var anchor in Enum.GetValues<OverlayAnchor>())
                foreach (var size in new[] { (32, 32), (320, 180), (360, 640), (1920, 1080) })
                {
                    settings.Anchor = anchor;
                    var layout = renderer.Layout(entries, size.Item1, size.Item2);
                    Require(layout.All(cap => cap.Bounds.Left >= 0 && cap.Bounds.Top >= 0 && cap.Bounds.Right <= size.Item1 + 0.001 && cap.Bounds.Bottom <= size.Item2 + 0.001), "Keys escaped the capture bounds.");
                }
                Require(renderer.Keycaps.Count == 5, "Only visible keycaps should be rendered on demand.");
                Require(renderer.Keycaps.Values.All(cap => cap.IsFrozen), "Keycaps cannot be passed safely to the encoding thread.");
                settings.Anchor = OverlayAnchor.TopLeft;
                var raster = renderer.RenderPreview([entries[0]], 520, 64)!;
                drawing.DrawImage(raster.Bitmap, new Rect(16, y, raster.Bounds.Width, raster.Bounds.Height));
                y += 76;
            }
        }
        var bitmap = new RenderTargetBitmap(520, 320, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        LabelRenderChecks.Save(bitmap, Path.Combine(directory, "keystroke-styles.png"));
    }

    public static BitmapSource Compose(LabelRaster? label, KeystrokeRaster? keys, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, (width + 1) & ~1, (height + 1) & ~1));
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
            drawing.DrawRectangle(Brushes.Blue, null, new Rect(0, 0, width, height));
            if (label is not null) drawing.DrawImage(label.Bitmap, new Rect(label.Bounds.X, label.Bounds.Y, label.Bounds.Width, label.Bounds.Height));
            if (keys is not null) drawing.DrawImage(keys.Bitmap, keys.Bounds);
            drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap((width + 1) & ~1, (height + 1) & ~1, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
