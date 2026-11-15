using Ghostcut.Core.Model;

namespace Ghostcut.Core.Timeline;

/// <summary>Result of a snap: the (possibly moved) value and the target it snapped to.</summary>
public readonly record struct SnapResult(TimeSpan Value, TimeSpan? Target)
{
    public bool Snapped => Target.HasValue;
}

public static class Snapper
{
    /// <summary>Snaps <paramref name="value"/> to the nearest target within <paramref name="threshold"/>.</summary>
    public static SnapResult Snap(TimeSpan value, IEnumerable<TimeSpan> targets, TimeSpan threshold)
    {
        TimeSpan? best = null;
        var bestDistance = TimeSpan.MaxValue;
        foreach (var target in targets)
        {
            var distance = (target - value).Duration();
            if (distance <= threshold && distance < bestDistance)
            {
                best = target;
                bestDistance = distance;
            }
        }
        return best is { } hit ? new SnapResult(hit, hit) : new SnapResult(value, null);
    }

    /// <summary>
    /// Snaps a span by whichever of its two edges is closer to a target; returns the new start.
    /// </summary>
    public static SnapResult SnapSpan(TimeSpan start, TimeSpan duration, IEnumerable<TimeSpan> targets, TimeSpan threshold)
    {
        var list = targets as IReadOnlyCollection<TimeSpan> ?? targets.ToList();
        var byStart = Snap(start, list, threshold);
        var byEnd = Snap(start + duration, list, threshold);
        if (!byStart.Snapped && !byEnd.Snapped)
            return new SnapResult(start, null);
        if (byEnd.Snapped && (!byStart.Snapped || (byEnd.Value - (start + duration)).Duration() < (byStart.Value - start).Duration()))
            return new SnapResult(byEnd.Value - duration, byEnd.Target);
        return byStart;
    }
}

/// <summary>
/// Collects snap targets: the real clip edges (where actual picture/sound starts or ends —
/// overhangs do not count), the playhead and the timeline origin.
/// </summary>
public static class SnapTargets
{
    /// <param name="includeChainClip">Filter for chain clips (index, clip); null = all.</param>
    /// <param name="includeAuxClip">Filter for aux clips; null = all.</param>
    public static List<TimeSpan> Collect(
        Project project,
        ChainLayout layout,
        TimeSpan? playhead,
        Func<ChainClipLayout, bool>? includeChainClip = null,
        Func<AuxClip, bool>? includeAuxClip = null)
    {
        var targets = new List<TimeSpan> { TimeSpan.Zero };
        if (playhead is { } p)
            targets.Add(p);
        foreach (var clip in layout.Clips)
        {
            if (includeChainClip != null && !includeChainClip(clip))
                continue;
            targets.Add(clip.Start);
            targets.Add(clip.End);
        }
        foreach (var track in project.AuxTracks)
        {
            foreach (var clip in track.Clips)
            {
                if (includeAuxClip != null && !includeAuxClip(clip))
                    continue;
                targets.Add(clip.Start);
                targets.Add(clip.End);
            }
        }
        return targets;
    }
}
