using System.Diagnostics;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder.Capture;

internal sealed record CpuRenderProgress(int Frames, int TotalFrames, double Percent);

internal static class CpuRecordingRenderer
{
    public static async Task RenderAsync(string ffmpegPath, string cleanVideoPath, string outputPath,
        int width, int height, double frameRate, Mp4OutputPlan outputPlan, QualityPreset quality,
        RecordingOverlays overlays, OverlaySettings overlaySettings,
        IReadOnlyList<RecordingEvent> events, int expectedFrames = 0,
        Action<CpuRenderProgress>? progress = null, CancellationToken cancellationToken = default)
    {
#if RECORDER_DIAGNOSTICS
        using var diagnosticScope = DiagnosticTrace.Step("Render.MP4", false);
#endif
        ArgumentNullException.ThrowIfNull(overlays);
        ArgumentNullException.ThrowIfNull(overlaySettings);
        ArgumentNullException.ThrowIfNull(events);
        if (!File.Exists(cleanVideoPath)) throw new FileNotFoundException("The clean recording intermediate is missing.", cleanVideoPath);
        outputPath = Path.GetFullPath(outputPath);
        var partialPath = outputPath + ".partial";
        if (File.Exists(outputPath)) throw new IOException($"The final recording already exists: {outputPath}.");
        if (File.Exists(partialPath)) throw new IOException($"A recoverable partial render already exists: {partialPath}.");
        var timeline = new OfflineOverlayTimeline(events, overlaySettings);
#if RECORDER_DIAGNOSTICS
        var compositor = DiagnosticTrace.Call("Render.CreateCompositor", () => new CpuOverlayCompositor(overlays.Label, overlays.Keystrokes, overlays.Clicks, width, height));
#else
        var compositor = new CpuOverlayCompositor(overlays.Label, overlays.Keystrokes, overlays.Clicks, width, height);
#endif
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
#if RECORDER_DIAGNOSTICS
        DiagnosticTrace.Write("FFMPEG decoder pid=" + decoder.Id);
#endif
        var diagnostics = decoder.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            var bitsPerPixel = quality switch { QualityPreset.Efficient => 0.08, QualityPreset.Crisp => 0.24, _ => 0.14 };
            var bitrate = (int)Math.Clamp(outputPlan.Width * (double)outputPlan.Height * frameRate * bitsPerPixel,
                500_000, 80_000_000);
            await using var encoder = new FfmpegMp4Encoder(ffmpegPath, partialPath, width, height,
                outputPlan, frameRate, bitrate);
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
#if RECORDER_DIAGNOSTICS
                using (DiagnosticTrace.Step("Render.DrawOverlays", true)) { compositor.Draw(frame, visible.Keystrokes, visible.Clicks); }
                using (DiagnosticTrace.Step("Render.Enqueue", true)) { await encoder.WriteAsync(frame, cancellationToken).ConfigureAwait(false); }
#else
                compositor.Draw(frame, visible.Keystrokes, visible.Clicks);
                await encoder.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
#endif
                frameIndex++;
#if RECORDER_DIAGNOSTICS
                DiagnosticTrace.Count("Render.frames");
#endif
                var total = Math.Max(expectedFrames, (int)Math.Min(int.MaxValue, frameIndex));
                progress?.Invoke(new CpuRenderProgress((int)Math.Min(int.MaxValue, frameIndex), total,
                    expectedFrames <= 0 ? 0 : Math.Min(99, frameIndex * 100d / expectedFrames)));
            }
            if (frameIndex == 0) throw new InvalidDataException("The clean recording contains no video frames.");
#if RECORDER_DIAGNOSTICS
            using (DiagnosticTrace.Step("Render.WaitDecoderExit", false)) { await decoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
#else
            await decoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
#endif
            var decoderErrors = await diagnostics.ConfigureAwait(false);
#if RECORDER_DIAGNOSTICS
            DiagnosticTrace.Write($"FFMPEG decoder exit={decoder.ExitCode}; stderr={decoderErrors}");
#endif
            if (decoder.ExitCode != 0)
                throw new InvalidOperationException($"FFmpeg could not decode the clean recording: {LastLines(decoderErrors)}");
            await encoder.CompleteAsync(cancellationToken).ConfigureAwait(false);
            if (!File.Exists(partialPath) || new FileInfo(partialPath).Length == 0)
                throw new InvalidDataException("FFmpeg reported success but produced an empty MP4.");
            File.Move(partialPath, outputPath);
            progress?.Invoke(new CpuRenderProgress((int)Math.Min(int.MaxValue, frameIndex),
                Math.Max(expectedFrames, (int)Math.Min(int.MaxValue, frameIndex)), 100));
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
#if RECORDER_DIAGNOSTICS
        using var diagnosticScope = DiagnosticTrace.Step("Render.ReadDecodedFrame", true);
#endif
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
