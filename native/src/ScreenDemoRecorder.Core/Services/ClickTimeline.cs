using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Core.Services;

public sealed record VisibleClick(PixelPoint Position, MouseClickButton Button, double Progress, double Opacity);

public sealed class ClickTimeline(ClickOverlaySettings settings)
{
    private readonly List<(PixelPoint Position, MouseClickButton Button, TimeSpan Time)> entries = [];

    public void Clear() => entries.Clear();

    public void Add(PixelPoint position, MouseClickButton button, TimeSpan time)
    {
        entries.Add((position, button, time));
        while (entries.Count > 24) entries.RemoveAt(0);
    }

    public VisibleClick[] VisibleAt(TimeSpan time)
    {
        entries.RemoveAll(entry => (time - entry.Time).TotalMilliseconds >= settings.DurationMilliseconds);
        return entries.Where(entry => entry.Time <= time).Select(entry =>
        {
            var progress = Math.Clamp((time - entry.Time).TotalMilliseconds / settings.DurationMilliseconds, 0, 1);
            var opacity = (1 - progress) * settings.Opacity;
            return new VisibleClick(entry.Position, entry.Button, progress, opacity);
        }).ToArray();
    }
}
