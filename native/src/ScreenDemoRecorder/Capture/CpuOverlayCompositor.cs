using System.Buffers;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Overlays;

namespace ScreenDemoRecorder.Capture;

internal sealed class CpuOverlayCompositor
{
    private sealed record Raster(byte[] Pixels, int Width, int Height);

    private readonly LabelRaster? label;
    private readonly KeystrokeRenderer? keys;
    private readonly ClickRenderer? clicks;
    private readonly Raster? labelRaster;
    private readonly Dictionary<string, Raster> keycaps = [];
    private readonly Dictionary<Core.Models.MouseClickButton, Raster> clickTextures = [];
    private readonly int frameWidth;
    private readonly int frameHeight;

    public CpuOverlayCompositor(LabelRaster? renderedLabel, KeystrokeRenderer? keystrokes,
        ClickRenderer? mouseClicks, int width, int height)
    {
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));
        label = renderedLabel;
        keys = keystrokes;
        clicks = mouseClicks;
        frameWidth = width;
        frameHeight = height;
        if (label is not null) labelRaster = Copy(label.Bitmap);
        if (keys is not null)
            foreach (var cap in keys.Keycaps) keycaps.Add(cap.Key, Copy(cap.Value));
        if (clicks is not null)
            foreach (var texture in clicks.Textures) clickTextures.Add(texture.Key, Copy(texture.Value));
    }

    public void Draw(CpuVideoFrame frame, IReadOnlyList<VisibleKeystroke> entries,
        IReadOnlyList<VisibleClick> mouseClicks)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width != frameWidth || frame.Height != frameHeight)
            throw new ArgumentException("The CPU frame geometry does not match the compositor.", nameof(frame));
        if (label is not null)
        {
            if (label.BackgroundBlur > 0)
                Blur(frame.Pixels.Span, frame.Stride, label.Container, label.BackgroundBlur);
            Blend(frame.Pixels.Span, frame.Stride, labelRaster!,
                new Rect(label.Bounds.X, label.Bounds.Y, label.Bounds.Width, label.Bounds.Height), 1);
        }
        foreach (var click in clicks?.Layout(mouseClicks) ?? [])
            Blend(frame.Pixels.Span, frame.Stride, clickTextures[click.Button], click.Bounds, click.Opacity);
        foreach (var cap in keys?.Layout(entries, frameWidth, frameHeight) ?? [])
        {
            if (!keycaps.TryGetValue(cap.Key, out var raster))
            {
                raster = Copy(keys!.Keycaps[cap.Key]);
                keycaps.Add(cap.Key, raster);
            }
            Blend(frame.Pixels.Span, frame.Stride, raster, cap.Bounds, cap.Opacity);
        }
    }

    internal static void Blend(Span<byte> destination, int destinationStride, BitmapSource source,
        Rect destinationBounds, double opacity) =>
        Blend(destination, destinationStride, Copy(source), destinationBounds, opacity);

    internal static void Blur(Span<byte> pixels, int stride, Core.Models.PixelRect bounds, int radius)
    {
        if (radius <= 0 || stride < 4 || pixels.Length % stride != 0) return;
        var frameWidth = stride / 4;
        var frameHeight = pixels.Length / stride;
        var left = Math.Clamp(bounds.X, 0, frameWidth);
        var top = Math.Clamp(bounds.Y, 0, frameHeight);
        var right = Math.Clamp(bounds.Right, 0, frameWidth);
        var bottom = Math.Clamp(bounds.Bottom, 0, frameHeight);
        var width = right - left;
        var height = bottom - top;
        if (width < 1 || height < 1) return;
        radius = Math.Min(radius, Math.Max(width, height));
        var divisor = radius * 2 + 1;
        var temporary = ArrayPool<byte>.Shared.Rent(checked(width * height * 3));
        try
        {
            for (var y = 0; y < height; y++)
            for (var channel = 0; channel < 3; channel++)
            {
                var sum = 0;
                for (var offset = -radius; offset <= radius; offset++)
                    sum += pixels[(top + y) * stride + (left + Math.Clamp(offset, 0, width - 1)) * 4 + channel];
                for (var x = 0; x < width; x++)
                {
                    temporary[(y * width + x) * 3 + channel] = (byte)((sum + divisor / 2) / divisor);
                    var removed = Math.Clamp(x - radius, 0, width - 1);
                    var added = Math.Clamp(x + radius + 1, 0, width - 1);
                    sum += pixels[(top + y) * stride + (left + added) * 4 + channel] -
                        pixels[(top + y) * stride + (left + removed) * 4 + channel];
                }
            }
            for (var x = 0; x < width; x++)
            for (var channel = 0; channel < 3; channel++)
            {
                var sum = 0;
                for (var offset = -radius; offset <= radius; offset++)
                    sum += temporary[(Math.Clamp(offset, 0, height - 1) * width + x) * 3 + channel];
                for (var y = 0; y < height; y++)
                {
                    pixels[(top + y) * stride + (left + x) * 4 + channel] = (byte)((sum + divisor / 2) / divisor);
                    var removed = Math.Clamp(y - radius, 0, height - 1);
                    var added = Math.Clamp(y + radius + 1, 0, height - 1);
                    sum += temporary[(added * width + x) * 3 + channel] -
                        temporary[(removed * width + x) * 3 + channel];
                }
            }
        }
        finally { ArrayPool<byte>.Shared.Return(temporary); }
    }

    private static Raster Copy(BitmapSource source)
    {
        var bitmap = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var stride = checked(bitmap.PixelWidth * 4);
        var pixels = new byte[checked(stride * bitmap.PixelHeight)];
        bitmap.CopyPixels(pixels, stride, 0);
        return new Raster(pixels, bitmap.PixelWidth, bitmap.PixelHeight);
    }

    private static void Blend(Span<byte> destination, int destinationStride, Raster source,
        Rect bounds, double opacity)
    {
        if (destinationStride < 4 || destination.Length % destinationStride != 0 ||
            bounds.Width <= 0 || bounds.Height <= 0 || opacity <= 0) return;
        var destinationWidth = destinationStride / 4;
        var destinationHeight = destination.Length / destinationStride;
        var left = Math.Max(0, (int)Math.Floor(bounds.Left));
        var top = Math.Max(0, (int)Math.Floor(bounds.Top));
        var right = Math.Min(destinationWidth, (int)Math.Ceiling(bounds.Right));
        var bottom = Math.Min(destinationHeight, (int)Math.Ceiling(bounds.Bottom));
        if (left >= right || top >= bottom) return;
        var globalOpacity = (int)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        Span<int> sample = stackalloc int[4];
        for (var y = top; y < bottom; y++)
        {
            var sourceY = Math.Clamp(((y + 0.5 - bounds.Top) * source.Height / bounds.Height) - 0.5, 0, source.Height - 1);
            var y0 = (int)Math.Floor(sourceY);
            var y1 = Math.Min(source.Height - 1, y0 + 1);
            var fy = sourceY - y0;
            for (var x = left; x < right; x++)
            {
                var sourceX = Math.Clamp(((x + 0.5 - bounds.Left) * source.Width / bounds.Width) - 0.5, 0, source.Width - 1);
                var x0 = (int)Math.Floor(sourceX);
                var x1 = Math.Min(source.Width - 1, x0 + 1);
                var fx = sourceX - x0;
                var destinationOffset = y * destinationStride + x * 4;
                for (var channel = 0; channel < 4; channel++)
                {
                    var topSample = Lerp(source.Pixels[(y0 * source.Width + x0) * 4 + channel],
                        source.Pixels[(y0 * source.Width + x1) * 4 + channel], fx);
                    var bottomSample = Lerp(source.Pixels[(y1 * source.Width + x0) * 4 + channel],
                        source.Pixels[(y1 * source.Width + x1) * 4 + channel], fx);
                    sample[channel] = (int)Math.Round(topSample + (bottomSample - topSample) * fy);
                }
                var alpha = (sample[3] * globalOpacity + 127) / 255;
                if (alpha == 0) continue;
                for (var channel = 0; channel < 3; channel++)
                {
                    var foreground = (sample[channel] * globalOpacity + 127) / 255;
                    destination[destinationOffset + channel] = (byte)Math.Min(255,
                        foreground + (destination[destinationOffset + channel] * (255 - alpha) + 127) / 255);
                }
                destination[destinationOffset + 3] = 255;
            }
        }
    }

    private static double Lerp(byte first, byte second, double amount) => first + (second - first) * amount;
}
