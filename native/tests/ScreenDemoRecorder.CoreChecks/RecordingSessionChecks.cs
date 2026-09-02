using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

internal static class RecordingSessionChecks
{
    public static async Task RunAsync(string directory)
    {
        CheckPauseAwareClock();
        var manifest = new RecordingSessionManifest
        {
            SessionId = "session-001",
            CreatedAtUtc = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero),
            ApplicationVersion = "1.2.3",
            SourceGeometry = new CaptureRegion { X = 10, Y = 20, Width = 640, Height = 360 },
            FrameRate = 30,
            Profile = new RecorderProfile(),
        };
        var store = await RecordingSessionStore.CreateAsync(directory, manifest);
        Require(File.Exists(store.ManifestPath), "The recording manifest was not created.");
        Require(!File.Exists(store.ManifestPath + ".tmp"), "The recording manifest temporary file was retained.");

        var restored = JsonSerializer.Deserialize<RecordingSessionManifest>(await File.ReadAllTextAsync(store.ManifestPath), JsonOptions());
        Require(restored is { SchemaVersion: RecordingSessionManifest.CurrentSchemaVersion, SessionId: "session-001", FrameRate: 30 } &&
            restored.SourceGeometry is { X: 10, Y: 20, Width: 640, Height: 360 },
            "The recording manifest did not survive a JSON round trip.");

        var duplicateRejected = false;
        try { await RecordingSessionStore.CreateAsync(directory, manifest); }
        catch (IOException) { duplicateRejected = true; }
        Require(duplicateRejected, "Creating a duplicate session overwrote recoverable recording data.");

        await using (var journal = store.CreateEventJournal())
        {
            await journal.AppendAsync(new RecordingEvent
            {
                Kind = RecordingEventKind.Keystroke,
                TimestampTicks = TimeSpan.FromMilliseconds(120).Ticks,
                Keys = ["Ctrl", "K"],
                DisplayText = "Ctrl + K",
            });
            Require((await ReadJournalLinesAsync(store.EventsPath)).Length == 1,
                "A flushed recording event was not immediately recoverable.");
            await journal.AppendAsync(new RecordingEvent
            {
                Kind = RecordingEventKind.MouseClick,
                TimestampTicks = TimeSpan.FromMilliseconds(250).Ticks,
                MouseButton = MouseClickButton.Left,
                Position = new PixelPoint(123, 45),
            });
        }

        var lines = await File.ReadAllLinesAsync(store.EventsPath);
        Require(lines.Length == 2, "The event journal did not preserve every appended event.");
        var first = JsonSerializer.Deserialize<RecordingEvent>(lines[0], JsonOptions());
        var second = JsonSerializer.Deserialize<RecordingEvent>(lines[1], JsonOptions());
        Require(first is { Sequence: 0, TimestampTicks: 1_200_000 } && first.Keys!.SequenceEqual(["Ctrl", "K"]),
            "The keystroke event JSON is invalid.");
        Require(second is { Sequence: 1, TimestampTicks: 2_500_000, Position: { X: 123, Y: 45 } },
            "The mouse event JSON is invalid.");

        var disposedJournal = new RecordingEventJournal(Path.Combine(store.DirectoryPath, "dispose-check.jsonl"));
        await disposedJournal.DisposeAsync();
        await disposedJournal.DisposeAsync();
        Console.WriteLine("Recording session: pause-aware clock, atomic manifest and recoverable JSONL journal passed.");
    }

    private static void CheckPauseAwareClock()
    {
        var time = new ManualTimeProvider();
        var clock = new RecordingTimelineClock(time);
        clock.Start();
        time.Advance(TimeSpan.FromMilliseconds(120));
        Require(clock.Pause() == TimeSpan.FromMilliseconds(120), "The recording clock did not accumulate active time.");
        time.Advance(TimeSpan.FromSeconds(5));
        Require(clock.Elapsed == TimeSpan.FromMilliseconds(120), "Paused time leaked into the recording timeline.");
        clock.Resume();
        time.Advance(TimeSpan.FromMilliseconds(80));
        Require(clock.Stop() == TimeSpan.FromMilliseconds(200), "The recording clock did not resume from its active timestamp.");
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamp;

        public void Advance(TimeSpan duration) => timestamp += duration.Ticks;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    private static async Task<string[]> ReadJournalLinesAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous);
        using var reader = new StreamReader(stream);
        return (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
