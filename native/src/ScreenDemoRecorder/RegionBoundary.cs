using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder;

internal sealed class RegionBoundary : IDisposable
{
    private readonly List<Window> strips = [];

    public RegionBoundary(DisplayInfo display, PixelRect region, string color, int thickness, bool show = true)
    {
        var x = display.Bounds.X + region.X;
        var y = display.Bounds.Y + region.Y;
        var pen = Math.Clamp(thickness, 1, Math.Min(region.Width, region.Height) / 2);
        var rectangles = new[]
        {
            new PixelRect(x, y, region.Width, pen), new PixelRect(x, y + region.Height - pen, region.Width, pen),
            new PixelRect(x, y, pen, region.Height), new PixelRect(x + region.Width - pen, y, pen, region.Height),
        };
        try
        {
            foreach (var rect in rectangles)
            {
                var strip = new Window
                {
                    Title = "Recording area boundary", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false, ShowActivated = false, Topmost = true,
                    AllowsTransparency = true, Background = RgbaColor.ToBrush(color),
                    Content = new Border { Background = RgbaColor.ToBrush(color), IsHitTestVisible = false },
                };
                strips.Add(strip);
                strip.SourceInitialized += (_, _) => NativeDesktop.Place(strip, rect, true);
                if (show) strip.Show();
                else new System.Windows.Interop.WindowInteropHelper(strip).EnsureHandle();
                if (NativeDesktop.WindowBounds(strip) != rect)
                    throw new InvalidOperationException("Windows did not apply the requested recording boundary bounds.");
            }
        }
        catch { Dispose(); throw; }
    }

    public bool IsExcluded => strips.All(NativeDesktop.IsExcluded);

    public void Dispose()
    {
        foreach (var strip in strips) strip.Close();
        strips.Clear();
    }
}

internal static class RgbaColor
{
    public static SolidColorBrush ToBrush(string rgba)
    {
        var r = Convert.ToByte(rgba.Substring(1, 2), 16);
        var g = Convert.ToByte(rgba.Substring(3, 2), 16);
        var b = Convert.ToByte(rgba.Substring(5, 2), 16);
        var a = rgba.Length == 9 ? Convert.ToByte(rgba.Substring(7, 2), 16) : (byte)255;
        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }
}
