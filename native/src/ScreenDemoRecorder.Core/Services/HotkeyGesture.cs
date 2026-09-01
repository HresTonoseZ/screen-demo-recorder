using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Core.Services;

public enum RecorderCommand { ToggleRecording, TogglePause, CancelRecording }

public readonly record struct HotkeyGesture(int VirtualKey, KeyModifiers Modifiers)
{
    public override string ToString()
    {
        List<string> parts = [];
        if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join("+", parts);
    }

    public static bool TryCreate(int virtualKey, KeyModifiers modifiers, out HotkeyGesture gesture, out string error)
    {
        gesture = default;
        error = "Choose a letter, number, function key or navigation key.";
        if (!KeystrokeFilter.Names.ContainsKey(virtualKey)) return false;
        if ((modifiers & ~(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Windows)) != 0) return false;
        if (virtualKey == 0x7B) { error = "F12 is reserved by Windows for debugging. Choose another key."; return false; }
        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Windows)) == 0 && virtualKey is not (>= 0x70 and <= 0x87))
        {
            error = "Include Ctrl or Alt to avoid taking over normal typing, or use a function key.";
            return false;
        }
        gesture = new(virtualKey, modifiers);
        error = "";
        return true;
    }

    public static bool TryParse(string? value, out HotkeyGesture gesture, out string error)
    {
        gesture = default;
        error = "Use one key with Ctrl, Alt, Shift or Win.";
        if (string.IsNullOrWhiteSpace(value)) return false;
        var modifiers = KeyModifiers.None;
        int? key = null;
        foreach (var raw in value.Replace("<", "").Replace(">", "").Split('+', StringSplitOptions.TrimEntries))
        {
            var token = raw.Replace(" ", "").ToUpperInvariant();
            var modifier = token switch
            {
                "CTRL" or "CONTROL" => KeyModifiers.Control,
                "ALT" => KeyModifiers.Alt,
                "SHIFT" => KeyModifiers.Shift,
                "WIN" or "WINDOWS" or "SUPER" or "CMD" => KeyModifiers.Windows,
                _ => KeyModifiers.None,
            };
            if (modifier != KeyModifiers.None)
            {
                if (modifiers.HasFlag(modifier)) return false;
                modifiers |= modifier;
                continue;
            }
            token = token switch { "ESCAPE" => "ESC", "RETURN" => "ENTER", "PGUP" => "PAGEUP", "PGDN" or "PAGEDOWN" => "PAGEDOWN", "DEL" => "DELETE", "INS" => "INSERT", _ => token };
            if (key is not null || !KeysByName.TryGetValue(token, out var code)) return false;
            key = code;
        }
        return key is { } virtualKey && TryCreate(virtualKey, modifiers, out gesture, out error);
    }

    public static Dictionary<RecorderCommand, HotkeyGesture> ReadBindings(CaptureSettings settings)
    {
        var result = new Dictionary<RecorderCommand, HotkeyGesture>();
        foreach (var (command, text) in new[]
        {
            (RecorderCommand.ToggleRecording, settings.RecordHotkey),
            (RecorderCommand.TogglePause, settings.PauseHotkey),
            (RecorderCommand.CancelRecording, settings.CancelHotkey),
        })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!TryParse(text, out var gesture, out var error)) throw new FormatException($"{ActionName(command)}: {error}");
            if (result.ContainsValue(gesture)) throw new FormatException($"{gesture} is assigned to more than one action.");
            result.Add(command, gesture);
        }
        return result;
    }

    public static string ActionName(RecorderCommand command) => command switch
    {
        RecorderCommand.ToggleRecording => "Start / stop & save",
        RecorderCommand.TogglePause => "Pause / resume",
        RecorderCommand.CancelRecording => "Discard recording",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    private static string KeyName(int key) => key switch
    {
        0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
        0x6A => "NumMultiply", 0x6B => "NumAdd", 0x6D => "NumSubtract", 0x6E => "NumDecimal", 0x6F => "NumDivide",
        0xAF => "VolumeUp", 0xAE => "VolumeDown", 0xBB => "Plus", 0xBD => "Minus",
        _ => KeystrokeFilter.Names[key].Replace(" ", ""),
    };

    private static readonly Dictionary<string, int> KeysByName = KeystrokeFilter.Names.Keys.ToDictionary(key => KeyName(key).ToUpperInvariant(), key => key);
}
