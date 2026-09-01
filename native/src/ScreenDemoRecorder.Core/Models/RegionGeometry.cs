namespace ScreenDemoRecorder.Core.Models;

public readonly record struct PixelPoint(int X, int Y);

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Contains(int x, int y) => x >= X && x <= Right && y >= Y && y <= Bottom;
    public CaptureRegion ToSavedRegion() => new() { X = X, Y = Y, Width = Width, Height = Height };
}

[Flags]
public enum RegionEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

public static class RegionGeometry
{
    public const int MinimumSize = 32;

    public static PixelRect Fit(PixelRect region, int width, int height, int minimumSize = MinimumSize)
    {
        minimumSize = Math.Max(1, minimumSize);
        var w = Math.Clamp(region.Width, Math.Min(minimumSize, width), width);
        var h = Math.Clamp(region.Height, Math.Min(minimumSize, height), height);
        return new PixelRect(Math.Clamp(region.X, 0, width - w), Math.Clamp(region.Y, 0, height - h), w, h);
    }

    public static PixelRect Move(PixelRect region, int dx, int dy, int width, int height, int snap = 0)
    {
        var moved = Fit(region with { X = region.X + dx, Y = region.Y + dy }, width, height);
        if (moved.X < snap) moved = moved with { X = 0 };
        if (moved.Y < snap) moved = moved with { Y = 0 };
        if (width - moved.Right < snap) moved = moved with { X = width - moved.Width };
        if (height - moved.Bottom < snap) moved = moved with { Y = height - moved.Height };
        return moved;
    }

    public static PixelRect Create(PixelPoint anchor, PixelPoint pointer, int width, int height,
        double? aspect = null, int minimumSize = MinimumSize)
    {
        minimumSize = Math.Max(1, minimumSize);
        var directionX = pointer.X < anchor.X ? -1 : 1;
        var directionY = pointer.Y < anchor.Y ? -1 : 1;
        var maximumWidth = directionX < 0 ? anchor.X : width - anchor.X;
        var maximumHeight = directionY < 0 ? anchor.Y : height - anchor.Y;
        var requestedWidth = Math.Abs(pointer.X - anchor.X);
        var requestedHeight = Math.Abs(pointer.Y - anchor.Y);
        double resultWidth;
        double resultHeight;
        if (aspect is not { } ratio || !double.IsFinite(ratio) || ratio <= 0)
        {
            resultWidth = Math.Clamp(requestedWidth, Math.Min(minimumSize, maximumWidth), maximumWidth);
            resultHeight = Math.Clamp(requestedHeight, Math.Min(minimumSize, maximumHeight), maximumHeight);
        }
        else
        {
            var desiredHeight = (requestedWidth * ratio + requestedHeight) / (ratio * ratio + 1);
            var maximumAspectHeight = Math.Min(maximumHeight, maximumWidth / ratio);
            var minimumAspectHeight = Math.Min(maximumAspectHeight, Math.Max(minimumSize, minimumSize / ratio));
            resultHeight = Math.Clamp(desiredHeight, minimumAspectHeight, maximumAspectHeight);
            resultWidth = resultHeight * ratio;
        }
        var pixelWidth = Math.Max(1, (int)Math.Round(resultWidth));
        var pixelHeight = Math.Max(1, (int)Math.Round(resultHeight));
        var x = directionX < 0 ? anchor.X - pixelWidth : anchor.X;
        var y = directionY < 0 ? anchor.Y - pixelHeight : anchor.Y;
        return Fit(new PixelRect(x, y, pixelWidth, pixelHeight), width, height,
            Math.Min(minimumSize, Math.Min(pixelWidth, pixelHeight)));
    }

    public static PixelRect Resize(PixelRect region, RegionEdges edges, int dx, int dy, int width, int height,
        double? aspect = null, int minimumSize = MinimumSize)
    {
        minimumSize = Math.Max(1, minimumSize);
        var hx = edges.HasFlag(RegionEdges.Left) ? -1 : edges.HasFlag(RegionEdges.Right) ? 1 : 0;
        var hy = edges.HasFlag(RegionEdges.Top) ? -1 : edges.HasFlag(RegionEdges.Bottom) ? 1 : 0;
        if (hx == 0 && hy == 0) return region;

        if (aspect is not { } ratio || !double.IsFinite(ratio) || ratio <= 0)
        {
            var x = hx < 0 ? Math.Clamp(region.X + dx, 0, region.Right - minimumSize) : region.X;
            var y = hy < 0 ? Math.Clamp(region.Y + dy, 0, region.Bottom - minimumSize) : region.Y;
            var right = hx > 0 ? Math.Clamp(region.Right + dx, region.X + minimumSize, width) : region.Right;
            var bottom = hy > 0 ? Math.Clamp(region.Bottom + dy, region.Y + minimumSize, height) : region.Bottom;
            return new PixelRect(x, y, right - x, bottom - y);
        }

        var ax = hx < 0 ? region.Right : hx > 0 ? region.X : region.X + region.Width / 2.0;
        var ay = hy < 0 ? region.Bottom : hy > 0 ? region.Y : region.Y + region.Height / 2.0;
        var maxWidth = hx < 0 ? ax : hx > 0 ? width - ax : 2 * Math.Min(ax, width - ax);
        var maxHeight = hy < 0 ? ay : hy > 0 ? height - ay : 2 * Math.Min(ay, height - ay);
        var requestedWidth = region.Width + hx * dx;
        var requestedHeight = region.Height + hy * dy;
        var targetHeight = hx == 0
            ? requestedHeight
            : hy == 0
                ? requestedWidth / ratio
                : (requestedWidth * ratio + requestedHeight) / (ratio * ratio + 1);
        var upper = Math.Min(maxHeight, maxWidth / ratio);
        var lower = Math.Min(upper, Math.Max(minimumSize, minimumSize / ratio));
        var newHeight = Math.Clamp(targetHeight, lower, upper);
        var newWidth = newHeight * ratio;
        var left = hx < 0 ? ax - newWidth : hx > 0 ? ax : ax - newWidth / 2;
        var top = hy < 0 ? ay - newHeight : hy > 0 ? ay : ay - newHeight / 2;
        return Fit(new PixelRect((int)Math.Round(left), (int)Math.Round(top),
            (int)Math.Round(newWidth), (int)Math.Round(newHeight)), width, height, minimumSize);
    }
}
