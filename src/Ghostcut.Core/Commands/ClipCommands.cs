using Ghostcut.Core.Model;

namespace Ghostcut.Core.Commands;

public enum TrimEdge
{
    /// <summary>The start of the clip (its <see cref="Clip.In"/>).</summary>
    Left,

    /// <summary>The end of the clip (its <see cref="Clip.Out"/>).</summary>
    Right,
}

internal static class ClipRules
{
    public static void ValidateRange(Project project, Clip clip)
    {
        var source = project.FindSource(clip.SourceId) ?? throw new EditException($"Unknown source {clip.SourceId}.");
        if (clip.In < TimeSpan.Zero || clip.Out > source.Duration || clip.Out <= clip.In)
            throw new EditException($"Clip range {clip.In}–{clip.Out} is outside of source {source.Name} (0–{source.Duration}).");
    }

    public static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;

    public static TimeSpan ChainStart(Project project, int index)
    {
        var start = TimeSpan.Zero;
        for (var i = 0; i < index; i++)
            start += project.Main.Clips[i].Duration;
        return start;
    }
}

/// <summary>Inserts a video clip into the ghost chain; later clips ripple right.</summary>
public sealed class InsertChainClipCommand(ChainClip clip, int? index = null) : IEditCommand
{
    public ChainClip Clip { get; } = clip;

    public string Description => "Add clip";

    public void Execute(Project project)
    {
        var source = project.FindSource(Clip.SourceId) ?? throw new EditException($"Unknown source {Clip.SourceId}.");
        if (!source.HasVideo)
            throw new EditException($"{source.Name} has no picture; the main track only takes video.");
        if (project.FindClip(Clip.Id) != null)
            throw new EditException($"Clip {Clip.Id} already exists.");
        ClipRules.ValidateRange(project, Clip);
        var at = Math.Clamp(index ?? project.Main.Clips.Count, 0, project.Main.Clips.Count);
        project.Main.Clips.Insert(at, Clip);
    }

    public void Undo(Project project) => project.Main.Clips.Remove(Clip);

    public static ChainClip FullClip(MediaSource source) =>
        new() { SourceId = source.Id, In = TimeSpan.Zero, Out = source.Duration };
}

/// <summary>Places an audio clip on a voice or music track.</summary>
public sealed class InsertAuxClipCommand(Guid trackId, AuxClip clip) : IEditCommand
{
    public Guid TrackId { get; } = trackId;

    public AuxClip Clip { get; } = clip;

    public string Description => "Add audio clip";

    public void Execute(Project project)
    {
        var track = project.FindAuxTrack(TrackId) ?? throw new EditException($"Unknown track {TrackId}.");
        var source = project.FindSource(Clip.SourceId) ?? throw new EditException($"Unknown source {Clip.SourceId}.");
        if (!source.HasAudio)
            throw new EditException($"{source.Name} has no sound.");
        if (project.FindClip(Clip.Id) != null)
            throw new EditException($"Clip {Clip.Id} already exists.");
        ClipRules.ValidateRange(project, Clip);
        if (Clip.Start < TimeSpan.Zero)
            throw new EditException("Clip start is before the timeline start.");
        track.Clips.Add(Clip);
    }

    public void Undo(Project project) => project.FindAuxTrack(TrackId)!.Clips.Remove(Clip);

    public static AuxClip FullClip(MediaSource source, TimeSpan start) =>
        new() { SourceId = source.Id, In = TimeSpan.Zero, Out = source.Duration, Start = start };
}

/// <summary>Removes a clip from the chain (later clips close the gap) or from an aux track.</summary>
public sealed class RemoveClipCommand(Guid clipId) : IEditCommand
{
    private Clip? _removed;
    private AuxTrack? _track;
    private int _index;

    public Guid ClipId { get; } = clipId;

    public string Description => "Delete clip";

    public void Execute(Project project)
    {
        var chainIndex = project.Main.Clips.FindIndex(c => c.Id == ClipId);
        if (chainIndex >= 0)
        {
            _removed = project.Main.Clips[chainIndex];
            _track = null;
            _index = chainIndex;
            project.Main.Clips.RemoveAt(chainIndex);
            return;
        }
        var found = project.FindAuxClip(ClipId) ?? throw new EditException($"Unknown clip {ClipId}.");
        _removed = found.Clip;
        _track = found.Track;
        _index = found.Track.Clips.IndexOf(found.Clip);
        found.Track.Clips.RemoveAt(_index);
    }

    public void Undo(Project project)
    {
        if (_track == null)
            project.Main.Clips.Insert(_index, (ChainClip)_removed!);
        else
            _track.Clips.Insert(_index, (AuxClip)_removed!);
    }
}

/// <summary>
/// Moves one edge of a clip to a new source time. The cut-off part stays as overhang. In the chain,
/// later clips ripple; a chain clip's start stays attached to its predecessor, so trimming the left
/// edge shifts the clip's content instead of its position. An aux clip keeps its content in place
/// when trimmed on the left (its start moves with the edge).
/// Values are clamped to the source and to <see cref="ProjectSettings.MinClipDuration"/>.
/// </summary>
public sealed class TrimClipCommand(Guid clipId, TrimEdge edge, TimeSpan sourceTime) : IEditCommand
{
    private TimeSpan _oldIn, _oldOut, _oldStart;

    public Guid ClipId { get; } = clipId;

    public TrimEdge Edge { get; } = edge;

    public TimeSpan SourceTime { get; } = sourceTime;

    public string Description => "Trim clip";

    public void Execute(Project project)
    {
        var clip = project.FindClip(ClipId) ?? throw new EditException($"Unknown clip {ClipId}.");
        var source = project.GetSource(clip.SourceId);
        var min = project.Settings.MinClipDuration;
        var aux = clip as AuxClip;
        _oldIn = clip.In;
        _oldOut = clip.Out;
        _oldStart = aux?.Start ?? TimeSpan.Zero;

        if (Edge == TrimEdge.Left)
        {
            var hi = clip.Out - min;
            if (hi < clip.In)
                hi = clip.In;
            var lo = TimeSpan.Zero;
            if (aux != null && aux.In - aux.Start > lo)
                lo = aux.In - aux.Start; // the start must not go before the timeline origin
            if (lo > hi)
                lo = hi;
            var newIn = ClipRules.Clamp(SourceTime, lo, hi);
            if (aux != null)
                aux.Start += newIn - aux.In;
            clip.In = newIn;
        }
        else
        {
            var lo = clip.In + min;
            if (lo > clip.Out)
                lo = clip.Out;
            var hi = source.Duration;
            if (hi < lo)
                hi = lo;
            clip.Out = ClipRules.Clamp(SourceTime, lo, hi);
        }
    }

    public void Undo(Project project)
    {
        var clip = project.FindClip(ClipId)!;
        clip.In = _oldIn;
        clip.Out = _oldOut;
        if (clip is AuxClip aux)
            aux.Start = _oldStart;
    }
}

/// <summary>Moves a chain clip to another position of the chain (reorder).</summary>
public sealed class MoveChainClipCommand(Guid clipId, int newIndex) : IEditCommand
{
    private int _oldIndex;

    public Guid ClipId { get; } = clipId;

    /// <summary>Target index in the chain after the clip has been taken out.</summary>
    public int NewIndex { get; } = newIndex;

    public string Description => "Move clip";

    public void Execute(Project project)
    {
        var clips = project.Main.Clips;
        _oldIndex = clips.FindIndex(c => c.Id == ClipId);
        if (_oldIndex < 0)
            throw new EditException($"Unknown chain clip {ClipId}.");
        var clip = clips[_oldIndex];
        clips.RemoveAt(_oldIndex);
        clips.Insert(Math.Clamp(NewIndex, 0, clips.Count), clip);
    }

    public void Undo(Project project)
    {
        var clips = project.Main.Clips;
        var index = clips.FindIndex(c => c.Id == ClipId);
        var clip = clips[index];
        clips.RemoveAt(index);
        clips.Insert(_oldIndex, clip);
    }
}

/// <summary>Moves an aux clip in time and optionally onto another aux track.</summary>
public sealed class MoveAuxClipCommand(Guid clipId, TimeSpan newStart, Guid? targetTrackId = null) : IEditCommand
{
    private TimeSpan _oldStart;
    private AuxTrack? _oldTrack;
    private int _oldIndex;

    public Guid ClipId { get; } = clipId;

    public TimeSpan NewStart { get; } = newStart;

    public Guid? TargetTrackId { get; } = targetTrackId;

    public string Description => "Move audio clip";

    public void Execute(Project project)
    {
        var (clip, track) = project.FindAuxClip(ClipId) ?? throw new EditException($"Unknown aux clip {ClipId}.");
        var target = TargetTrackId is { } id
            ? project.FindAuxTrack(id) ?? throw new EditException($"Unknown track {id}.")
            : track;
        _oldStart = clip.Start;
        _oldTrack = track;
        _oldIndex = track.Clips.IndexOf(clip);
        clip.Start = NewStart < TimeSpan.Zero ? TimeSpan.Zero : NewStart;
        if (!ReferenceEquals(target, track))
        {
            track.Clips.RemoveAt(_oldIndex);
            target.Clips.Add(clip);
        }
    }

    public void Undo(Project project)
    {
        var (clip, track) = project.FindAuxClip(ClipId)!.Value;
        clip.Start = _oldStart;
        if (!ReferenceEquals(track, _oldTrack))
        {
            track.Clips.Remove(clip);
            _oldTrack!.Clips.Insert(_oldIndex, clip);
        }
    }
}

/// <summary>
/// Splits a clip at a timeline time into two clips of the same source. Each keeps the other's part as
/// overhang. In the chain the second clip follows directly, so it lands on another lane.
/// </summary>
public sealed class SplitClipCommand(Guid clipId, TimeSpan timelineTime, Guid? newClipId = null) : IEditCommand
{
    private TimeSpan _oldOut, _oldFadeOut;
    private Clip? _second;
    private AuxTrack? _track;

    public Guid ClipId { get; } = clipId;

    public TimeSpan TimelineTime { get; } = timelineTime;

    /// <summary>Id of the second half (known before execution).</summary>
    public Guid NewClipId { get; } = newClipId ?? Guid.NewGuid();

    public string Description => "Split clip";

    public void Execute(Project project)
    {
        var min = project.Settings.MinClipDuration;
        var chainIndex = project.Main.Clips.FindIndex(c => c.Id == ClipId);
        if (chainIndex >= 0)
        {
            var first = project.Main.Clips[chainIndex];
            var start = ClipRules.ChainStart(project, chainIndex);
            var split = SplitPoint(first, start, min);
            var second = first.Clone();
            second.Id = NewClipId;
            second.In = split;
            second.FadeInFromBlack = TimeSpan.Zero;
            _oldOut = first.Out;
            _oldFadeOut = first.FadeOutToBlack;
            first.Out = split;
            first.FadeOutToBlack = TimeSpan.Zero;
            project.Main.Clips.Insert(chainIndex + 1, second);
            _second = second;
            _track = null;
            return;
        }

        var (clip, track) = project.FindAuxClip(ClipId) ?? throw new EditException($"Unknown clip {ClipId}.");
        var auxSplit = SplitPoint(clip, clip.Start, min);
        var auxSecond = clip.Clone();
        auxSecond.Id = NewClipId;
        auxSecond.In = auxSplit;
        auxSecond.Start = clip.Start + (auxSplit - clip.In);
        _oldOut = clip.Out;
        clip.Out = auxSplit;
        track.Clips.Insert(track.Clips.IndexOf(clip) + 1, auxSecond);
        _second = auxSecond;
        _track = track;
    }

    public void Undo(Project project)
    {
        if (_track == null)
        {
            project.Main.Clips.Remove((ChainClip)_second!);
            var first = project.FindChainClip(ClipId)!;
            first.Out = _oldOut;
            first.FadeOutToBlack = _oldFadeOut;
        }
        else
        {
            _track.Clips.Remove((AuxClip)_second!);
            project.FindAuxClip(ClipId)!.Value.Clip.Out = _oldOut;
        }
    }

    private TimeSpan SplitPoint(Clip clip, TimeSpan clipStart, TimeSpan min)
    {
        var split = clip.In + (TimelineTime - clipStart);
        if (split - clip.In < min || clip.Out - split < min)
            throw new EditException("The split point is outside the clip or too close to its edge.");
        return split;
    }
}

/// <summary>Sets the volume of a clip in dB.</summary>
public sealed class SetClipGainCommand(Guid clipId, double gainDb) : IEditCommand
{
    private double _previous;

    public Guid ClipId { get; } = clipId;

    public double GainDb { get; } = gainDb;

    public string Description => "Change clip volume";

    public void Execute(Project project)
    {
        var clip = project.FindClip(ClipId) ?? throw new EditException($"Unknown clip {ClipId}.");
        _previous = clip.GainDb;
        clip.GainDb = Math.Clamp(GainDb, EditLimits.MinGainDb, EditLimits.MaxGainDb);
    }

    public void Undo(Project project) => project.FindClip(ClipId)!.GainDb = _previous;
}

/// <summary>Sets fade from / to black of a chain clip; zero turns a fade off.</summary>
public sealed class SetClipFadesCommand(Guid clipId, TimeSpan fadeIn, TimeSpan fadeOut) : IEditCommand
{
    private TimeSpan _oldIn, _oldOut;

    public Guid ClipId { get; } = clipId;

    public TimeSpan FadeIn { get; } = fadeIn;

    public TimeSpan FadeOut { get; } = fadeOut;

    public string Description => "Change clip fades";

    public void Execute(Project project)
    {
        var clip = project.FindChainClip(ClipId) ?? throw new EditException($"Unknown chain clip {ClipId}.");
        _oldIn = clip.FadeInFromBlack;
        _oldOut = clip.FadeOutToBlack;
        var fadeIn = ClipRules.Clamp(FadeIn, TimeSpan.Zero, clip.Duration);
        clip.FadeInFromBlack = fadeIn;
        clip.FadeOutToBlack = ClipRules.Clamp(FadeOut, TimeSpan.Zero, clip.Duration - fadeIn);
    }

    public void Undo(Project project)
    {
        var clip = project.FindChainClip(ClipId)!;
        clip.FadeInFromBlack = _oldIn;
        clip.FadeOutToBlack = _oldOut;
    }
}
