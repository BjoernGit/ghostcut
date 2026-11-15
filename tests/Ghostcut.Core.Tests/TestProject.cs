using Ghostcut.Core.Commands;
using Ghostcut.Core.Model;

namespace Ghostcut.Core.Tests;

/// <summary>Builds projects with synthetic sources (no media files needed).</summary>
internal sealed class TestProject
{
    public Project Project { get; } = new();

    public History History { get; }

    public TestProject()
    {
        History = new History(Project);
    }

    public static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    public MediaSource AddSource(double seconds, bool video = true, bool audio = true, string name = "clip.mp4")
    {
        var source = new MediaSource
        {
            Path = Path.Combine("media", name),
            Duration = S(seconds),
            HasVideo = video,
            HasAudio = audio,
            Width = video ? 1920 : 0,
            Height = video ? 1080 : 0,
            FrameRate = video ? 30 : 0,
        };
        History.Execute(new AddSourceCommand(source));
        return source;
    }

    /// <summary>Adds a chain clip using [inSec, outSec) of a new source of the given length.</summary>
    public ChainClip AddChain(double sourceSeconds, double inSec, double outSec)
    {
        var source = AddSource(sourceSeconds);
        var clip = new ChainClip { SourceId = source.Id, In = S(inSec), Out = S(outSec) };
        History.Execute(new InsertChainClipCommand(clip));
        return clip;
    }

    public AuxTrack AddTrack(AuxTrackKind kind = AuxTrackKind.Music)
    {
        var track = new AuxTrack { Kind = kind, Name = kind.ToString() };
        History.Execute(new AddAuxTrackCommand(track));
        return track;
    }

    public AuxClip AddAux(AuxTrack track, double sourceSeconds, double inSec, double outSec, double start)
    {
        var source = AddSource(sourceSeconds, video: false, name: "music.wav");
        var clip = new AuxClip { SourceId = source.Id, In = S(inSec), Out = S(outSec), Start = S(start) };
        History.Execute(new InsertAuxClipCommand(track.Id, clip));
        return clip;
    }
}
