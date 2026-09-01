using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder;

internal sealed class RegionBoundary : IDisposable
{
    private readonly List<(Window Window, PixelRect Bounds)> edges = [];

    public RegionBoundary(DisplayInfo display, PixelRect region, string color, int thickness, bool show = true)
        : this(new PixelRect(display.Bounds.X + region.X, display.Bounds.Y + region.Y, region.Width, region.Height), color, thickness, show)
    {
    }

    public RegionBoundary(PixelRect bounds, string color, int thickness, bool show = true)
    {
        var pen = Math.Clamp(thickness, 1, Math.Max(1, Math.Min(bounds.Width, bounds.Height) / 2));
        var desktop = NativeDesktop.VirtualScreenBounds();
        var top = new PixelRect(bounds.X, bounds.Y - pen >= desktop.Y ? bounds.Y - pen : bounds.Y, bounds.Width, pen);
        var bottom = new PixelRect(bounds.X, bounds.Bottom + pen <= desktop.Bottom ? bounds.Bottom : bounds.Bottom - pen, bounds.Width, pen);
        var left = new PixelRect(bounds.X - pen >= desktop.X ? bounds.X - pen : bounds.X, bounds.Y, pen, bounds.Height);
        var right = new PixelRect(bounds.Right + pen <= desktop.Right ? bounds.Right : bounds.Right - pen, bounds.Y, pen, bounds.Height);
        var (brush, opacity) = RgbaColor.Parse(color);

        try
        {
            foreach (var edgeBounds in new[] { top, bottom, left, right })
            {
                var window = new Window
                {
                    Title = "Recording area boundary",
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Focusable = false,
                    Topmost = true,
                    Background = brush,
                    Opacity = opacity,
                    Width = 1,
                    Height = 1,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -32000,
                    Top = -32000,
                };
                edges.Add((window, edgeBounds));
                if (show) window.Show();
                else _ = new WindowInteropHelper(window).EnsureHandle();
                NativeDesktop.Place(window, edgeBounds, true, requireCaptureExclusion: false);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool IsExcluded => edges.Any(edge => NativeDesktop.IsExcluded(edge.Window));

    public bool IsVisible => edges.Count > 0 && edges.All(edge => edge.Window.IsVisible);

    public bool HasExpectedBounds => edges.All(edge => NativeDesktop.WindowBounds(edge.Window) == edge.Bounds);

    public bool IsPassive => edges.All(edge => NativeDesktop.IsPassiveOverlay(edge.Window));

    public void Dispose()
    {
        foreach (var edge in edges) edge.Window.Close();
        edges.Clear();
    }
}

internal static class RgbaColor
{
    public static (SolidColorBrush Brush, double Opacity) Parse(string rgba)
    {
        var r = Convert.ToByte(rgba.Substring(1, 2), 16);
        var g = Convert.ToByte(rgba.Substring(3, 2), 16);
        var b = Convert.ToByte(rgba.Substring(5, 2), 16);
        var a = rgba.Length == 9 ? Convert.ToByte(rgba.Substring(7, 2), 16) : byte.MaxValue;
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return (brush, a / 255.0);
    }
}
