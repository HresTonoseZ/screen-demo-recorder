using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder.Core.Services;

public sealed record VisibleKeystroke(KeyChord Chord, double Opacity);

public sealed class KeystrokeTimeline(KeystrokeOverlaySettings settings)
{
    private readonly List<(KeyChord Chord, TimeSpan Time)> entries = [];

    public void Clear() => entries.Clear();

    public void Add(KeyChord chord, TimeSpan time)
    {
        if (settings.MergeCombinations && entries.Count > 0 && entries[^1].Chord.Identity == chord.Identity &&
            (time - entries[^1].Time).TotalMilliseconds is >= 0 &&
            (time - entries[^1].Time).TotalMilliseconds <= settings.MergeWindowMilliseconds)
            entries[^1] = (chord, time);
        else entries.Add((chord, time));
        while (entries.Count > settings.MaximumStackEntries) entries.RemoveAt(0);
    }

    public VisibleKeystroke[] VisibleAt(TimeSpan time)
    {
        entries.RemoveAll(entry => (time - entry.Time).TotalMilliseconds >= settings.VisibleDurationMilliseconds + settings.FadeDurationMilliseconds);
        return entries.Where(entry => entry.Time <= time).Select(entry =>
        {
            var fading = (time - entry.Time).TotalMilliseconds - settings.VisibleDurationMilliseconds;
            var opacity = fading <= 0 ? 1 : settings.FadeDurationMilliseconds == 0 ? 0 : 1 - fading / settings.FadeDurationMilliseconds;
            return new VisibleKeystroke(entry.Chord, Math.Clamp(opacity, 0, 1) * settings.Opacity);
        }).ToArray();
    }
}
