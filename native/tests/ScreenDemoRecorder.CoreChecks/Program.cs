using System.Text.Json;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

if (args is [var legacySettingsPath])
{
    var document = LegacySettingsMigrator.Migrate(await File.ReadAllTextAsync(legacySettingsPath));
    Console.WriteLine($"Validated {document.Profiles.Count} legacy profile(s) for migration.");
    return 0;
}

var testRoot = Path.Combine(Path.GetTempPath(), "ScreenDemoRecorder.CoreChecks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);

try
{
    var portableStore = new ProfileStore();
    Require(string.Equals(Path.GetDirectoryName(portableStore.SettingsPath),
        Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase),
        "Default settings are not stored beside the executable.");
    await CheckDefaultProfileAsync(Path.Combine(testRoot, "default"));
    await CheckProfileOperationsAsync(Path.Combine(testRoot, "profiles"));
    await CheckProfileTransferAsync(Path.Combine(testRoot, "transfer"));
    await CheckLegacyMigrationAsync(Path.Combine(testRoot, "migration"));
    ParityContractChecks.Run();
    await CheckProfileIsolationAsync(Path.Combine(testRoot, "isolation"));
    await CheckFailedHotkeySaveAsync(Path.Combine(testRoot, "failed-hotkeys"));
    CheckRegionGeometry();
    CaptureCoordinateChecks.Run();
    CheckOverlayPlacement();
    CheckMp4OutputPlanning();
    KeystrokeChecks.Run();
    ClickChecks.Run();
    HotkeyGestureChecks.Run();
    CheckRecordingOutput(Path.Combine(testRoot, "recordings"));
    await RecordingSessionChecks.RunAsync(Path.Combine(testRoot, "sessions"));
    GifChecks.Run(Path.Combine(testRoot, "gifs"));
    Console.WriteLine("All native core checks passed.");
    return 0;
}
finally
{
    if (Directory.Exists(testRoot))
    {
        Directory.Delete(testRoot, true);
    }
}

static async Task CheckDefaultProfileAsync(string directory)
{
    var store = NewStore(directory);
    await store.LoadAsync();
    Require(store.ActiveProfileName == "Default", "The default profile was not selected.");
    Require(store.ProfileNames.Count == 1, "A new settings file must contain one profile.");
    Require(File.Exists(Path.Combine(directory, "settings-v2.json")), "Default settings were not persisted.");
    Require(store.GetActiveProfile().Overlays.Label.Lines.Count == 1, "The default universal label must contain one neutral editable row.");
}

static async Task CheckProfileOperationsAsync(string directory)
{
    var store = NewStore(directory);
    await store.LoadAsync();
    var profile = store.GetActiveProfile();
    profile.Capture.RecordingFps = 60;
    profile.Capture.RecordHotkey = "Ctrl+Alt+F6";
    profile.Capture.CancelHotkey = "";
    profile.Capture.Source = CaptureSource.Window;
    profile.Capture.WindowTitle = "  Example Document  ";
    profile.Capture.WindowProcessName = "example";
    profile.Capture.WindowClassName = "ExampleWindow";
    profile.Capture.GifFps = 23.976;
    profile.Output.Width = 961;
    profile.Output.Mp4Width = 1280;
    profile.Output.GifPaletteColors = 137;
    profile.Output.GifDither = false;
    profile.Output.GifLoopCount = 3;
    profile.Output.GifFrameStep = 2;
    profile.Output.FinalFrameDurationMilliseconds = 1250;
    profile.Output.KeepSourceVideo = true;
    profile.Output.OpenFolderAfterSave = true;
    profile.Overlays.Keystrokes.Enabled = true;
    profile.Overlays.Desktop.ShowLabel = true;
    profile.Overlays.Desktop.ShowKeystrokes = true;
    profile.Overlays.Desktop.ShowMouseClicks = true;
    profile.Overlays.Label.Lines.Add(new LabelTextLine { Text = "Third row" });
    await store.UpdateActiveAsync(profile);

    var duplicate = await store.DuplicateAsync("Tutorial");
    Require(duplicate == "Tutorial", "The requested profile name was not used.");
    await store.RenameActiveAsync("Blender tutorial");

    var reloaded = NewStore(directory);
    await reloaded.LoadAsync();
    Require(reloaded.ActiveProfileName == "Blender tutorial", "The renamed profile was not persisted.");
    var restored = reloaded.GetActiveProfile();
    Require(restored.Capture.RecordingFps == 60, "The recording FPS did not survive a round trip.");
    Require(restored.Capture.GifFps == 23.976 && restored.Output is { Width: 961, GifPaletteColors: 137, GifDither: false,
        GifLoopCount: 3, GifFrameStep: 2, FinalFrameDurationMilliseconds: 1250, KeepSourceVideo: true, OpenFolderAfterSave: true },
        "Advanced GIF settings did not survive profile duplication and reload.");
    Require(restored.Output.Mp4Width == 1280, "The MP4 resolution did not survive profile duplication and reload.");
    Require(restored.Capture.RecordHotkey == "Ctrl+Alt+F6" && restored.Capture.CancelHotkey == "", "Assigned and cleared shortcuts did not survive profile duplication and reload.");
    Require(restored.Capture is { Source: CaptureSource.Window, WindowTitle: "Example Document", WindowProcessName: "example", WindowClassName: "ExampleWindow" },
        "The selected window identity did not survive profile duplication and reload.");
    Require(restored.Overlays.Keystrokes.Enabled, "The keystroke overlay did not survive a round trip.");
    Require(restored.Overlays.Desktop is { ShowLabel: true, ShowKeystrokes: true, ShowMouseClicks: true },
        "Live desktop overlay settings did not survive a round trip.");
    Require(restored.Overlays.Label.Lines.Count == 2, "Universal label rows did not survive a round trip.");
}

static async Task CheckProfileTransferAsync(string directory)
{
    var store = NewStore(directory);
    await store.LoadAsync();
    var profile = store.GetActiveProfile();
    profile.Application.AlwaysOnTop = false;
    profile.Application.MinimizeToTray = true;
    profile.Application.Theme = ApplicationTheme.Light;
    profile.Overlays.Label.BackgroundColor = "#22446688";
    profile.Overlays.Label.BackgroundBlur = 14;
    profile.Overlays.Label.ShadowBlur = 19;
    profile.Overlays.Label.Lines[0].StrokeWidth = 3;
    profile.Overlays.Label.Lines[0].ShadowColor = "#102938AA";
    profile.Overlays.Label.Lines[0].ShadowBlur = 5;
    profile.Overlays.Label.Lines[0].ShadowOffsetX = 2;
    profile.Overlays.Label.Lines[0].ShadowOffsetY = 4;
    profile.Capture.HighlightClicks = true;
    profile.Overlays.Clicks.LeftColor = "#334455FF";
    profile.Overlays.Clicks.RightColor = "#FF8844FF";
    profile.Overlays.Clicks.Size = 58;
    profile.Overlays.Clicks.DurationMilliseconds = 900;
    profile.Selection.SelectionColor = "#12345678";
    profile.Selection.DashLength = 17;
    profile.Selection.DashGap = 11;
    profile.Selection.HandleColor = "#ABCDEFEE";
    profile.Selection.HandleBorderColor = "#102030FF";
    profile.Selection.HandleBorderWidth = 4;
    profile.Selection.HandleSize = 22;
    profile.Selection.HandleShape = SelectionHandleShape.Square;
    profile.Selection.DimensionColor = "#FEDCBAFF";
    profile.Selection.DimensionSize = 18;
    await store.UpdateActiveAsync(profile);

    var exportPath = Path.Combine(directory, "profile.json");
    await store.ExportActiveAsync(exportPath);
    Require(File.Exists(exportPath), "Profile export was not written.");
    var exported = await File.ReadAllTextAsync(exportPath);
    Require(exported.Contains("\"schemaVersion\": 2") && exported.Contains("\"minimizeToTray\": true"),
        "Profile export omitted its schema or application settings.");

    var importedName = await store.ImportAsync(exportPath);
    Require(importedName == "Default 2" && store.ActiveProfileName == importedName, "Imported profile naming or activation changed.");
    var imported = store.GetActiveProfile();
    Require(imported.Application is { AlwaysOnTop: false, MinimizeToTray: true, Theme: ApplicationTheme.Light } &&
        imported.Overlays.Label is { BackgroundColor: "#22446688", BackgroundBlur: 14, ShadowBlur: 19 } &&
        imported.Overlays.Label.Lines[0] is { StrokeWidth: 3, ShadowColor: "#102938AA", ShadowBlur: 5, ShadowOffsetX: 2, ShadowOffsetY: 4 },
        "Advanced application or label settings did not survive profile transfer.");
    Require(imported.Capture.HighlightClicks && imported.Overlays.Clicks is
        { LeftColor: "#334455FF", RightColor: "#FF8844FF", Size: 58, DurationMilliseconds: 900 },
        "Mouse-click visualization settings did not survive profile transfer.");
    Require(imported.Selection is { SelectionColor: "#12345678", DashLength: 17, DashGap: 11,
        HandleColor: "#ABCDEFEE", HandleBorderColor: "#102030FF", HandleBorderWidth: 4, HandleSize: 22,
        HandleShape: SelectionHandleShape.Square, DimensionColor: "#FEDCBAFF", DimensionSize: 18 },
        "Advanced selection appearance did not survive profile transfer.");

    var recentPath = Path.Combine(directory, "saved.mp4");
    await store.AddRecentFileAsync(recentPath);
    await store.AddRecentFileAsync(recentPath.ToUpperInvariant());
    Require(store.RecentFiles.Count == 1 && string.Equals(store.RecentFiles[0], Path.GetFullPath(recentPath.ToUpperInvariant()),
        StringComparison.Ordinal), "Recent recordings were not normalized or deduplicated.");

    var countBeforeInvalid = store.ProfileNames.Count;
    var activeBeforeInvalid = store.ActiveProfileName;
    var invalidPath = Path.Combine(directory, "invalid-profile.json");
    await File.WriteAllTextAsync(invalidPath, exported.Replace("\"profile\": {", "\"unexpected\": true,\n  \"profile\": {", StringComparison.Ordinal));
    var invalidRejected = false;
    try { await store.ImportAsync(invalidPath); }
    catch (JsonException) { invalidRejected = true; }
    Require(invalidRejected && store.ProfileNames.Count == countBeforeInvalid && store.ActiveProfileName == activeBeforeInvalid,
        "Invalid import mutated the profile store.");

    var legacyPath = Path.Combine(directory, "legacy-profile.json");
    await File.WriteAllTextAsync(legacyPath, LegacyProfileExportJson());
    var legacyName = await store.ImportAsync(legacyPath);
    var legacy = store.GetActiveProfile();
    Require(legacyName == "Legacy import" && legacy.Application is { AlwaysOnTop: false, MinimizeToTray: true, Theme: ApplicationTheme.Dark } &&
        legacy.Overlays.Label.Lines[0].Text == "Migrated row", "A version 1 exported profile did not migrate during import.");

    await store.ResetActiveAsync();
    var reset = store.GetActiveProfile();
    Require(store.ActiveProfileName == legacyName && reset.Application.Theme == ApplicationTheme.System && reset.Overlays.Label.Lines.Count == 1,
        "Reset did not preserve the profile name or restore defaults.");
    Console.WriteLine("Profile transfer: atomic export/import, strict validation, legacy migration and reset passed.");
}

static async Task CheckLegacyMigrationAsync(string directory)
{
    Directory.CreateDirectory(directory);
    var legacyPath = Path.Combine(directory, "settings.json");
    await File.WriteAllTextAsync(legacyPath, LegacySettingsJson());

    var store = NewStore(directory);
    await store.LoadAsync();
    var profile = store.GetActiveProfile();
    Require(profile.Capture.Source == CaptureSource.Region, "The legacy region mode was not migrated.");
    Require(profile.Capture.Region is { Width: 1280, Height: 720 }, "The legacy region was not migrated.");
    Require(profile.Overlays.Label.Lines.Select(line => line.Text).SequenceEqual(["Legacy title", "Legacy subtitle"]), "Legacy caption text was not converted to universal rows.");
    Require(profile.Overlays.Label.BackgroundBlur == 7 && profile.Overlays.Label.Lines[0] is
        { ShadowColor: "#123456AA", ShadowBlur: 6, ShadowOffsetX: 2, ShadowOffsetY: 3 },
        "Legacy background blur or per-row shadow was not migrated.");
    Require(!profile.Overlays.Keystrokes.Enabled, "Keystrokes must remain opt-in after migration.");
    Require(profile.Selection is { DashLength: 13, DashGap: 7, HandleColor: "#AABBCCDD", HandleBorderColor: "#112233FF",
        HandleBorderWidth: 3, HandleSize: 18, HandleShape: SelectionHandleShape.Square,
        DimensionColor: "#FFEEDDFF", DimensionSize: 15 }, "Legacy selection appearance was not fully migrated.");
    Require(File.Exists(Path.Combine(directory, "settings-v1.backup.json")), "The legacy profile backup was not created.");

    var serialized = JsonSerializer.Serialize(profile);
    Require(!serialized.Contains("badge", StringComparison.OrdinalIgnoreCase), "The retired badge must not exist in the native profile schema.");
}

static ProfileStore NewStore(string directory)
{
    return new ProfileStore(
        Path.Combine(directory, "settings-v2.json"),
        Path.Combine(directory, "settings.json"));
}

static async Task CheckProfileIsolationAsync(string directory)
{
    var store = NewStore(directory);
    await store.LoadAsync();
    var firstName = store.ActiveProfileName;
    var first = store.GetActiveProfile();
    first.Capture.RecordingFps = 23.976;
    first.Overlays.Label.Lines[0].Text = "First";
    await store.DuplicateAsync("Second");
    await store.UpdateAsync(firstName, first);
    Require(store.GetActiveProfile().Overlays.Label.Lines[0].Text != "First", "A pending save changed the wrong profile.");
    first.Overlays.Label.Lines[0].Text = "Changed outside store";
    await store.ActivateAsync(firstName);
    Require(store.GetActiveProfile().Overlays.Label.Lines[0].Text == "First", "The store retained mutable profile references.");
    Require(store.GetActiveProfile().Capture.RecordingFps == 23.976, "Custom frame rates were rounded.");
    await store.DeleteActiveAsync();
    var rejected = false;
    try { await store.DeleteActiveAsync(); } catch (InvalidOperationException) { rejected = true; }
    Require(rejected, "Deleting the final profile must be rejected.");
}

static async Task CheckFailedHotkeySaveAsync(string directory)
{
    var store = NewStore(directory);
    await store.LoadAsync();
    var previous = store.GetActiveProfile().Capture.RecordHotkey;
    var changed = store.GetActiveProfile();
    changed.Capture.RecordHotkey = "Ctrl+Alt+F6";
    Directory.CreateDirectory(store.SettingsPath + ".tmp");
    var rejected = false;
    try { await store.UpdateActiveAsync(changed); }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { rejected = true; }
    Require(rejected, "The simulated profile-write failure did not occur.");
    Require(store.GetActiveProfile().Capture.RecordHotkey == previous, "A failed save changed the active shortcut in memory.");
    var restored = NewStore(directory);
    await restored.LoadAsync();
    Require(restored.GetActiveProfile().Capture.RecordHotkey == previous, "A failed save changed the stored shortcut.");
}

static void CheckRegionGeometry()
{
    var r = new PixelRect(100, 80, 640, 360);
    Require(RegionGeometry.Move(r, 20, 30, 1920, 1080) == new PixelRect(120, 110, 640, 360), "Whole-region drag changed its size.");
    Require(RegionGeometry.Move(r, -500, -500, 1920, 1080) == new PixelRect(0, 0, 640, 360), "Movement escaped the display.");
    Require(RegionGeometry.Move(r, 9999, 9999, 1920, 1080) == new PixelRect(1280, 720, 640, 360), "Movement escaped the far display edge.");
    Require(RegionGeometry.Move(r, -95, -75, 1920, 1080, 12).X == 0, "Movement did not snap to the display edge.");
    Require(RegionGeometry.Move(r, 1, 0, 1920, 1080).X == 101, "One-pixel nudging failed.");
    Require(RegionGeometry.Move(r, 10, 0, 1920, 1080).X == 110, "Ten-pixel nudging failed.");
    Require(RegionGeometry.Resize(r, RegionEdges.Left, 10, 0, 1920, 1080) == new PixelRect(110, 80, 630, 360), "Left edge did not preserve the right edge.");
    Require(RegionGeometry.Resize(r, RegionEdges.Bottom, 0, 20, 1920, 1080).Height == 380, "Bottom edge resize failed.");
    Require(RegionGeometry.Resize(new PixelRect(100, 80, 160, 120), RegionEdges.Right, -500, 0,
        1920, 1080, minimumSize: 64).Width == 64, "The profile-specific minimum region size was ignored.");
    var created = RegionGeometry.Create(new PixelPoint(900, 500), new PixelPoint(420, 230), 1920, 1080, 16.0 / 9);
    Require(created.Right == 900 && created.Bottom == 500, "Aspect-locked drawing lost its starting corner.");
    Require(Math.Abs(created.Width - created.Height * 16.0 / 9) < 2, "Aspect-locked drawing did not create the requested ratio.");
    var previousWidth = 0;
    for (var delta = 0; delta <= 400; delta += 4)
    {
        var resized = RegionGeometry.Resize(r, RegionEdges.Right | RegionEdges.Bottom, delta, delta / 2,
            1920, 1080, 16.0 / 9);
        Require(resized.Width >= previousWidth, "Aspect-locked corner resizing reversed while the pointer moved outward.");
        previousWidth = resized.Width;
    }
    var allEdges = new[] { RegionEdges.Left, RegionEdges.Right, RegionEdges.Top, RegionEdges.Bottom,
        RegionEdges.Top | RegionEdges.Left, RegionEdges.Top | RegionEdges.Right,
        RegionEdges.Bottom | RegionEdges.Left, RegionEdges.Bottom | RegionEdges.Right };
    foreach (var edge in allEdges)
    foreach (var delta in new[] { -5000, -40, 0, 40, 5000 })
    foreach (var ratio in new double?[] { null, 16.0 / 9 })
    {
        var resized = RegionGeometry.Resize(r, edge, delta, delta, 1920, 1080, ratio);
        Require(resized.X >= 0 && resized.Y >= 0 && resized.Right <= 1920 && resized.Bottom <= 1080, $"Resize left the display: {edge}.");
        Require(resized.Width >= 32 && resized.Height >= 32, $"Minimum size violated: {edge}.");
        if (ratio is { } aspect)
            Require(Math.Abs(resized.Width - resized.Height * aspect) < 2, $"Aspect ratio changed: {edge}.");
    }
    var restored = RegionGeometry.Fit(new PixelRect(1500, 900, 1280, 720), 1280, 720);
    Require(restored == new PixelRect(0, 0, 1280, 720), "Restoring to a smaller monitor failed.");
    Console.WriteLine("Region geometry: movement, snapping, drag creation, smooth aspect lock, eight resize handles and bounds passed.");
}

static void CheckMp4OutputPlanning()
{
    Require(Mp4OutputPlan.Create(1920, 1080, 1280) is { ContentWidth: 1280, ContentHeight: 720, Width: 1280, Height: 720, IsResized: true },
        "The HD MP4 preset did not preserve the capture aspect ratio.");
    Require(Mp4OutputPlan.Create(1280, 720, 1920) is { ContentWidth: 1280, ContentHeight: 720, IsResized: false },
        "MP4 output enlarged a smaller capture without adding detail.");
    Require(Mp4OutputPlan.Create(321, 181, 0) is { ContentWidth: 321, ContentHeight: 181, Width: 322, Height: 182, IsResized: false },
        "Original MP4 sizing did not retain odd content with encoder padding.");
    Require(Mp4OutputPlan.Create(321, 181, 160) is { ContentWidth: 160, ContentHeight: 90, Width: 160, Height: 90, IsResized: true },
        "Custom MP4 sizing did not round proportionally.");
    Require(Mp4OutputPlan.Create(1080, 1920, 1280) is { ContentWidth: 1080, ContentHeight: 1920, IsResized: false },
        "A portrait recording was unnecessarily enlarged.");
    var rejected = false;
    try { _ = Mp4OutputPlan.Create(1920, 1080, 12); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Require(rejected, "An invalid MP4 width was accepted.");
    Console.WriteLine("MP4 planning: presets, custom width, portrait, no-upscale and odd padding passed.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void CheckRecordingOutput(string directory)
{
    Require(RecordingOutput.ValidateFilenameTemplate("  Demo_{date}_{time}_{title}_{counter}  ") ==
        "Demo_{date}_{time}_{title}_{counter}", "A valid filename template was not normalized.");
    foreach (var invalidTemplate in new[] { "", "Demo_{unknown}", "Demo_{date", "Demo_date}" })
    {
        var rejected = false;
        try { RecordingOutput.ValidateFilenameTemplate(invalidTemplate); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, $"Invalid filename template was accepted: {invalidTemplate}");
    }
    var settings = new OutputSettings { Directory = directory, FilenameTemplate = "Example" };
    string first;
    using (var output = new RecordingOutput(settings, "Title"))
    {
        output.Stream.Write([1, 2, 3]);
        first = output.Commit();
    }
    using (var output = new RecordingOutput(settings, "Title"))
    {
        output.Stream.Write([4, 5]);
        var second = output.Commit();
        Require(second != first && File.ReadAllBytes(first).SequenceEqual(new byte[] { 1, 2, 3 }), "Saving overwrote an existing recording.");
    }
    settings.FilenameTemplate = "{title}";
    using (var output = new RecordingOutput(settings, "../../unsafe:name"))
    {
        var safePath = output.Commit();
        Require(Path.GetDirectoryName(safePath) == Path.GetFullPath(directory), "A filename escaped the output directory.");
    }
    using (var output = new RecordingOutput(settings, "CON"))
        Require(Path.GetFileName(output.Commit()) == "_CON.mp4", "Reserved Windows filenames were not sanitized.");
    string cancelled;
    using (var output = new RecordingOutput(settings, "Cancel"))
    {
        cancelled = output.TemporaryPath;
        output.Discard();
    }
    Require(!File.Exists(cancelled) && File.Exists(first), "Discard removed the wrong output.");
    string recovery;
    using (var output = new RecordingOutput(settings, "Recovery"))
    {
        recovery = output.TemporaryPath;
        output.Stream.WriteByte(42);
    }
    Require(File.ReadAllBytes(recovery).SequenceEqual(new byte[] { 42 }), "An incomplete recording was deleted on failure.");
    using (var output = new RecordingOutput(settings, "External"))
    {
        var externalPath = output.PrepareForExternalWriter();
        Require(!File.Exists(externalPath), "The output reservation remained locked for an external encoder.");
        File.WriteAllBytes(externalPath, [24]);
        var committed = output.Commit();
        Require(File.ReadAllBytes(committed).SequenceEqual(new byte[] { 24 }),
            "An externally encoded recording was not committed atomically.");
    }
    Console.WriteLine("Recording output: template validation, collision protection, external writers, safe filenames, discard and recovery passed.");
}

static void CheckOverlayPlacement()
{
    Require(OverlayPlacement.Place(1920, 1080, 560, 80, OverlayAnchor.BottomCenter, 0, 24) == new PixelRect(680, 976, 560, 80),
        "Bottom anchor margins must point inward, matching migrated profiles.");
    Require(OverlayPlacement.Place(1920, 1080, 560, 80, OverlayAnchor.TopRight, 24, 24) == new PixelRect(1336, 24, 560, 80),
        "Right anchor margins must point inward.");
    foreach (var anchor in Enum.GetValues<OverlayAnchor>())
    foreach (var frame in new[] { (Width: 320, Height: 180), (Width: 720, Height: 1280), (Width: 32, Height: 32) })
    foreach (var offset in new[] { -9999, 0, 24, 9999 })
    {
        var box = OverlayPlacement.Place(frame.Width, frame.Height, 560, 120, anchor, offset, offset);
        Require(box.X >= 0 && box.Y >= 0 && box.Right <= frame.Width && box.Bottom <= frame.Height,
            "An overlay escaped the captured frame.");
    }
    Console.WriteLine("Overlay placement: nine anchors, inward margins, portrait frames and edge bounds passed.");
}

static string LegacySettingsJson() => """
{
  "schema_version": 1,
  "active_profile": "Legacy",
  "profiles": {
    "Legacy": {
      "capture": {
        "mode": "region",
        "monitor": 1,
        "region": [100, 80, 1280, 720],
        "recording_fps": 30,
        "gif_fps": 12,
        "capture_cursor": true,
        "countdown_seconds": 3,
        "maximum_duration_seconds": 60,
        "toggle_hotkey": "<ctrl>+<shift>+<f9>",
        "pause_hotkey": "<ctrl>+<shift>+<f8>",
        "cancel_hotkey": "<ctrl>+<shift>+<f10>"
      },
      "output": {
        "directory": "C:\\Videos",
        "filename_template": "{date}_{time}_{title}_{counter}",
        "width": 960,
        "palette_colors": 128,
        "dither": true,
        "loop": 0,
        "frame_step": 1,
        "final_frame_duration_ms": 0,
        "save_source_video": false,
        "open_folder_after_save": false
      },
      "caption": {
        "enabled": true,
        "anchor": "bottom_center",
        "offset_x": 0,
        "offset_y": 24,
        "width": 560,
        "background_blur": 7,
        "title": { "enabled": true, "text": "Legacy title", "size": 22, "bold": true, "italic": false, "color": "#FFFFFFFF", "shadow_color": "#123456AA", "shadow_blur": 6, "shadow_offset_x": 2, "shadow_offset_y": 3 },
        "subtitle": { "enabled": true, "text": "Legacy subtitle", "size": 14, "bold": false, "italic": false, "color": "#C4D0E4FF" },
        "badge": { "enabled": true, "text": "DEMO" }
      },
      "selection": {
        "line_color": "#4C97FFFF",
        "line_width": 2,
        "dash_length": 13,
        "dash_gap": 7,
        "handle_color": "#AABBCCDD",
        "handle_border": "#112233FF",
        "handle_border_width": 3,
        "handle_size": 18,
        "handle_shape": "square",
        "dim_color": "#00000099",
        "show_dimensions": true,
        "dimension_color": "#FFEEDDFF",
        "dimension_size": 15
      }
    }
  },
  "recent_files": []
}
""";

static string LegacyProfileExportJson() => """
{
  "schema_version": 1,
  "name": "Legacy import",
  "profile": {
    "caption": {
      "enabled": true,
      "title": { "enabled": true, "text": "Migrated row", "size": 20, "bold": true, "color": "#FFFFFFFF" },
      "subtitle": { "enabled": false }
    },
    "application": {
      "always_on_top": false,
      "minimize_to_tray": true,
      "theme": "dark"
    }
  }
}
""";
