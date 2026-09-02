using ScreenDemoRecorder.Core.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace ScreenDemoRecorder.Capture;

internal static class CpuFrameReadbackChecks
{
    public static void Run()
    {
        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];
        var result = D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            levels, out ID3D11Device device, out ID3D11DeviceContext context);
        if (result.Failure)
            result = D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport,
                levels, out device, out context);
        result.CheckError();
        using (device)
        using (context)
        using (var source = device.CreateTexture2D(new Texture2DDescription
        {
            Width = 8,
            Height = 6,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
        }))
        using (var target = device.CreateRenderTargetView(source))
        using (var reader = new StagingFrameReader(device, context))
        {
            context.ClearRenderTargetView(target, new Color4(0.75f, 0.5f, 0.25f, 1));
            using var frame = reader.Read(source, new PixelRect(2, 1, 3, 4), TimeSpan.FromMilliseconds(125));
            Require(frame is { Width: 3, Height: 4, Stride: 12, Timestamp.TotalMilliseconds: 125 },
                "CPU readback changed frame geometry or timestamp.");
            var pixels = frame.Pixels.Span;
            Require(pixels.Length == 48, "CPU readback returned an incorrectly sized BGRA buffer.");
            for (var offset = 0; offset < pixels.Length; offset += 4)
                Require(pixels[offset] is >= 62 and <= 66 && pixels[offset + 1] is >= 126 and <= 130 &&
                    pixels[offset + 2] is >= 189 and <= 193 && pixels[offset + 3] == 255,
                    "CPU readback did not preserve BGRA pixels across mapped row pitch.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
