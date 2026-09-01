namespace ScreenDemoRecorder.Core.Models;

public static class OverlayPlacement
{
    public static PixelRect Place(int frameWidth, int frameHeight, int width, int height,
        OverlayAnchor anchor, int offsetX, int offsetY)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);
        width = Math.Clamp(width, 1, frameWidth);
        height = Math.Clamp(height, 1, frameHeight);
        var x = anchor switch
        {
            OverlayAnchor.TopRight or OverlayAnchor.CenterRight or OverlayAnchor.BottomRight => frameWidth - width - offsetX,
            OverlayAnchor.TopCenter or OverlayAnchor.Center or OverlayAnchor.BottomCenter => (frameWidth - width) / 2 + offsetX,
            _ => offsetX,
        };
        var y = anchor switch
        {
            OverlayAnchor.BottomLeft or OverlayAnchor.BottomCenter or OverlayAnchor.BottomRight => frameHeight - height - offsetY,
            OverlayAnchor.CenterLeft or OverlayAnchor.Center or OverlayAnchor.CenterRight => (frameHeight - height) / 2 + offsetY,
            _ => offsetY,
        };
        return new PixelRect(Math.Clamp(x, 0, frameWidth - width), Math.Clamp(y, 0, frameHeight - height), width, height);
    }
}
