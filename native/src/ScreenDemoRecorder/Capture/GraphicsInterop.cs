using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ScreenDemoRecorder.Capture;

internal static class GraphicsInterop
{
    private static readonly Guid CaptureItemInterface = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem ForMonitor(nint monitor)
    {
        var factory = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var pointer = factory.CreateForMonitor(monitor, CaptureItemInterface);
        try { return GraphicsCaptureItem.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    public static GraphicsCaptureItem ForWindow(nint window)
    {
        var factory = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var pointer = factory.CreateForWindow(window, CaptureItemInterface);
        try { return GraphicsCaptureItem.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    public static void Release(GraphicsCaptureItem item) =>
        ((IWinRTObject)item).NativeObject.Dispose();

    public static IDirect3DDevice Wrap(ID3D11Device device)
    {
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var pointer));
        try { return MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    public static IDirect3DSurface Wrap(ID3D11Texture2D texture)
    {
        using var dxgi = texture.QueryInterface<IDXGISurface>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgi.NativePointer, out var pointer));
        try { return MarshalInterface<IDirect3DSurface>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    public static ID3D11Texture2D Unwrap(IDirect3DSurface surface) =>
        new(surface.As<IDirect3DDxgiInterfaceAccess>().GetInterface(typeof(ID3D11Texture2D).GUID));

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);
        nint CreateForMonitor(nint monitor, in Guid iid);
    }

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        nint GetInterface(in Guid iid);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint result);
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(nint surface, out nint result);
}
