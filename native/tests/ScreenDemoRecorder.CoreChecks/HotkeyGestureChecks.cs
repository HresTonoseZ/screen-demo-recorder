using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

internal static class HotkeyGestureChecks
{
    public static void Run()
    {
        Require(HotkeyGesture.TryParse("<control>+<shift>+<f9>", out var migrated, out _) && migrated.ToString() == "Ctrl+Shift+F9", "Migrated shortcut syntax was not accepted.");
        Require(HotkeyGesture.TryParse("alt+page down", out var navigation, out _) && navigation.ToString() == "Alt+PageDown", "Navigation shortcut parsing changed.");
        foreach (var value in new[] { "A", "Shift+A", "F12", "Ctrl+F12", "Ctrl+Shift", "Ctrl+S+C", "Ctrl+Ctrl+S", "Ctrl++S", "Unknown+S" })
            Require(!HotkeyGesture.TryParse(value, out _, out _), $"Invalid or reserved shortcut accepted: {value}");
        foreach (var key in KeystrokeFilter.Names.Keys)
        foreach (var modifiers in new[] { KeyModifiers.Control, KeyModifiers.Alt | KeyModifiers.Shift, KeyModifiers.Control | KeyModifiers.Windows })
        {
            if (!HotkeyGesture.TryCreate(key, modifiers, out var gesture, out _)) continue;
            Require(HotkeyGesture.TryParse(gesture.ToString(), out var roundtrip, out _) && roundtrip == gesture, $"Shortcut failed round-trip: {gesture}");
        }
        var settings = new CaptureSettings { RecordHotkey = "Ctrl+F9", PauseHotkey = "Ctrl+F9", CancelHotkey = "" };
        try { HotkeyGesture.ReadBindings(settings); throw new InvalidOperationException("Duplicate shortcuts were accepted."); }
        catch (FormatException) { }
        var disabled = new RecorderProfile { Capture = new CaptureSettings { RecordHotkey = "", PauseHotkey = "", CancelHotkey = "" } };
        ProfileValidator.Normalize(disabled);
        Require(HotkeyGesture.ReadBindings(disabled.Capture).Count == 0, "Normalization re-enabled cleared shortcuts.");
        var hidden = new KeystrokeOverlaySettings { Enabled = true };
        var filter = new KeystrokeFilter(hidden, new CaptureSettings { RecordHotkey = "Alt+PageDown" });
        Require(filter.Filter(0x22, KeyModifiers.Alt) is null, "Assigned navigation shortcut leaked into the overlay.");
        Console.WriteLine("Hotkeys: legacy syntax, round-trip keys/modifiers, reserved keys, duplicate rejection, disabled bindings and overlay privacy passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
