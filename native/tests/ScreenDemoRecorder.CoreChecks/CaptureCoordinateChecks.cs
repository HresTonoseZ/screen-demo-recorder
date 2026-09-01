using ScreenDemoRecorder.Core.Models;

internal static class CaptureCoordinateChecks
{
    public static void Run()
    {
        var leftDisplay = new PixelRect(-2560, -180, 2560, 1440);
        var region = new PixelRect(320, 140, 1280, 720);
        Check(CaptureCoordinates.MapScreenPoint(leftDisplay, region, new PixelPoint(-2240, -40)) == new PixelPoint(0, 0),
            "A region on a negative virtual-desktop origin lost its top-left pixel.");
        Check(CaptureCoordinates.MapScreenPoint(leftDisplay, region, new PixelPoint(-961, 679)) == new PixelPoint(1279, 719),
            "A region on a negative virtual-desktop origin lost its bottom-right pixel.");
        Check(CaptureCoordinates.MapScreenPoint(leftDisplay, region, new PixelPoint(-960, 679)) is null,
            "A point immediately outside the capture region was accepted.");
        Check(CaptureCoordinates.MapScreenPoint(leftDisplay, region, new PixelPoint(-2241, -40)) is null,
            "A point before the capture region was accepted.");

        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var logicalWidth = 1920 / scale;
            var logicalHeight = 1080 / scale;
            var mapped = CaptureCoordinates.FromViewport(logicalWidth * 0.25, logicalHeight * 0.75,
                logicalWidth, logicalHeight, 1920, 1080);
            Check(mapped == new PixelPoint(480, 810), $"Viewport mapping changed at {scale:P0} display scaling.");
        }

        var extreme = CaptureCoordinates.MapScreenPoint(
            new PixelRect(int.MinValue + 100, int.MinValue + 100, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080), new PixelPoint(int.MaxValue, int.MaxValue));
        Check(extreme is null, "Extreme virtual coordinates overflowed into the capture region.");
        Console.WriteLine("Capture coordinates: negative monitor origins, region edges and 100-200% viewport scaling passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
