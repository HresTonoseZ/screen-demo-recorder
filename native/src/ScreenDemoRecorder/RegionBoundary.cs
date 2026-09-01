using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder;

internal sealed class RegionBoundary : IDisposable
{
    private readonly Window window;
    private readonly PixelRect expectedBounds;

    public RegionBoundary(DisplayInfo display, PixelRect region, string color, int thickness, bool show = true)
        : this(new PixelRect(display.Bounds.X + region.X, display.Bounds.Y + region.Y, region.Width, region.Height), color, thickness, show)
    {
    }

    public RegionBoundary(PixelRect bounds, string color, int thickness, bool show = true)
    {
        expectedBounds = bounds;
        var pen = Math.Clamp(thickness, 1, Math.Min(bounds.Width, bounds.Height) / 2);
        var frame = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            BorderBrush = RgbaColor.ToBrush(color),
            BorderThickness = new Thickness(pen),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        window = new Window
        {
            Title = "Recording area boundary",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Width = 1,
            Height = 1,
            Opacity = show ? 0 : 1,
            Content = new Viewbox { Stretch = Stretch.Fill, Child = frame, IsHitTestVisible = false },
        };
        try
        {
            if (show) window.Show();
            else new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            NativeDesktop.Place(window, bounds, true, requireCaptureExclusion: false);
            _ = NativeDesktop.TryExclude(window);
            if (show) window.Opacity = 1;
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    public bool IsExcluded => NativeDesktop.IsExcluded(window);
    public bool IsVisible => window.IsVisible;
    public bool HasExpectedBounds => NativeDesktop.WindowBounds(window) == expectedBounds;
    public bool IsPassive => NativeDesktop.IsPassiveOverlay(window);

    public void Dispose() => window.Close();
}

internal static class RgbaColor
{
    public static SolidColorBrush ToBrush(string rgba)
    {
        var r = Convert.ToByte(rgba.Substring(1, 2), 16);
        var g = Convert.ToByte(rgba.Substring(3, 2), 16);
        var b = Convert.ToByte(rgba.Substring(5, 2), 16);
        var a = rgba.Length == 9 ? Convert.ToByte(rgba.Substring(7, 2), 16) : (byte)255;
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
