using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder;

public partial class MainWindow
{
    private GlobalHotkeys? hotkeys;
    private string? hotkeyError;
    private bool editingHotkeys;

    private void InitializeHotkeys()
    {
        hotkeys = new GlobalHotkeys(command =>
        {
            if (!IsLoaded || !IsVisible || !IsEnabled || editingHotkeys || !IsWindowEnabled(new WindowInteropHelper(this).Handle)) return;
            ExecuteRecordingCommand(command);
        });
        ConfigureHotkeys();
    }

    private void ConfigureHotkeys()
    {
        try
        {
            hotkeys?.Clear();
            if (editingHotkeys) return;
            var bindings = HotkeyGesture.ReadBindings(profile.Capture);
            hotkeyError = hotkeys?.Apply(bindings);
            ShortcutSummary.Text = hotkeyError is not null ? "Shortcuts unavailable · open Shortcuts to fix"
                : bindings.TryGetValue(RecorderCommand.ToggleRecording, out var record) ? $"{record} · Start / stop & save" : "Start shortcut off · use Record";
            ShortcutSummary.ToolTip = hotkeyError ?? string.Join("\n", bindings.Select(binding => $"{binding.Value} — {HotkeyGesture.ActionName(binding.Key)}"));
        }
        catch (Exception error)
        {
            hotkeyError = error.Message;
            ShortcutSummary.Text = "Shortcuts unavailable · open Shortcuts to fix";
            ShortcutSummary.ToolTip = error.Message;
        }
        ShortcutSummary.Foreground = hotkeyError is null ? (Brush)FindResource("MutedBrush") : Brushes.Goldenrod;
    }

    private async void EditHotkeys_Click(object sender, RoutedEventArgs e)
    {
        if (recordingBusy || editingHotkeys || profileOperation) return;
        editingHotkeys = true;
        try
        {
            await SaveNowAsync();
            hotkeys?.Clear();
            var editor = new HotkeyEditorWindow(profile.Capture, SaveHotkeysAsync) { Owner = this };
            editor.ShowDialog();
        }
        catch (Exception error) { ShowError(error, "Cannot Edit Shortcuts"); }
        finally { editingHotkeys = false; ConfigureHotkeys(); }
    }

    private async Task<string?> SaveHotkeysAsync(CaptureSettings candidate)
    {
        var bindings = HotkeyGesture.ReadBindings(candidate);
        var error = hotkeys?.Apply(bindings);
        if (error is not null) return error;
        var previous = (profile.Capture.RecordHotkey, profile.Capture.PauseHotkey, profile.Capture.CancelHotkey);
        profile.Capture.RecordHotkey = candidate.RecordHotkey;
        profile.Capture.PauseHotkey = candidate.PauseHotkey;
        profile.Capture.CancelHotkey = candidate.CancelHotkey;
        try { await SaveNowAsync(); return null; }
        catch
        {
            (profile.Capture.RecordHotkey, profile.Capture.PauseHotkey, profile.Capture.CancelHotkey) = previous;
            hotkeys?.Clear();
            throw;
        }
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(nint window);
}
