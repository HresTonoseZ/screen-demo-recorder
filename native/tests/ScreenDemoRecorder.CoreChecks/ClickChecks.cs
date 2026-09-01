using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

internal static class ClickChecks
{
    public static void Run()
    {
        var settings = new ClickOverlaySettings { DurationMilliseconds = 600, Opacity = 0.8 };
        var timeline = new ClickTimeline(settings);
        timeline.Add(new PixelPoint(120, 80), MouseClickButton.Left, TimeSpan.FromMilliseconds(100));
        Check(timeline.VisibleAt(TimeSpan.FromMilliseconds(50)).Length == 0, "A future mouse click became visible.");
        var start = timeline.VisibleAt(TimeSpan.FromMilliseconds(100)).Single();
        Check(start.Position == new PixelPoint(120, 80) && start.Button == MouseClickButton.Left && start.Progress == 0 && start.Opacity == 0.8,
            "A mouse click did not start at its exact position and opacity.");
        var middle = timeline.VisibleAt(TimeSpan.FromMilliseconds(400)).Single();
        Check(Math.Abs(middle.Progress - 0.5) < 0.001 && Math.Abs(middle.Opacity - 0.4) < 0.001,
            "Mouse-click animation timing is not deterministic.");
        Check(timeline.VisibleAt(TimeSpan.FromMilliseconds(700)).Length == 0, "An expired mouse click was retained.");
        for (var index = 0; index < 100; index++)
            timeline.Add(new PixelPoint(index, index), MouseClickButton.Right, TimeSpan.FromMilliseconds(1000 + index));
        Check(timeline.VisibleAt(TimeSpan.FromMilliseconds(1100)).Length <= 24, "The mouse-click timeline is unbounded.");
        timeline.Clear();
        Check(timeline.VisibleAt(TimeSpan.FromMilliseconds(1100)).Length == 0, "Clearing mouse-click session data failed.");
        Console.WriteLine("Mouse clicks: exact positions, buttons, bounded history, deterministic expansion and fade passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
