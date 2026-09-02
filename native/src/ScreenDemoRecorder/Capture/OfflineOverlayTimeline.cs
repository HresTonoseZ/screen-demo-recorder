using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder.Capture;

internal sealed class OfflineOverlayTimeline
{
    private readonly RecordingEvent[] events;
    private readonly KeystrokeTimeline keystrokes;
    private readonly ClickTimeline clicks;
    private int nextEvent;

    public OfflineOverlayTimeline(IEnumerable<RecordingEvent> recordingEvents, OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(recordingEvents);
        ArgumentNullException.ThrowIfNull(settings);
        events = recordingEvents.OrderBy(entry => entry.TimestampTicks).ThenBy(entry => entry.Sequence).ToArray();
        keystrokes = new KeystrokeTimeline(settings.Keystrokes);
        clicks = new ClickTimeline(settings.Clicks);
    }

    public (VisibleKeystroke[] Keystrokes, VisibleClick[] Clicks) VisibleAt(TimeSpan time)
    {
        while (nextEvent < events.Length && events[nextEvent].TimestampTicks <= time.Ticks)
        {
            var entry = events[nextEvent++];
            if (entry.Kind == RecordingEventKind.Keystroke && entry.Keys is { Length: > 0 })
                keystrokes.Add(new KeyChord(entry.Keys), TimeSpan.FromTicks(entry.TimestampTicks));
            else if (entry.Kind == RecordingEventKind.MouseClick && entry.Position is not null && entry.MouseButton is not null)
                clicks.Add(entry.Position.Value, entry.MouseButton.Value, TimeSpan.FromTicks(entry.TimestampTicks));
        }
        return (keystrokes.VisibleAt(time), clicks.VisibleAt(time));
    }
}
