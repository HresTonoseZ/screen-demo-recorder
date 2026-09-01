using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Overlays;

internal static class LabelRenderChecks
{
    public static void Run(string directory)
    {
        var settings = TestLabel();
        settings.Enabled = false;
        Require(LabelRenderer.Render(settings, 320, 180) is null, "A disabled label rendered pixels.");
        settings.Enabled = true;
        var raster = LabelRenderer.Render(settings, 320, 180)!;
        Require(raster.Container.X == 48 && raster.Container.Y == 64, "The label origin changed.");
        Require(raster.Container.Width == settings.Width, "The preview uses an arbitrary label scale.");
        Require(raster.Lines is [{ Id: var lineId, Bounds: var lineBounds }] && lineId == settings.Lines[0].Id &&
            lineBounds.X >= raster.Container.X && lineBounds.Y >= raster.Container.Y &&
            lineBounds.Right <= raster.Container.Right && lineBounds.Bottom <= raster.Container.Bottom,
            "Editable line geometry does not match the rendered label.");
        var bytes = Pixels(raster.Bitmap);
        var pixel = (4 * raster.Bitmap.PixelWidth + 4) * 4;
        Require(bytes[pixel] == 0 && bytes[pixel + 1] == 0 && Math.Abs(bytes[pixel + 2] - 128) <= 1 && bytes[pixel + 3] == 128,
            "RGBA colors were not rendered as premultiplied BGRA.");
        Save(raster.Bitmap, Path.Combine(directory, "label-raster.png"));
        var oneRowHeight = raster.Container.Height;
        settings.Lines.Add(new LabelTextLine { Text = "Second row", Size = 16, IsItalic = true });
        var twoRows = LabelRenderer.Render(settings, 320, 180)!;
        Require(twoRows.Container.Height > oneRowHeight, "Adding a row did not expand the label.");
        Require(twoRows.Lines.Count == 2 && twoRows.Lines[0].Bounds.Bottom <= twoRows.Lines[1].Bounds.Y,
            "Rendered text-row hit regions overlap or are missing.");
        settings.Lines[1].Enabled = false;
        Require(LabelRenderer.Render(settings, 320, 180)!.Container.Height == oneRowHeight, "Disabled rows still take space.");
        settings.Lines[0].StrokeWidth = 2;
        Require(!Pixels(LabelRenderer.Render(settings, 320, 180)!.Bitmap).SequenceEqual(bytes), "Text strokes did not render.");
        settings.Lines[0].Text = "A long label row that should wrap to the available width";
        settings.Width = 120;
        Require(LabelRenderer.Render(settings, 320, 180)!.Container.Height > oneRowHeight, "Long text did not wrap.");
        Require(LabelRenderer.Render(settings, 32, 32)!.TextClipped, "A clipped label was not reported.");
        settings = TestLabel(); settings.Lines.Clear();
        Require(LabelRenderer.Render(settings, 320, 180) is null, "An empty label leaves a ghost panel.");
        settings = TestLabel(); settings.BackgroundColor = "#00000000";
        var textOnly = LabelRenderer.Render(settings, 320, 180)!;
        Require(Pixels(textOnly.Bitmap)[pixel + 3] == 0, "Text-only mode still has a background.");
        settings = TestLabel();
        var withoutTextShadow = Pixels(LabelRenderer.Render(settings, 320, 180)!.Bitmap);
        settings.Lines[0].ShadowColor = "#000000B0"; settings.Lines[0].ShadowBlur = 4;
        settings.Lines[0].ShadowOffsetX = 3; settings.Lines[0].ShadowOffsetY = 3;
        var textShadow = LabelRenderer.Render(settings, 320, 180)!;
        Require(textShadow.Bounds.Width > textShadow.Container.Width && !Pixels(textShadow.Bitmap).SequenceEqual(withoutTextShadow),
            "Per-row text shadows were clipped or did not render.");
        Save(textShadow.Bitmap, Path.Combine(directory, "label-text-shadow.png"));
        settings = TestLabel();
        settings.ShadowColor = "#00000090"; settings.ShadowBlur = 8;
        var shadow = LabelRenderer.Render(settings, 320, 180)!;
        Require(shadow.Bounds.Width > shadow.Container.Width, "Shadow margins were clipped before compositing.");
        Save(shadow.Bitmap, Path.Combine(directory, "label-shadow.png"));
        var fontFile = Directory.EnumerateFiles(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "*.ttf")
            .FirstOrDefault();
        if (fontFile is not null)
        {
            var fileFamily = LabelRenderer.ResolveFontFamily(fontFile);
            Require(fileFamily.BaseUri is not null && fileFamily.Source.StartsWith("./#", StringComparison.Ordinal),
                "A legacy font-file path was not resolved as a WPF font family.");
            settings = TestLabel(); settings.Lines[0].FontFamily = fontFile;
            Require(LabelRenderer.Render(settings, 320, 180) is not null, "A label using a font file could not be rendered.");
        }
    }

    public static LabelOverlaySettings TestLabel() => new()
    {
        Anchor = OverlayAnchor.TopLeft, OffsetX = 48, OffsetY = 64, Width = 180,
        BackgroundColor = "#FF000080", BackgroundBlur = 0, BorderWidth = 0, CornerRadius = 0, ShadowColor = "#00000000",
        PaddingX = 12, PaddingY = 12,
        Lines = [new LabelTextLine { Text = "Label test", Size = 24, IsBold = true, Alignment = "center", ShadowColor = "#00000000", ShadowOffsetY = 0 }],
    };

    public static BitmapSource Compose(LabelRaster label, int width, int height, Color background)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, (width + 1) & ~1, (height + 1) & ~1));
            drawing.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
            drawing.DrawImage(label.Bitmap, new Rect(label.Bounds.X, label.Bounds.Y, label.Bounds.Width, label.Bounds.Height));
            drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap((width + 1) & ~1, (height + 1) & ~1, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }

    public static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var bytes = new byte[source.PixelWidth * source.PixelHeight * 4];
        converted.CopyPixels(bytes, source.PixelWidth * 4, 0);
        return bytes;
    }

    public static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
