using System.ComponentModel;
using System.Runtime.InteropServices;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Capture;

internal sealed class MouseClickCapture : IAsyncDisposable
{
    private readonly Action<int, int, MouseClickButton> clicked;
    private readonly HookProc callback;
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private uint threadId;
    private volatile bool stopping;
    private Exception? failure;

    public MouseClickCapture(Action<int, int, MouseClickButton> onClicked)
    {
        clicked = onClicked;
        callback = OnMouse;
        new Thread(Run) { IsBackground = true, Name = "Recording mouse clicks" }.Start();
    }

    public Exception? Failure => Volatile.Read(ref failure);

    public Task Ready => started.Task;

    private void Run()
    {
        nint hook = 0;
        try
        {
            threadId = GetCurrentThreadId();
            PeekMessageW(out _, 0, 0, 0, 0);
            if (stopping) { started.TrySetResult(); return; }
            hook = SetWindowsHookExW(14, callback, GetModuleHandleW(null), 0);
            if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot enable mouse-click capture.");
            started.TrySetResult();
            while (!stopping)
            {
                var result = GetMessageW(out var message, 0, 0, 0);
                if (result == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (result == 0) break;
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
        catch (Exception error) { Interlocked.CompareExchange(ref failure, error, null); started.TrySetException(error); }
        finally
        {
            if (hook != 0 && !UnhookWindowsHookEx(hook))
                Interlocked.CompareExchange(ref failure, new Win32Exception(Marshal.GetLastWin32Error(), "Cannot release mouse-click capture."), null);
            ended.TrySetResult();
        }
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        try
        {
            if (code == 0 && !stopping && message is 0x0201 or 0x0204)
            {
                var input = Marshal.PtrToStructure<LowLevelMouseInput>(data);
                clicked(input.X, input.Y, message == 0x0201 ? MouseClickButton.Left : MouseClickButton.Right);
            }
        }
        catch (Exception error) { Interlocked.CompareExchange(ref failure, error, null); RequestStop(); }
        return CallNextHookEx(0, code, message, data);
    }

    public void RequestStop()
    {
        stopping = true;
        if (threadId != 0 && !ended.Task.IsCompleted) PostThreadMessageW(threadId, 0x12, 0, 0);
    }

    public async ValueTask DisposeAsync()
    {
        RequestStop();
        await ended.Task;
    }

    private delegate nint HookProc(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window; public uint Id; public nuint WParam; public nint LParam;
        public uint Time; public int X; public int Y; public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookExW(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessageW(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PeekMessageW(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? module);
}
