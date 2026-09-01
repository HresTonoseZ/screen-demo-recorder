using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Core.Services;

[Flags]
public enum KeyModifiers { None = 0, Control = 1, Alt = 2, Shift = 4, Windows = 8 }

public sealed record KeyChord(string[] Keys)
{
    public string Identity => string.Join("+", Keys);
}

public sealed class KeystrokeFilter(KeystrokeOverlaySettings settings, CaptureSettings capture)
{
    private readonly HashSet<HotkeyGesture> hidden = new(
        new[] { capture.RecordHotkey, capture.PauseHotkey, capture.CancelHotkey }
            .Select(text => HotkeyGesture.TryParse(text, out var gesture, out _) ? (HotkeyGesture?)gesture : null)
            .OfType<HotkeyGesture>());

    public KeyChord? Filter(int virtualKey, KeyModifiers modifiers, bool altGr = false)
    {
        if (!settings.Enabled || !Names.TryGetValue(virtualKey, out var name)) return null;
        var textKey = IsTextKey(virtualKey);
        var shortcut = (modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Windows)) != 0 && !altGr;
        var functionKey = virtualKey is >= 0x70 and <= 0x87;
        if (textKey && !shortcut && (settings.HideNormalTyping || settings.DisplayMode != KeystrokeDisplayMode.AllKeys)) return null;
        if (settings.DisplayMode == KeystrokeDisplayMode.ShortcutsOnly && !shortcut && !functionKey &&
            !(modifiers.HasFlag(KeyModifiers.Shift) && !textKey)) return null;
        List<string> keys = [];
        if (modifiers.HasFlag(KeyModifiers.Control)) keys.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt)) keys.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift)) keys.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Windows)) keys.Add("Win");
        keys.Add(name);
        var chord = new KeyChord(keys.ToArray());
        return settings.HideRecorderHotkeys && hidden.Contains(new HotkeyGesture(virtualKey, modifiers)) ? null : chord;
    }

    // Key names describe physical keys, not composed characters, clipboard text or IME input.
    public static IReadOnlyDictionary<int, string> Names { get; } = BuildNames();
    public static IEnumerable<string> KeycapNames => Names.Values.Concat(["Ctrl", "Alt", "Shift", "Win"]).Distinct();

    private static bool IsTextKey(int key) => key is 0x20 or >= 0x30 and <= 0x5A or >= 0x60 and <= 0x6F or >= 0xBA and <= 0xE2;

    private static Dictionary<int, string> BuildNames()
    {
        Dictionary<int, string> names = new()
        {
            [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x14] = "Caps Lock",
            [0x1B] = "Esc", [0x20] = "Space", [0x21] = "Page Up", [0x22] = "Page Down", [0x23] = "End", [0x24] = "Home",
            [0x25] = "←", [0x26] = "↑", [0x27] = "→", [0x28] = "↓", [0x2C] = "Print Screen", [0x2D] = "Insert", [0x2E] = "Delete",
            [0x5D] = "Menu", [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num −", [0x6E] = "Num .", [0x6F] = "Num /",
            [0x90] = "Num Lock", [0x91] = "Scroll Lock", [0xAD] = "Mute", [0xAE] = "Volume −", [0xAF] = "Volume +",
            [0xB0] = "Next", [0xB1] = "Previous", [0xB2] = "Stop", [0xB3] = "Play / Pause",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "−", [0xBE] = ".", [0xBF] = "/",
            [0xC0] = "`", [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'", [0xE2] = "OEM 102",
        };
        for (var key = 0x30; key <= 0x39; key++) names[key] = ((char)key).ToString();
        for (var key = 0x41; key <= 0x5A; key++) names[key] = ((char)key).ToString();
        for (var key = 0x60; key <= 0x69; key++) names[key] = $"Num {key - 0x60}";
        for (var key = 0x70; key <= 0x87; key++) names[key] = $"F{key - 0x6F}";
        return names;
    }
}
