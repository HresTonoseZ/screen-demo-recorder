using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Core.Services;

public static class ProfileValidator
{
    public static ProfileDocument Normalize(ProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion != ProfileDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported settings schema: {document.SchemaVersion}.");
        }

        document.Profiles ??= new Dictionary<string, RecorderProfile>(StringComparer.OrdinalIgnoreCase);
        document.RecentFiles ??= [];
        document.ActiveProfile ??= string.Empty;
        if (document.Profiles.Count == 0)
        {
            document.Profiles["Default"] = new RecorderProfile();
        }

        var profiles = new Dictionary<string, RecorderProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawName, profile) in document.Profiles)
        {
            var name = rawName.Trim();
            if (name.Length == 0)
            {
                throw new InvalidDataException("Profile names must not be empty.");
            }

            if (!profiles.TryAdd(name, Normalize(profile)))
            {
                throw new InvalidDataException($"Duplicate profile name: {name}.");
            }
        }

        document.Profiles = profiles;
        if (!profiles.ContainsKey(document.ActiveProfile))
        {
            document.ActiveProfile = profiles.Keys.First();
        }

        document.RecentFiles = document.RecentFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
        return document;
    }

    public static RecorderProfile Normalize(RecorderProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Capture is null) throw new InvalidDataException("Capture settings are missing.");
        if (profile.Output is null) throw new InvalidDataException("Output settings are missing.");
        if (profile.Overlays is null) throw new InvalidDataException("Overlay settings are missing.");
        if (profile.Selection is null) throw new InvalidDataException("Selection settings are missing.");
        if (profile.Application is null) throw new InvalidDataException("Application settings are missing.");
        if (profile.Overlays.Label is null) throw new InvalidDataException("Label settings are missing.");
        if (profile.Overlays.Keystrokes is null) throw new InvalidDataException("Keystroke settings are missing.");
        if (profile.Overlays.Clicks is null) throw new InvalidDataException("Mouse-click settings are missing.");
        profile.Overlays.Desktop ??= new DesktopOverlaySettings();

        profile.Capture.DisplayIndex = Math.Max(1, profile.Capture.DisplayIndex);
        profile.Capture.WindowTitle = OptionalText(profile.Capture.WindowTitle);
        profile.Capture.WindowProcessName = OptionalText(profile.Capture.WindowProcessName);
        profile.Capture.WindowClassName = OptionalText(profile.Capture.WindowClassName);
        profile.Capture.AspectWidth = Math.Clamp(profile.Capture.AspectWidth, 1, 1000);
        profile.Capture.AspectHeight = Math.Clamp(profile.Capture.AspectHeight, 1, 1000);
        profile.Capture.RegionMinimumSize = Math.Clamp(profile.Capture.RegionMinimumSize, 16, 1000);
        profile.Capture.RecordingFps = Math.Clamp(profile.Capture.RecordingFps, 1, 120);
        profile.Capture.GifFps = Math.Clamp(profile.Capture.GifFps, 1, 60);
        profile.Capture.CountdownSeconds = Math.Clamp(profile.Capture.CountdownSeconds, 0, 10);
        profile.Capture.MaximumDurationSeconds = Math.Clamp(profile.Capture.MaximumDurationSeconds, 0, 86_400);
        profile.Capture.RecordHotkey = profile.Capture.RecordHotkey?.Trim() ?? "Ctrl+Shift+F9";
        profile.Capture.PauseHotkey = profile.Capture.PauseHotkey?.Trim() ?? "Ctrl+Shift+F8";
        profile.Capture.CancelHotkey = profile.Capture.CancelHotkey?.Trim() ?? "Ctrl+Shift+F10";
        if (profile.Capture.Region is not null)
        {
            profile.Capture.Region.X = Math.Max(0, profile.Capture.Region.X);
            profile.Capture.Region.Y = Math.Max(0, profile.Capture.Region.Y);
            profile.Capture.Region.Width = Math.Clamp(profile.Capture.Region.Width, profile.Capture.RegionMinimumSize, 7680);
            profile.Capture.Region.Height = Math.Clamp(profile.Capture.Region.Height, profile.Capture.RegionMinimumSize, 4320);
        }

        profile.Output.Directory = RequireText(
            profile.Output.Directory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screen Demos"));
        try { profile.Output.FilenameTemplate = RecordingOutput.ValidateFilenameTemplate(profile.Output.FilenameTemplate); }
        catch (ArgumentException error) { throw new InvalidDataException(error.Message, error); }
        profile.Output.Width = Math.Clamp(profile.Output.Width, 64, 7680);
        profile.Output.Mp4Width = profile.Output.Mp4Width == 0 ? 0 : Math.Clamp(profile.Output.Mp4Width, 64, 7680);
        profile.Output.GifPaletteColors = Math.Clamp(profile.Output.GifPaletteColors, 2, 256);
        profile.Output.GifLoopCount = Math.Clamp(profile.Output.GifLoopCount, 0, 10_000);
        profile.Output.GifFrameStep = Math.Clamp(profile.Output.GifFrameStep, 1, 30);
        profile.Output.FinalFrameDurationMilliseconds = Math.Clamp(profile.Output.FinalFrameDurationMilliseconds, 0, 60_000);

        NormalizeLabel(profile.Overlays.Label);
        NormalizeKeystrokes(profile.Overlays.Keystrokes);
        NormalizeClicks(profile.Overlays.Clicks);

        profile.Selection.SelectionColor = NormalizeColor(profile.Selection.SelectionColor, "#7B61FFFF");
        profile.Selection.RecordingColor = NormalizeColor(profile.Selection.RecordingColor, "#EE4B5FFF");
        profile.Selection.DimColor = NormalizeColor(profile.Selection.DimColor, "#00000080");
        profile.Selection.HandleColor = NormalizeColor(profile.Selection.HandleColor, "#FFFFFFFF");
        profile.Selection.HandleBorderColor = NormalizeColor(profile.Selection.HandleBorderColor, "#2F70EEFF");
        profile.Selection.DimensionColor = NormalizeColor(profile.Selection.DimensionColor, "#FFFFFFFF");
        profile.Selection.LineWidth = Math.Clamp(profile.Selection.LineWidth, 1, 20);
        profile.Selection.DashLength = Math.Clamp(profile.Selection.DashLength, 1, 100);
        profile.Selection.DashGap = Math.Clamp(profile.Selection.DashGap, 1, 100);
        profile.Selection.HandleBorderWidth = Math.Clamp(profile.Selection.HandleBorderWidth, 1, 20);
        profile.Selection.HandleSize = Math.Clamp(profile.Selection.HandleSize, 6, 80);
        profile.Selection.DimensionSize = Math.Clamp(profile.Selection.DimensionSize, 8, 72);
        return profile;
    }

    private static void NormalizeLabel(LabelOverlaySettings label)
    {
        label.OffsetX = Math.Clamp(label.OffsetX, -7680, 7680);
        label.OffsetY = Math.Clamp(label.OffsetY, -4320, 4320);
        label.Width = Math.Clamp(label.Width, 80, 7680);
        label.PaddingX = Math.Clamp(label.PaddingX, 0, 500);
        label.PaddingY = Math.Clamp(label.PaddingY, 0, 500);
        label.LineGap = Math.Clamp(label.LineGap, 0, 500);
        label.CornerRadius = Math.Clamp(label.CornerRadius, 0, 500);
        label.BackgroundColor = NormalizeColor(label.BackgroundColor, "#090E18D9");
        label.BackgroundBlur = Math.Clamp(label.BackgroundBlur, 0, 100);
        label.BorderColor = NormalizeColor(label.BorderColor, "#FFFFFF30");
        label.BorderWidth = Math.Clamp(label.BorderWidth, 0, 30);
        label.ShadowColor = NormalizeColor(label.ShadowColor, "#00000070");
        label.ShadowBlur = Math.Clamp(label.ShadowBlur, 0, 100);
        label.ShadowOffsetX = Math.Clamp(label.ShadowOffsetX, -100, 100);
        label.ShadowOffsetY = Math.Clamp(label.ShadowOffsetY, -100, 100);

        label.Lines ??= [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in label.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Id) || !ids.Add(line.Id))
            {
                line.Id = Guid.NewGuid().ToString("N");
                ids.Add(line.Id);
            }

            line.Text ??= string.Empty;
            line.FontFamily = RequireText(line.FontFamily, "Segoe UI Variable");
            line.Size = Math.Clamp(line.Size, 6, 300);
            line.Color = NormalizeColor(line.Color, "#FFFFFFFF");
            line.Alignment = line.Alignment is "left" or "center" or "right" ? line.Alignment : "center";
            line.StrokeWidth = Math.Clamp(line.StrokeWidth, 0, 30);
            line.StrokeColor = NormalizeColor(line.StrokeColor, "#000000FF");
            line.ShadowColor = NormalizeColor(line.ShadowColor, "#00000080");
            line.ShadowBlur = Math.Clamp(line.ShadowBlur, 0, 100);
            line.ShadowOffsetX = Math.Clamp(line.ShadowOffsetX, -100, 100);
            line.ShadowOffsetY = Math.Clamp(line.ShadowOffsetY, -100, 100);
        }
    }

    private static void NormalizeKeystrokes(KeystrokeOverlaySettings keystrokes)
    {
        keystrokes.OffsetX = Math.Clamp(keystrokes.OffsetX, -7680, 7680);
        keystrokes.OffsetY = Math.Clamp(keystrokes.OffsetY, -4320, 4320);
        keystrokes.Scale = double.IsFinite(keystrokes.Scale) ? Math.Clamp(keystrokes.Scale, 0.5, 3) : 1;
        keystrokes.Opacity = double.IsFinite(keystrokes.Opacity) ? Math.Clamp(keystrokes.Opacity, 0.1, 1) : 0.95;
        keystrokes.MergeWindowMilliseconds = Math.Clamp(keystrokes.MergeWindowMilliseconds, 0, 1000);
        keystrokes.VisibleDurationMilliseconds = Math.Clamp(keystrokes.VisibleDurationMilliseconds, 250, 10_000);
        keystrokes.FadeDurationMilliseconds = Math.Clamp(keystrokes.FadeDurationMilliseconds, 0, 5000);
        keystrokes.MaximumStackEntries = Math.Clamp(keystrokes.MaximumStackEntries, 1, 10);
    }

    private static void NormalizeClicks(ClickOverlaySettings clicks)
    {
        clicks.LeftColor = NormalizeColor(clicks.LeftColor, "#7B61FFFF");
        clicks.RightColor = NormalizeColor(clicks.RightColor, "#FFB020FF");
        clicks.Size = Math.Clamp(clicks.Size, 16, 160);
        clicks.RingWidth = Math.Clamp(clicks.RingWidth, 1, 20);
        clicks.DurationMilliseconds = Math.Clamp(clicks.DurationMilliseconds, 150, 2000);
        clicks.Opacity = double.IsFinite(clicks.Opacity) ? Math.Clamp(clicks.Opacity, 0.1, 1) : 0.9;
    }

    private static string RequireText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? OptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        if (value is null || value.Length is not (7 or 9) || value[0] != '#')
        {
            return fallback;
        }

        return value.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF") ? fallback : value.ToUpperInvariant();
    }
}
