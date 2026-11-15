using Ghostcut.Core.Commands;
using Ghostcut.Core.Model;
using static Ghostcut.Core.Tests.TestProject;

namespace Ghostcut.Core.Tests;

public class ImportPlannerTests
{
    private static MediaSource File(string name, double seconds, bool video) =>
        new() { Path = Path.Combine("media", name), Duration = S(seconds), HasVideo = video, HasAudio = true };

    [Fact]
    public void Auto_puts_video_into_the_chain_and_audio_on_a_new_music_track()
    {
        var t = new TestProject();
        var command = ImportPlanner.Plan(t.Project, new[] { File("a.mp4", 3, true), File("song.mp3", 30, false) }, new ImportTarget.Auto(S(1)))!;

        t.History.Execute(command);

        Assert.Single(t.Project.Main.Clips);
        var music = Assert.Single(t.Project.AuxTracks);
        Assert.Equal(AuxTrackKind.Music, music.Kind);
        Assert.Equal(S(1), music.Clips[0].Start);

        t.History.Undo(); // the whole drop is one step
        Assert.Empty(t.Project.Main.Clips);
        Assert.Empty(t.Project.AuxTracks);
        Assert.Empty(t.Project.Sources);
    }

    [Fact]
    public void Chain_target_inserts_at_the_index_and_reuses_known_files()
    {
        var t = new TestProject();
        var a = t.AddChain(2, 0, 2);
        var b = t.AddChain(2, 0, 2);
        var known = t.Project.Sources[0];
        var again = new MediaSource { Path = known.Path, Duration = known.Duration, HasVideo = true, HasAudio = true };

        t.History.Execute(ImportPlanner.Plan(t.Project, new[] { again, File("c.mp4", 1, true) }, new ImportTarget.Chain(1))!);

        Assert.Equal(4, t.Project.Main.Clips.Count);
        Assert.Equal(a.Id, t.Project.Main.Clips[0].Id);
        Assert.Equal(known.Id, t.Project.Main.Clips[1].SourceId);
        Assert.Equal(b.Id, t.Project.Main.Clips[3].Id);
        Assert.Equal(3, t.Project.Sources.Count);
    }

    [Fact]
    public void Aux_target_places_files_one_after_another()
    {
        var t = new TestProject();
        var voice = t.AddTrack(AuxTrackKind.Voice);

        t.History.Execute(ImportPlanner.Plan(t.Project, new[] { File("v1.wav", 2, false), File("v2.wav", 3, false) }, new ImportTarget.Aux(voice.Id, S(4)))!);

        Assert.Equal(new[] { S(4), S(6) }, voice.Clips.Select(c => c.Start));
    }
}
