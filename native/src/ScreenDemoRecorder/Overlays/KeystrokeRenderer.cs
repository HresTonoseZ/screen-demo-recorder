using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder.Overlays;

internal sealed record KeycapPlacement(string Key, Rect Bounds, double Opacity);
internal sealed record KeystrokeRaster(BitmapSource Bitmap, Rect Bounds);

internal sealed class KeystrokeRenderer
{
    private readonly Dictionary<string, BitmapSource> keycaps;
    public IReadOnlyDictionary<string, BitmapSource> Keycaps => keycaps;
    private readonly KeystrokeOverlaySettings settings;

    public KeystrokeRenderer(KeystrokeOverlaySettings style, IEnumerable<string>? keys = null)
    {
        settings = style;
        keycaps = (keys ?? KeystrokeFilter.KeycapNames).Distinct().ToDictionary(key => key, key => RenderKeycap(key, style));
    }

    public KeycapPlacement[] Layout(IReadOnlyList<VisibleKeystroke> entries, int width, int height)
    {
        if (entries.Count == 0) return [];
        foreach (var key in entries.SelectMany(entry => entry.Chord.Keys)) EnsureKeycap(key);
        var gap = 6 * settings.Scale;
        var rowGap = 8 * settings.Scale;
        var rowHeight = keycaps.Values.First().PixelHeight;
        var widths = entries.Select(entry => entry.Chord.Keys.Sum(key => keycaps[key].PixelWidth) + (entry.Chord.Keys.Length - 1) * gap).ToArray();
        var stackWidth = widths.Max();
        var stackHeight = rowHeight * entries.Count + rowGap * (entries.Count - 1);
        var fit = Math.Min(1, Math.Min(width / stackWidth, height / stackHeight));
        var right = settings.Anchor is OverlayAnchor.TopRight or OverlayAnchor.CenterRight or OverlayAnchor.BottomRight;
        var center = settings.Anchor is OverlayAnchor.TopCenter or OverlayAnchor.Center or OverlayAnchor.BottomCenter;
        var bottom = settings.Anchor is OverlayAnchor.BottomLeft or OverlayAnchor.BottomCenter or OverlayAnchor.BottomRight;
        // Keystroke profiles use signed screen offsets, unlike label margin offsets.
        var box = OverlayPlacement.Place(width, height, (int)Math.Ceiling(stackWidth * fit), (int)Math.Ceiling(stackHeight * fit),
            settings.Anchor, right ? -settings.OffsetX : settings.OffsetX, bottom ? -settings.OffsetY : settings.OffsetY);
        List<KeycapPlacement> result = [];
        for (var row = 0; row < entries.Count; row++)
        {
            var x = box.X + (right ? stackWidth - widths[row] : center ? (stackWidth - widths[row]) / 2 : 0) * fit;
            var y = box.Y + row * (rowHeight + rowGap) * fit;
            foreach (var key in entries[row].Chord.Keys)
            {
                var bitmap = keycaps[key];
                result.Add(new(key, new Rect(x, y, bitmap.PixelWidth * fit, bitmap.PixelHeight * fit), entries[row].Opacity));
                x += (bitmap.PixelWidth + gap) * fit;
            }
        }
        return result.ToArray();
    }

    public KeystrokeRaster? RenderPreview(IReadOnlyList<VisibleKeystroke> entries, int width, int height)
    {
        var placements = Layout(entries, width, height);
        if (placements.Length == 0) return null;
        var bounds = placements.Select(cap => cap.Bounds).Aggregate(Rect.Union);
        bounds = new Rect(Math.Floor(bounds.X), Math.Floor(bounds.Y), Math.Ceiling(bounds.Right) - Math.Floor(bounds.X), Math.Ceiling(bounds.Bottom) - Math.Floor(bounds.Y));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        foreach (var cap in placements)
        {
            var local = cap.Bounds; local.Offset(-bounds.X, -bounds.Y);
            drawing.PushOpacity(cap.Opacity);
            drawing.DrawImage(keycaps[cap.Key], local);
            drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap((int)bounds.Width, (int)bounds.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        return new(bitmap, bounds);
    }

    private void EnsureKeycap(string key)
    {
        if (keycaps.ContainsKey(key)) return;
        keycaps[key] = RenderKeycap(key, settings);
    }

    private static BitmapSource RenderKeycap(string key, KeystrokeOverlaySettings settings)
    {
        var (face, edge, text) = settings.Style switch
        {
            KeystrokeStylePreset.Light => ("#F7F8FCFA", "#A6B1C3FF", "#182235FF"),
            KeystrokeStylePreset.Accent => ("#7656EDF5", "#BEABFFFF", "#FFFFFFFF"),
            KeystrokeStylePreset.Minimal => ("#11182770", "#FFFFFF40", "#FFFFFFFF"),
            _ => ("#171E2CF5", "#71809B90", "#F4F7FFFF"),
        };
        var scale = settings.Scale;
        var label = new FormattedText(key, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 18 * scale, LabelRenderer.Brush(text), 1);
        var width = (int)Math.Ceiling(Math.Max(42 * scale, label.Width + 24 * scale));
        var height = (int)Math.Ceiling(48 * scale);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRoundedRectangle(LabelRenderer.Brush("#00000065"), null, new Rect(scale, 3 * scale, width - 2 * scale, height - 4 * scale), 8 * scale, 8 * scale);
            drawing.DrawRoundedRectangle(LabelRenderer.Brush(face), new Pen(LabelRenderer.Brush(edge), scale),
                new Rect(scale, scale, width - 2 * scale, height - 5 * scale), 8 * scale, 8 * scale);
            drawing.DrawText(label, new Point((width - label.Width) / 2, (height - 4 * scale - label.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }
}
