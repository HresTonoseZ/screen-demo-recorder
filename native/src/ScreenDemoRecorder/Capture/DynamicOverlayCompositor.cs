using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Overlays;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;
using D2DPixelFormat = Vortice.DCommon.PixelFormat;

namespace ScreenDemoRecorder.Capture;

internal sealed class DynamicOverlayCompositor : IDisposable
{
    private ID2D1Factory1? factory;
    private ID2D1Device? device;
    private ID2D1DeviceContext? context;
    private readonly KeystrokeRenderer? keys;
    private readonly ClickRenderer? clicks;
    private readonly Dictionary<string, ID2D1Bitmap1> keycaps = [];
    private readonly Dictionary<MouseClickButton, ID2D1Bitmap1> clickTextures = [];
    private readonly int frameWidth;
    private readonly int frameHeight;
    private bool disposed;

    public DynamicOverlayCompositor(ID3D11Device graphicsDevice,
        KeystrokeRenderer? keystrokes, ClickRenderer? mouseClicks, int width, int height)
    {
        keys = keystrokes;
        clicks = mouseClicks;
        frameWidth = width;
        frameHeight = height;
        try
        {
            factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.MultiThreaded);
            using var dxgi = graphicsDevice.QueryInterface<IDXGIDevice>();
            device = factory.CreateDevice(dxgi);
            context = device.CreateDeviceContext(DeviceContextOptions.None);
            if (keys is not null)
                foreach (var cap in keys.Keycaps) keycaps.Add(cap.Key, Upload(cap.Value));
            if (clicks is not null)
                foreach (var texture in clicks.Textures) clickTextures.Add(texture.Key, Upload(texture.Value));
        }
        catch { Dispose(); throw; }
    }

    public void Draw(ID3D11Texture2D frame, IReadOnlyList<VisibleKeystroke> entries, IReadOnlyList<VisibleClick> mouseClicks)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var caps = keys?.Layout(entries, frameWidth, frameHeight) ?? [];
        var clickRings = clicks?.Layout(mouseClicks) ?? [];
        if (caps.Length == 0 && clickRings.Length == 0) return;

        foreach (var cap in caps)
            if (!keycaps.ContainsKey(cap.Key)) keycaps.Add(cap.Key, Upload(keys!.Keycaps[cap.Key]));

        var drawing = context ?? throw new ObjectDisposedException(nameof(DynamicOverlayCompositor));
        using var surface = frame.QueryInterface<IDXGISurface>();
        using var target = drawing.CreateBitmapFromDxgiSurface(surface,
            new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Ignore), 96, 96,
                BitmapOptions.Target | BitmapOptions.CannotDraw));
        drawing.Target = target;
        try
        {
            drawing.BeginDraw();
            drawing.PrimitiveBlend = PrimitiveBlend.SourceOver;
            drawing.PushAxisAlignedClip(new Vortice.RawRectF(0, 0, frameWidth, frameHeight), AntialiasMode.Aliased);
            foreach (var click in clickRings)
                DrawBitmap(clickTextures[click.Button], click.Bounds, click.Opacity);
            foreach (var cap in caps)
                DrawBitmap(keycaps[cap.Key], cap.Bounds, cap.Opacity);
            drawing.PopAxisAlignedClip();
            drawing.EndDraw().CheckError();
        }
        finally { drawing.Target = null; }

        void DrawBitmap(ID2D1Bitmap1 bitmap, System.Windows.Rect bounds, double opacity)
        {
            drawing.DrawBitmap(bitmap,
                new Vortice.RawRectF((float)bounds.Left, (float)bounds.Top, (float)bounds.Right, (float)bounds.Bottom),
                (float)Math.Clamp(opacity, 0, 1), InterpolationMode.Linear, null, null);
        }
    }

    private ID2D1Bitmap1 Upload(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return context!.CreateBitmap(new SizeI(bitmap.PixelWidth, bitmap.PixelHeight), pinned.AddrOfPinnedObject(),
                (uint)bitmap.PixelWidth * 4,
                new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied), 96, 96));
        }
        finally { pinned.Free(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var bitmap in keycaps.Values) bitmap.Dispose();
        foreach (var bitmap in clickTextures.Values) bitmap.Dispose();
        keycaps.Clear();
        clickTextures.Clear();
        context?.Dispose(); context = null;
        device?.Dispose(); device = null;
        factory?.Dispose(); factory = null;
    }
}
