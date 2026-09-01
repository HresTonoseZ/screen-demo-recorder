using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder;

internal sealed class RegionBoundary : IDisposable
{
    private readonly List<Window> strips = [];
    private readonly List<PixelRect> expectedBounds = [];

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
                    Background = RgbaColor.ToBrush(color),
                    Width = 1, Height = 1, Opacity = show ? 0 : 1,
                    Content = new Border { Background = RgbaColor.ToBrush(color), IsHitTestVisible = false },
                };
                strips.Add(strip);
                expectedBounds.Add(rect);
                if (show) strip.Show();
                else new System.Windows.Interop.WindowInteropHelper(strip).EnsureHandle();
                NativeDesktop.Place(strip, rect, true, requireCaptureExclusion: false);
                _ = NativeDesktop.TryExclude(strip);
                if (show) strip.Opacity = 1;
            }
        }
        catch { Dispose(); throw; }
    }

    public bool IsExcluded => strips.All(NativeDesktop.IsExcluded);
    public bool IsVisible => strips.Count == expectedBounds.Count && strips.All(strip => strip.IsVisible);
    public bool HasExpectedBounds => strips.Count == expectedBounds.Count &&
        strips.Zip(expectedBounds).All(pair => NativeDesktop.WindowBounds(pair.First) == pair.Second);
    public bool IsPassive => strips.Count == expectedBounds.Count && strips.All(NativeDesktop.IsPassiveOverlay);

    public void Dispose()
    {
        foreach (var strip in strips) strip.Close();
        strips.Clear();
        expectedBounds.Clear();
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
