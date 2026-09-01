using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder;

public partial class HotkeyEditorWindow : Window
{
    private readonly Func<CaptureSettings, Task<string?>> save;
    private RecorderCommand? listening;
    private bool saving;
    internal CaptureSettings Result { get; }

    internal HotkeyEditorWindow(CaptureSettings settings, Func<CaptureSettings, Task<string?>> onSave)
    {
        InitializeComponent();
        save = onSave;
        Result = new CaptureSettings { RecordHotkey = settings.RecordHotkey, PauseHotkey = settings.PauseHotkey, CancelHotkey = settings.CancelHotkey };
        SourceInitialized += (_, _) => NativeDesktop.Exclude(this);
        RefreshButtons();
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<RecorderCommand>(tag, out var command)) BeginCapture(command);
    }

    internal void BeginCapture(RecorderCommand command)
    {
        listening = command;
        RefreshButtons();
        ShortcutStatus.Text = "Press the new shortcut. Esc keeps the previous assignment.";
        ButtonFor(command).Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (listening is not { } command) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            listening = null; RefreshButtons(); ShortcutStatus.Text = "Assignment cancelled."; return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var modifiers = KeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= KeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= KeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= KeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= KeyModifiers.Windows;
        Assign(command, KeyInterop.VirtualKeyFromKey(key), modifiers);
    }

    internal bool Assign(RecorderCommand command, int key, KeyModifiers modifiers)
    {
        if (!HotkeyGesture.TryCreate(key, modifiers, out var gesture, out var error)) { ShortcutStatus.Text = error; return false; }
        Set(command, gesture.ToString());
        listening = null;
        RefreshButtons();
        ShortcutStatus.Text = $"{HotkeyGesture.ActionName(command)}: {gesture}";
        return true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<RecorderCommand>(tag, out var command))
        {
            Set(command, ""); listening = null; RefreshButtons(); ShortcutStatus.Text = "Shortcut disabled. The on-screen button stays available.";
        }
    }

    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new CaptureSettings();
        Result.RecordHotkey = defaults.RecordHotkey; Result.PauseHotkey = defaults.PauseHotkey; Result.CancelHotkey = defaults.CancelHotkey;
        listening = null; RefreshButtons(); ShortcutStatus.Text = "Defaults restored. Save to apply them to this profile.";
    }

    private void Set(RecorderCommand command, string value)
    {
        switch (command)
        {
            case RecorderCommand.ToggleRecording: Result.RecordHotkey = value; break;
            case RecorderCommand.TogglePause: Result.PauseHotkey = value; break;
            case RecorderCommand.CancelRecording: Result.CancelHotkey = value; break;
        }
    }

    private Button ButtonFor(RecorderCommand command) => command switch
    {
        RecorderCommand.ToggleRecording => RecordShortcutButton,
        RecorderCommand.TogglePause => PauseShortcutButton,
        RecorderCommand.CancelRecording => CancelShortcutButton,
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    private void RefreshButtons()
    {
        foreach (var (command, text) in new[] { (RecorderCommand.ToggleRecording, Result.RecordHotkey), (RecorderCommand.TogglePause, Result.PauseHotkey), (RecorderCommand.CancelRecording, Result.CancelHotkey) })
            ButtonFor(command).Content = listening == command ? "Press shortcut…" : string.IsNullOrWhiteSpace(text) ? "Not assigned" : HotkeyGesture.TryParse(text, out var gesture, out _) ? gesture.ToString() : text;
        SaveShortcutsButton.IsEnabled = listening is null;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (saving || listening is not null) return;
        try
        {
            HotkeyGesture.ReadBindings(Result);
            saving = true; EditorPanel.IsEnabled = false;
            var error = await save(Result);
            if (error is not null) { ShortcutStatus.Text = error; return; }
            saving = false;
            DialogResult = true;
        }
        catch (Exception error) { ShortcutStatus.Text = error.Message; }
        finally { saving = false; EditorPanel.IsEnabled = true; }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (saving) e.Cancel = true;
        base.OnClosing(e);
    }
}
