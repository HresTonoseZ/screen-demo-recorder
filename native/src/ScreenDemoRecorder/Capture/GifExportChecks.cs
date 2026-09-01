using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Overlays;
using Windows.Media.Editing;
using Windows.Storage;

namespace ScreenDemoRecorder.Capture;

internal static class GifExportChecks
{
    public static async Task RunAsync(string sourcePath, string keysPath, PixelRect crop, string directory)
    {
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));
        var video = await (await StorageFile.GetFileFromPathAsync(sourcePath)).Properties.GetVideoPropertiesAsync();
        var content = new PixelRect(0, 0, crop.Width, crop.Height);
        var profile = new RecorderProfile();
        profile.Output.Directory = directory;
        profile.Output.Width = 160;
        profile.Output.GifLoopCount = 3;
        profile.Output.GifFrameStep = 2;
        profile.Output.GifDither = false;
        var plan = new GifExportPlan(crop.Width, crop.Height, video.Duration, profile.Capture, profile.Output);
        var updates = new List<GifProgress>();
        var path = await GifExport.RunAsync(sourcePath, content, profile, new ImmediateProgress<GifProgress>(updates.Add));
        var gif = Load(path);
        Require(gif.Frames.Count == plan.FrameCount, "GIF resampling produced the wrong frame count.");
        Require(gif.Frames.All(f => f.PixelWidth == plan.Width && f.PixelHeight == plan.Height), "GIF resize or odd-edge crop failed.");
        Require(Enumerable.Range(0, gif.Frames.Count).All(i => Delay(gif.Frames[i]) == plan.DelayCentiseconds(i)), "GIF frame delays changed during encoding.");
        Require(updates.Count > 0 && updates[0].Frames == 1 && updates[^1].Percent == 100 &&
            updates.Select(update => update.Frames).SequenceEqual(updates.Select(update => update.Frames).Order()), "GIF progress did not reach completion in order.");
        var metadata = (BitmapMetadata)gif.Metadata;
        var repeatData = (byte[])metadata.GetQuery("/appext/Data");
        Require(repeatData.Length >= 4 && repeatData.Take(4).SequenceEqual(new byte[] { 3, 1, 3, 0 }), "GIF repeat metadata was not stored.");
        var first = Color(gif.Frames[0]); var last = Color(gif.Frames[^1]);
        var sourceFirst = await VideoColorAtAsync(sourcePath, TimeSpan.FromTicks(plan.StartTicks(0)), plan.Width, plan.Height);
        var sourceLast = await VideoColorAtAsync(sourcePath, TimeSpan.FromTicks(plan.StartTicks(plan.FrameCount - 1)), plan.Width, plan.Height);
        Require(sourceFirst[1] > 170 && sourceFirst[0] < 80 && sourceLast[0] > 170 && sourceLast[1] < 80,
            $"The source MP4 lost the green/blue recording sequence: first {string.Join(',', sourceFirst)}, last {string.Join(',', sourceLast)}.");
        Require(first.Zip(sourceFirst, (actual, expected) => Math.Abs(actual - expected)).Max() < 30 &&
            last.Zip(sourceLast, (actual, expected) => Math.Abs(actual - expected)).Max() < 30,
            $"GIF endpoint colors differ from the source MP4: GIF {string.Join(',', first)} / {string.Join(',', last)}, MP4 {string.Join(',', sourceFirst)} / {string.Join(',', sourceLast)}.");
        Require(first[1] > 170 && first[0] < 80 && last[0] > 170 && last[1] < 80, "GIF lost the green/blue recording sequence.");
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
        Require(!Directory.EnumerateFiles(directory, "*.partial.gif").Any(), "Cancelled GIF left a partial output.");
        Require((await File.ReadAllBytesAsync(path)).SequenceEqual(originalGif), "GIF cancellation changed an existing export.");
        Require(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)).SequenceEqual(sourceHash), "GIF conversion changed the source MP4.");
        using (var cancelledBeforeStart = new CancellationTokenSource())
        {
            cancelledBeforeStart.Cancel();
            var cancelled = false;
            try { await GifExport.RunAsync(sourcePath, content, profile, cancellation: cancelledBeforeStart.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Require(cancelled, "Immediate GIF cancellation was ignored.");
        }

        profile.Output.Width = 64;
        profile.Output.GifPaletteColors = 2;
        profile.Output.GifFrameStep = 1;
        profile.Capture.GifFps = 60;
        var smallPath = await GifExport.RunAsync(sourcePath, content, profile);
        var small = Load(smallPath);
        var smallPlan = new GifExportPlan(crop.Width, crop.Height, video.Duration, profile.Capture, profile.Output);
        Require(smallPath != path && (await File.ReadAllBytesAsync(path)).SequenceEqual(originalGif), "A second GIF export overwrote the first.");
        Require(small.Frames.Count == smallPlan.FrameCount, $"GIF repeated-frame sampling failed: {small.Frames.Count} instead of {smallPlan.FrameCount}.");
        Require(small.Frames.All(frame => UsedColorCount(frame) <= 2), "GIF uses more than the selected two colors in a frame.");
        var unexpectedSourceFrame = small.Frames.Select((frame, index) => (Color: Color(frame), Index: index))
            .FirstOrDefault(frame => !IsGreen(frame.Color) && !IsBlue(frame.Color));
        Require(unexpectedSourceFrame.Color is null,
            $"The recorded MP4 contains an unexpected frame at GIF sample {unexpectedSourceFrame.Index}: {string.Join(',', unexpectedSourceFrame.Color ?? [])}.");

        profile.Output.Width = crop.Width;
        profile.Output.GifDither = true;
        profile.Output.GifPaletteColors = 256;
        profile.Output.GifFrameStep = 1;
        profile.Output.GifLoopCount = 0;
        profile.Output.FinalFrameDurationMilliseconds = 1500;
        profile.Capture.GifFps = 10;
        var keyGifPath = await GifExport.RunAsync(keysPath, content, profile);
        var keyGif = Load(keyGifPath);
        Require(Delay(keyGif.Frames[^1]) == 150, "GIF last-frame hold was ignored.");
        var unexpectedKeyFrame = keyGif.Frames.Select((frame, index) => (Color: Color(frame), Index: index))
            .FirstOrDefault(frame => !IsBlue(frame.Color));
        Require(unexpectedKeyFrame.Color is null,
            $"The overlay MP4 contains an unexpected background frame at GIF sample {unexpectedKeyFrame.Index}: {string.Join(',', unexpectedKeyFrame.Color ?? [])}.");
        foreach (var (index, name) in new[] { (4, "visible"), (13, "expired") })
        {
            var actual = keyGif.Frames[index];
            using var expectedStream = File.OpenRead(Path.Combine(directory, $"keys-{name}-encoded.png"));
            var expected = BitmapFrame.Create(expectedStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var cropped = new CroppedBitmap(expected, new Int32Rect(0, 0, crop.Width, crop.Height));
            var difference = LabelRenderChecks.Pixels(actual).Zip(LabelRenderChecks.Pixels(cropped), (a, b) => Math.Abs(a - b)).Average();
            Require(difference < 10, $"GIF label and {name} keys differ from MP4: {difference:F2}.");
            LabelRenderChecks.Save(actual, Path.Combine(directory, $"gif-keys-{name}.png"));
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "gif-result.txt"),
            $"PASS: streaming GIF, resize, decoder padding/aperture, odd-edge crop, frame count, all sampled background frames, repeated-frame sampling, delays, repeats, last hold, two-color/full palette, dithering, label/keys/clicks, progress, mid-export/immediate cancellation, collision safety and MP4 preservation.\n{path}\n{keyGifPath}\n");
    }

    private static GifBitmapDecoder Load(string path)
    {
        using var stream = File.OpenRead(path);
        return new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    private static ushort Delay(BitmapFrame frame) => (ushort)((BitmapMetadata)frame.Metadata).GetQuery("/grctlext/Delay");

    private static byte[] Color(BitmapSource frame)
    {
        var color = new byte[4];
        new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(5, 5, 1, 1), color, 4, 0);
        return color;
    }

    private static bool IsGreen(byte[] color) => color[1] >= 80 && color[1] >= color[0] + 20 && color[1] >= color[2] + 20;
    private static bool IsBlue(byte[] color) => color[0] >= 80 && color[0] >= color[1] + 20 && color[0] >= color[2] + 20;

    private static async Task<byte[]> VideoColorAtAsync(string path, TimeSpan position, int width, int height)
    {
        var composition = new MediaComposition();
        composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path)));
        using var thumbnail = await composition.GetThumbnailAsync(position, width, height, VideoFramePrecision.NearestFrame);
        using var stream = thumbnail.AsStreamForRead();
        return Color(BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
    }

    private static int UsedColorCount(BitmapSource frame)
    {
        // GIF color tables can include unused padding entries; count decoded colors, not table capacity.
        var pixels = new int[frame.PixelWidth * frame.PixelHeight];
        new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0).CopyPixels(pixels, frame.PixelWidth * 4, 0);
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
