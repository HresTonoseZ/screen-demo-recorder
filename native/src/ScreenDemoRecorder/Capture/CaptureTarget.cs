using ScreenDemoRecorder.Core.Models;
using Windows.Graphics.Capture;

namespace ScreenDemoRecorder.Capture;

internal sealed record CaptureTarget(GraphicsCaptureItem Item, PixelRect Area, Func<string?>? Validate = null,
    Func<PixelPoint, PixelPoint?>? MapScreenPoint = null);

internal static class CaptureTargetFactory
{
    public static CaptureTarget Create(CaptureSettings settings, IReadOnlyList<DisplayInfo> displays, DesktopWindowInfo? selectedWindow)
    {
        if (settings.Source == CaptureSource.Window)
        {
            if (selectedWindow is null ||
                !string.Equals(selectedWindow.ProcessName, settings.WindowProcessName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(selectedWindow.ClassName, settings.WindowClassName, StringComparison.Ordinal))
                throw new InvalidOperationException("Select an open window first.");
            var problem = WindowProblem(selectedWindow.Handle, selectedWindow.ProcessId, selectedWindow.ClassName);
            if (problem is not null) throw new InvalidOperationException(problem);
            var item = GraphicsInterop.ForWindow(selectedWindow.Handle);
            if (item.Size.Width < 2 || item.Size.Height < 2) throw new InvalidOperationException("The selected window has no recordable area.");
            return new CaptureTarget(item, new PixelRect(0, 0, item.Size.Width, item.Size.Height),
                () => WindowProblem(selectedWindow.Handle, selectedWindow.ProcessId, selectedWindow.ClassName),
                point => MapWindowPoint(selectedWindow.Handle, point, item.Size.Width, item.Size.Height));
        }

        var display = settings.DisplayDeviceName is { } device
            ? displays.FirstOrDefault(candidate => candidate.DeviceName == device)
            : displays.FirstOrDefault(candidate => candidate.Index == settings.DisplayIndex);
        if (display is null) throw new InvalidOperationException("Select a connected display first.");
        var area = settings.Source switch
        {
            CaptureSource.Display => new PixelRect(0, 0, display.Bounds.Width, display.Bounds.Height),
            CaptureSource.Region when settings.Region is { } region => new PixelRect(region.X, region.Y, region.Width, region.Height),
            _ => throw new InvalidOperationException("Select a capture region first."),
        };
        if (RegionGeometry.Fit(area, display.Bounds.Width, display.Bounds.Height, settings.RegionMinimumSize) != area)
            throw new InvalidOperationException("The saved region no longer fits. Select a new area.");
        return new CaptureTarget(GraphicsInterop.ForMonitor(display.Monitor), area, MapScreenPoint: point =>
            CaptureCoordinates.MapScreenPoint(display.Bounds, area, point));
    }

    private static PixelPoint? MapWindowPoint(nint handle, PixelPoint point, int width, int height)
    {
        if (!NativeDesktop.TryGetWindow(handle, out var window)) return null;
        return CaptureCoordinates.MapScreenPoint(window.Bounds, new PixelRect(0, 0, width, height), point);
    }

    private static string? WindowProblem(nint handle, uint processId, string className)
    {
        if (!NativeDesktop.TryGetWindow(handle, out var current) || current.ProcessId != processId || current.ClassName != className)
            return "The selected window was closed. Choose it again.";
        if (current.IsMinimized) return "Restore the selected window before recording.";
        return null;
    }
}
