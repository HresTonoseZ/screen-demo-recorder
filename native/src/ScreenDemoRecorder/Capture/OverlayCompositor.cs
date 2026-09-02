using System.Runtime.InteropServices;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Overlays;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using D2DPixelFormat = Vortice.DCommon.PixelFormat;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;

namespace ScreenDemoRecorder.Capture;

internal sealed class OverlayCompositor : IDisposable
{
    private ID2D1Factory1? factory;
    private ID2D1Device? device;
    private ID2D1DeviceContext? context;
    private ID2D1Bitmap1? bitmap;
    private ID2D1Bitmap1? blurSource;
    private ID2D1Effect? blurEffect;
    private readonly LabelRaster? label;
    private readonly KeystrokeRenderer? keys;
    private readonly ClickRenderer? clicks;
    private readonly Dictionary<string, ID2D1Bitmap1> keycaps = [];
    private readonly Dictionary<MouseClickButton, ID2D1Bitmap1> clickTextures = [];
    private readonly int frameWidth, frameHeight;

    public OverlayCompositor(ID3D11Device graphicsDevice, LabelRaster? raster,
        KeystrokeRenderer? keystrokes, ClickRenderer? mouseClicks, int width, int height)
    {
        label = raster;
        keys = keystrokes;
        clicks = mouseClicks;
        frameWidth = width; frameHeight = height;
        try
        {
            factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.MultiThreaded);
            using var dxgi = graphicsDevice.QueryInterface<IDXGIDevice>();
            device = factory.CreateDevice(dxgi);
            context = device.CreateDeviceContext(DeviceContextOptions.None);
            if (raster is not null) bitmap = Upload(raster.Bitmap);
            if (raster is { BackgroundBlur: > 0 })
            {
                blurSource = context.CreateBitmap(new SizeI(width, height), IntPtr.Zero, 0,
                    new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Ignore),
                        96, 96, BitmapOptions.None));
                blurEffect = new ID2D1Effect(context.CreateEffect(EffectGuids.GaussianBlur));
                blurEffect.SetInput(0, blurSource, true);
                blurEffect.SetValue((uint)GaussianBlurProperties.StandardDeviation, (float)raster.BackgroundBlur);
            }
            if (keys is not null)
                foreach (var cap in keys.Keycaps) keycaps.Add(cap.Key, Upload(cap.Value));
            if (clicks is not null)
                foreach (var texture in clicks.Textures) clickTextures.Add(texture.Key, Upload(texture.Value));
        }
        catch { Dispose(); throw; }
    }

    private ID2D1Bitmap1 Upload(BitmapSource raster)
    {
        var bytes = new byte[raster.PixelWidth * raster.PixelHeight * 4];
        raster.CopyPixels(bytes, raster.PixelWidth * 4, 0);
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return context!.CreateBitmap(new SizeI(raster.PixelWidth, raster.PixelHeight), pinned.AddrOfPinnedObject(),
                (uint)raster.PixelWidth * 4, new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied), 96, 96));
        }
        finally { pinned.Free(); }
    }

    public void Draw(ID3D11Texture2D frame, IReadOnlyList<VisibleKeystroke> entries, IReadOnlyList<VisibleClick> mouseClicks)
    {
        var caps = keys?.Layout(entries, frameWidth, frameHeight) ?? [];
        var clickRings = clicks?.Layout(mouseClicks) ?? [];
        if (label is null && caps.Length == 0 && clickRings.Length == 0) return;
        foreach (var cap in caps)
            if (!keycaps.ContainsKey(cap.Key)) keycaps.Add(cap.Key, Upload(keys!.Keycaps[cap.Key]));

        var drawing = context ?? throw new ObjectDisposedException(nameof(OverlayCompositor));
        using var surface = frame.QueryInterface<IDXGISurface>();
        using var target = drawing.CreateBitmapFromDxgiSurface(surface,
            new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Ignore), 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
        drawing.Target = target;
        try
        {
            blurSource?.CopyFromBitmap(target).CheckError();
            drawing.BeginDraw();
            drawing.PrimitiveBlend = PrimitiveBlend.SourceOver;
            drawing.PushAxisAlignedClip(new Vortice.RawRectF(0, 0, frameWidth, frameHeight), AntialiasMode.Aliased);
            if (label is { BackgroundBlur: > 0 } && blurEffect is not null)
            {
                drawing.PushAxisAlignedClip(new Vortice.RawRectF(label.Container.X, label.Container.Y,
                    label.Container.Right, label.Container.Bottom), AntialiasMode.PerPrimitive);
                drawing.DrawImage(blurEffect, InterpolationMode.Linear, CompositeMode.SourceOver);
                drawing.PopAxisAlignedClip();
            }
            if (label is not null)
                drawing.DrawImage(bitmap!, new System.Numerics.Vector2(label.Bounds.X, label.Bounds.Y),
                    InterpolationMode.NearestNeighbor, CompositeMode.SourceOver);
            foreach (var click in clickRings)
                DrawBitmap(clickTextures[click.Button], click.Bounds, click.Opacity);
            foreach (var cap in caps)
                DrawBitmap(keycaps[cap.Key], cap.Bounds, cap.Opacity);
            drawing.PopAxisAlignedClip();
            drawing.EndDraw().CheckError();
        }
        finally { drawing.Target = null; }

        void DrawBitmap(ID2D1Bitmap1 source, System.Windows.Rect bounds, double opacity)
        {
            drawing.DrawBitmap(source,
                new Vortice.RawRectF((float)bounds.Left, (float)bounds.Top, (float)bounds.Right, (float)bounds.Bottom),
                (float)Math.Clamp(opacity, 0, 1), InterpolationMode.Linear, null, null);
        }
    }

    public void Dispose()
    {
        blurEffect?.Dispose(); blurEffect = null;
        blurSource?.Dispose(); blurSource = null;
        bitmap?.Dispose(); bitmap = null;
        foreach (var keycap in keycaps.Values) keycap.Dispose();
        foreach (var texture in clickTextures.Values) texture.Dispose();
        keycaps.Clear();
        clickTextures.Clear();
        context?.Dispose(); context = null;
        device?.Dispose(); device = null;
        factory?.Dispose(); factory = null;
    }
}
