using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;

namespace ScreenDemoRecorder.Capture;

internal sealed class FfmpegLosslessEncoder : IAsyncDisposable
{
    private readonly Channel<CpuVideoFrame> frames;
    private readonly Process process;
    private readonly Task<string> errors;
    private readonly Task worker;
    private bool completed;

    public FfmpegLosslessEncoder(string executablePath, string outputPath, int width, int height, double frameRate, int capacity = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!File.Exists(executablePath)) throw new FileNotFoundException("The bundled FFmpeg executable is missing.", executablePath);
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));
        if (!double.IsFinite(frameRate) || frameRate is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(frameRate));
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (File.Exists(outputPath)) throw new IOException($"The clean intermediate already exists: {outputPath}.");

        frames = Channel.CreateBounded<CpuVideoFrame>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        process = new Process { StartInfo = CreateStartInfo(executablePath, outputPath, width, height, frameRate) };
        if (!process.Start()) throw new InvalidOperationException("FFmpeg did not start.");
        errors = process.StandardError.ReadToEndAsync();
        worker = WriteFramesAsync();
    }

    public async ValueTask WriteAsync(CpuVideoFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        try
        {
            if (completed) throw new InvalidOperationException("The lossless encoder has already completed.");
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
                throw new InvalidOperationException($"FFmpeg lossless encoding failed with exit code {process.ExitCode}: {LastLines(diagnostics)}");
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

    internal static ProcessStartInfo CreateStartInfo(string executablePath, string outputPath, int width, int height, double frameRate)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executablePath),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!,
        };
        string[] arguments =
        [
            "-hide_banner", "-nostdin", "-loglevel", "error", "-f", "rawvideo", "-pixel_format", "bgra",
            "-video_size", $"{width}x{height}", "-framerate", frameRate.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", "pipe:0", "-an", "-c:v", "ffv1", "-level", "3", "-slicecrc", "1", "-f", "matroska",
            Path.GetFullPath(outputPath),
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
                using (frame)
                {
                    await process.StandardInput.BaseStream.WriteAsync(frame.Pixels).ConfigureAwait(false);
                }
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

    private static string LastLines(string value)
    {
        var lines = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, lines.TakeLast(12));
    }
}
