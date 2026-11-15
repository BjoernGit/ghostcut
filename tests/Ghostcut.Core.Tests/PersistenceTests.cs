using Ghostcut.Core.Commands;
using Ghostcut.Core.Model;
using Ghostcut.Core.Persistence;
using static Ghostcut.Core.Tests.TestProject;

namespace Ghostcut.Core.Tests;

public class PersistenceTests
{
    private static TestProject Sample()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 1, 4);
        t.History.Execute(new SetClipFadesCommand(a.Id, S(0.5), S(0)));
        t.History.Execute(new SetClipGainCommand(a.Id, -6));
        t.AddChain(8, 0, 3);
        var voice = t.AddTrack(AuxTrackKind.Voice);
        t.AddAux(voice, 5, 0.5, 4, start: 1.25);
        t.History.Execute(new SetTrackMuteCommand(voice.Id, true));
        t.History.Execute(new SetAudioEdgeFadeCommand(TimeSpan.FromMilliseconds(20)));
        return t;
    }

    [Fact]
    public void Round_trip_keeps_everything()
    {
        var original = Sample().Project;

        var json = ProjectSerializer.Serialize(original);
        var loaded = ProjectSerializer.Deserialize(json);

        Assert.Equal(json, ProjectSerializer.Serialize(loaded));
        Assert.Equal(original.Main.Clips.Select(c => (c.Id, c.In, c.Out, c.GainDb, c.FadeInFromBlack)),
                     loaded.Main.Clips.Select(c => (c.Id, c.In, c.Out, c.GainDb, c.FadeInFromBlack)));
        Assert.True(loaded.AuxTracks[0].Muted);
        Assert.Equal(S(1.25), loaded.AuxTracks[0].Clips[0].Start);
        Assert.Equal(TimeSpan.FromMilliseconds(20), loaded.Settings.AudioEdgeFade);
        Assert.Equal(original.Sources.Select(s => s.Path), loaded.Sources.Select(s => s.Path));
    }

    [Fact]
    public void Json_is_readable()
    {
        var json = ProjectSerializer.Serialize(Sample().Project);

        Assert.Contains("\"formatVersion\": 1", json);
        Assert.Contains("\"kind\": \"voice\"", json);
        Assert.Contains("\"in\": \"00:00:01\"", json);
    }

    [Fact]
    public void A_newer_format_is_rejected()
    {
        var json = ProjectSerializer.Serialize(Sample().Project).Replace("\"formatVersion\": 1", "\"formatVersion\": 99");

        var ex = Assert.Throws<InvalidDataException>(() => ProjectSerializer.Deserialize(json));
        Assert.Contains("newer", ex.Message);
    }

    [Fact]
    public void Broken_references_are_rejected()
    {
        var project = Sample().Project;
        project.Sources.RemoveAt(0);

        Assert.Throws<InvalidDataException>(() => ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project)));
        Assert.Throws<InvalidDataException>(() => ProjectSerializer.Deserialize("{ not json"));
    }

    [Fact]
    public void Save_writes_atomically_and_keeps_a_backup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ghostcut-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "test" + ProjectSerializer.FileExtension);
        try
        {
            var t = Sample();
            ProjectSerializer.Save(t.Project, path);
            t.History.Execute(new SetClipGainCommand(t.Project.Main.Clips[0].Id, -12));
            ProjectSerializer.Save(t.Project, path);

            Assert.Equal(-12, ProjectSerializer.Load(path).Main.Clips[0].GainDb);
            Assert.Equal(-6, ProjectSerializer.Load(path + ".bak").Main.Clips[0].GainDb);
            Assert.Equal(2, Directory.GetFiles(dir).Length); // no temp files left
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
