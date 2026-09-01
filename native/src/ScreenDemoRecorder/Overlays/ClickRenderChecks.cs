using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder.Overlays;

internal static class ClickRenderChecks
{
    public static void Run(string directory)
    {
        var settings = new ClickOverlaySettings();
        var renderer = new ClickRenderer(settings);
        Require(renderer.Textures.Values.All(texture => texture.IsFrozen), "Mouse-click textures cannot be passed safely to the encoding thread.");
        VisibleClick[] clicks =
        [
            new(new PixelPoint(90, 90), MouseClickButton.Left, 0, settings.Opacity),
            new(new PixelPoint(220, 90), MouseClickButton.Right, 0.5, settings.Opacity / 2),
        ];
        var layout = renderer.Layout(clicks);
        Require(layout.Length == 2 && Math.Abs(layout[0].Bounds.X + layout[0].Bounds.Width / 2 - 90) < 0.001 &&
            Math.Abs(layout[1].Bounds.Y + layout[1].Bounds.Height / 2 - 90) < 0.001,
            "Mouse-click rings moved away from their pointer positions.");
        Require(layout[1].Bounds.Width > layout[0].Bounds.Width && layout[1].Opacity < layout[0].Opacity,
            "Mouse-click rings do not expand and fade over time.");
        Require(renderer.Textures.Values.All(texture => LabelRenderChecks.Pixels(texture)
                .Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0)),
            "Mouse-click preview rendered no pixels.");
        var bitmap = Compose(null, null, renderer, clicks, 320, 180);
        LabelRenderChecks.Save(bitmap, Path.Combine(directory, "click-styles.png"));
    }

    public static BitmapSource Compose(LabelRaster? label, KeystrokeRaster? keys, ClickRenderer renderer,
        IReadOnlyList<VisibleClick> clicks, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, (width + 1) & ~1, (height + 1) & ~1));
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
            drawing.DrawRectangle(Brushes.Blue, null, new Rect(0, 0, width, height));
            if (label is not null) drawing.DrawImage(label.Bitmap, new Rect(label.Bounds.X, label.Bounds.Y, label.Bounds.Width, label.Bounds.Height));
            foreach (var click in renderer.Layout(clicks))
            {
                drawing.PushOpacity(click.Opacity);
                drawing.DrawImage(renderer.Textures[click.Button], click.Bounds);
                drawing.Pop();
            }
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
