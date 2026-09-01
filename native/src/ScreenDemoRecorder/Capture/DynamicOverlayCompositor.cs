using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Overlays;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using WpfRect = System.Windows.Rect;

namespace ScreenDemoRecorder.Capture;

internal sealed class DynamicOverlayCompositor : IDisposable
{
    private sealed record Raster(byte[] Pixels, int Width, int Height);

    private readonly ID3D11DeviceContext context;
    private readonly ID3D11Texture2D staging;
    private readonly KeystrokeRenderer? keys;
    private readonly ClickRenderer? clicks;
    private readonly Dictionary<string, Raster> keycaps = [];
    private readonly Dictionary<MouseClickButton, Raster> clickTextures = [];
    private readonly int frameWidth;
    private readonly int frameHeight;
    private byte[] pixels = [];
    private bool disposed;

    public DynamicOverlayCompositor(ID3D11Device device, ID3D11DeviceContext deviceContext,
        KeystrokeRenderer? keystrokes, ClickRenderer? mouseClicks, int width, int height)
    {
        context = deviceContext;
        keys = keystrokes;
        clicks = mouseClicks;
        frameWidth = width;
        frameHeight = height;
        staging = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read | CpuAccessFlags.Write,
        });
        if (keys is not null)
            foreach (var cap in keys.Keycaps) keycaps.Add(cap.Key, Read(cap.Value));
        if (clicks is not null)
            foreach (var texture in clicks.Textures) clickTextures.Add(texture.Key, Read(texture.Value));
    }

    public void Draw(ID3D11Texture2D frame, IReadOnlyList<VisibleKeystroke> entries, IReadOnlyList<VisibleClick> mouseClicks)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var caps = keys?.Layout(entries, frameWidth, frameHeight) ?? [];
        var clickRings = clicks?.Layout(mouseClicks) ?? [];
        if (caps.Length == 0 && clickRings.Length == 0) return;

        foreach (var cap in caps)
            if (!keycaps.ContainsKey(cap.Key)) keycaps.Add(cap.Key, Read(keys!.Keycaps[cap.Key]));

        var allBounds = caps.Select(cap => cap.Bounds).Concat(clickRings.Select(click => click.Bounds)).ToArray();
        var left = Math.Clamp((int)Math.Floor(allBounds.Min(box => box.Left)), 0, frameWidth);
        var top = Math.Clamp((int)Math.Floor(allBounds.Min(box => box.Top)), 0, frameHeight);
        var right = Math.Clamp((int)Math.Ceiling(allBounds.Max(box => box.Right)), 0, frameWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(allBounds.Max(box => box.Bottom)), 0, frameHeight);
        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0) return;

        context.CopySubresourceRegion(staging, 0, 0, 0, 0, frame, 0, new Box(left, top, 0, right, bottom, 1));
        context.Map(staging, 0, MapMode.ReadWrite, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
        try
        {
            var stride = width * 4;
            if (pixels.Length < stride * height) pixels = new byte[stride * height];
            for (var y = 0; y < height; y++)
                Marshal.Copy(mapped.DataPointer + y * (int)mapped.RowPitch, pixels, y * stride, stride);

            foreach (var click in clickRings)
                Blend(clickTextures[click.Button], click.Bounds, click.Opacity, left, top, width, height, stride);
            foreach (var cap in caps)
                Blend(keycaps[cap.Key], cap.Bounds, cap.Opacity, left, top, width, height, stride);

            for (var y = 0; y < height; y++)
                Marshal.Copy(pixels, y * stride, mapped.DataPointer + y * (int)mapped.RowPitch, stride);
        }
        finally
        {
            context.Unmap(staging, 0);
        }
        context.CopySubresourceRegion(frame, 0, (uint)left, (uint)top, 0, staging, 0, new Box(0, 0, 0, width, height, 1));
    }

    private void Blend(Raster source, WpfRect destination, double opacity, int originX, int originY,
        int targetWidth, int targetHeight, int targetStride)
    {
        var left = Math.Max(0, (int)Math.Floor(destination.Left) - originX);
        var top = Math.Max(0, (int)Math.Floor(destination.Top) - originY);
        var right = Math.Min(targetWidth, (int)Math.Ceiling(destination.Right) - originX);
        var bottom = Math.Min(targetHeight, (int)Math.Ceiling(destination.Bottom) - originY);
        if (left >= right || top >= bottom || destination.Width <= 0 || destination.Height <= 0) return;

        var clippedOpacity = Math.Clamp(opacity, 0, 1);
        for (var y = top; y < bottom; y++)
        {
            var screenY = y + originY;
            var sourceY = Math.Clamp((int)((screenY - destination.Top) * source.Height / destination.Height), 0, source.Height - 1);
            for (var x = left; x < right; x++)
            {
                var screenX = x + originX;
                var sourceX = Math.Clamp((int)((screenX - destination.Left) * source.Width / destination.Width), 0, source.Width - 1);
                var sourceOffset = (sourceY * source.Width + sourceX) * 4;
                var targetOffset = y * targetStride + x * 4;
                var alpha = source.Pixels[sourceOffset + 3] / 255.0 * clippedOpacity;
                if (alpha <= 0) continue;
                var inverse = 1 - alpha;
                for (var channel = 0; channel < 3; channel++)
                    pixels[targetOffset + channel] = (byte)Math.Clamp(
                        Math.Round(source.Pixels[sourceOffset + channel] * clippedOpacity + pixels[targetOffset + channel] * inverse), 0, 255);
                pixels[targetOffset + 3] = (byte)Math.Clamp(
                    Math.Round(source.Pixels[sourceOffset + 3] * clippedOpacity + pixels[targetOffset + 3] * inverse), 0, 255);
            }
        }
    }

    private static Raster Read(BitmapSource bitmap)
    {
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return new Raster(bytes, bitmap.PixelWidth, bitmap.PixelHeight);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        staging.Dispose();
        keycaps.Clear();
        clickTextures.Clear();
        pixels = [];
    }
}
