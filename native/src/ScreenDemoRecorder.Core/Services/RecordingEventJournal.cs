using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Core.Services;

public sealed class RecordingEventJournal : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly FileStream stream;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private long nextSequence;
    private long lastTimestampTicks;
    private bool disposed;

    public RecordingEventJournal(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
    }

    public async ValueTask<RecordingEvent> AppendAsync(RecordingEvent entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.TimestampTicks < 0) throw new ArgumentOutOfRangeException(nameof(entry), "Event time cannot be negative.");
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (nextSequence > 0 && entry.TimestampTicks < lastTimestampTicks)
                throw new InvalidOperationException("Recording events must be appended in timestamp order.");
            entry.SchemaVersion = RecordingEvent.CurrentSchemaVersion;
            entry.Sequence = nextSequence++;
            var json = JsonSerializer.Serialize(entry, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(true);
            lastTimestampTicks = entry.TimestampTicks;
            return entry;
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    internal static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
