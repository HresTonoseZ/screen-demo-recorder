using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

internal static class GifChecks
{
    public static void Run(string directory)
    {
        var capture = new CaptureSettings();
        var output = new OutputSettings { Directory = directory, Width = 960 };
        foreach (var fps in new[] { 1, 12, 23.976, 30, 50, 60 })
        foreach (var step in new[] { 1, 3, 30 })
        {
            capture.GifFps = fps; output.GifFrameStep = step;
            var duration = TimeSpan.FromSeconds(61.137);
            var plan = new GifExportPlan(1920, 1080, duration, capture, output);
            Require(plan.Width == 960 && plan.Height == 540, "GIF resizing changed the aspect ratio.");
            Require(Math.Abs(Enumerable.Range(0, plan.FrameCount).Sum(i => plan.DelayCentiseconds(i)) * .01 - duration.TotalSeconds) <= .011,
                "GIF frame-step or rounded delays changed playback duration.");
            Require(plan.StartTicks(plan.FrameCount - 1) < duration.Ticks, "A GIF frame starts after the video ends.");
        }
        capture.GifFps = 12; output.GifFrameStep = 1; output.FinalFrameDurationMilliseconds = 1500;
        var held = new GifExportPlan(321, 181, TimeSpan.FromSeconds(1), capture, output);
        Require(held.DelayCentiseconds(held.FrameCount - 1) == 150 && held.Height == 541, "Last-frame hold or odd-sized resizing failed.");
        capture.GifFps = double.NaN;
        var rejected = false;
        try { _ = new GifExportPlan(321, 181, TimeSpan.FromSeconds(1), capture, output); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "Invalid GIF FPS was accepted.");
        using (var first = new RecordingOutput(output, "Title", OutputFormat.Gif, "pair-{counter}"))
        {
            first.Stream.WriteByte(42);
            Require(Path.GetFileName(first.Commit()) == "pair-{counter}.gif", "A paired GIF reinterpreted its literal filename.");
        }
        using (var second = new RecordingOutput(output, "Title", OutputFormat.Gif, "pair-{counter}"))
            Require(Path.GetFileName(second.Commit()) == "pair-{counter}_002.gif", "GIF collision protection failed.");
        Require(File.ReadAllBytes(Path.Combine(directory, "pair-{counter}.gif")).SequenceEqual(new byte[] { 42 }), "An existing GIF was overwritten.");
        Console.WriteLine("GIF planning: aspect ratio, fractional FPS, frame-step timing, last-frame hold and safe paired output passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
