using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Overlays;

namespace ScreenDemoRecorder.Capture;

internal sealed record RecordingOverlays(
    LabelRaster? Label,
    KeystrokeRenderer? Keystrokes,
    ClickRenderer? Clicks);

internal static class RecordingOverlayPipeline
{
    public static RecordingOverlays Create(RecorderProfile profile, int width, int height) => new(
        LabelRenderer.Render(profile.Overlays.Label, width, height),
        profile.Overlays.Keystrokes.Enabled ? new KeystrokeRenderer(profile.Overlays.Keystrokes) : null,
        profile.Capture.HighlightClicks ? new ClickRenderer(profile.Overlays.Clicks) : null);
}
