using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Overlays;

internal sealed record LabelLineRaster(string Id, PixelRect Bounds);
internal sealed record LabelRaster(BitmapSource Bitmap, PixelRect Bounds, PixelRect Container, bool TextClipped,
    IReadOnlyList<LabelLineRaster> Lines, int BackgroundBlur);

internal static class LabelRenderer
{
    public static LabelRaster? Render(LabelOverlaySettings settings, int frameWidth, int frameHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);
        var lines = settings.Lines.Where(line => line.Enabled && !string.IsNullOrWhiteSpace(line.Text)).ToArray();
        if (!settings.Enabled || lines.Length == 0) return null;

        var width = Math.Clamp(settings.Width, 1, frameWidth);
        var paddingX = Math.Min(settings.PaddingX + settings.BorderWidth, (width - 1) / 2);
        var paddingY = settings.PaddingY + settings.BorderWidth;
        var formatted = lines.Select(line => new FormattedText(line.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(ResolveFontFamily(line.FontFamily), line.IsItalic ? FontStyles.Italic : FontStyles.Normal,
                line.IsBold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), line.Size, Brush(line.Color), 1)
        {
            MaxTextWidth = Math.Max(1, width - paddingX * 2),
            TextAlignment = line.Alignment switch { "left" => TextAlignment.Left, "right" => TextAlignment.Right, _ => TextAlignment.Center },
        }).ToArray();
        var naturalHeight = (int)Math.Ceiling(formatted.Sum(text => text.Height) + paddingY * 2 + (lines.Length - 1) * settings.LineGap);
        var height = Math.Clamp(naturalHeight, 1, frameHeight);
        var container = OverlayPlacement.Place(frameWidth, frameHeight, width, height, settings.Anchor, settings.OffsetX, settings.OffsetY);
        List<LabelLineRaster> layouts = [];
        double layoutY = paddingY;
        for (var index = 0; index < lines.Length; index++)
        {
            var top = (int)Math.Floor(layoutY);
            var nextTop = (int)Math.Floor(layoutY + formatted[index].Height + settings.LineGap);
            var textBottom = (int)Math.Ceiling(layoutY + formatted[index].Height);
            var bottom = Math.Min(height, index == lines.Length - 1 ? textBottom : Math.Min(textBottom, nextTop));
            var availableHeight = height - top;
            if (availableHeight > 0)
                layouts.Add(new LabelLineRaster(lines[index].Id, new PixelRect(container.X + paddingX, container.Y + top,
                    Math.Max(1, width - paddingX * 2), Math.Max(1, Math.Min(bottom - top, availableHeight)))));
            layoutY += formatted[index].Height + settings.LineGap;
        }
        var shadow = Brush(settings.ShadowColor);
        var containerShadowMargin = ShadowMargin(shadow.Color.A, settings.ShadowBlur, settings.ShadowOffsetX, settings.ShadowOffsetY);
        var textShadowMargin = lines.Max(line => ShadowMargin(Brush(line.ShadowColor).Color.A,
            line.ShadowBlur, line.ShadowOffsetX, line.ShadowOffsetY) + line.StrokeWidth);
        var margin = Math.Max(containerShadowMargin, textShadowMargin);
        var root = new DrawingVisual();
        if (shadow.Color.A != 0)
        {
            var shadowVisual = new DrawingVisual();
            if (settings.ShadowBlur > 0) shadowVisual.Effect = new BlurEffect { Radius = settings.ShadowBlur, KernelType = KernelType.Gaussian };
            using (var drawing = shadowVisual.RenderOpen())
                drawing.DrawRoundedRectangle(shadow, null, new Rect(margin + settings.ShadowOffsetX, margin + settings.ShadowOffsetY, width, height),
                    settings.CornerRadius, settings.CornerRadius);
            root.Children.Add(shadowVisual);
        }
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
        {
            var halfBorder = settings.BorderWidth / 2.0;
            var box = new Rect(margin + halfBorder, margin + halfBorder, Math.Max(0, width - settings.BorderWidth), Math.Max(0, height - settings.BorderWidth));
            drawing.DrawRoundedRectangle(Brush(settings.BackgroundColor), settings.BorderWidth > 0 ? new Pen(Brush(settings.BorderColor), settings.BorderWidth) : null,
                box, settings.CornerRadius, settings.CornerRadius);
        }
        root.Children.Add(background);
        var y = (double)margin + paddingY;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineShadow = Brush(line.ShadowColor);
            if (lineShadow.Color.A != 0 && (line.ShadowBlur > 0 || line.ShadowOffsetX != 0 || line.ShadowOffsetY != 0))
            {
                var shadowVisual = new DrawingVisual();
                if (line.ShadowBlur > 0) shadowVisual.Effect = new BlurEffect { Radius = line.ShadowBlur, KernelType = KernelType.Gaussian };
                using (var drawing = shadowVisual.RenderOpen())
                {
                    var position = new Point(margin + paddingX + line.ShadowOffsetX, y + line.ShadowOffsetY);
                    drawing.DrawGeometry(lineShadow,
                        line.StrokeWidth > 0 ? new Pen(Brush(line.StrokeColor), line.StrokeWidth * 2) { LineJoin = PenLineJoin.Round } : null,
                        formatted[index].BuildGeometry(position));
                }
                root.Children.Add(shadowVisual);
            }
            y += formatted[index].Height + settings.LineGap;
        }
        var text = new DrawingVisual();
        using (var drawing = text.RenderOpen())
        {
            drawing.PushClip(new RectangleGeometry(new Rect(margin, margin, width, height)));
            y = (double)margin + paddingY;
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var position = new Point(margin + paddingX, y);
                if (line.StrokeWidth > 0)
                    drawing.DrawGeometry(null, new Pen(Brush(line.StrokeColor), line.StrokeWidth * 2) { LineJoin = PenLineJoin.Round }, formatted[index].BuildGeometry(position));
                drawing.DrawText(formatted[index], position);
                y += formatted[index].Height + settings.LineGap;
            }
            drawing.Pop();
        }
        root.Children.Add(text);
        var bitmap = new RenderTargetBitmap(width + margin * 2, height + margin * 2, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return new LabelRaster(bitmap, new PixelRect(container.X - margin, container.Y - margin, bitmap.PixelWidth, bitmap.PixelHeight),
            container, naturalHeight > height, layouts, settings.BackgroundBlur);
    }

    public static SolidColorBrush Brush(string rgba)
    {
        var red = Convert.ToByte(rgba.Substring(1, 2), 16);
        var green = Convert.ToByte(rgba.Substring(3, 2), 16);
        var blue = Convert.ToByte(rgba.Substring(5, 2), 16);
        var alpha = rgba.Length == 9 ? Convert.ToByte(rgba.Substring(7, 2), 16) : byte.MaxValue;
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    internal static FontFamily ResolveFontFamily(string value)
    {
        if (!Path.IsPathFullyQualified(value) || !File.Exists(value)) return new FontFamily(value);
        try
        {
            var file = new Uri(Path.GetFullPath(value), UriKind.Absolute);
            var glyph = new GlyphTypeface(file);
            var familyName = glyph.FamilyNames.TryGetValue(CultureInfo.CurrentUICulture, out var localized)
                ? localized
                : glyph.FamilyNames.Values.First();
            var directory = Path.GetDirectoryName(file.LocalPath)
                ?? throw new InvalidDataException("The font file has no parent directory.");
            return new FontFamily(new Uri(directory + Path.DirectorySeparatorChar, UriKind.Absolute), $"./#{familyName}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or UriFormatException)
        {
            return new FontFamily("Segoe UI Variable");
        }
    }

    private static int ShadowMargin(byte alpha, int blur, int offsetX, int offsetY) =>
        alpha == 0 || (blur == 0 && offsetX == 0 && offsetY == 0)
            ? 0
            : blur * 2 + Math.Max(Math.Abs(offsetX), Math.Abs(offsetY)) + 1;
}
