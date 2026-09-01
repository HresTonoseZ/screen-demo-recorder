using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder;

public sealed class RegionSelectorWindow : Window
{
    private readonly RegionSurface surface;
    public PixelRect SelectedRegion => surface.Region;
    public bool LockAspectRatio => surface.LockAspect;
    public bool SnapToEdges => surface.SnapEdges;

    public RegionSelectorWindow(DisplayInfo display, CaptureSettings settings, SelectionSettings? selection = null)
    {
        Title = "Select Recording Area";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        var w = display.Bounds.Width;
        var h = display.Bounds.Height;
        var initial = settings.Region is { } r ? new PixelRect(r.X, r.Y, r.Width, r.Height)
            : new PixelRect(w / 6, h / 6, w * 2 / 3, h * 2 / 3);
        surface = new RegionSurface(w, h, initial, settings, selection ?? new SelectionSettings());
        var root = new Grid();
        root.Children.Add(surface);
        var commands = new WrapPanel { Orientation = Orientation.Horizontal };
        var toolbar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(36, 39, 46)),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Child = commands,
        };
        foreach (var size in new[] { (640, 360), (1280, 720), (1920, 1080) })
        {
            var preset = new Button { Content = $"{size.Item1}×{size.Item2}", Margin = new Thickness(3), IsEnabled = size.Item1 <= w && size.Item2 <= h };
            preset.Click += (_, _) => { surface.SetSize(size.Item1, size.Item2); surface.Focus(); };
            commands.Children.Add(preset);
        }
        var aspect = new CheckBox { Content = $"Lock {settings.AspectWidth}:{settings.AspectHeight}", IsChecked = settings.LockAspectRatio, Margin = new Thickness(10, 0, 10, 0) };
        aspect.Click += (_, _) => { surface.LockAspect = aspect.IsChecked == true; surface.Focus(); };
        commands.Children.Add(aspect);
        var snap = new CheckBox { Content = "Snap", IsChecked = settings.SnapToEdges, Margin = new Thickness(0, 0, 10, 0) };
        snap.Click += (_, _) => { surface.SnapEdges = snap.IsChecked == true; surface.Focus(); };
        commands.Children.Add(snap);
        var accept = new Button { Content = "Use area", Style = (Style)FindResource("PrimaryButtonStyle"), Margin = new Thickness(3) };
        accept.Click += (_, _) => DialogResult = true;
        commands.Children.Add(accept);
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(3) };
        cancel.Click += (_, _) => DialogResult = false;
        commands.Children.Add(cancel);
        root.Children.Add(toolbar);
        root.Children.Add(new TextBlock
        {
            Text = "Drag the top grip or anywhere inside to move · Drag an edge to resize · Arrows: 1 px · Shift+arrows: 10 px · Enter: accept · Esc: cancel",
            Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(230, 20, 22, 28)),
            Padding = new Thickness(12), Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
        });
        Content = root;
        SourceInitialized += (_, _) => NativeDesktop.Place(this, display.Bounds, false);
        Loaded += (_, _) => surface.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
            if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
        };
        surface.Accepted += (_, _) => DialogResult = true;
    }
}

internal sealed class RegionSurface : FrameworkElement
{
    private readonly int screenWidth, screenHeight;
    private readonly CaptureSettings settings;
    private readonly SelectionSettings selection;
    private PixelRect start;
    private Point startPoint;
    private RegionEdges edges;
    private bool drawing, dragging;
    public event EventHandler? Accepted;
    public PixelRect Region { get; private set; }
    public bool LockAspect { get; set; }
    public bool SnapEdges { get; set; }

    public RegionSurface(int width, int height, PixelRect initial, CaptureSettings capture, SelectionSettings appearance)
    {
        screenWidth = width; screenHeight = height; settings = capture; selection = appearance;
        Region = RegionGeometry.Fit(initial, width, height, capture.RegionMinimumSize);
        LockAspect = capture.LockAspectRatio;
        SnapEdges = capture.SnapToEdges;
        Focusable = true;
    }

    public void SetSize(int width, int height)
    {
        Region = RegionGeometry.Fit(Region with { Width = width, Height = height }, screenWidth, screenHeight, settings.RegionMinimumSize);
        InvalidateVisual();
    }

    private Point ToPixels(Point p)
    {
        var mapped = CaptureCoordinates.FromViewport(p.X, p.Y, ActualWidth, ActualHeight, screenWidth, screenHeight);
        return new Point(mapped.X, mapped.Y);
    }
    private Rect ScreenRect => new(Region.X * ActualWidth / screenWidth, Region.Y * ActualHeight / screenHeight,
        Region.Width * ActualWidth / screenWidth, Region.Height * ActualHeight / screenHeight);
    private Rect DragBarRect
    {
        get
        {
            var region = ScreenRect;
            if (region.Width < 18 || region.Height < 18) return Rect.Empty;
            var barWidth = Math.Min(180, Math.Max(18, region.Width - 20));
            var barHeight = Math.Min(28, Math.Max(18, region.Height - 12));
            var topMargin = Math.Min(selection.HandleSize / 2.0 + 7, Math.Max(2, region.Height - barHeight));
            return new Rect(region.X + (region.Width - barWidth) / 2, region.Y + topMargin,
                barWidth, barHeight);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var rect = ScreenRect;
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)), new RectangleGeometry(rect));
        dc.DrawGeometry(Brush(selection.DimColor), null, outside);
        var line = new Pen(Brush(selection.SelectionColor), selection.LineWidth)
        {
            DashStyle = new DashStyle([selection.DashLength / (double)selection.LineWidth,
                selection.DashGap / (double)selection.LineWidth], 0),
        };
        dc.DrawRectangle(null, line, rect);
        var handleSize = selection.HandleSize;
        foreach (var point in HandlePoints(rect))
            dc.DrawRoundedRectangle(Brush(selection.HandleColor), new Pen(Brush(selection.HandleBorderColor), selection.HandleBorderWidth),
                new Rect(point.X - handleSize / 2.0, point.Y - handleSize / 2.0, handleSize, handleSize),
                selection.HandleShape == SelectionHandleShape.Circle ? handleSize / 2.0 : 1,
                selection.HandleShape == SelectionHandleShape.Circle ? handleSize / 2.0 : 1);
        var dragBar = DragBarRect;
        if (!dragBar.IsEmpty)
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(235, 36, 39, 46)),
                new Pen(Brush(selection.SelectionColor), 1), dragBar, 7, 7);
            var grip = new Pen(Brush(selection.DimensionColor), 1.5);
            var centerX = dragBar.X + dragBar.Width / 2;
            var centerY = dragBar.Y + dragBar.Height / 2;
            foreach (var offset in new[] { -5.0, 0.0, 5.0 })
                dc.DrawLine(grip, new Point(centerX + offset, centerY - 4), new Point(centerX + offset, centerY + 4));
        }
        if (!selection.ShowDimensions) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = new FormattedText($"{Region.Width} × {Region.Height}", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), selection.DimensionSize, Brush(selection.DimensionColor), dpi);
        var tx = Math.Clamp(rect.X + (rect.Width - text.Width) / 2, 8, Math.Max(8, ActualWidth - text.Width - 20));
        var ty = Math.Clamp(rect.Bottom + 12, 80, Math.Max(80, ActualHeight - 75));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(36, 39, 46)), null,
            new Rect(tx - 8, ty - 5, text.Width + 16, text.Height + 10), 6, 6);
        dc.DrawText(text, new Point(tx, ty));
    }

    private static Point[] HandlePoints(Rect r) =>
    [new(r.Left, r.Top), new(r.Left + r.Width / 2, r.Top), new(r.Right, r.Top),
     new(r.Left, r.Top + r.Height / 2), new(r.Right, r.Top + r.Height / 2),
     new(r.Left, r.Bottom), new(r.Left + r.Width / 2, r.Bottom), new(r.Right, r.Bottom)];

    private RegionEdges HitEdges(Point p)
    {
        var r = ScreenRect;
        var hitSize = Math.Max(9, selection.HandleSize / 2.0 + 4);
        if (p.X < r.Left - hitSize || p.X > r.Right + hitSize || p.Y < r.Top - hitSize || p.Y > r.Bottom + hitSize) return RegionEdges.None;
        var result = RegionEdges.None;
        if (Math.Abs(p.X - r.Left) <= hitSize) result |= RegionEdges.Left;
        else if (Math.Abs(p.X - r.Right) <= hitSize) result |= RegionEdges.Right;
        if (Math.Abs(p.Y - r.Top) <= hitSize) result |= RegionEdges.Top;
        else if (Math.Abs(p.Y - r.Bottom) <= hitSize) result |= RegionEdges.Bottom;
        return result;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var pos = e.GetPosition(this);
        if (e.ClickCount == 2 && ScreenRect.Contains(pos)) { Accepted?.Invoke(this, EventArgs.Empty); return; }
        start = Region; startPoint = ToPixels(pos); edges = DragBarRect.Contains(pos) ? RegionEdges.None : HitEdges(pos);
        drawing = edges == RegionEdges.None && !ScreenRect.Contains(pos);
        dragging = true;
        CaptureMouse(); e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        if (!dragging)
        {
            if (DragBarRect.Contains(position)) { Cursor = Cursors.SizeAll; return; }
            var hit = HitEdges(position);
            Cursor = hit switch
            {
                RegionEdges.Left or RegionEdges.Right => Cursors.SizeWE,
                RegionEdges.Top or RegionEdges.Bottom => Cursors.SizeNS,
                RegionEdges.Left | RegionEdges.Top or RegionEdges.Right | RegionEdges.Bottom => Cursors.SizeNWSE,
                RegionEdges.Right | RegionEdges.Top or RegionEdges.Left | RegionEdges.Bottom => Cursors.SizeNESW,
                _ => ScreenRect.Contains(position) ? Cursors.SizeAll : Cursors.Cross,
            };
            return;
        }
        var p = ToPixels(position);
        var dx = (int)Math.Round(p.X - startPoint.X);
        var dy = (int)Math.Round(p.Y - startPoint.Y);
        double? ratio = LockAspect || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? (double)settings.AspectWidth / settings.AspectHeight : null;
        if (drawing)
        {
            Region = RegionGeometry.Create(new PixelPoint((int)Math.Round(startPoint.X), (int)Math.Round(startPoint.Y)),
                new PixelPoint((int)Math.Round(p.X), (int)Math.Round(p.Y)), screenWidth, screenHeight, ratio,
                Math.Min(settings.RegionMinimumSize, Math.Min(screenWidth, screenHeight)));
        }
        else if (edges == RegionEdges.None)
            Region = RegionGeometry.Move(start, dx, dy, screenWidth, screenHeight, SnapEdges ? 12 : 0);
        else
        {
            if (SnapEdges && ratio is null)
            {
                if (edges.HasFlag(RegionEdges.Left) && Math.Abs(start.X + dx) < 12) dx = -start.X;
                if (edges.HasFlag(RegionEdges.Right) && Math.Abs(screenWidth - start.Right - dx) < 12) dx = screenWidth - start.Right;
                if (edges.HasFlag(RegionEdges.Top) && Math.Abs(start.Y + dy) < 12) dy = -start.Y;
                if (edges.HasFlag(RegionEdges.Bottom) && Math.Abs(screenHeight - start.Bottom - dy) < 12) dy = screenHeight - start.Bottom;
            }
            Region = RegionGeometry.Resize(start, edges, dx, dy, screenWidth, screenHeight, ratio, settings.RegionMinimumSize);
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        dragging = false; ReleaseMouseCapture(); e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) { dragging = false; base.OnLostMouseCapture(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
        var dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
        if (dx == 0 && dy == 0) return;
        Region = RegionGeometry.Move(Region, dx, dy, screenWidth, screenHeight);
        InvalidateVisual(); e.Handled = true;
    }

    private static SolidColorBrush Brush(string rgba)
    {
        var red = Convert.ToByte(rgba.Substring(1, 2), 16);
        var green = Convert.ToByte(rgba.Substring(3, 2), 16);
        var blue = Convert.ToByte(rgba.Substring(5, 2), 16);
        var alpha = rgba.Length == 9 ? Convert.ToByte(rgba.Substring(7, 2), 16) : byte.MaxValue;
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }
}
