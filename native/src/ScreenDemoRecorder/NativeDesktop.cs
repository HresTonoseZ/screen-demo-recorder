using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder;

public sealed record DisplayInfo(int Index, string DeviceName, PixelRect Bounds, bool Primary, nint Monitor = 0)
{
    public string Label => $"Display {Index} · {Bounds.Width} × {Bounds.Height}{(Primary ? " · Main" : string.Empty)}";
    public override string ToString() => Label;
}

public sealed record DesktopWindowInfo(nint Handle, uint ProcessId, string Title, string ProcessName, string ClassName,
    PixelRect Bounds, bool IsMinimized)
{
    public string Details => $"{ProcessName} · {Bounds.Width} × {Bounds.Height}{(IsMinimized ? " · Minimized" : string.Empty)}";
    public string Monogram => ProcessName.Length == 0 ? "?" : ProcessName[..1].ToUpperInvariant();
    public bool Matches(string? title, string? processName, string? className) =>
        string.Equals(Title, title, StringComparison.Ordinal) &&
        string.Equals(ProcessName, processName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(ClassName, className, StringComparison.Ordinal);
}

internal static class NativeDesktop
{
    private delegate bool MonitorCallback(nint monitor, nint dc, ref NativeRect rect, nint data);
    private delegate bool WindowCallback(nint window, nint data);

    public static IReadOnlyList<DisplayInfo> Displays()
    {
        var monitors = new List<(string Name, PixelRect Bounds, bool Primary, nint Monitor)>();
        MonitorCallback callback = (nint monitor, nint dc, ref NativeRect rect, nint data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = string.Empty };
            if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception();
            monitors.Add((info.Device, new PixelRect(info.Monitor.Left, info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top), (info.Flags & 1) != 0, monitor));
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0)) throw new Win32Exception();
        return monitors.OrderByDescending(m => m.Primary).ThenBy(m => m.Name, StringComparer.Ordinal)
            .Select((m, i) => new DisplayInfo(i + 1, m.Name, m.Bounds, m.Primary, m.Monitor)).ToArray();
    }

    public static IReadOnlyList<DesktopWindowInfo> Windows()
    {
        var windows = new List<DesktopWindowInfo>();
        var ownProcess = (uint)Environment.ProcessId;
        WindowCallback callback = (window, _) =>
        {
            if (TryGetWindow(window, out var info) && info.ProcessId != ownProcess && IsApplicationWindow(window)) windows.Add(info);
            return true;
        };
        if (!EnumWindows(callback, 0)) throw new Win32Exception();
        return windows;
    }

    public static bool TryGetWindow(nint window, out DesktopWindowInfo info)
    {
        info = default!;
        if (window == 0 || !IsWindow(window) || !IsWindowVisible(window) || IsCloaked(window)) return false;
        var length = GetWindowTextLength(window);
        if (length <= 0) return false;
        var titleBuffer = new StringBuilder(length + 1);
        if (GetWindowText(window, titleBuffer, titleBuffer.Capacity) <= 0) return false;
        var classBuffer = new StringBuilder(256);
        if (GetClassName(window, classBuffer, classBuffer.Capacity) <= 0) return false;
        _ = GetWindowThreadProcessId(window, out var processId);
        string processName;
        try { processName = Process.GetProcessById((int)processId).ProcessName; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
        if (!GetWindowRect(window, out var rect)) return false;
        var bounds = new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        if (bounds.Width < 2 || bounds.Height < 2) return false;
        info = new DesktopWindowInfo(window, processId, titleBuffer.ToString(), processName, classBuffer.ToString(), bounds, IsIconic(window));
        return true;
    }

    private static bool IsApplicationWindow(nint window)
    {
        var style = GetWindowLongPtr(window, -20).ToInt64();
        var appWindow = (style & 0x00040000) != 0;
        var toolWindow = (style & 0x00000080) != 0;
        return appWindow || (!toolWindow && GetWindow(window, 4) == 0);
    }

    private static bool IsCloaked(nint window)
    {
        return DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(uint)) == 0 && cloaked != 0;
    }

    public static void Exclude(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (DwmIsCompositionEnabled(out var compositionEnabled) != 0 || !compositionEnabled)
            throw new InvalidOperationException("Desktop composition is required to exclude recorder controls from capture.");
        if (!SetWindowDisplayAffinity(hwnd, 0x11)) throw new Win32Exception();
        if (!GetWindowDisplayAffinity(hwnd, out var affinity) || affinity != 0x11)
            throw new InvalidOperationException("Windows did not enable capture exclusion for a recorder window.");
    }

    public static bool IsExcluded(Window window)
    {
        return GetWindowDisplayAffinity(new WindowInteropHelper(window).Handle, out var affinity) && affinity == 0x11;
    }

    public static bool TryExclude(Window window)
    {
        try { Exclude(window); return true; }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException) { return false; }
    }

    public static bool IsPassiveOverlay(Window window)
    {
        var style = GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64();
        const long required = 0x00000020 | 0x00000080 | 0x08000000;
        return (style & required) == required;
    }

    public static PixelRect WindowBounds(Window window)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out var rect)) throw new Win32Exception();
        return new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public static uint DpiForWindow(Window window)
    {
        var dpi = GetDpiForWindow(new WindowInteropHelper(window).EnsureHandle());
        return dpi == 0 ? throw new Win32Exception() : dpi;
    }

    public static bool IsPerMonitorV2() =>
        AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), new nint(-4));

    public static void Place(Window window, PixelRect bounds, bool clickThrough, bool requireCaptureExclusion = true)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (requireCaptureExclusion) Exclude(window);
        var style = GetWindowLongPtr(hwnd, -20).ToInt64() | 0x80;
        if (clickThrough) style |= 0x20 | 0x08000000;
        SetWindowLongPtr(hwnd, -20, (nint)style);
        void ApplyPhysicalBounds()
        {
            if (!SetWindowPos(hwnd, new nint(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010))
                throw new Win32Exception();
            if (WindowBounds(window) != bounds)
                throw new InvalidOperationException("Windows did not apply the requested physical-pixel window bounds.");
        }
        window.DpiChanged += (_, _) => ApplyPhysicalBounds();
        ApplyPhysicalBounds();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int maximum);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder text, int maximum);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out uint value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
}
