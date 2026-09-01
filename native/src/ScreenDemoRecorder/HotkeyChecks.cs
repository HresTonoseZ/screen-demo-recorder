using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder;

internal static class HotkeyChecks
{
    public static async Task RunAsync(string directory)
    {
        await CheckRegistrationAsync();
        await CheckCountdownControlAsync(directory);
    }

    private static async Task CheckRegistrationAsync()
    {
        GlobalHotkeys? owner = null, contender = null, released = null;
        var received = new List<RecorderCommand>();
        var dispatched = new TaskCompletionSource<RecorderCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            HotkeyGesture gesture = default;
            string? error = null;
            owner = new GlobalHotkeys(command => { received.Add(command); dispatched.TrySetResult(command); });
            foreach (var key in Enumerable.Range(0x7C, 12))
            {
                gesture = new HotkeyGesture(key, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);
                error = owner.Apply(new Dictionary<RecorderCommand, HotkeyGesture> { [RecorderCommand.ToggleRecording] = gesture });
                if (error is null) break;
            }
            Require(error is null && owner.Count == 1, "No test shortcut could be registered.");
            contender = new GlobalHotkeys(received.Add);
            Require(contender.Apply(new Dictionary<RecorderCommand, HotkeyGesture> { [RecorderCommand.TogglePause] = gesture }) is not null && contender.Count == 0,
                "Shortcut conflict did not roll back its registration.");
            HotkeyGesture spare = default;
            foreach (var key in Enumerable.Range(0x7C, 12))
            {
                spare = new HotkeyGesture(key, KeyModifiers.Control | KeyModifiers.Alt);
                error = contender.Apply(new Dictionary<RecorderCommand, HotkeyGesture> { [RecorderCommand.TogglePause] = spare });
                if (error is null) break;
            }
            Require(error is null, "No secondary test shortcut could be registered.");
            contender.Clear();
            Require(contender.Apply(new Dictionary<RecorderCommand, HotkeyGesture>
                { [RecorderCommand.TogglePause] = spare, [RecorderCommand.ToggleRecording] = gesture }) is not null && contender.Count == 0,
                "A late conflict left a partially active shortcut set.");
            Require(contender.Apply(new Dictionary<RecorderCommand, HotkeyGesture> { [RecorderCommand.TogglePause] = spare }) is null,
                "A failed registration leaked an earlier shortcut.");
            contender.Clear();
            var payload = (nint)((gesture.VirtualKey << 16) | (int)GlobalHotkeys.NativeModifiers(gesture.Modifiers));
            Require(PostMessageW(owner.Handle, 0x0312, GlobalHotkeys.Identifier(RecorderCommand.ToggleRecording), payload), "Cannot post an internal test message.");
            Require(await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(2)) == RecorderCommand.ToggleRecording, "A registered shortcut did not reach the canonical action.");
            Require(PostMessageW(owner.Handle, 0x0312, GlobalHotkeys.Identifier(RecorderCommand.ToggleRecording), payload), "Cannot queue a stale test message.");
            Require(owner.Apply(new Dictionary<RecorderCommand, HotkeyGesture> { [RecorderCommand.TogglePause] = gesture }) is null, "Shortcut remapping failed.");
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Require(received.Count == 1, "A queued old shortcut survived profile remapping.");
            owner.Dispose(); owner = null;
            released = new GlobalHotkeys(received.Add);
            Require(released.Apply(new Dictionary<RecorderCommand, HotkeyGesture> { [RecorderCommand.TogglePause] = gesture }) is null,
                "Closing the shortcut owner did not release its key.");
            released.Clear();
            Require(released.Count == 0, "Shortcut cleanup retained a registration.");
        }
        finally { released?.Dispose(); contender?.Dispose(); owner?.Dispose(); }
    }

    private static async Task CheckCountdownControlAsync(string directory)
    {
        var store = new ProfileStore(Path.Combine(directory, "hotkey-control-settings.json"), Path.Combine(directory, "missing-legacy.json"));
        await store.LoadAsync();
        var profile = store.GetActiveProfile();
        profile.Capture.Source = CaptureSource.Display;
        profile.Capture.CountdownSeconds = 3;
        profile.Output.Directory = Path.Combine(directory, "hotkey-control-output");
        await store.UpdateAsync(store.ActiveProfileName, profile);
        var window = new MainWindow(store, previewMode: true);
        try
        {
            window.ExecuteRecordingCommand(RecorderCommand.TogglePause);
            window.ExecuteRecordingCommand(RecorderCommand.CancelRecording);
            Require(window.ActiveRecordingTask is null, "Idle pause or discard started a recording.");
            window.IsEnabled = false;
            window.ExecuteRecordingCommand(RecorderCommand.ToggleRecording);
            Require(window.ActiveRecordingTask is null, "A disabled controller started a recording.");
            window.IsEnabled = true;
            window.ExecuteRecordingCommand(RecorderCommand.ToggleRecording);
            Require(window.ActiveRecordingTask is not null, "Start shortcut did not use the record action.");
            window.ExecuteRecordingCommand(RecorderCommand.TogglePause);
            window.ExecuteRecordingCommand(RecorderCommand.ToggleRecording);
            await window.ActiveRecordingTask!.WaitAsync(TimeSpan.FromSeconds(2));
            Require(window.StatusText.Text == "Countdown cancelled", "Start/stop shortcut did not cancel the countdown.");
            window.ExecuteRecordingCommand(RecorderCommand.ToggleRecording);
            window.ExecuteRecordingCommand(RecorderCommand.CancelRecording);
            await window.ActiveRecordingTask!.WaitAsync(TimeSpan.FromSeconds(2));
            Require(window.StatusText.Text == "Countdown cancelled", "Discard shortcut did not cancel the countdown.");
            Require(!Directory.Exists(profile.Output.Directory) || !Directory.EnumerateFiles(profile.Output.Directory).Any(), "Countdown cancellation created a recording.");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
