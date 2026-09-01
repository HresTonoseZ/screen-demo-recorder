namespace ScreenDemoRecorder.Core.Models;

public enum CaptureSource
{
    Region,
    Display,
    Window,
}

public enum OutputFormat
{
    Gif,
    Mp4,
}

public enum QualityPreset
{
    Efficient,
    Balanced,
    Crisp,
    Custom,
}

public enum OverlayAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

public enum LabelStylePreset
{
    Clean,
    Glass,
    Accent,
    Dark,
    TextOnly,
    Custom,
}

public enum KeystrokeDisplayMode
{
    ShortcutsOnly,
    NonTextKeys,
    AllKeys,
}

public enum KeystrokeStylePreset
{
    Dark,
    Light,
    Accent,
    Minimal,
    Custom,
}

public enum ApplicationTheme
{
    System,
    Light,
    Dark,
}

public enum SelectionHandleShape
{
    Circle,
    Square,
}

public enum MouseClickButton
{
    Left,
    Right,
}

public sealed class ProfileDocument
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string ActiveProfile { get; set; } = "Default";

    public Dictionary<string, RecorderProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Default"] = new RecorderProfile(),
    };

    public List<string> RecentFiles { get; set; } = [];
}

public sealed class ProfileExportDocument
{
    public int SchemaVersion { get; set; } = ProfileDocument.CurrentSchemaVersion;

    public string? Name { get; set; }

    public RecorderProfile? Profile { get; set; }
}

public sealed class RecorderProfile
{
    public CaptureSettings Capture { get; set; } = new();

    public OutputSettings Output { get; set; } = new();

    public OverlaySettings Overlays { get; set; } = new();

    public SelectionSettings Selection { get; set; } = new();

    public ApplicationSettings Application { get; set; } = new();
}

public sealed class CaptureSettings
{
    public CaptureSource Source { get; set; } = CaptureSource.Region;

    public int DisplayIndex { get; set; } = 1;

    public string? DisplayDeviceName { get; set; }

    public string? WindowTitle { get; set; }

    public string? WindowProcessName { get; set; }

    public string? WindowClassName { get; set; }

    public CaptureRegion? Region { get; set; }

    public bool LockAspectRatio { get; set; }

    public int AspectWidth { get; set; } = 16;

    public int AspectHeight { get; set; } = 9;

    public bool SnapToEdges { get; set; } = true;

    public int RegionMinimumSize { get; set; } = 32;

    public double RecordingFps { get; set; } = 30;

    public bool AutomaticFps { get; set; }

    public double GifFps { get; set; } = 12;

    public bool ShowCursor { get; set; } = true;

    public bool HighlightClicks { get; set; }

    public int CountdownSeconds { get; set; } = 3;

    public int MaximumDurationSeconds { get; set; } = 60;

    public string RecordHotkey { get; set; } = "Ctrl+Shift+F9";

    public string PauseHotkey { get; set; } = "Ctrl+Shift+F8";

    public string CancelHotkey { get; set; } = "Ctrl+Shift+F10";
}

public sealed class CaptureRegion
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;
}

public sealed class OutputSettings
{
    public OutputFormat Format { get; set; } = OutputFormat.Mp4;

    public QualityPreset Quality { get; set; } = QualityPreset.Balanced;

    public string Directory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "Screen Demos");

    public string FilenameTemplate { get; set; } = "Demo_{date}_{time}_{counter}";

    public int Width { get; set; } = 1920;

    public int Mp4Width { get; set; }

    public int GifPaletteColors { get; set; } = 128;

    public bool GifDither { get; set; } = true;

    public int GifLoopCount { get; set; }

    public int GifFrameStep { get; set; } = 1;

    public int FinalFrameDurationMilliseconds { get; set; }

    public bool KeepSourceVideo { get; set; }

    public bool OpenFolderAfterSave { get; set; }
}

public sealed class OverlaySettings
{
    public LabelOverlaySettings Label { get; set; } = new();

    public KeystrokeOverlaySettings Keystrokes { get; set; } = new();

    public ClickOverlaySettings Clicks { get; set; } = new();
}

public sealed class LabelOverlaySettings
{
    public bool Enabled { get; set; } = true;

    public LabelStylePreset Style { get; set; } = LabelStylePreset.Glass;

    public OverlayAnchor Anchor { get; set; } = OverlayAnchor.BottomCenter;

    public int OffsetX { get; set; }

    public int OffsetY { get; set; } = 24;

    public int Width { get; set; } = 560;

    public int PaddingX { get; set; } = 20;

    public int PaddingY { get; set; } = 14;

    public int LineGap { get; set; } = 5;

    public int CornerRadius { get; set; } = 12;

    public string BackgroundColor { get; set; } = "#090E18D9";

    public int BackgroundBlur { get; set; } = 12;

    public string BorderColor { get; set; } = "#FFFFFF30";

    public int BorderWidth { get; set; } = 1;

    public string ShadowColor { get; set; } = "#00000070";

    public int ShadowBlur { get; set; } = 8;

    public int ShadowOffsetX { get; set; }

    public int ShadowOffsetY { get; set; } = 4;

    public List<LabelTextLine> Lines { get; set; } =
    [
        new() { Text = "Your text here", Size = 22, IsBold = true },
    ];
}

public sealed class LabelTextLine : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private string id = Guid.NewGuid().ToString("N");
    private bool enabled = true;
    private string text = "Text";
    private string fontFamily = "Segoe UI Variable";
    private double size = 18;
    private bool isBold;
    private bool isItalic;
    private string color = "#FFFFFFFF";
    private string alignment = "center";
    private int strokeWidth;
    private string strokeColor = "#000000FF";
    private string shadowColor = "#00000080";
    private int shadowBlur;
    private int shadowOffsetX;
    private int shadowOffsetY = 1;
    public string Id { get => id; set => Set(ref id, value); }

    public bool Enabled { get => enabled; set => Set(ref enabled, value); }

    public string Text { get => text; set => Set(ref text, value); }

    public string FontFamily { get => fontFamily; set => Set(ref fontFamily, value); }

    public double Size
    {
        get => size;
        set => Set(ref size, value);
    }

    public bool IsBold { get => isBold; set => Set(ref isBold, value); }

    public bool IsItalic { get => isItalic; set => Set(ref isItalic, value); }

    public string Color { get => color; set => Set(ref color, value); }

    public string Alignment { get => alignment; set => Set(ref alignment, value); }

    public int StrokeWidth { get => strokeWidth; set => Set(ref strokeWidth, value); }

    public string StrokeColor { get => strokeColor; set => Set(ref strokeColor, value); }

    public string ShadowColor { get => shadowColor; set => Set(ref shadowColor, value); }

    public int ShadowBlur { get => shadowBlur; set => Set(ref shadowBlur, value); }

    public int ShadowOffsetX { get => shadowOffsetX; set => Set(ref shadowOffsetX, value); }

    public int ShadowOffsetY { get => shadowOffsetY; set => Set(ref shadowOffsetY, value); }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}

public sealed class KeystrokeOverlaySettings
{
    public bool Enabled { get; set; }

    public KeystrokeDisplayMode DisplayMode { get; set; } = KeystrokeDisplayMode.ShortcutsOnly;

    public KeystrokeStylePreset Style { get; set; } = KeystrokeStylePreset.Dark;

    public OverlayAnchor Anchor { get; set; } = OverlayAnchor.TopRight;

    public int OffsetX { get; set; } = -24;

    public int OffsetY { get; set; } = 24;

    public double Scale { get; set; } = 1;

    public double Opacity { get; set; } = 0.95;

    public int MergeWindowMilliseconds { get; set; } = 200;

    public int VisibleDurationMilliseconds { get; set; } = 1200;

    public int FadeDurationMilliseconds { get; set; } = 250;

    public int MaximumStackEntries { get; set; } = 3;

    public bool MergeCombinations { get; set; } = true;

    public bool HideNormalTyping { get; set; } = true;

    public bool HideRecorderHotkeys { get; set; } = true;
}

public sealed class ClickOverlaySettings
{
    public string LeftColor { get; set; } = "#7B61FFFF";

    public string RightColor { get; set; } = "#FFB020FF";

    public int Size { get; set; } = 46;

    public int RingWidth { get; set; } = 4;

    public int DurationMilliseconds { get; set; } = 650;

    public double Opacity { get; set; } = 0.9;
}

public sealed class SelectionSettings
{
    public string SelectionColor { get; set; } = "#7B61FFFF";

    public string RecordingColor { get; set; } = "#EE4B5FFF";

    public int LineWidth { get; set; } = 2;

    public int DashLength { get; set; } = 9;

    public int DashGap { get; set; } = 6;

    public string HandleColor { get; set; } = "#FFFFFFFF";

    public string HandleBorderColor { get; set; } = "#2F70EEFF";

    public int HandleBorderWidth { get; set; } = 2;

    public int HandleSize { get; set; } = 12;

    public SelectionHandleShape HandleShape { get; set; } = SelectionHandleShape.Circle;

    public string DimColor { get; set; } = "#00000080";

    public bool ShowDimensions { get; set; } = true;

    public string DimensionColor { get; set; } = "#FFFFFFFF";

    public int DimensionSize { get; set; } = 12;

    public bool KeepBoundaryVisible { get; set; } = true;
}

public sealed class ApplicationSettings
{
    public bool AlwaysOnTop { get; set; } = true;

    public bool MinimizeToTray { get; set; }

    public ApplicationTheme Theme { get; set; } = ApplicationTheme.System;
}
