namespace ScreenDemoRecorder.Core.Models;

public static class CaptureCoordinates
{
    public static PixelPoint FromViewport(double x, double y, double viewportWidth, double viewportHeight,
        int pixelWidth, int pixelHeight)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(viewportWidth) ||
            !double.IsFinite(viewportHeight) || viewportWidth <= 0 || viewportHeight <= 0 ||
            pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth), "Viewport and pixel dimensions must be finite and positive.");

        return new PixelPoint(
            (int)Math.Round(x * pixelWidth / viewportWidth),
            (int)Math.Round(y * pixelHeight / viewportHeight));
    }

    public static PixelPoint? MapScreenPoint(PixelRect screenBounds, PixelRect captureArea, PixelPoint screenPoint)
    {
        if (captureArea.Width <= 0 || captureArea.Height <= 0) return null;
        var x = (long)screenPoint.X - screenBounds.X - captureArea.X;
        var y = (long)screenPoint.Y - screenBounds.Y - captureArea.Y;
        return x >= 0 && y >= 0 && x < captureArea.Width && y < captureArea.Height
            ? new PixelPoint((int)x, (int)y)
            : null;
    }
}
