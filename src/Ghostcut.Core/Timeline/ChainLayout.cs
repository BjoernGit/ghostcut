using Ghostcut.Core.Model;

namespace Ghostcut.Core.Timeline;

/// <summary>Where one chain clip sits on the timeline and on which lane it is drawn.</summary>
public readonly record struct ChainClipLayout(
    ChainClip Clip,
    int Index,
    TimeSpan Start,
    TimeSpan End,
    TimeSpan LeftOverhang,
    TimeSpan RightOverhang,
    int Lane)
{
    /// <summary>Start of the clip including its greyed-out left overhang.</summary>
    public TimeSpan FootprintStart => Start - LeftOverhang;

    /// <summary>End of the clip including its greyed-out right overhang.</summary>
    public TimeSpan FootprintEnd => End + RightOverhang;

    public TimeSpan Duration => End - Start;

    /// <summary>Maps a timeline time inside (or around) this clip to source time.</summary>
    public TimeSpan ToSourceTime(TimeSpan timelineTime) => Clip.In + (timelineTime - Start);
}

/// <summary>
/// Derived layout of the ghost chain: gap-free positions plus lane assignment. Lanes alternate
/// 0/1; a clip never shares the lane of its predecessor, and if its footprint (clip plus
/// overhangs) would collide with a clip already on a lane, it moves to the next free lane, which
/// opens extra lanes (2, 3, …) when needed.
/// </summary>
public sealed class ChainLayout
{
    /// <summary>The ghost chain always shows at least this many lanes.</summary>
    public const int MinLaneCount = 2;

    private ChainLayout(IReadOnlyList<ChainClipLayout> clips, int laneCount)
    {
        Clips = clips;
        LaneCount = laneCount;
    }

    public IReadOnlyList<ChainClipLayout> Clips { get; }

    public int LaneCount { get; }

    public TimeSpan Duration => Clips.Count == 0 ? TimeSpan.Zero : Clips[^1].End;

    /// <summary>
    /// Computes the layout. <paramref name="frozenLanes"/> pins the lane of known clips (used while
    /// dragging so nothing jumps under the mouse); clips not in it are assigned normally.
    /// </summary>
    public static ChainLayout Compute(Project project, IReadOnlyDictionary<Guid, int>? frozenLanes = null)
    {
        var result = new List<ChainClipLayout>(project.Main.Clips.Count);
        var lanes = new List<List<(TimeSpan Start, TimeSpan End)>>();
        var position = TimeSpan.Zero;
        var previousLane = -1;
        var maxLane = -1;

        for (var index = 0; index < project.Main.Clips.Count; index++)
        {
            var clip = project.Main.Clips[index];
            var sourceDuration = project.FindSource(clip.SourceId)?.Duration ?? clip.Out;
            var leftOverhang = Max(TimeSpan.Zero, clip.In);
            var rightOverhang = Max(TimeSpan.Zero, sourceDuration - clip.Out);
            var start = position;
            var end = start + clip.Duration;
            var footprint = (Start: start - leftOverhang, End: end + rightOverhang);

            int lane;
            if (frozenLanes != null && frozenLanes.TryGetValue(clip.Id, out var frozen))
                lane = frozen;
            else
                lane = FindLane(lanes, footprint, previousLane);

            while (lanes.Count <= lane)
                lanes.Add(new List<(TimeSpan, TimeSpan)>());
            lanes[lane].Add(footprint);

            result.Add(new ChainClipLayout(clip, index, start, end, leftOverhang, rightOverhang, lane));
            maxLane = Math.Max(maxLane, lane);
            previousLane = lane;
            position = end;
        }

        return new ChainLayout(result, Math.Max(MinLaneCount, maxLane + 1));
    }

    private static int FindLane(List<List<(TimeSpan Start, TimeSpan End)>> lanes, (TimeSpan Start, TimeSpan End) footprint, int previousLane)
    {
        for (var lane = 0; ; lane++)
        {
            if (lane == previousLane)
                continue;
            if (lane >= lanes.Count)
                return lane;
            var collides = false;
            foreach (var occupied in lanes[lane])
            {
                // Touching footprints do not collide.
                if (footprint.Start < occupied.End && occupied.Start < footprint.End)
                {
                    collides = true;
                    break;
                }
            }
            if (!collides)
                return lane;
        }
    }

    /// <summary>The clip that plays at <paramref name="time"/> (start inclusive, end exclusive).</summary>
    public ChainClipLayout? ClipAt(TimeSpan time)
    {
        foreach (var layout in Clips)
            if (time >= layout.Start && time < layout.End)
                return layout;
        return null;
    }

    public ChainClipLayout? Find(Guid clipId)
    {
        foreach (var layout in Clips)
            if (layout.Clip.Id == clipId)
                return layout;
        return null;
    }

    /// <summary>Lane per clip id, to freeze the assignment during a drag.</summary>
    public Dictionary<Guid, int> LaneMap()
    {
        var map = new Dictionary<Guid, int>(Clips.Count);
        foreach (var layout in Clips)
            map[layout.Clip.Id] = layout.Lane;
        return map;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
