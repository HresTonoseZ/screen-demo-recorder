using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenDemoRecorder.Capture;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Overlays;

namespace ScreenDemoRecorder;

internal sealed class DesktopOverlayWindow : IDisposable
{
    private readonly Window window;
    private readonly Canvas frame;
    private readonly Image labelImage = new() { IsHitTestVisible = false };
    private readonly Image keystrokeImage = new() { IsHitTestVisible = false };
    private readonly Canvas clickCanvas = new() { IsHitTestVisible = false };
    private readonly PixelRect screenBounds;
    private readonly KeystrokeRenderer? keystrokeRenderer;
    private readonly ClickRenderer? clickRenderer;
    private readonly KeystrokeTimeline? keystrokeTimeline;
    private readonly ClickTimeline? clickTimeline;
    private readonly KeystrokeFilter? keystrokeFilter;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer timer;
    private KeyboardCapture? keyboard;
    private MouseClickCapture? mouse;
    private volatile bool disposed;

    public DesktopOverlayWindow(PixelRect bounds, OverlaySettings overlays, CaptureSettings capture)
    {
        screenBounds = bounds;
        frame = new Canvas { Width = bounds.Width, Height = bounds.Height, IsHitTestVisible = false };
        frame.Children.Add(labelImage);
        frame.Children.Add(clickCanvas);
        frame.Children.Add(keystrokeImage);
        window = new Window
        {
            Title = "Live recording overlays",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            Topmost = true,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Width = 1,
            Height = 1,
            Opacity = 0,
            Content = new Viewbox { Stretch = Stretch.Fill, Child = frame, IsHitTestVisible = false },
        };

        if (overlays.Desktop.ShowLabel)
        {
            var label = LabelRenderer.Render(overlays.Label, bounds.Width, bounds.Height, forceEnabled: true);
            if (label is not null)
            {
                labelImage.Source = label.Bitmap;
                labelImage.Width = label.Bitmap.PixelWidth;
                labelImage.Height = label.Bitmap.PixelHeight;
                Canvas.SetLeft(labelImage, label.Bounds.X);
                Canvas.SetTop(labelImage, label.Bounds.Y);
            }
        }

        if (overlays.Desktop.ShowKeystrokes)
        {
            keystrokeRenderer = new KeystrokeRenderer(overlays.Keystrokes);
            keystrokeTimeline = new KeystrokeTimeline(overlays.Keystrokes);
            keystrokeFilter = new KeystrokeFilter(overlays.Keystrokes, capture);
            keyboard = new KeyboardCapture(OnKeyPressed);
        }

        if (overlays.Desktop.ShowMouseClicks)
        {
            clickRenderer = new ClickRenderer(overlays.Clicks);
            clickTimeline = new ClickTimeline(overlays.Clicks);
            mouse = new MouseClickCapture(OnMouseClicked);
        }

        timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        timer.Tick += RenderDynamicOverlays;

        try
        {
            window.Show();
            NativeDesktop.Place(window, bounds, true, requireCaptureExclusion: false);
            IsExcludedFromCapture = NativeDesktop.TryExclude(window);
            window.Opacity = 1;
            if (keyboard is not null || mouse is not null) timer.Start();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool IsExcludedFromCapture { get; }

    internal bool IsVisible => window.IsVisible;

    internal bool HasExpectedBounds => NativeDesktop.WindowBounds(window) == screenBounds;

    internal bool IsPassive => NativeDesktop.IsPassiveOverlay(window);

    private void OnKeyPressed(int virtualKey, KeyModifiers modifiers, bool altGr)
    {
        var chord = keystrokeFilter?.Filter(virtualKey, modifiers, altGr);
        if (chord is null || disposed) return;
        _ = window.Dispatcher.BeginInvoke(() => keystrokeTimeline?.Add(chord, clock.Elapsed));
    }

    private void OnMouseClicked(int x, int y, MouseClickButton button)
    {
        if (disposed || x < screenBounds.X || y < screenBounds.Y || x >= screenBounds.Right || y >= screenBounds.Bottom) return;
        var point = new PixelPoint(x - screenBounds.X, y - screenBounds.Y);
        _ = window.Dispatcher.BeginInvoke(() => clickTimeline?.Add(point, button, clock.Elapsed));
    }

    private void RenderDynamicOverlays(object? sender, EventArgs e)
    {
        var now = clock.Elapsed;
        if (keystrokeRenderer is not null && keystrokeTimeline is not null)
        {
            var raster = keystrokeRenderer.RenderPreview(keystrokeTimeline.VisibleAt(now), screenBounds.Width, screenBounds.Height);
            keystrokeImage.Source = raster?.Bitmap;
            keystrokeImage.Visibility = raster is null ? Visibility.Collapsed : Visibility.Visible;
            if (raster is not null)
            {
                keystrokeImage.Width = raster.Bitmap.PixelWidth;
                keystrokeImage.Height = raster.Bitmap.PixelHeight;
                Canvas.SetLeft(keystrokeImage, raster.Bounds.X);
                Canvas.SetTop(keystrokeImage, raster.Bounds.Y);
            }
        }

        clickCanvas.Children.Clear();
        if (clickRenderer is null || clickTimeline is null) return;
        foreach (var click in clickRenderer.Layout(clickTimeline.VisibleAt(now)))
        {
            var image = new Image
            {
                Source = clickRenderer.Textures[click.Button],
                Width = click.Bounds.Width,
                Height = click.Bounds.Height,
                Opacity = click.Opacity,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(image, click.Bounds.X);
            Canvas.SetTop(image, click.Bounds.Y);
            clickCanvas.Children.Add(image);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer?.Stop();
        if (timer is not null) timer.Tick -= RenderDynamicOverlays;
        // Never wait for global hooks on the WPF dispatcher. Windows may delay a
        // hook thread while another application is busy, which previously froze
        // the checkbox and the whole main window.
        keyboard?.RequestStop();
        keyboard = null;
        mouse?.RequestStop();
        mouse = null;
        window.Close();
    }
}
