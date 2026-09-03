using System.Runtime.InteropServices;
using ScreenDemoRecorder.Core.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace ScreenDemoRecorder.Capture;

internal sealed class StagingFrameReader(ID3D11Device device, ID3D11DeviceContext context) : IDisposable
{
    private ID3D11Texture2D? staging;
    private int width;
    private int height;

    public CpuVideoFrame Read(ID3D11Texture2D source, PixelRect area, TimeSpan timestamp)
    {
        CopyFrom(source, area);
        return ReadMapped(timestamp);
    }

    public void CopyFrom(ID3D11Texture2D source, PixelRect area)
    {
#if RECORDER_DIAGNOSTICS
        using var diagnosticScope = DiagnosticTrace.Step("D3D.CopyFrom", true);
#endif
        ArgumentNullException.ThrowIfNull(source);
#if RECORDER_DIAGNOSTICS
        var description = DiagnosticTrace.NativeCall("D3D.GetTextureDescription", () => source.Description);
#else
        var description = source.Description;
#endif
        if (area.X < 0 || area.Y < 0 || area.Width < 1 || area.Height < 1 ||
            area.Right > description.Width || area.Bottom > description.Height)
            throw new ArgumentOutOfRangeException(nameof(area), "The CPU readback area does not fit the source texture.");
        EnsureStagingTexture(area.Width, area.Height);
#if RECORDER_DIAGNOSTICS
        DiagnosticTrace.NativeCall("D3D.CopySubresourceRegion", () =>
            context.CopySubresourceRegion(staging!, 0, 0, 0, 0, source, 0,
                new Box(area.X, area.Y, 0, area.Right, area.Bottom, 1)));
#else
        context.CopySubresourceRegion(staging!, 0, 0, 0, 0, source, 0,
            new Box(area.X, area.Y, 0, area.Right, area.Bottom, 1));
#endif
    }

    public CpuVideoFrame ReadMapped(TimeSpan timestamp)
    {
#if RECORDER_DIAGNOSTICS
        using var diagnosticScope = DiagnosticTrace.Step("D3D.ReadMapped", true);
#endif
        if (staging is null) throw new InvalidOperationException("No GPU frame has been copied for CPU readback.");
        var frame = new CpuVideoFrame(width, height, timestamp);
        var mapped = default(MappedSubresource);
        var isMapped = false;
        try
        {
#if RECORDER_DIAGNOSTICS
            DiagnosticTrace.NativeCall("D3D.Map", () =>
                context.Map(staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out mapped).CheckError());
#else
            context.Map(staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out mapped).CheckError();
#endif
            isMapped = true;
#if RECORDER_DIAGNOSTICS
            using (DiagnosticTrace.NativeStep("D3D.CopyMappedRows"))
            {
#endif
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(mapped.DataPointer + checked(row * (int)mapped.RowPitch),
                    frame.Buffer, row * frame.Stride, frame.Stride);
            }
#if RECORDER_DIAGNOSTICS
            }
#endif
            return frame;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
        finally
        {
            if (isMapped)
            {
#if RECORDER_DIAGNOSTICS
                DiagnosticTrace.NativeCall("D3D.Unmap", () => context.Unmap(staging!, 0));
#else
                context.Unmap(staging!, 0);
#endif
            }
        }
    }

    public void Dispose()
    {
#if RECORDER_DIAGNOSTICS
        DiagnosticTrace.NativeCall("D3D.DisposeStagingTexture", () => staging?.Dispose());
#else
        staging?.Dispose();
#endif
        staging = null;
    }

    private void EnsureStagingTexture(int requiredWidth, int requiredHeight)
    {
        if (staging is not null && width == requiredWidth && height == requiredHeight) return;
#if RECORDER_DIAGNOSTICS
        DiagnosticTrace.NativeCall("D3D.ReleasePreviousStagingTexture", () => staging?.Dispose());
        staging = DiagnosticTrace.NativeCall("D3D.CreateStagingTexture", () =>
            device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                (uint)requiredWidth, (uint)requiredHeight, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read)));
        DiagnosticTrace.Write($"STAGING width={requiredWidth}; height={requiredHeight}; format=B8G8R8A8_UNorm; access=Read");
#else
        staging?.Dispose();
        staging = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
            (uint)requiredWidth, (uint)requiredHeight, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
#endif
        width = requiredWidth;
        height = requiredHeight;
    }
}
