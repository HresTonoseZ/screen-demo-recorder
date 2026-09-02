using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Core.Services;

public sealed class RecordingSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public string DirectoryPath { get; }

    public string ManifestPath => Path.Combine(DirectoryPath, "session.json");

    public string EventsPath => Path.Combine(DirectoryPath, "events.jsonl");

    public string CleanVideoPath => Path.Combine(DirectoryPath, "clean.mkv");

    public string PartialRenderPath => Path.Combine(DirectoryPath, "rendering.partial.mp4");

    private RecordingSessionStore(string directoryPath)
    {
        DirectoryPath = directoryPath;
    }

    public static async Task<RecordingSessionStore> CreateAsync(
        string rootDirectory,
        RecordingSessionManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        Validate(manifest);
        var directory = Path.Combine(Path.GetFullPath(rootDirectory), manifest.SessionId);
        if (Directory.Exists(directory))
            throw new IOException($"Recording session already exists: {manifest.SessionId}.");
        Directory.CreateDirectory(directory);
        var store = new RecordingSessionStore(directory);
        await store.WriteManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
        return store;
    }

    public static async Task<(RecordingSessionStore Store, RecordingSessionManifest Manifest)> OpenAsync(
        string directoryPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var store = new RecordingSessionStore(Path.GetFullPath(directoryPath));
        if (!File.Exists(store.ManifestPath))
            throw new FileNotFoundException("The recording session manifest is missing.", store.ManifestPath);
        await using var stream = new FileStream(store.ManifestPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var manifest = await JsonSerializer.DeserializeAsync<RecordingSessionManifest>(stream, JsonOptions,
            cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("The recording session manifest is empty.");
        Validate(manifest);
        if (!string.Equals(Path.GetFileName(store.DirectoryPath), manifest.SessionId, StringComparison.Ordinal))
            throw new InvalidDataException("The recording session directory does not match its manifest.");
        return (store, manifest);
    }

    public static string[] FindRecoverable(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root)
            .Where(directory => File.Exists(Path.Combine(directory, "session.json")) &&
                File.Exists(Path.Combine(directory, "clean.mkv")) && File.Exists(Path.Combine(directory, "events.jsonl")))
            .OrderBy(directory => directory, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<RecordingEvent[]> ReadEventsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(EventsPath)) return [];
        var lines = await File.ReadAllLinesAsync(EventsPath, cancellationToken).ConfigureAwait(false);
        List<RecordingEvent> events = [];
        for (var index = 0; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index])) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<RecordingEvent>(lines[index], JsonOptions)
                    ?? throw new InvalidDataException($"Recording event {index} is empty.");
                if (entry.SchemaVersion != RecordingEvent.CurrentSchemaVersion || entry.TimestampTicks < 0)
                    throw new InvalidDataException($"Recording event {index} is invalid.");
                events.Add(entry);
            }
            catch (JsonException) when (index == lines.Length - 1)
            {
                // A process crash can leave only the final append incomplete.
            }
        }
        return events.OrderBy(entry => entry.TimestampTicks).ThenBy(entry => entry.Sequence).ToArray();
    }

    public async Task WriteManifestAsync(RecordingSessionManifest manifest, CancellationToken cancellationToken = default)
    {
        Validate(manifest);
        if (!string.Equals(Path.GetFileName(DirectoryPath), manifest.SessionId, StringComparison.Ordinal))
            throw new InvalidOperationException("The manifest session ID does not match its directory.");
        var temporaryPath = ManifestPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }
            File.Move(temporaryPath, ManifestPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public RecordingEventJournal CreateEventJournal() => new(EventsPath);

    private static void Validate(RecordingSessionManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != RecordingSessionManifest.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported recording session schema: {manifest.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(manifest.SessionId) || manifest.SessionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            manifest.SessionId is "." or "..")
            throw new InvalidDataException("The recording session ID is not a valid directory name.");
        if (string.IsNullOrWhiteSpace(manifest.ApplicationVersion))
            throw new InvalidDataException("The application version is required.");
        if (manifest.SourceGeometry is null || manifest.SourceGeometry.Width < 2 || manifest.SourceGeometry.Height < 2)
            throw new InvalidDataException("The recording source geometry is invalid.");
        if (!double.IsFinite(manifest.FrameRate) || manifest.FrameRate is < 1 or > 120)
            throw new InvalidDataException("The recording frame rate is invalid.");
        if (manifest.ActiveDurationTicks < 0)
            throw new InvalidDataException("The active recording duration cannot be negative.");
        if (manifest.Profile is null)
            throw new InvalidDataException("The recording profile snapshot is required.");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
