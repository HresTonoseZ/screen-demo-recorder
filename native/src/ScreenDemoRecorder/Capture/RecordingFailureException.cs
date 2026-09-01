using System.IO;

namespace ScreenDemoRecorder.Capture;

internal sealed class RecordingFailureException : IOException
{
    public string Summary { get; }
    public string? RecoveryPath { get; }
    public bool CanUseSafeSettings { get; }

    public RecordingFailureException(string summary, Exception cause, string? recoveryPath, bool canUseSafeSettings)
        : base(recoveryPath is null ? summary : $"{summary}\nAn unfinished recording was retained at:\n{recoveryPath}", cause)
    {
        Summary = summary;
        RecoveryPath = recoveryPath;
        CanUseSafeSettings = canUseSafeSettings;
    }
}
