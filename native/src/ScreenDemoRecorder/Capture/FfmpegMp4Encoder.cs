using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder.Capture;

internal sealed class FfmpegMp4Encoder : IAsyncDisposable
{
    private readonly Channel<CpuVideoFrame> frames;
    private readonly Process process;
    private readonly Task<string> errors;
    private readonly Task worker;
    private bool completed;

    public FfmpegMp4Encoder(string executablePath, string outputPath, int inputWidth, int inputHeight,
        Mp4OutputPlan outputPlan, double frameRate, int bitrate, int capacity = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!File.Exists(executablePath)) throw new FileNotFoundException("The bundled FFmpeg executable is missing.", executablePath);
        if (inputWidth < 2) throw new ArgumentOutOfRangeException(nameof(inputWidth));
        if (inputHeight < 2) throw new ArgumentOutOfRangeException(nameof(inputHeight));
        ArgumentNullException.ThrowIfNull(outputPlan);
        if (outputPlan.CaptureWidth != inputWidth || outputPlan.CaptureHeight != inputHeight)
            throw new ArgumentException("The MP4 output plan does not match the input geometry.", nameof(outputPlan));
        if (!double.IsFinite(frameRate) || frameRate is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(frameRate));
        if (bitrate < 1) throw new ArgumentOutOfRangeException(nameof(bitrate));
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (File.Exists(outputPath)) throw new IOException($"The final MP4 already exists: {outputPath}.");

        frames = Channel.CreateBounded<CpuVideoFrame>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });
        process = new Process { StartInfo = CreateStartInfo(executablePath, outputPath,
            inputWidth, inputHeight, outputPlan, frameRate, bitrate) };
        if (!process.Start()) throw new InvalidOperationException("FFmpeg did not start the CPU H.264 encoder.");
        errors = process.StandardError.ReadToEndAsync();
        worker = WriteFramesAsync();
    }

    public async ValueTask WriteAsync(CpuVideoFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        try
        {
            if (completed) throw new InvalidOperationException("The MP4 encoder has already completed.");
            await frames.Writer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (completed) return;
        completed = true;
        frames.Writer.TryComplete();
        try
        {
            await worker.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var diagnostics = await errors.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"FFmpeg CPU H.264 encoding failed with exit code {process.ExitCode}: {LastLines(diagnostics)}");
        }
        catch
        {
            if (!process.HasExited) process.Kill(true);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!completed)
            {
                completed = true;
                frames.Writer.TryComplete();
                if (!process.HasExited) process.Kill(true);
                try { await worker.ConfigureAwait(false); }
                catch (Exception) when (process.HasExited) { }
                await errors.ConfigureAwait(false);
            }
        }
        finally
        {
            while (frames.Reader.TryRead(out var frame)) frame.Dispose();
            process.Dispose();
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executablePath, string outputPath,
        int inputWidth, int inputHeight, Mp4OutputPlan outputPlan, double frameRate, int bitrate)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executablePath),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!,
        };
        string[] arguments =
        [
            "-hide_banner", "-nostdin", "-loglevel", "error", "-f", "rawvideo", "-pixel_format", "bgra",
            "-video_size", $"{inputWidth}x{inputHeight}", "-framerate", frameRate.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", "pipe:0", "-an", "-vf", $"scale={outputPlan.ContentWidth}:{outputPlan.ContentHeight}:flags=lanczos," +
                $"pad={outputPlan.Width}:{outputPlan.Height}:(ow-iw)/2:(oh-ih)/2,format=yuv420p",
            "-c:v", "libopenh264", "-b:v", bitrate.ToString(CultureInfo.InvariantCulture),
            "-maxrate", (bitrate * 3L / 2).ToString(CultureInfo.InvariantCulture),
            "-bufsize", (bitrate * 2L).ToString(CultureInfo.InvariantCulture),
            "-movflags", "+faststart", "-f", "mp4", Path.GetFullPath(outputPath),
        ];
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private async Task WriteFramesAsync()
    {
        try
        {
            await foreach (var frame in frames.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                using (frame) await process.StandardInput.BaseStream.WriteAsync(frame.Pixels).ConfigureAwait(false);
            }
            await process.StandardInput.BaseStream.FlushAsync().ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (Exception error)
        {
            frames.Writer.TryComplete(error);
            if (!process.HasExited) process.Kill(true);
            throw;
        }
    }

    private static string LastLines(string value) => string.Join(Environment.NewLine,
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(12));
}
