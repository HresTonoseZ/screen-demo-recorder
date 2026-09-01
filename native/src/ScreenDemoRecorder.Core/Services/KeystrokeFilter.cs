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
        if (!settings.Enabled) return null;
        var knownKey = Names.TryGetValue(virtualKey, out var name);
        if (!knownKey && settings.DisplayMode != KeystrokeDisplayMode.AllKeys) return null;
        name ??= $"VK {virtualKey:X2}";
        var textKey = IsTextKey(virtualKey);
        var ownModifier = ModifierForKey(virtualKey);
        var chordModifiers = modifiers & ~ownModifier;
        var shortcut = (chordModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Windows)) != 0 && !altGr;
        var functionKey = virtualKey is >= 0x70 and <= 0x87;
        if (settings.DisplayMode != KeystrokeDisplayMode.AllKeys && textKey && !shortcut) return null;
        if (settings.DisplayMode == KeystrokeDisplayMode.ShortcutsOnly && !shortcut && !functionKey &&
            !(chordModifiers.HasFlag(KeyModifiers.Shift) && !textKey)) return null;
        List<string> keys = [];
        if (chordModifiers.HasFlag(KeyModifiers.Control)) keys.Add("Ctrl");
        if (chordModifiers.HasFlag(KeyModifiers.Alt)) keys.Add("Alt");
        if (chordModifiers.HasFlag(KeyModifiers.Shift)) keys.Add("Shift");
        if (chordModifiers.HasFlag(KeyModifiers.Windows)) keys.Add("Win");
        keys.Add(name);
        var chord = new KeyChord(keys.ToArray());
        return settings.HideRecorderHotkeys && hidden.Contains(new HotkeyGesture(virtualKey, modifiers)) ? null : chord;
    }

    // Key names describe physical keys, not composed characters, clipboard text or IME input.
    public static IReadOnlyDictionary<int, string> Names { get; } = BuildNames();
    public static IEnumerable<string> KeycapNames => Names.Values.Concat(["Ctrl", "Alt", "Shift", "Win"]).Distinct();

    private static bool IsTextKey(int key) => key is 0x20 or >= 0x30 and <= 0x5A or >= 0x60 and <= 0x6F or >= 0xBA and <= 0xE2;

    private static KeyModifiers ModifierForKey(int key) => key switch
    {
        0x10 or 0xA0 or 0xA1 => KeyModifiers.Shift,
        0x11 or 0xA2 or 0xA3 => KeyModifiers.Control,
        0x12 or 0xA4 or 0xA5 => KeyModifiers.Alt,
        0x5B or 0x5C => KeyModifiers.Windows,
        _ => KeyModifiers.None,
    };

    private static Dictionary<int, string> BuildNames()
    {
        Dictionary<int, string> names = new()
        {
            [0x03] = "Break", [0x08] = "Backspace", [0x09] = "Tab", [0x0C] = "Clear", [0x0D] = "Enter",
            [0x10] = "Shift", [0x11] = "Ctrl", [0x12] = "Alt", [0x13] = "Pause", [0x14] = "Caps Lock",
            [0x1B] = "Esc", [0x20] = "Space", [0x21] = "Page Up", [0x22] = "Page Down", [0x23] = "End", [0x24] = "Home",
            [0x25] = "←", [0x26] = "↑", [0x27] = "→", [0x28] = "↓", [0x2C] = "Print Screen", [0x2D] = "Insert", [0x2E] = "Delete",
            [0x2F] = "Help", [0x5B] = "Left Win", [0x5C] = "Right Win", [0x5D] = "Menu", [0x5F] = "Sleep",
            [0x6A] = "Num *", [0x6B] = "Num +", [0x6C] = "Num separator", [0x6D] = "Num −", [0x6E] = "Num .", [0x6F] = "Num /",
            [0x90] = "Num Lock", [0x91] = "Scroll Lock", [0xAD] = "Mute", [0xAE] = "Volume −", [0xAF] = "Volume +",
            [0xB0] = "Next", [0xB1] = "Previous", [0xB2] = "Stop", [0xB3] = "Play / Pause",
            [0xA0] = "Left Shift", [0xA1] = "Right Shift", [0xA2] = "Left Ctrl", [0xA3] = "Right Ctrl",
            [0xA4] = "Left Alt", [0xA5] = "Right Alt", [0xA6] = "Browser Back", [0xA7] = "Browser Forward",
            [0xA8] = "Browser Refresh", [0xA9] = "Browser Stop", [0xAA] = "Browser Search", [0xAB] = "Browser Favorites",
            [0xAC] = "Browser Home", [0xB4] = "Mail", [0xB5] = "Media", [0xB6] = "App 1", [0xB7] = "App 2",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "−", [0xBE] = ".", [0xBF] = "/",
            [0xC0] = "`", [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'", [0xE2] = "OEM 102",
            [0xE5] = "Process", [0xE7] = "Packet", [0xF6] = "Attn", [0xF7] = "CrSel", [0xF8] = "ExSel",
            [0xF9] = "Erase EOF", [0xFA] = "Play", [0xFB] = "Zoom", [0xFD] = "PA1", [0xFE] = "OEM Clear",
        };
        for (var key = 0x30; key <= 0x39; key++) names[key] = ((char)key).ToString();
        for (var key = 0x41; key <= 0x5A; key++) names[key] = ((char)key).ToString();
        for (var key = 0x60; key <= 0x69; key++) names[key] = $"Num {key - 0x60}";
        for (var key = 0x70; key <= 0x87; key++) names[key] = $"F{key - 0x6F}";
        return names;
    }
}
