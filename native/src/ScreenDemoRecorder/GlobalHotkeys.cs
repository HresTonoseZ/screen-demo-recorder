using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder;

internal sealed class GlobalHotkeys : IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private readonly HwndSource source;
    private readonly Action<RecorderCommand> execute;
    private readonly Dictionary<int, (RecorderCommand Command, HotkeyGesture Gesture)> registered = [];
    private bool disposed;
    internal nint Handle => source.Handle;
    internal int Count => registered.Count;

    public GlobalHotkeys(Action<RecorderCommand> handler)
    {
        execute = handler;
        source = new HwndSource(new HwndSourceParameters("Recorder shortcuts") { ParentWindow = new nint(-3), WindowStyle = 0 });
        source.AddHook(WindowMessage);
    }

    public string? Apply(IReadOnlyDictionary<RecorderCommand, HotkeyGesture> bindings)
    {
        source.Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        Clear();
        foreach (var (command, gesture) in bindings)
        {
            var id = Identifier(command);
            if (!RegisterHotKey(source.Handle, id, NativeModifiers(gesture.Modifiers) | 0x4000, (uint)gesture.VirtualKey))
            {
                var error = Marshal.GetLastWin32Error();
                Clear();
                return error == 1409
                    ? $"{gesture} is already in use by another application. Choose another shortcut or clear it."
                    : $"Cannot register {gesture}: {new Win32Exception(error).Message}";
            }
            registered.Add(id, (command, gesture));
        }
        return null;
    }

    public void Clear()
    {
        source.Dispatcher.VerifyAccess();
        foreach (var id in registered.Keys.ToArray())
        {
            if (!UnregisterHotKey(source.Handle, id)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot release a recorder shortcut.");
            registered.Remove(id);
        }
        // This message-only window owns no messages or registrations from other components.
        while (PeekMessageW(out _, source.Handle, HotkeyMessage, HotkeyMessage, 1)) { }
    }

    private nint WindowMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == HotkeyMessage && registered.TryGetValue((int)wParam, out var binding))
        {
            var key = (int)(((long)lParam >> 16) & 0xFFFF);
            var modifiers = (uint)((long)lParam & 0xFFFF);
            if (key == binding.Gesture.VirtualKey && modifiers == NativeModifiers(binding.Gesture.Modifiers))
            {
                handled = true;
                execute(binding.Command);
            }
        }
        return 0;
    }

    internal static int Identifier(RecorderCommand command) => 0x5300 + (int)command;
    internal static uint NativeModifiers(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Alt) ? 1u : 0) | (modifiers.HasFlag(KeyModifiers.Control) ? 2u : 0) |
        (modifiers.HasFlag(KeyModifiers.Shift) ? 4u : 0) | (modifiers.HasFlag(KeyModifiers.Windows) ? 8u : 0);

    public void Dispose()
    {
        if (disposed) return;
        try { Clear(); }
        finally { disposed = true; source.RemoveHook(WindowMessage); source.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window; public uint Id; public nuint WParam; public nint LParam;
        public uint Time; public int X; public int Y; public uint Private;
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out Message message, nint window, int min, int max, uint remove);
}
