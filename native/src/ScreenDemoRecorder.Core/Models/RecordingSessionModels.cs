namespace ScreenDemoRecorder.Core.Models;

public enum RecordingEventKind
{
    Keystroke,
    MouseClick,
    Paused,
    Resumed,
    Diagnostic,
}

public sealed class RecordingSessionManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public required string SessionId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public required string ApplicationVersion { get; set; }

    public required CaptureRegion SourceGeometry { get; set; }

    public double FrameRate { get; set; }

    public long ActiveDurationTicks { get; set; }

    public required RecorderProfile Profile { get; set; }
}

public sealed class RecordingEvent
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public long Sequence { get; set; }

    public RecordingEventKind Kind { get; set; }

    public long TimestampTicks { get; set; }

    public string[]? Keys { get; set; }

    public string? DisplayText { get; set; }

    public MouseClickButton? MouseButton { get; set; }

    public PixelPoint? Position { get; set; }

    public string? DiagnosticName { get; set; }

    public long? DiagnosticValue { get; set; }
}
