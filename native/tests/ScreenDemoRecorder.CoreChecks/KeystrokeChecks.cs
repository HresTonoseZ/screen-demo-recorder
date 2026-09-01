using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

internal static class KeystrokeChecks
{
    public static void Run()
    {
        var settings = new KeystrokeOverlaySettings();
        var capture = new CaptureSettings { RecordHotkey = "<shift>+<ctrl>+<f9>" };
        var filter = new KeystrokeFilter(settings, capture);
        Check(filter.Filter(0x53, KeyModifiers.Control) is null, "Keyboard overlays must be opt-in.");
        settings.Enabled = true;
        Check(filter.Filter(0x53, KeyModifiers.Control | KeyModifiers.Shift)?.Identity == "Ctrl+Shift+S", "Modifiers must form one ordered chord.");
        Check(filter.Filter(0x78, KeyModifiers.Control | KeyModifiers.Shift) is null, "Migrated recorder shortcuts must stay hidden.");
        Check(filter.Filter(0x70, KeyModifiers.None)?.Identity == "F1", "Function-key shortcuts should be visible.");
        Check(filter.Filter(0x25, KeyModifiers.None) is null, "Shortcuts-only mode must hide unmodified navigation.");
        Check(filter.Filter(0x09, KeyModifiers.Shift)?.Identity == "Shift+Tab", "Shift navigation should be visible.");
        foreach (var key in Enumerable.Range(0x30, 10).Concat(Enumerable.Range(0x41, 26)).Concat(Enumerable.Range(0x60, 10)).Concat(new[] { 0x20, 0xBA, 0xDB }))
        {
            Check(filter.Filter(key, KeyModifiers.None) is null, "Normal typing leaked into shortcuts-only mode.");
            Check(filter.Filter(key, KeyModifiers.Shift) is null, "Shift typing leaked into shortcuts-only mode.");
            Check(filter.Filter(key, KeyModifiers.Control | KeyModifiers.Alt, altGr: true) is null, "AltGr text leaked as a shortcut.");
        }
        settings.DisplayMode = KeystrokeDisplayMode.NonTextKeys;
        Check(filter.Filter(0x25, KeyModifiers.None)?.Identity == "←", "Non-text navigation is missing.");
        settings.HideNormalTyping = false;
        Check(filter.Filter(0x41, KeyModifiers.None) is null, "Non-text mode must never show typing.");
        settings.DisplayMode = KeystrokeDisplayMode.AllKeys;
        Check(filter.Filter(0x41, KeyModifiers.None)?.Identity == "A", "Explicit all-keys mode is not working.");
        settings.HideNormalTyping = true;
        Check(filter.Filter(0x41, KeyModifiers.None) is null, "Hide normal typing must override all-keys mode.");
        for (var key = 0; key < 256; key++)
        {
            var chord = filter.Filter(key, KeyModifiers.Control);
            Check(chord is null || chord.Keys.All(KeystrokeFilter.KeycapNames.Contains), "A filtered key has no renderable keycap.");
        }
        var timeline = new KeystrokeTimeline(settings);
        var save = new KeyChord(["Ctrl", "S"]);
        timeline.Add(save, TimeSpan.Zero);
        timeline.Add(save, TimeSpan.FromMilliseconds(100));
        Check(timeline.VisibleAt(TimeSpan.FromMilliseconds(150)).Length == 1, "Quick repeated shortcuts must merge.");
        Check(timeline.VisibleAt(TimeSpan.FromMilliseconds(1300)).Single().Opacity == settings.Opacity, "Visible duration changed.");
        Check(Math.Abs(timeline.VisibleAt(TimeSpan.FromMilliseconds(1425)).Single().Opacity - settings.Opacity / 2) < 0.001, "The fade is not time-based.");
        Check(timeline.VisibleAt(TimeSpan.FromMilliseconds(1550)).Length == 0, "Expired keyboard entries were retained.");
        for (var i = 0; i < 1000; i++) timeline.Add(new KeyChord([i % 2 == 0 ? "S" : "C"]), TimeSpan.FromMilliseconds(2000 + i));
        Check(timeline.VisibleAt(TimeSpan.FromMilliseconds(3000)).Length == settings.MaximumStackEntries, "The keyboard stack is unbounded.");
        settings.MergeCombinations = false;
        settings.FadeDurationMilliseconds = 0;
        var separate = new KeystrokeTimeline(settings);
        separate.Add(save, TimeSpan.Zero); separate.Add(save, TimeSpan.FromMilliseconds(50));
        Check(separate.VisibleAt(TimeSpan.FromMilliseconds(100)).Length == 2, "Disabling repeat merging was ignored.");
        Check(separate.VisibleAt(TimeSpan.FromSeconds(5)).Length == 0, "Zero-duration fade did not expire.");
        Console.WriteLine("Keystrokes: opt-in, modes, typing/AltGr privacy, recorder shortcut filtering, bounded stack, repeat merging and fade passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
