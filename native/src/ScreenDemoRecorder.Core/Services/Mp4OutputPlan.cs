namespace ScreenDemoRecorder.Core.Services;

public sealed record Mp4OutputPlan(int CaptureWidth, int CaptureHeight, int ContentWidth, int ContentHeight, int Width, int Height)
{
    public bool IsResized => ContentWidth != CaptureWidth || ContentHeight != CaptureHeight;

    public static Mp4OutputPlan Create(int captureWidth, int captureHeight, int requestedWidth)
    {
        if (captureWidth < 2 || captureHeight < 2) throw new ArgumentOutOfRangeException(nameof(captureWidth));
        if (requestedWidth != 0 && requestedWidth is < 64 or > 7680)
            throw new ArgumentOutOfRangeException(nameof(requestedWidth), "MP4 width must be Original or from 64 to 7680 pixels.");

        var widthLimit = requestedWidth == 0 ? captureWidth : Math.Min(captureWidth, requestedWidth);
        var scale = Math.Min(1, Math.Min((double)widthLimit / captureWidth,
            Math.Min(7680d / captureWidth, 7680d / captureHeight)));
        var contentWidth = Math.Clamp((int)Math.Round(captureWidth * scale), 2, 7680);
        var contentHeight = Math.Clamp((int)Math.Round(captureHeight * scale), 2, 7680);
        return new Mp4OutputPlan(captureWidth, captureHeight, contentWidth, contentHeight,
            Even(contentWidth), Even(contentHeight));
    }

    private static int Even(int value) => (value + 1) & ~1;
}
