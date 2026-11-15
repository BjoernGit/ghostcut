using Ghostcut.Core.Model;
using Ghostcut.Core.Timeline;

namespace Ghostcut.Core.Commands;

/// <summary>Where dropped/imported files should go.</summary>
public abstract record ImportTarget
{
    /// <summary>Video to the end of the chain, pure audio to the first music track at <see cref="AudioStart"/>.</summary>
    public sealed record Auto(TimeSpan AudioStart) : ImportTarget;

    /// <summary>Insert video into the chain at a clip index.</summary>
    public sealed record Chain(int Index) : ImportTarget;

    /// <summary>Place sound on an aux track, one file after the other from <see cref="Start"/>.</summary>
    public sealed record Aux(Guid TrackId, TimeSpan Start) : ImportTarget;
}

/// <summary>
/// Turns probed files plus a drop target into one undoable command. Files that are already part of
/// the project reuse their source.
/// </summary>
public static class ImportPlanner
{
    public static CompositeCommand? Plan(Project project, IReadOnlyList<MediaSource> probed, ImportTarget target)
    {
        var commands = new List<IEditCommand>();
        var sources = new List<MediaSource>();
        foreach (var file in probed)
        {
            var existing = project.Sources.Find(s => string.Equals(s.Path, file.Path, StringComparison.OrdinalIgnoreCase))
                           ?? sources.Find(s => string.Equals(s.Path, file.Path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                sources.Add(existing);
                continue;
            }
            commands.Add(new AddSourceCommand(file));
            sources.Add(file);
        }

        var chainIndex = target is ImportTarget.Chain c ? Math.Clamp(c.Index, 0, project.Main.Clips.Count) : project.Main.Clips.Count;
        Guid? auxTrack = target is ImportTarget.Aux a ? a.TrackId : null;
        var auxTime = target switch
        {
            ImportTarget.Aux aux => aux.Start,
            ImportTarget.Auto auto => auto.AudioStart,
            ImportTarget.Chain => ChainBoundary(project, chainIndex),
            _ => TimeSpan.Zero,
        };
        var placed = 0;

        foreach (var source in sources)
        {
            var toChain = target is not ImportTarget.Aux && source.HasVideo;
            if (toChain)
            {
                commands.Add(new InsertChainClipCommand(InsertChainClipCommand.FullClip(source), chainIndex++));
                placed++;
                continue;
            }
            if (!source.HasAudio)
                continue;
            if (auxTrack == null)
            {
                var music = project.AuxTracks.Find(t => t.Kind == AuxTrackKind.Music);
                if (music == null)
                {
                    music = new AuxTrack { Kind = AuxTrackKind.Music, Name = NextTrackName(project, AuxTrackKind.Music) };
                    commands.Add(new AddAuxTrackCommand(music));
                }
                auxTrack = music.Id;
            }
            commands.Add(new InsertAuxClipCommand(auxTrack.Value, InsertAuxClipCommand.FullClip(source, auxTime)));
            auxTime += source.Duration;
            placed++;
        }

        if (placed == 0)
            return null;
        var description = probed.Count == 1 ? $"Import {probed[0].Name}" : $"Import {probed.Count} files";
        return new CompositeCommand(description, commands);
    }

    public static string NextTrackName(Project project, AuxTrackKind kind)
    {
        var count = project.AuxTracks.Count(t => t.Kind == kind);
        return $"{kind} {count + 1}";
    }

    private static TimeSpan ChainBoundary(Project project, int index)
    {
        var layout = ChainLayout.Compute(project);
        return index < layout.Clips.Count ? layout.Clips[index].Start : layout.Duration;
    }
}
