using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder.Capture;

internal static class RecordingRecovery
{
    public static string[] Find() => RecordingSessionStore.FindRecoverable(CpuRecordingSession.SessionRootPath);

    public static async Task<string> RenderAsync(string sessionDirectory,
        Action<CpuRenderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var opened = await RecordingSessionStore.OpenAsync(sessionDirectory, cancellationToken).ConfigureAwait(false);
        var session = opened.Store;
        var manifest = opened.Manifest;
        if (!File.Exists(session.CleanVideoPath))
            throw new FileNotFoundException("The recoverable clean recording is missing.", session.CleanVideoPath);
        var events = await session.ReadEventsAsync(cancellationToken).ConfigureAwait(false);
        var width = manifest.SourceGeometry.Width;
        var height = manifest.SourceGeometry.Height;
        var overlays = RecordingOverlayPipeline.Create(manifest.Profile, width, height);
        var outputPlan = Mp4OutputPlan.Create(width, height,
            manifest.Profile.Output.Format == OutputFormat.Mp4 ? manifest.Profile.Output.Mp4Width : 0);
        var renderPath = Path.Combine(session.DirectoryPath, "composed.mp4");
        var partialPath = renderPath + ".partial";
        if (File.Exists(renderPath)) File.Delete(renderPath);
        if (File.Exists(partialPath)) File.Delete(partialPath);
        var expectedFrames = Math.Max(1, (int)Math.Ceiling(
            TimeSpan.FromTicks(manifest.ActiveDurationTicks).TotalSeconds * manifest.FrameRate));
        await CpuRecordingRenderer.RenderAsync(FfmpegRuntime.RequireExecutable(), session.CleanVideoPath,
            renderPath, width, height, manifest.FrameRate, outputPlan, manifest.Profile.Output.Quality,
            overlays, manifest.Profile.Overlays, events, expectedFrames, progress, cancellationToken).ConfigureAwait(false);
        using var output = new RecordingOutput(manifest.Profile.Output,
            manifest.Profile.Overlays.Label.Lines.FirstOrDefault(line => line.Enabled)?.Text ?? "Recording");
        var temporaryOutput = output.PrepareForExternalWriter();
        File.Move(renderPath, temporaryOutput);
        var destination = output.Commit();
        if (manifest.Profile.Output.Format == OutputFormat.Gif)
        {
            var gif = await GifExport.RunAsync(destination, new PixelRect(0, 0, width, height),
                manifest.Profile, null, cancellationToken).ConfigureAwait(false);
            if (!manifest.Profile.Output.KeepSourceVideo) File.Delete(destination);
            destination = gif;
        }
        CpuRecordingSession.RemoveSession(session.DirectoryPath);
        return destination;
    }
}
