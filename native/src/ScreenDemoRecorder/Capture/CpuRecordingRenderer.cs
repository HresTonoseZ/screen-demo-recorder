using System.Diagnostics;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Capture;

internal static class CpuRecordingRenderer
{
    public static async Task RenderAsync(string ffmpegPath, string cleanVideoPath, string outputPath,
        int width, int height, double frameRate, RecordingOverlays overlays, OverlaySettings overlaySettings,
        IReadOnlyList<RecordingEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(overlays);
        ArgumentNullException.ThrowIfNull(overlaySettings);
        ArgumentNullException.ThrowIfNull(events);
        if (!File.Exists(cleanVideoPath)) throw new FileNotFoundException("The clean recording intermediate is missing.", cleanVideoPath);
        outputPath = Path.GetFullPath(outputPath);
        var partialPath = outputPath + ".partial";
        if (File.Exists(outputPath)) throw new IOException($"The final recording already exists: {outputPath}.");
        if (File.Exists(partialPath)) throw new IOException($"A recoverable partial render already exists: {partialPath}.");
        var timeline = new OfflineOverlayTimeline(events, overlaySettings);
        var compositor = new CpuOverlayCompositor(overlays.Label, overlays.Keystrokes, overlays.Clicks, width, height);
        var decode = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(ffmpegPath),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(ffmpegPath))!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        string[] arguments = ["-hide_banner", "-nostdin", "-loglevel", "error", "-i", Path.GetFullPath(cleanVideoPath),
            "-map", "0:v:0", "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1"];
        foreach (var argument in arguments) decode.ArgumentList.Add(argument);
        using var decoder = Process.Start(decode) ?? throw new InvalidOperationException("FFmpeg did not start the clean-video decoder.");
        var diagnostics = decoder.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await using var encoder = new FfmpegMp4Encoder(ffmpegPath, partialPath, width, height, frameRate);
            long frameIndex = 0;
            while (true)
            {
                var frame = new CpuVideoFrame(width, height,
                    TimeSpan.FromSeconds(frameIndex / frameRate));
                if (!await ReadFrameAsync(decoder.StandardOutput.BaseStream, frame.Pixels, cancellationToken).ConfigureAwait(false))
                {
                    frame.Dispose();
                    break;
                }
                var visible = timeline.VisibleAt(frame.Timestamp);
                compositor.Draw(frame, visible.Keystrokes, visible.Clicks);
                await encoder.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                frameIndex++;
            }
            if (frameIndex == 0) throw new InvalidDataException("The clean recording contains no video frames.");
            await decoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var decoderErrors = await diagnostics.ConfigureAwait(false);
            if (decoder.ExitCode != 0)
                throw new InvalidOperationException($"FFmpeg could not decode the clean recording: {LastLines(decoderErrors)}");
            await encoder.CompleteAsync(cancellationToken).ConfigureAwait(false);
            if (!File.Exists(partialPath) || new FileInfo(partialPath).Length == 0)
                throw new InvalidDataException("FFmpeg reported success but produced an empty MP4.");
            File.Move(partialPath, outputPath);
        }
        catch
        {
            if (!decoder.HasExited) decoder.Kill(true);
            throw;
        }
    }

    private static async Task<bool> ReadFrameAsync(Stream input, Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var read = await input.ReadAsync(destination[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0) return false;
                throw new InvalidDataException("FFmpeg returned a truncated BGRA frame.");
            }
            offset += read;
        }
        return true;
    }

    private static string LastLines(string value) => string.Join(Environment.NewLine,
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(12));
}
