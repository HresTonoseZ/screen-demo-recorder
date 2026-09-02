using System.Globalization;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Core.Services;

public sealed class RecordingOutput : IDisposable
{
    private readonly string directory;
    private readonly string template;
    private readonly DateTime created;
    private readonly string title;
    private readonly string extension;
    private readonly string? baseName;
    private bool committed;

    public string TemporaryPath { get; }
    public FileStream Stream { get; }

    public RecordingOutput(OutputSettings settings, string labelTitle, OutputFormat format = OutputFormat.Mp4, string? baseName = null)
    {
        extension = format switch { OutputFormat.Mp4 => ".mp4", OutputFormat.Gif => ".gif", _ => throw new ArgumentOutOfRangeException(nameof(format)) };
        directory = Path.GetFullPath(settings.Directory);
        template = ValidateFilenameTemplate(settings.FilenameTemplate);
        this.baseName = baseName;
        title = labelTitle;
        created = DateTime.Now;
        Directory.CreateDirectory(directory);
        TemporaryPath = Path.Combine(directory, $".recording-{Guid.NewGuid():N}.partial{extension}");
        Stream = new FileStream(TemporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    public string Commit()
    {
        Stream.Dispose();
        for (var counter = 1; counter <= 100_000; counter++)
        {
            var name = baseName ?? template.Replace("{date}", created.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Replace("{time}", created.ToString("HH-mm-ss", CultureInfo.InvariantCulture))
                .Replace("{title}", title).Replace("{counter}", counter.ToString("D3", CultureInfo.InvariantCulture));
            name = Sanitize(name);
            if (counter > 1 && (baseName is not null || !template.Contains("{counter}", StringComparison.Ordinal))) name += $"_{counter:D3}";
            var destination = Path.Combine(directory, name + extension);
            try
            {
                File.Move(TemporaryPath, destination, overwrite: false);
                committed = true;
                return destination;
            }
            catch (IOException) when (File.Exists(destination)) { }
        }
        throw new IOException($"No available recording filename. The recording remains at {TemporaryPath}.");
    }

    public string PrepareForExternalWriter()
    {
        if (committed) throw new InvalidOperationException("The recording output has already been committed.");
        Stream.Dispose();
        File.Delete(TemporaryPath);
        return TemporaryPath;
    }

    public void Discard()
    {
        Stream.Dispose();
        if (!committed) File.Delete(TemporaryPath);
    }

    public void Dispose() => Stream.Dispose();

    public static string ValidateFilenameTemplate(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrEmpty(candidate))
            throw new ArgumentException("Enter a filename or keep the default template.");
        for (var index = 0; index < candidate.Length; index++)
        {
            if (candidate[index] == '}')
                throw new ArgumentException("The filename contains a closing brace without a placeholder.");
            if (candidate[index] != '{') continue;
            var end = candidate.IndexOf('}', index + 1);
            if (end < 0) throw new ArgumentException("The filename contains an unfinished placeholder.");
            var placeholder = candidate[index..(end + 1)];
            if (placeholder is not ("{date}" or "{time}" or "{title}" or "{counter}"))
                throw new ArgumentException($"Unknown filename placeholder: {placeholder}.");
            index = end;
        }
        return candidate;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) || c < 32 ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (name.Length > 160) name = name[..160].TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(name)) name = "Recording";
        var stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
            name = "_" + name;
        return name;
    }
}
