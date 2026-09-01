using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenDemoRecorder.Capture;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Overlays;

namespace ScreenDemoRecorder;

internal sealed class DesktopOverlayWindow : IDisposable
{
    private readonly List<OverlaySurfaceWindow> surfaces = [];
    private readonly List<OverlaySurfaceWindow> keystrokeSurfaces = [];
    private readonly List<OverlaySurfaceWindow> clickSurfaces = [];
    private readonly PixelRect screenBounds;
    private readonly KeystrokeRenderer? keystrokeRenderer;
    private readonly ClickRenderer? clickRenderer;
    private readonly KeystrokeTimeline? keystrokeTimeline;
    private readonly ClickTimeline? clickTimeline;
    private readonly KeystrokeFilter? keystrokeFilter;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer timer;
    private KeyboardCapture? keyboard;
    private MouseClickCapture? mouse;
    private volatile bool disposed;

    public DesktopOverlayWindow(PixelRect bounds, OverlaySettings overlays, CaptureSettings capture)
    {
        screenBounds = bounds;
        timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        timer.Tick += RenderDynamicOverlays;

        try
        {
            if (overlays.Desktop.ShowLabel)
            {
                var label = LabelRenderer.Render(overlays.Label, bounds.Width, bounds.Height, forceEnabled: true);
                if (label is not null)
                {
                    var surface = CreateSurface("Live label overlay");
                    surface.Update(label.Bitmap, ToRect(label.Bounds), 1);
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

            if (keyboard is not null || mouse is not null) timer.Start();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool IsExcludedFromCapture => surfaces.Any(surface => surface.IsExcludedFromCapture);

    internal bool IsVisible => surfaces.Any(surface => surface.IsVisible);

    internal bool HasExpectedBounds => surfaces.Where(surface => surface.IsVisible).All(surface =>
        surface.Bounds is { } bounds && bounds.X >= screenBounds.X && bounds.Y >= screenBounds.Y &&
        bounds.Right <= screenBounds.Right && bounds.Bottom <= screenBounds.Bottom);

    internal bool IsPassive => surfaces.All(surface => surface.IsPassive);

    internal bool HasCaptureSizedSurface => surfaces.Any(surface => surface.Bounds is { } bounds &&
        bounds.Width >= screenBounds.Width && bounds.Height >= screenBounds.Height);

    internal int VisibleSurfaceCount => surfaces.Count(surface => surface.IsVisible);

    internal void AddKeystrokeForChecks(int virtualKey, KeyModifiers modifiers = KeyModifiers.None) =>
        OnKeyPressed(virtualKey, modifiers, false);

    internal void AddMouseClickForChecks(int x, int y, MouseClickButton button) => OnMouseClicked(x, y, button);

    private OverlaySurfaceWindow CreateSurface(string title)
    {
        var surface = new OverlaySurfaceWindow(screenBounds, title);
        surfaces.Add(surface);
        return surface;
    }

    private void OnKeyPressed(int virtualKey, KeyModifiers modifiers, bool altGr)
    {
        var chord = keystrokeFilter?.Filter(virtualKey, modifiers, altGr);
        if (chord is null || disposed) return;
        _ = dispatcher.BeginInvoke(() =>
        {
            if (!disposed) keystrokeTimeline?.Add(chord, clock.Elapsed);
        });
    }

    private void OnMouseClicked(int x, int y, MouseClickButton button)
    {
        if (disposed || x < screenBounds.X || y < screenBounds.Y || x >= screenBounds.Right || y >= screenBounds.Bottom) return;
        var point = new PixelPoint(x - screenBounds.X, y - screenBounds.Y);
        _ = dispatcher.BeginInvoke(() =>
        {
            if (!disposed) clickTimeline?.Add(point, button, clock.Elapsed);
        });
    }

    private void RenderDynamicOverlays(object? sender, EventArgs e)
    {
        var now = clock.Elapsed;
        if (keystrokeRenderer is not null && keystrokeTimeline is not null)
        {
            var placements = keystrokeRenderer.Layout(keystrokeTimeline.VisibleAt(now), screenBounds.Width, screenBounds.Height);
            EnsureSurfaceCount(keystrokeSurfaces, placements.Length, "Live keystroke overlay");
            for (var index = 0; index < placements.Length; index++)
            {
                var placement = placements[index];
                keystrokeSurfaces[index].Update(keystrokeRenderer.Keycaps[placement.Key], placement.Bounds, placement.Opacity);
            }
            HideUnused(keystrokeSurfaces, placements.Length);
        }

        if (clickRenderer is null || clickTimeline is null) return;
        var clicks = clickRenderer.Layout(clickTimeline.VisibleAt(now));
        EnsureSurfaceCount(clickSurfaces, clicks.Length, "Live mouse-click overlay");
        for (var index = 0; index < clicks.Length; index++)
        {
            var click = clicks[index];
            var texture = clickRenderer.Textures[click.Button];
            var maximumSize = Math.Ceiling(texture.PixelWidth * 1.3);
            var hostBounds = new Rect(
                click.Bounds.X + (click.Bounds.Width - maximumSize) / 2,
                click.Bounds.Y + (click.Bounds.Height - maximumSize) / 2,
                maximumSize,
                maximumSize);
            clickSurfaces[index].Update(texture, click.Bounds, click.Opacity, hostBounds);
        }
        HideUnused(clickSurfaces, clicks.Length);
    }

    private void EnsureSurfaceCount(List<OverlaySurfaceWindow> collection, int count, string title)
    {
        while (collection.Count < count)
        {
            var surface = CreateSurface(title);
            collection.Add(surface);
        }
    }

    private static void HideUnused(List<OverlaySurfaceWindow> collection, int used)
    {
        for (var index = used; index < collection.Count; index++) collection[index].Hide();
    }

    private static Rect ToRect(PixelRect bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        timer.Tick -= RenderDynamicOverlays;
        keyboard?.RequestStop();
        keyboard = null;
        mouse?.RequestStop();
        mouse = null;
        foreach (var surface in surfaces) surface.Dispose();
        surfaces.Clear();
        keystrokeSurfaces.Clear();
        clickSurfaces.Clear();
    }

    private sealed class OverlaySurfaceWindow : IDisposable
    {
        private readonly PixelRect screenBounds;
        private readonly Canvas canvas = new() { IsHitTestVisible = false, ClipToBounds = true };
        private readonly Image image = new() { IsHitTestVisible = false, Stretch = Stretch.Fill };
        private readonly Window window;
        private PixelRect? placedBounds;
        private bool disposed;

        public OverlaySurfaceWindow(PixelRect screenBounds, string title)
        {
            this.screenBounds = screenBounds;
            canvas.Children.Add(image);
            window = new Window
            {
                Title = title,
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
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                Content = new Viewbox { Stretch = Stretch.Fill, Child = canvas, IsHitTestVisible = false },
            };
        }

        public bool IsExcludedFromCapture => window.IsVisible && NativeDesktop.IsExcluded(window);

        public bool IsVisible => window.IsVisible;

        public bool IsPassive => !window.IsVisible || NativeDesktop.IsPassiveOverlay(window);

        public PixelRect? Bounds => window.IsVisible ? NativeDesktop.WindowBounds(window) : null;

        public void Update(BitmapSource bitmap, Rect localBounds, double opacity, Rect? hostBounds = null)
        {
            if (disposed) return;
            var host = hostBounds ?? localBounds;
            var left = Math.Max(0, (int)Math.Floor(host.Left));
            var top = Math.Max(0, (int)Math.Floor(host.Top));
            var right = Math.Min(screenBounds.Width, (int)Math.Ceiling(host.Right));
            var bottom = Math.Min(screenBounds.Height, (int)Math.Ceiling(host.Bottom));
            if (right <= left || bottom <= top || opacity <= 0)
            {
                Hide();
                return;
            }

            var width = right - left;
            var height = bottom - top;
            canvas.Width = width;
            canvas.Height = height;
            image.Source = bitmap;
            image.Width = localBounds.Width;
            image.Height = localBounds.Height;
            image.Opacity = Math.Clamp(opacity, 0, 1);
            Canvas.SetLeft(image, localBounds.Left - left);
            Canvas.SetTop(image, localBounds.Top - top);
            var physicalBounds = new PixelRect(screenBounds.X + left, screenBounds.Y + top, width, height);
            var wasVisible = window.IsVisible;
            if (!wasVisible) window.Show();
            if (!wasVisible || placedBounds != physicalBounds)
            {
                NativeDesktop.Place(window, physicalBounds, true, requireCaptureExclusion: false);
                placedBounds = physicalBounds;
            }
        }

        public void Hide()
        {
            if (disposed || !window.IsVisible) return;
            window.Hide();
            image.Source = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            window.Close();
        }
    }
}
