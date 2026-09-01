using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;
using D2DPixelFormat = Vortice.DCommon.PixelFormat;

namespace ScreenDemoRecorder.Capture;

internal sealed class FrameScaler : IDisposable
{
    private ID2D1Factory1? factory;
    private ID2D1Device? device;
    private ID2D1DeviceContext? context;

    public FrameScaler(ID3D11Device graphicsDevice)
    {
        try
        {
            factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.MultiThreaded);
            using var dxgi = graphicsDevice.QueryInterface<IDXGIDevice>();
            device = factory.CreateDevice(dxgi);
            context = device.CreateDeviceContext(DeviceContextOptions.None);
        }
        catch { Dispose(); throw; }
    }

    public void Scale(ID3D11Texture2D source, ID3D11Texture2D destination, int sourceWidth, int sourceHeight, int width, int height)
    {
        var drawing = context ?? throw new ObjectDisposedException(nameof(FrameScaler));
        using var sourceSurface = source.QueryInterface<IDXGISurface>();
        using var destinationSurface = destination.QueryInterface<IDXGISurface>();
        using var input = drawing.CreateBitmapFromDxgiSurface(sourceSurface,
            new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied), 96, 96));
        using var target = drawing.CreateBitmapFromDxgiSurface(destinationSurface,
            new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied), 96, 96,
                BitmapOptions.Target | BitmapOptions.CannotDraw));
        drawing.Target = target;
        try
        {
            drawing.BeginDraw();
            drawing.Clear(new Color4(0, 0, 0, 1));
            drawing.DrawBitmap(input, new Vortice.RawRectF(0, 0, width, height), 1, InterpolationMode.HighQualityCubic,
                new Vortice.RawRectF(0, 0, sourceWidth, sourceHeight), null);
            drawing.EndDraw().CheckError();
        }
        finally { drawing.Target = null; }
    }

    public void Dispose()
    {
        context?.Dispose(); context = null;
        device?.Dispose(); device = null;
        factory?.Dispose(); factory = null;
    }
}
