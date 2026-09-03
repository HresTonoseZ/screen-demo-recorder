using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using Windows.Media.Editing;
using Windows.Storage;

namespace ScreenDemoRecorder.Capture;

internal static class GifExportChecks
{
    public static async Task RunAsync(string sourcePath, PixelRect content, string directory)
    {
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));
        var video = await (await StorageFile.GetFileFromPathAsync(sourcePath)).Properties.GetVideoPropertiesAsync();
        var profile = new RecorderProfile();
        profile.Output.Directory = directory;
        profile.Output.Width = Math.Min(160, content.Width);
        profile.Output.GifLoopCount = 3;
        profile.Output.GifFrameStep = 2;
        profile.Output.GifDither = false;
        profile.Capture.GifFps = 30;
        var plan = new GifExportPlan(content.Width, content.Height, video.Duration, profile.Capture, profile.Output);
        var updates = new List<GifProgress>();
        var path = await GifExport.RunAsync(sourcePath, content, profile,
            new ImmediateProgress<GifProgress>(updates.Add));
        var gif = Load(path);
        Require(gif.Frames.Count == plan.FrameCount, "GIF resampling produced the wrong frame count.");
        Require(gif.Frames.All(frame => frame.PixelWidth == plan.Width && frame.PixelHeight == plan.Height),
            "GIF resize changed the requested geometry.");
        Require(Enumerable.Range(0, gif.Frames.Count).All(index =>
                Delay(gif.Frames[index]) == plan.DelayCentiseconds(index)),
            "GIF frame delays changed during encoding.");
        Require(updates.Count > 0 && updates[0].Frames == 1 && updates[^1].Percent == 100 &&
            updates.Select(update => update.Frames).SequenceEqual(updates.Select(update => update.Frames).Order()),
            "GIF progress did not reach completion in order.");
        var repeatData = (byte[])((BitmapMetadata)gif.Metadata).GetQuery("/appext/Data");
        Require(repeatData.Length >= 4 && repeatData.Take(4).SequenceEqual(new byte[] { 3, 1, 3, 0 }),
            "GIF repeat metadata was not stored.");
        foreach (var index in new[] { 0, gif.Frames.Count / 2, gif.Frames.Count - 1 }.Distinct())
        {
            var expected = await VideoColorAtAsync(sourcePath, TimeSpan.FromTicks(plan.StartTicks(index)),
                plan.Width, plan.Height);
            var actual = Color(gif.Frames[index]);
            Require(actual.Zip(expected, (left, right) => Math.Abs(left - right)).Max() < 38,
                $"GIF sample {index} differs from the CPU-rendered MP4.");
        }

        var originalGif = await File.ReadAllBytesAsync(path);
        using (var cancellation = new CancellationTokenSource())
        {
            var cancelled = false;
            try
            {
                await GifExport.RunAsync(sourcePath, content, profile,
                    new ImmediateProgress<GifProgress>(_ => cancellation.Cancel()), cancellation.Token);
            }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled, "GIF cancellation did not interrupt encoding.");
        }
        Require(!Directory.EnumerateFiles(directory, "*.partial.gif").Any(),
            "Cancelled GIF export left a partial file.");
        Require((await File.ReadAllBytesAsync(path)).SequenceEqual(originalGif),
            "Cancelled GIF export changed an existing output.");
        Require(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)).SequenceEqual(sourceHash),
            "GIF conversion changed the source MP4.");
        using (var cancelledBeforeStart = new CancellationTokenSource())
        {
            cancelledBeforeStart.Cancel();
            var cancelled = false;
            try { await GifExport.RunAsync(sourcePath, content, profile, cancellation: cancelledBeforeStart.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled, "Immediate GIF cancellation was ignored.");
        }

        profile.Output.Width = Math.Min(64, content.Width);
        profile.Output.GifPaletteColors = 2;
        profile.Output.GifFrameStep = 1;
        profile.Capture.GifFps = 60;
        var smallPath = await GifExport.RunAsync(sourcePath, content, profile);
        var small = Load(smallPath);
        Require(smallPath != path, "A second GIF export overwrote the first.");
        Require(small.Frames.All(frame => UsedColorCount(frame) <= 2),
            "Two-color GIF export used more than two decoded colors.");

        profile.Output.Width = content.Width;
        profile.Output.GifDither = true;
        profile.Output.GifPaletteColors = 256;
        profile.Output.GifLoopCount = 0;
        profile.Output.FinalFrameDurationMilliseconds = 1500;
        var fullPath = await GifExport.RunAsync(sourcePath, content, profile);
        var full = Load(fullPath);
        Require(Delay(full.Frames[^1]) == 150, "GIF final-frame hold was ignored.");
        await File.WriteAllTextAsync(Path.Combine(directory, "gif-result.txt"),
            $"PASS: CPU-rendered MP4 to streaming GIF, resize, frame sampling, delays, repeats, final hold, palettes, dithering, progress, cancellation, collision safety and MP4 preservation.\n{path}\n{fullPath}\n");
    }

    private static GifBitmapDecoder Load(string path)
    {
        using var stream = File.OpenRead(path);
        return new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    private static ushort Delay(BitmapFrame frame) =>
        (ushort)((BitmapMetadata)frame.Metadata).GetQuery("/grctlext/Delay");

    private static byte[] Color(BitmapSource frame)
    {
        var color = new byte[4];
        new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0)
            .CopyPixels(new Int32Rect(frame.PixelWidth - 6, 5, 1, 1), color, 4, 0);
        return color;
    }

    private static async Task<byte[]> VideoColorAtAsync(string path, TimeSpan position, int width, int height)
    {
        var composition = new MediaComposition();
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path)));
        using var thumbnail = await composition.GetThumbnailAsync(position, width, height,
            VideoFramePrecision.NearestFrame);
        using var stream = thumbnail.AsStreamForRead();
        return Color(BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
    }

    private static int UsedColorCount(BitmapSource frame)
    {
        var pixels = new int[frame.PixelWidth * frame.PixelHeight];
        new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0)
            .CopyPixels(pixels, frame.PixelWidth * 4, 0);
        return pixels.Distinct().Count();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
