using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder.Overlays;

internal sealed record ClickPlacement(MouseClickButton Button, Rect Bounds, double Opacity);

internal sealed class ClickRenderer
{
    private readonly ClickOverlaySettings settings;

    public IReadOnlyDictionary<MouseClickButton, BitmapSource> Textures { get; }

    public ClickRenderer(ClickOverlaySettings appearance)
    {
        settings = appearance;
        Textures = new Dictionary<MouseClickButton, BitmapSource>
        {
            [MouseClickButton.Left] = RenderTexture(appearance.LeftColor, appearance),
            [MouseClickButton.Right] = RenderTexture(appearance.RightColor, appearance),
        };
    }

    public ClickPlacement[] Layout(IReadOnlyList<VisibleClick> clicks)
    {
        return clicks.Select(click =>
        {
            var eased = 1 - Math.Pow(1 - click.Progress, 3);
            var diameter = settings.Size * (0.55 + 0.75 * eased);
            var texture = Textures[click.Button];
            var renderedSize = diameter * texture.PixelWidth / settings.Size;
            return new ClickPlacement(click.Button,
                new Rect(click.Position.X - renderedSize / 2, click.Position.Y - renderedSize / 2, renderedSize, renderedSize),
                click.Opacity);
        }).ToArray();
    }

    private static BitmapSource RenderTexture(string color, ClickOverlaySettings appearance)
    {
        var margin = Math.Max(6, appearance.RingWidth * 3);
        var size = appearance.Size + margin * 2;
        var box = new Rect(margin + appearance.RingWidth / 2.0, margin + appearance.RingWidth / 2.0,
            appearance.Size - appearance.RingWidth, appearance.Size - appearance.RingWidth);
        var root = new DrawingVisual();
        var glow = new DrawingVisual { Effect = new BlurEffect { Radius = appearance.RingWidth * 1.8, KernelType = KernelType.Gaussian } };
        using (var drawing = glow.RenderOpen())
            drawing.DrawEllipse(null, new Pen(OpacityBrush(color, 0.55), appearance.RingWidth * 1.8),
                new Point(size / 2.0, size / 2.0), box.Width / 2, box.Height / 2);
        root.Children.Add(glow);
        var ring = new DrawingVisual();
        using (var drawing = ring.RenderOpen())
        {
            drawing.DrawEllipse(OpacityBrush(color, 0.13), new Pen(LabelRenderer.Brush(color), appearance.RingWidth),
                new Point(size / 2.0, size / 2.0), box.Width / 2, box.Height / 2);
            drawing.DrawEllipse(LabelRenderer.Brush(color), null, new Point(size / 2.0, size / 2.0),
                Math.Max(2, appearance.RingWidth * 0.8), Math.Max(2, appearance.RingWidth * 0.8));
        }
        root.Children.Add(ring);
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }

    private static SolidColorBrush OpacityBrush(string color, double opacity)
    {
        var source = LabelRenderer.Brush(color).Color;
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(source.A * opacity), source.R, source.G, source.B));
        brush.Freeze();
        return brush;
    }
}
