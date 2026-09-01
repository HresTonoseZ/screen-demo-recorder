using System.Text.Json;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Core.Services;

public static class LegacySettingsMigrator
{
    public static ProfileDocument Migrate(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        if (!root.TryGetProperty("schema_version", out var schema) || schema.GetInt32() != 1)
            throw new InvalidDataException("Only version 1 legacy settings can be migrated.");
        if (!TryObject(root, "profiles", out var profilesElement))
        {
            throw new InvalidDataException("Legacy settings do not contain profiles.");
        }

        var profiles = new Dictionary<string, RecorderProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in profilesElement.EnumerateObject())
        {
            profiles[property.Name] = MigrateProfile(property.Value);
        }

        if (profiles.Count == 0)
        {
            throw new InvalidDataException("Legacy settings do not contain any profiles.");
        }

        var activeProfile = ReadString(root, "active_profile", profiles.Keys.First());
        if (!profiles.ContainsKey(activeProfile))
        {
            activeProfile = profiles.Keys.First();
        }

        var document = new ProfileDocument
        {
            ActiveProfile = activeProfile,
            Profiles = profiles,
            RecentFiles = ReadStringArray(root, "recent_files"),
        };
        return ProfileValidator.Normalize(document);
    }

    private static RecorderProfile MigrateProfile(JsonElement source)
    {
        var profile = new RecorderProfile();
        if (TryObject(source, "capture", out var capture))
        {
            profile.Capture.Source = ReadString(capture, "mode", "monitor") == "region"
                ? CaptureSource.Region
                : CaptureSource.Display;
            profile.Capture.DisplayIndex = ReadInt(capture, "monitor", 1);
            profile.Capture.Region = ReadRegion(capture);
            profile.Capture.LockAspectRatio = ReadBool(capture, "region_lock_aspect", false);
            profile.Capture.AspectWidth = ReadInt(capture, "region_aspect_width", 16);
            profile.Capture.AspectHeight = ReadInt(capture, "region_aspect_height", 9);
            profile.Capture.SnapToEdges = ReadBool(capture, "region_snap_to_edges", true);
            profile.Capture.RegionMinimumSize = ReadInt(capture, "region_minimum_size", 32);
            profile.Capture.RecordingFps = ReadDouble(capture, "recording_fps", 30);
            profile.Capture.GifFps = ReadDouble(capture, "gif_fps", 12);
            profile.Capture.ShowCursor = ReadBool(capture, "capture_cursor", true);
            profile.Capture.CountdownSeconds = ReadInt(capture, "countdown_seconds", 3);
            profile.Capture.MaximumDurationSeconds = ReadInt(capture, "maximum_duration_seconds", 60);
            profile.Capture.RecordHotkey = NormalizeLegacyHotkey(ReadString(capture, "toggle_hotkey", "Ctrl+Shift+F9"));
            profile.Capture.PauseHotkey = NormalizeLegacyHotkey(ReadString(capture, "pause_hotkey", "Ctrl+Shift+F8"));
            profile.Capture.CancelHotkey = NormalizeLegacyHotkey(ReadString(capture, "cancel_hotkey", "Ctrl+Shift+F10"));
        }

        if (TryObject(source, "output", out var output))
        {
            profile.Output.Format = OutputFormat.Gif;
            profile.Output.Directory = ReadString(output, "directory", profile.Output.Directory);
            profile.Output.FilenameTemplate = ReadString(output, "filename_template", profile.Output.FilenameTemplate);
            profile.Output.Width = ReadInt(output, "width", 960);
            profile.Output.GifPaletteColors = ReadInt(output, "palette_colors", 128);
            profile.Output.GifDither = ReadBool(output, "dither", true);
            profile.Output.GifLoopCount = ReadInt(output, "loop", 0);
            profile.Output.GifFrameStep = ReadInt(output, "frame_step", 1);
            profile.Output.FinalFrameDurationMilliseconds = ReadInt(output, "final_frame_duration_ms", 0);
            profile.Output.KeepSourceVideo = ReadBool(output, "save_source_video", false);
            profile.Output.OpenFolderAfterSave = ReadBool(output, "open_folder_after_save", false);
        }

        if (TryObject(source, "caption", out var caption))
        {
            MigrateLabel(profile.Overlays.Label, caption);
        }

        if (TryObject(source, "selection", out var selection))
        {
            profile.Selection.SelectionColor = ReadString(selection, "line_color", profile.Selection.SelectionColor);
            profile.Selection.DashLength = ReadInt(selection, "dash_length", profile.Selection.DashLength);
            profile.Selection.DashGap = ReadInt(selection, "dash_gap", profile.Selection.DashGap);
            profile.Selection.HandleColor = ReadString(selection, "handle_color", profile.Selection.HandleColor);
            profile.Selection.HandleBorderColor = ReadString(selection, "handle_border", profile.Selection.HandleBorderColor);
            profile.Selection.HandleBorderWidth = ReadInt(selection, "handle_border_width", profile.Selection.HandleBorderWidth);
            profile.Selection.LineWidth = ReadInt(selection, "line_width", profile.Selection.LineWidth);
            profile.Selection.HandleSize = ReadInt(selection, "handle_size", profile.Selection.HandleSize);
            profile.Selection.HandleShape = ReadString(selection, "handle_shape", "circle") == "square"
                ? SelectionHandleShape.Square : SelectionHandleShape.Circle;
            profile.Selection.DimColor = ReadString(selection, "dim_color", profile.Selection.DimColor);
            profile.Selection.ShowDimensions = ReadBool(selection, "show_dimensions", true);
            profile.Selection.DimensionColor = ReadString(selection, "dimension_color", profile.Selection.DimensionColor);
            profile.Selection.DimensionSize = ReadInt(selection, "dimension_size", profile.Selection.DimensionSize);
        }

        if (TryObject(source, "application", out var application))
        {
            profile.Application.AlwaysOnTop = ReadBool(application, "always_on_top", true);
            profile.Application.MinimizeToTray = ReadBool(application, "minimize_to_tray", false);
            profile.Application.Theme = ReadString(application, "theme", "system") switch
            {
                "dark" => ApplicationTheme.Dark,
                "light" => ApplicationTheme.Light,
                _ => ApplicationTheme.System,
            };
        }

        return ProfileValidator.Normalize(profile);
    }

    private static void MigrateLabel(LabelOverlaySettings label, JsonElement caption)
    {
        label.Enabled = ReadBool(caption, "enabled", true);
        label.Style = LabelStylePreset.Custom;
        label.Anchor = ParseAnchor(ReadString(caption, "anchor", "bottom_center"));
        label.OffsetX = ReadInt(caption, "offset_x", 0);
        label.OffsetY = ReadInt(caption, "offset_y", 24);
        label.Width = ReadInt(caption, "width", 560);
        label.PaddingX = ReadInt(caption, "padding_x", 20);
        label.PaddingY = ReadInt(caption, "padding_y", 14);
        label.LineGap = ReadInt(caption, "line_gap", 5);
        label.CornerRadius = ReadInt(caption, "corner_radius", 12);
        label.BackgroundColor = ReadString(caption, "background", label.BackgroundColor);
        label.BackgroundBlur = ReadInt(caption, "background_blur", 0);
        label.BorderColor = ReadString(caption, "border", label.BorderColor);
        label.BorderWidth = ReadInt(caption, "border_width", 1);
        label.ShadowColor = ReadString(caption, "shadow_color", label.ShadowColor);
        label.ShadowBlur = ReadInt(caption, "shadow_blur", 8);
        label.ShadowOffsetX = ReadInt(caption, "shadow_offset_x", 0);
        label.ShadowOffsetY = ReadInt(caption, "shadow_offset_y", 4);
        var alignment = ReadString(caption, "text_alignment", "center");
        if (alignment is not ("left" or "center" or "right")) alignment = "center";

        label.Lines.Clear();
        if (TryObject(caption, "title", out var title) && ReadBool(title, "enabled", true))
        {
            label.Lines.Add(MigrateLine(title, 22, true, "#FFFFFFFF"));
        }

        if (TryObject(caption, "subtitle", out var subtitle) && ReadBool(subtitle, "enabled", true))
        {
            label.Lines.Add(MigrateLine(subtitle, 14, false, "#C4D0E4FF"));
        }
        foreach (var line in label.Lines) line.Alignment = alignment;
    }

    private static LabelTextLine MigrateLine(JsonElement source, double defaultSize, bool defaultBold, string defaultColor)
    {
        return new LabelTextLine
        {
            Text = ReadString(source, "text", string.Empty),
            FontFamily = ReadString(source, "font", "Segoe UI Variable"),
            Size = ReadDouble(source, "size", defaultSize),
            IsBold = ReadBool(source, "bold", defaultBold),
            IsItalic = ReadBool(source, "italic", false),
            Color = ReadString(source, "color", defaultColor),
            StrokeWidth = ReadInt(source, "stroke_width", 0),
            StrokeColor = ReadString(source, "stroke_color", "#000000FF"),
            ShadowColor = ReadString(source, "shadow_color", "#00000080"),
            ShadowBlur = ReadInt(source, "shadow_blur", 0),
            ShadowOffsetX = ReadInt(source, "shadow_offset_x", 0),
            ShadowOffsetY = ReadInt(source, "shadow_offset_y", 1),
        };
    }

    private static CaptureRegion? ReadRegion(JsonElement capture)
    {
        if (!capture.TryGetProperty("region", out var region) || region.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = region.EnumerateArray().Select(item => item.GetInt32()).ToArray();
        return values.Length == 4
            ? new CaptureRegion { X = values[0], Y = values[1], Width = values[2], Height = values[3] }
            : null;
    }

    private static OverlayAnchor ParseAnchor(string value)
    {
        return value switch
        {
            "top_left" => OverlayAnchor.TopLeft,
            "top_center" => OverlayAnchor.TopCenter,
            "top_right" => OverlayAnchor.TopRight,
            "center_left" => OverlayAnchor.CenterLeft,
            "center" => OverlayAnchor.Center,
            "center_right" => OverlayAnchor.CenterRight,
            "bottom_left" => OverlayAnchor.BottomLeft,
            "bottom_right" => OverlayAnchor.BottomRight,
            _ => OverlayAnchor.BottomCenter,
        };
    }

    private static string NormalizeLegacyHotkey(string value)
    {
        var normalized = value
            .Replace("<ctrl>", "Ctrl", StringComparison.OrdinalIgnoreCase)
            .Replace("<shift>", "Shift", StringComparison.OrdinalIgnoreCase)
            .Replace("<alt>", "Alt", StringComparison.OrdinalIgnoreCase)
            .Replace("<cmd>", "Win", StringComparison.OrdinalIgnoreCase)
            .Replace("<", string.Empty, StringComparison.Ordinal)
            .Replace(">", string.Empty, StringComparison.Ordinal);
        return HotkeyGesture.TryParse(normalized, out var gesture, out _) ? gesture.ToString() : normalized;
    }

    private static bool TryObject(JsonElement parent, string name, out JsonElement value)
    {
        return parent.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;
    }

    private static string ReadString(JsonElement parent, string name, string fallback)
    {
        return parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static int ReadInt(JsonElement parent, string name, int fallback)
    {
        return parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;
    }

    private static double ReadDouble(JsonElement parent, string name, double fallback)
    {
        return parent.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : fallback;
    }

    private static bool ReadBool(JsonElement parent, string name, bool fallback)
    {
        return parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
    }

    private static List<string> ReadStringArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .OfType<string>()
            .ToList();
    }
}
