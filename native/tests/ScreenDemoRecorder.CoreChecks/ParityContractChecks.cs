using System.Text.Json;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

internal static class ParityContractChecks
{
    public static void Run()
    {
        CheckNativeSurface();
        CheckLegacyProfileMigration();
        Console.WriteLine("Functional parity: native capture/output/profile surface and every retained legacy profile field passed.");
    }

    private static void CheckNativeSurface()
    {
        Require(Enum.GetValues<CaptureSource>().SequenceEqual(
            [CaptureSource.Region, CaptureSource.Display, CaptureSource.Window]),
            "The native capture-source contract changed.");
        Require(Enum.GetValues<OutputFormat>().SequenceEqual([OutputFormat.Gif, OutputFormat.Mp4]),
            "The native output-format contract changed.");
        var profile = new RecorderProfile();
        Require(profile.Overlays.Label.Lines.Count == 1 && profile.Overlays.Label.Lines[0].Text == "Your text here",
            "The universal label no longer starts with one neutral editable row.");
        Require(profile.Overlays.Keystrokes is { Enabled: false, DisplayMode: KeystrokeDisplayMode.ShortcutsOnly,
            HideNormalTyping: true, HideRecorderHotkeys: true },
            "The safe pressed-key defaults changed.");
        Require(!profile.Capture.HighlightClicks && profile.Selection.KeepBoundaryVisible,
            "Click visualization or persistent boundary defaults changed.");
    }

    private static void CheckLegacyProfileMigration()
    {
        var document = LegacySettingsMigrator.Migrate(LegacyJson);
        Require(document.ActiveProfile == "Parity" && document.Profiles.Count == 1 &&
            document.RecentFiles.SequenceEqual([@"C:\Recordings\one.gif", @"C:\Recordings\two.mp4"]),
            "Legacy document identity or recent recordings were not retained.");
        var profile = document.Profiles["Parity"];
        Require(profile.Capture is
            {
                Source: CaptureSource.Region, DisplayIndex: 2, LockAspectRatio: true,
                AspectWidth: 21, AspectHeight: 9, SnapToEdges: false, RegionMinimumSize: 64,
                RecordingFps: 23.976, GifFps: 17.5, ShowCursor: false,
                CountdownSeconds: 7, MaximumDurationSeconds: 1234,
                RecordHotkey: "Ctrl+Alt+F6", PauseHotkey: "Ctrl+Shift+F7", CancelHotkey: "Alt+F8",
            } && profile.Capture.Region is { X: 101, Y: 82, Width: 1281, Height: 721 },
            "Legacy capture settings were not fully migrated.");
        Require(profile.Output is
            {
                Format: OutputFormat.Gif, Directory: @"C:\Parity", FilenameTemplate: "{title}_{counter}",
                Width: 1111, GifPaletteColors: 93, GifDither: false, GifLoopCount: 5,
                GifFrameStep: 3, FinalFrameDurationMilliseconds: 777,
                KeepSourceVideo: true, OpenFolderAfterSave: true,
            }, "Legacy output settings were not fully migrated.");
        Require(profile.Overlays.Label is
            {
                Enabled: true, Style: LabelStylePreset.Custom, Anchor: OverlayAnchor.CenterRight,
                OffsetX: -37, OffsetY: 43, Width: 611, PaddingX: 23, PaddingY: 17,
                LineGap: 8, CornerRadius: 19, BackgroundColor: "#102030A0", BackgroundBlur: 11,
                BorderColor: "#405060B0", BorderWidth: 3, ShadowColor: "#11121390",
                ShadowBlur: 13, ShadowOffsetX: -4, ShadowOffsetY: 6,
            }, "Legacy label-container settings were not fully migrated.");
        Require(profile.Overlays.Label.Lines is [{ } title, { } subtitle] &&
            title is
            {
                Text: "Parity title", FontFamily: "Segoe UI", Size: 27, IsBold: false, IsItalic: true,
                Color: "#A1B2C3D4", Alignment: "right", StrokeWidth: 2, StrokeColor: "#102938FF",
                ShadowColor: "#182736A0", ShadowBlur: 5, ShadowOffsetX: 2, ShadowOffsetY: 3,
            } && subtitle is
            {
                Text: "Parity subtitle", FontFamily: "Arial", Size: 15, IsBold: true, IsItalic: false,
                Color: "#D4C3B2A1", Alignment: "right", StrokeWidth: 1, StrokeColor: "#564738FF",
                ShadowColor: "#91827380", ShadowBlur: 4, ShadowOffsetX: -2, ShadowOffsetY: 2,
            }, "Legacy title/subtitle fields were not converted to universal rows.");
        Require(profile.Selection is
            {
                SelectionColor: "#12345678", LineWidth: 4, DashLength: 17, DashGap: 12,
                HandleColor: "#ABCDEFEE", HandleBorderColor: "#102030FF", HandleBorderWidth: 5,
                HandleSize: 24, HandleShape: SelectionHandleShape.Square, DimColor: "#01020399",
                ShowDimensions: false, DimensionColor: "#FFEEDDFF", DimensionSize: 18,
            }, "Legacy selection appearance was not fully migrated.");
        Require(profile.Application is
            { AlwaysOnTop: false, MinimizeToTray: true, Theme: ApplicationTheme.Light },
            "Legacy application behavior was not fully migrated.");
        Require(!profile.Overlays.Keystrokes.Enabled && !profile.Capture.HighlightClicks,
            "New input overlays must remain opt-in after migration.");
        Require(!JsonSerializer.Serialize(profile).Contains("badge", StringComparison.OrdinalIgnoreCase),
            "The intentionally retired badge entered the native schema.");
    }

    private const string LegacyJson = """
    {
      "schema_version": 1,
      "active_profile": "Parity",
      "profiles": {
        "Parity": {
          "capture": {
            "mode": "region", "monitor": 2, "region": [101, 82, 1281, 721],
            "region_lock_aspect": true, "region_aspect_width": 21, "region_aspect_height": 9,
            "region_snap_to_edges": false, "region_minimum_size": 64,
            "recording_fps": 23.976, "gif_fps": 17.5, "capture_cursor": false,
            "countdown_seconds": 7, "maximum_duration_seconds": 1234,
            "toggle_hotkey": "<ctrl>+<alt>+<f6>", "pause_hotkey": "<ctrl>+<shift>+<f7>",
            "cancel_hotkey": "<alt>+<f8>"
          },
          "output": {
            "directory": "C:\\Parity", "filename_template": "{title}_{counter}", "width": 1111,
            "palette_colors": 93, "dither": false, "loop": 5, "frame_step": 3,
            "final_frame_duration_ms": 777, "save_source_video": true, "open_folder_after_save": true
          },
          "caption": {
            "enabled": true, "anchor": "center_right", "offset_x": -37, "offset_y": 43, "width": 611,
            "padding_x": 23, "padding_y": 17, "line_gap": 8, "text_alignment": "right",
            "corner_radius": 19, "background": "#102030A0", "background_blur": 11,
            "border": "#405060B0", "border_width": 3, "shadow_color": "#11121390",
            "shadow_blur": 13, "shadow_offset_x": -4, "shadow_offset_y": 6,
            "title": {
              "enabled": true, "text": "Parity title", "font": "Segoe UI", "size": 27,
              "bold": false, "italic": true, "color": "#A1B2C3D4", "stroke_width": 2,
              "stroke_color": "#102938FF", "shadow_color": "#182736A0", "shadow_blur": 5,
              "shadow_offset_x": 2, "shadow_offset_y": 3
            },
            "subtitle": {
              "enabled": true, "text": "Parity subtitle", "font": "Arial", "size": 15,
              "bold": true, "italic": false, "color": "#D4C3B2A1", "stroke_width": 1,
              "stroke_color": "#564738FF", "shadow_color": "#91827380", "shadow_blur": 4,
              "shadow_offset_x": -2, "shadow_offset_y": 2
            },
            "badge": { "enabled": true, "text": "RETIRED" }
          },
          "selection": {
            "line_color": "#12345678", "line_width": 4, "dash_length": 17, "dash_gap": 12,
            "handle_color": "#ABCDEFEE", "handle_border": "#102030FF", "handle_border_width": 5,
            "handle_size": 24, "handle_shape": "square", "dim_color": "#01020399",
            "show_dimensions": false, "dimension_color": "#FFEEDDFF", "dimension_size": 18
          },
          "application": { "always_on_top": false, "minimize_to_tray": true, "theme": "light" }
        }
      },
      "recent_files": ["C:\\Recordings\\one.gif", "C:\\Recordings\\two.mp4"]
    }
    """;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
