using Ghostcut.Core.Model;

namespace Ghostcut.Core.Commands;

/// <summary>Registers an imported media file in the project.</summary>
public sealed class AddSourceCommand(MediaSource source) : IEditCommand
{
    public MediaSource Source { get; } = source;

    public string Description => $"Import {Source.Name}";

    public void Execute(Project project)
    {
        if (project.FindSource(Source.Id) != null)
            throw new EditException($"Source {Source.Id} already exists.");
        if (Source.Duration <= TimeSpan.Zero)
            throw new EditException($"Source {Source.Name} has no duration.");
        project.Sources.Add(Source);
    }

    public void Undo(Project project) => project.Sources.Remove(Source);
}

/// <summary>Adds a voice or music track.</summary>
public sealed class AddAuxTrackCommand(AuxTrack track, int? index = null) : IEditCommand
{
    public AuxTrack Track { get; } = track;

    public string Description => $"Add {Track.Kind} track";

    public void Execute(Project project)
    {
        if (project.FindAuxTrack(Track.Id) != null)
            throw new EditException($"Track {Track.Id} already exists.");
        var at = Math.Clamp(index ?? project.AuxTracks.Count, 0, project.AuxTracks.Count);
        project.AuxTracks.Insert(at, Track);
    }

    public void Undo(Project project) => project.AuxTracks.Remove(Track);
}

/// <summary>Removes a voice or music track together with its clips.</summary>
public sealed class RemoveAuxTrackCommand(Guid trackId) : IEditCommand
{
    private AuxTrack? _removed;
    private int _index;

    public Guid TrackId { get; } = trackId;

    public string Description => "Remove track";

    public void Execute(Project project)
    {
        var track = project.FindAuxTrack(TrackId) ?? throw new EditException($"Unknown track {TrackId}.");
        _index = project.AuxTracks.IndexOf(track);
        project.AuxTracks.RemoveAt(_index);
        _removed = track;
    }

    public void Undo(Project project) => project.AuxTracks.Insert(_index, _removed!);
}

/// <summary>Mutes or unmutes a track; <c>null</c> addresses the main track.</summary>
public sealed class SetTrackMuteCommand(Guid? auxTrackId, bool muted) : IEditCommand
{
    private bool _previous;

    public Guid? AuxTrackId { get; } = auxTrackId;

    public bool Muted { get; } = muted;

    public string Description => Muted ? "Mute track" : "Unmute track";

    public void Execute(Project project)
    {
        if (AuxTrackId is { } id)
        {
            var track = project.FindAuxTrack(id) ?? throw new EditException($"Unknown track {id}.");
            _previous = track.Muted;
            track.Muted = Muted;
        }
        else
        {
            _previous = project.Main.Muted;
            project.Main.Muted = Muted;
        }
    }

    public void Undo(Project project)
    {
        if (AuxTrackId is { } id)
            project.FindAuxTrack(id)!.Muted = _previous;
        else
            project.Main.Muted = _previous;
    }
}

/// <summary>Sets the length of the automatic anti-click fade at audio clip edges.</summary>
public sealed class SetAudioEdgeFadeCommand(TimeSpan fade) : IEditCommand
{
    private TimeSpan _previous;

    public TimeSpan Fade { get; } = fade;

    public string Description => "Change audio edge fade";

    public void Execute(Project project)
    {
        _previous = project.Settings.AudioEdgeFade;
        project.Settings.AudioEdgeFade = Clamp(Fade, TimeSpan.Zero, EditLimits.MaxAudioEdgeFade);
    }

    public void Undo(Project project) => project.Settings.AudioEdgeFade = _previous;

    private static TimeSpan Clamp(TimeSpan v, TimeSpan min, TimeSpan max) => v < min ? min : v > max ? max : v;
}
