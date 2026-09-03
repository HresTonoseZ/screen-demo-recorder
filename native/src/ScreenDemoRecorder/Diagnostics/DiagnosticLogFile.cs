using System.Text;
using System.Text.RegularExpressions;

namespace ScreenDemoRecorder;

// Only diagnostic builds compile this file. Each run owns a unique filename prefix.
internal sealed class DiagnosticLogFile : IDisposable
{
    internal const long DefaultMaximumBytes = 8 * 1024 * 1024;
    internal const int DefaultRunFiles = 5;
    internal const int DefaultRetainedFiles = 20;
    private static readonly Regex OwnedName = new(
        @"^diagnostic-\d{8}-\d{9}-\d+-[a-f0-9]{8}-\d{4,}\.log$", RegexOptions.CultureInvariant);
    private readonly string directory;
    private readonly string prefix;
    private readonly long maximumBytes;
    private readonly int runFiles;
    private readonly Queue<string> files = new();
    private FileStream stream = null!;
    private int part;
    internal string CurrentPath { get; private set; } = "";

    internal DiagnosticLogFile(string directory, long maximumBytes = DefaultMaximumBytes,
        int runFiles = DefaultRunFiles)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 256);
        ArgumentOutOfRangeException.ThrowIfLessThan(runFiles, 1);
        this.directory = Path.GetFullPath(directory);
        this.maximumBytes = maximumBytes;
        this.runFiles = runFiles;
        var runId = Guid.NewGuid().ToString("N")[..8];
        prefix = $"diagnostic-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Environment.ProcessId}-{runId}";
        Directory.CreateDirectory(this.directory);
        Prune(this.directory, DateTime.UtcNow, DefaultRetainedFiles - 1);
        OpenNext();
    }

    internal void Write(string message)
    {
        // Bound even exceptionally large exception messages. Preserve the beginning of the stack.
        var limit = (int)Math.Min(16_000, maximumBytes / 4 - 32);
        if (message.Length > limit) message = message[..limit] + " [truncated]";
        var bytes = Encoding.UTF8.GetBytes(message + Environment.NewLine);
        if (stream.Position + bytes.Length > maximumBytes)
        {
            stream.Dispose();
            Prune(directory, DateTime.UtcNow, DefaultRetainedFiles - 1);
            OpenNext();
        }
        stream.Write(bytes);
        stream.Flush();
    }

    private void OpenNext()
    {
        CurrentPath = Path.Combine(directory, $"{prefix}-{part++:D4}.log");
        stream = new FileStream(CurrentPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
        files.Enqueue(CurrentPath);
        while (files.Count > runFiles) TryDelete(files.Dequeue());
    }

    internal static void Prune(string directory, DateTime now, int keep)
    {
        var candidates = new DirectoryInfo(directory).EnumerateFiles("diagnostic-*.log")
            .Where(file => OwnedName.IsMatch(file.Name) && (file.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        for (var index = 0; index < candidates.Length; index++)
            if (index >= keep || now - candidates[index].LastWriteTimeUtc > TimeSpan.FromDays(7))
                TryDelete(candidates[index].FullName);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => stream.Dispose();
}
