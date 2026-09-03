using System.IO;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using SharpGen.Runtime.Win32;
using Vortice.MediaFoundation;
using Vortice.WIC;
using static Vortice.MediaFoundation.MediaFactory;

namespace ScreenDemoRecorder.Capture;

internal readonly record struct GifProgress(int Frames, int TotalFrames)
{
    public double Percent => Frames * 100.0 / TotalFrames;
}

internal static class GifExport
{
    public static Task<string> RunAsync(string sourcePath, PixelRect content, RecorderProfile profile,
        IProgress<GifProgress>? progress = null, CancellationToken cancellation = default) =>
        Task.Run(() => Convert(sourcePath, content, profile, progress, cancellation), cancellation);

    private static string Convert(string sourcePath, PixelRect content, RecorderProfile profile,
        IProgress<GifProgress>? progress, CancellationToken cancellation)
    {
#if RECORDER_DIAGNOSTICS
        using var diagnosticScope = DiagnosticTrace.Step("GIF.Convert", false);
#endif
        cancellation.ThrowIfCancellationRequested();
#if RECORDER_DIAGNOSTICS
        using (DiagnosticTrace.Step("GIF.MFStartup", false)) { MFStartup().CheckError(); }
#else
        MFStartup().CheckError();
#endif
        try { return ConvertCore(sourcePath, content, profile, progress, cancellation); }
#if RECORDER_DIAGNOSTICS
        finally { using (DiagnosticTrace.Step("GIF.MFShutdown", false)) { MFShutdown().CheckError(); } }
#else
        finally { MFShutdown().CheckError(); }
#endif
    }

    private static string ConvertCore(string sourcePath, PixelRect content, RecorderProfile profile,
        IProgress<GifProgress>? progress, CancellationToken cancellation)
    {
#if RECORDER_DIAGNOSTICS
        using var diagnosticScope = DiagnosticTrace.Step("GIF.ConvertCore", false);
#endif
        using var attributes = MFCreateAttributes(1);
        attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, true).CheckError();
#if RECORDER_DIAGNOSTICS
        using var reader = DiagnosticTrace.Call("GIF.CreateSourceReader", () => MFCreateSourceReaderFromURL(Path.GetFullPath(sourcePath), attributes));
#else
        using var reader = MFCreateSourceReaderFromURL(Path.GetFullPath(sourcePath), attributes);
#endif
        reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
        using var requested = MFCreateMediaType();
        requested.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        requested.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32).CheckError();
#if RECORDER_DIAGNOSTICS
        using (DiagnosticTrace.Step("GIF.SetMediaType", false)) { reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, requested); }
#else
        reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, requested);
#endif
        using var actual = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
        var size = actual.GetUInt64(MediaTypeAttributeKeys.FrameSize);
        var width = checked((int)(size >> 32));
        var height = checked((int)(size & uint.MaxValue));
        if (content.X != 0 || content.Y != 0 || content.Width < 1 || content.Height < 1 || content.Width > width || content.Height > height)
            throw new ArgumentException("The GIF content area does not fit the source video.");
        var expectedWidth = (content.Width + 1) & ~1;
        var expectedHeight = (content.Height + 1) & ~1;
        var layout = ReadLayout(actual, expectedWidth, expectedHeight);
        var durationValue = reader.GetPresentationAttribute(SourceReaderIndex.MediaSource, PresentationDescriptionAttributeKeys.Duration);
        var duration = TimeSpan.FromTicks(checked((long)(ulong)durationValue.Value));
        var plan = new GifExportPlan(content.Width, content.Height, duration, profile.Capture, profile.Output);
        using var output = new RecordingOutput(profile.Output, "Recording", OutputFormat.Gif, Path.GetFileNameWithoutExtension(sourcePath));
        try
        {
            using (var factory = new IWICImagingFactory())
            using (var encoder = factory.CreateEncoder(ContainerFormat.Gif, output.Stream, BitmapEncoderCacheOption.NoCache))
            {
                using (var metadata = encoder.MetadataQueryWriter)
                {
                    SetMetadata(metadata, "/appext/Application", Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                    var loops = Math.Clamp(profile.Output.GifLoopCount, 0, 10_000);
                    SetMetadata(metadata, "/appext/Data", new byte[] { 3, 1, (byte)loops, (byte)(loops >> 8), 0 });
                }
                var pixels = new byte[checked(content.Width * content.Height * 4)];
                IMFSample? current = null;
                var currentLayout = layout;
                var index = 0;
                var progressClock = Stopwatch.StartNew();
                try
                {
                    while (true)
                    {
                        cancellation.ThrowIfCancellationRequested();
#if RECORDER_DIAGNOSTICS
                        using var diagnosticDecode = DiagnosticTrace.Step("GIF.ReadSampleAndProcess", true);
#endif
                        var next = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None,
                            out _, out var flags, out var timestamp);
#if RECORDER_DIAGNOSTICS
                        DiagnosticTrace.Count("GIF.samplesRead");
#endif
                        try
                        {
                            if ((flags & SourceReaderFlag.Error) != 0) throw new IOException("The video decoder failed.");
                            if ((flags & SourceReaderFlag.CurrentMediaTypeChanged) != 0)
                            {
                                // H.264 decoding may add block padding. Its display aperture is the actual video.
                                using var changed = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
                                layout = ReadLayout(changed, expectedWidth, expectedHeight);
                            }
                            if (next is not null && current is not null) WriteUntil(Math.Max(0, timestamp));
                            if (next is not null) { current?.Dispose(); current = next; currentLayout = layout; next = null; }
                            if ((flags & SourceReaderFlag.EndOfStream) != 0) break;
                        }
                        finally { next?.Dispose(); }
                    }
                    if (current is null) throw new IOException("The source recording contains no video frames.");
                    WriteUntil(duration.Ticks);
                    cancellation.ThrowIfCancellationRequested();
#if RECORDER_DIAGNOSTICS
                    using (DiagnosticTrace.Step("GIF.CommitEncoder", false)) { encoder.Commit(); }
#else
                    encoder.Commit();
#endif

                    void WriteUntil(long boundary)
                    {
                        if (index >= plan.FrameCount || plan.StartTicks(index) >= boundary) return;
                        CopyPixels(current!, content.Width, content.Height, currentLayout, pixels);
                        using var bitmap = factory.CreateBitmapFromMemory((uint)content.Width, (uint)content.Height,
                            PixelFormat.Format32bppBGR, pixels, (uint)(content.Width * 4));
                        using var scaler = factory.CreateBitmapScaler();
                        scaler.Initialize(bitmap, (uint)plan.Width, (uint)plan.Height, BitmapInterpolationMode.HighQualityCubic);
                        using var palette = factory.CreatePalette();
#if RECORDER_DIAGNOSTICS
                        using (DiagnosticTrace.Step("GIF.Palette", true)) { palette.InitializeFromBitmap(scaler, (uint)Math.Clamp(profile.Output.GifPaletteColors, 2, 256), false); }
#else
                        palette.InitializeFromBitmap(scaler, (uint)Math.Clamp(profile.Output.GifPaletteColors, 2, 256), false);
#endif
                        using var indexed = factory.CreateFormatConverter();
                        indexed.Initialize(scaler, PixelFormat.Format8bppIndexed,
                            profile.Output.GifDither ? BitmapDitherType.ErrorDiffusion : BitmapDitherType.None,
                            palette, 0, BitmapPaletteType.Custom);
                        while (index < plan.FrameCount && plan.StartTicks(index) < boundary)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            using var frame = encoder.CreateNewFrame(out var options);
                            using (options) frame.Initialize(options).CheckError();
                            frame.SetSize((uint)plan.Width, (uint)plan.Height).CheckError();
                            frame.SetPixelFormat(PixelFormat.Format8bppIndexed);
                            frame.SetPalette(palette);
                            using (var metadata = frame.MetadataQueryWriter)
                            {
                                SetMetadata(metadata, "/grctlext/Delay", plan.DelayCentiseconds(index));
                                SetMetadata(metadata, "/grctlext/Disposal", (byte)1);
                            }
#if RECORDER_DIAGNOSTICS
                            using (DiagnosticTrace.Step("GIF.WriteFrame", true)) { frame.WriteSource(indexed).CheckError(); }
#else
                            frame.WriteSource(indexed).CheckError();
#endif
                            frame.Commit();
                            index++;
#if RECORDER_DIAGNOSTICS
                            DiagnosticTrace.Count("GIF.framesWritten");
#endif
                            if (index == 1 || index == plan.FrameCount || progressClock.ElapsedMilliseconds >= 100)
                            {
                                progress?.Report(new GifProgress(index, plan.FrameCount));
                                progressClock.Restart();
                            }
                        }
                    }
                }
                finally { current?.Dispose(); }
            }
            cancellation.ThrowIfCancellationRequested();
            return output.Commit();
        }
        catch
        {
            // Only this export's incomplete GIF is removed; the source MP4 is never touched here.
            output.Discard();
            throw;
        }
    }

    private readonly record struct FrameLayout(int Height, int Stride, int OffsetX, int OffsetY);

    private static FrameLayout ReadLayout(IMFMediaType type, int videoWidth, int videoHeight)
    {
        if (type.GetGUID(MediaTypeAttributeKeys.Subtype) != VideoFormatGuids.Rgb32)
            throw new IOException("The video decoder changed its pixel format.");
        var size = type.GetUInt64(MediaTypeAttributeKeys.FrameSize);
        var width = checked((int)(size >> 32));
        var height = checked((int)(size & uint.MaxValue));
        var left = 0; var top = 0; var visibleWidth = width; var visibleHeight = height;
        if (type.GetBlobSize(MediaTypeAttributeKeys.MinimumDisplayAperture, out var length).Success && length >= 16)
        {
            // MFVideoArea: two MFOffset values (fraction + signed pixels), followed by SIZE.
            var area = type.GetBlob(MediaTypeAttributeKeys.MinimumDisplayAperture).AsSpan();
            if (BinaryPrimitives.ReadUInt16LittleEndian(area) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(area[4..]) != 0)
                throw new IOException("The decoder returned a fractional display aperture.");
            left = BinaryPrimitives.ReadInt16LittleEndian(area[2..]);
            top = BinaryPrimitives.ReadInt16LittleEndian(area[6..]);
            visibleWidth = BinaryPrimitives.ReadInt32LittleEndian(area[8..]);
            visibleHeight = BinaryPrimitives.ReadInt32LittleEndian(area[12..]);
        }
        if (visibleWidth != videoWidth || visibleHeight != videoHeight || left < 0 || top < 0 ||
            left + (long)visibleWidth > width || top + (long)visibleHeight > height)
            throw new IOException("The video decoder changed the visible frame size.");
        var stride = type.GetUInt32(MediaTypeAttributeKeys.DefaultStride, out var packedStride).Success
            ? unchecked((int)packedStride) : checked(width * 4);
        return new FrameLayout(height, stride, left, top);
    }

    private static void CopyPixels(IMFSample sample, int width, int height, FrameLayout layout, byte[] pixels)
    {
#if RECORDER_DIAGNOSTICS
        using var diagnosticScope = DiagnosticTrace.Step("GIF.CopyPixels", true);
#endif
        using var buffer = sample.ConvertToContiguousBuffer();
        using var twoDimensional = buffer.QueryInterfaceOrNull<IMF2DBuffer>();
        if (twoDimensional is not null)
        {
            twoDimensional.Lock2D(out var scanline, out var pitch);
            try { CopyRows(scanline, pitch); }
            finally { twoDimensional.Unlock2D(); }
        }
        else
        {
            buffer.Lock(out var start, out _, out var length);
            try
            {
                if (Math.Abs((long)layout.Stride) * layout.Height > length) throw new IOException("The decoded frame buffer is incomplete.");
                var scanline = layout.Stride < 0 ? start + checked((layout.Height - 1) * -layout.Stride) : start;
                CopyRows(scanline, layout.Stride);
            }
            finally { buffer.Unlock(); }
        }

        void CopyRows(IntPtr scanline, int pitch)
        {
            var rowBytes = checked(width * 4);
            if (Math.Abs((long)pitch) < rowBytes + layout.OffsetX * 4L) throw new IOException("The decoder returned an invalid row stride.");
            for (var y = 0; y < height; y++)
                Marshal.Copy(scanline + checked((y + layout.OffsetY) * pitch + layout.OffsetX * 4), pixels, y * rowBytes, rowBytes);
        }
    }

    private static void SetMetadata(IWICMetadataQueryWriter writer, string key, object value)
    {
        Variant variant;
        if (value is byte[] bytes) Marshal.ThrowExceptionForHR(InitPropVariantFromBuffer(bytes, (uint)bytes.Length, out variant));
        else variant = new Variant { Value = value };
        try { writer.SetMetadataByName(key, variant); }
        finally { PropVariantClear(ref variant); }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref Variant variant);

    [DllImport("propsys.dll")]
    private static extern int InitPropVariantFromBuffer(byte[] buffer, uint length, out Variant variant);
}
