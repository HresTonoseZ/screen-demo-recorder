using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

internal static class LiveOverlayLifecycleChecks
{
    public static void Run()
    {
        var desktop = new DesktopOverlaySettings
        {
            ShowLabel = true,
            ShowKeystrokes = true,
            ShowMouseClicks = true,
        };
        Require(desktop is { ShowLabel: true, ShowKeystrokes: true, ShowMouseClicks: true },
            "The regression scenario must enable every live presentation preview.");

        FakeRecording? recording = new() { Elapsed = TimeSpan.FromSeconds(3) };
        var recordingTime = LiveOverlayTimeSource.Bind(recording, static session => session.Elapsed);
        recording = null;
        Require(recordingTime() == TimeSpan.FromSeconds(3),
            "The live presentation clock followed the cleared recording slot instead of the active session.");

        var recordingAvailable = true;
        var reads = 0;
        using var timeline = new LiveOverlayTimeSource(() =>
        {
            reads++;
            if (!recordingAvailable)
                throw new NullReferenceException("The completed recording is no longer available.");
            return TimeSpan.FromSeconds(3);
        });

        Require(timeline.TryGetCurrent(out var current) && current == TimeSpan.FromSeconds(3),
            "The live presentation timeline did not read the active recording time.");

        timeline.Dispose();
        recordingAvailable = false;
        Require(!timeline.TryGetCurrent(out _),
            "A delayed presentation tick remained active while GIF export was starting.");
        Require(reads == 1,
            "The disposed presentation timeline accessed the completed recording.");

        Console.WriteLine("Live presentation lifecycle: delayed ticks are inert before GIF export passed.");
    }

    private sealed class FakeRecording
    {
        public TimeSpan Elapsed { get; init; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
