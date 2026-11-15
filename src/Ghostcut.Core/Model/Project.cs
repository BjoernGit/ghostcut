using System.Text.Json.Serialization;

namespace Ghostcut.Core.Model;

/// <summary>
/// The whole edit. All changes go through commands (<see cref="Commands.IEditCommand"/>) so that
/// undo/redo, the UI and later an agent share one path.
/// </summary>
public sealed class Project
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public ProjectSettings Settings { get; set; } = new();

    public List<MediaSource> Sources { get; set; } = new();

    public MainTrack Main { get; set; } = new();

    public List<AuxTrack> AuxTracks { get; set; } = new();

    public MediaSource? FindSource(Guid id) => Sources.Find(s => s.Id == id);

    public MediaSource GetSource(Guid id) =>
        FindSource(id) ?? throw new InvalidOperationException($"Unknown source {id}.");

    public AuxTrack? FindAuxTrack(Guid id) => AuxTracks.Find(t => t.Id == id);

    public ChainClip? FindChainClip(Guid id) => Main.Clips.Find(c => c.Id == id);

    /// <summary>Finds an aux clip together with the track that holds it.</summary>
    public (AuxClip Clip, AuxTrack Track)? FindAuxClip(Guid id)
    {
        foreach (var track in AuxTracks)
        {
            var clip = track.Clips.Find(c => c.Id == id);
            if (clip != null)
                return (clip, track);
        }
        return null;
    }

    public Clip? FindClip(Guid id) => (Clip?)FindChainClip(id) ?? FindAuxClip(id)?.Clip;

    [JsonIgnore]
    public TimeSpan ChainDuration
    {
        get
        {
            var total = TimeSpan.Zero;
            foreach (var clip in Main.Clips)
                total += clip.Duration;
            return total;
        }
    }

    /// <summary>End of the last clip on any track.</summary>
    [JsonIgnore]
    public TimeSpan Duration
    {
        get
        {
            var end = ChainDuration;
            foreach (var track in AuxTracks)
                foreach (var clip in track.Clips)
                    if (clip.End > end)
                        end = clip.End;
            return end;
        }
    }
}

/// <summary>Per-project tunables.</summary>
public sealed class ProjectSettings
{
    /// <summary>Length of the automatic anti-click fade at every audio clip edge.</summary>
    public TimeSpan AudioEdgeFade { get; set; } = TimeSpan.FromMilliseconds(10);

    /// <summary>Shortest clip a trim or split may produce.</summary>
    public TimeSpan MinClipDuration { get; set; } = TimeSpan.FromMilliseconds(100);
}
