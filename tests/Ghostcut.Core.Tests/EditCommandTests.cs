using Ghostcut.Core.Commands;
using Ghostcut.Core.Model;
using Ghostcut.Core.Timeline;
using static Ghostcut.Core.Tests.TestProject;

namespace Ghostcut.Core.Tests;

public class TrimTests
{
    [Fact]
    public void Trimming_the_right_edge_ripples_later_clips()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 4);
        t.AddChain(10, 0, 2);

        t.History.Execute(new TrimClipCommand(a.Id, TrimEdge.Right, S(3)));

        var layout = ChainLayout.Compute(t.Project);
        Assert.Equal(S(3), layout.Clips[0].End);
        Assert.Equal(S(3), layout.Clips[1].Start);
        Assert.Equal(S(7), layout.Clips[0].RightOverhang);
    }

    [Fact]
    public void Trimming_the_left_edge_keeps_the_clip_attached_to_its_predecessor()
    {
        var t = new TestProject();
        t.AddChain(10, 0, 2);
        var b = t.AddChain(10, 0, 6);

        t.History.Execute(new TrimClipCommand(b.Id, TrimEdge.Left, S(2.5)));

        var layout = ChainLayout.Compute(t.Project);
        Assert.Equal(S(2), layout.Clips[1].Start);          // still attached
        Assert.Equal(S(5.5), layout.Clips[1].End);          // content shifted, clip shorter
        Assert.Equal(S(2.5), layout.Clips[1].LeftOverhang); // cut-off part stays as ghost
    }

    [Fact]
    public void Trim_can_pull_back_an_overhang()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 3, 5);

        t.History.Execute(new TrimClipCommand(a.Id, TrimEdge.Left, S(1)));
        t.History.Execute(new TrimClipCommand(a.Id, TrimEdge.Right, S(9)));

        Assert.Equal(S(1), a.In);
        Assert.Equal(S(9), a.Out);
    }

    [Fact]
    public void Trim_is_clamped_to_the_source()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 3, 5);

        t.History.Execute(new TrimClipCommand(a.Id, TrimEdge.Left, S(-4)));
        t.History.Execute(new TrimClipCommand(a.Id, TrimEdge.Right, S(25)));

        Assert.Equal(S(0), a.In);
        Assert.Equal(S(10), a.Out);
    }

    [Fact]
    public void Trim_keeps_the_minimum_clip_duration()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 3, 5);
        var min = t.Project.Settings.MinClipDuration;

        t.History.Execute(new TrimClipCommand(a.Id, TrimEdge.Left, S(7)));
        Assert.Equal(S(5) - min, a.In);

        t.History.Execute(new TrimClipCommand(a.Id, TrimEdge.Right, S(0)));
        Assert.Equal(a.In + min, a.Out);
    }

    [Fact]
    public void Left_trim_of_an_aux_clip_keeps_its_content_in_place()
    {
        var t = new TestProject();
        var track = t.AddTrack();
        var clip = t.AddAux(track, 20, 2, 10, start: 5);

        t.History.Execute(new TrimClipCommand(clip.Id, TrimEdge.Left, S(4)));

        Assert.Equal(S(7), clip.Start);
        Assert.Equal(S(4), clip.In);
        Assert.Equal(S(13), clip.End);
    }

    [Fact]
    public void Left_trim_of_an_aux_clip_stops_at_the_timeline_origin()
    {
        var t = new TestProject();
        var track = t.AddTrack();
        var clip = t.AddAux(track, 20, 5, 10, start: 1);

        t.History.Execute(new TrimClipCommand(clip.Id, TrimEdge.Left, S(0)));

        Assert.Equal(S(0), clip.Start);
        Assert.Equal(S(4), clip.In);
    }

    [Fact]
    public void Undo_restores_a_trim()
    {
        var t = new TestProject();
        var track = t.AddTrack();
        var clip = t.AddAux(track, 20, 2, 10, start: 5);

        t.History.Execute(new TrimClipCommand(clip.Id, TrimEdge.Left, S(4)));
        t.History.Undo();

        Assert.Equal((S(2), S(10), S(5)), (clip.In, clip.Out, clip.Start));
    }
}

public class SplitTests
{
    [Fact]
    public void Split_creates_two_clips_with_mutual_overhangs()
    {
        var t = new TestProject();
        t.AddChain(4, 0, 4);
        var b = t.AddChain(10, 1, 7); // timeline 4..10
        var split = new SplitClipCommand(b.Id, S(6));

        t.History.Execute(split);

        var layout = ChainLayout.Compute(t.Project);
        Assert.Equal(3, layout.Clips.Count);
        var first = layout.Clips[1];
        var second = layout.Clips[2];
        Assert.Equal(split.NewClipId, second.Clip.Id);
        Assert.Equal(first.Clip.SourceId, second.Clip.SourceId);
        Assert.Equal((S(1), S(3)), (first.Clip.In, first.Clip.Out));
        Assert.Equal((S(3), S(7)), (second.Clip.In, second.Clip.Out));
        Assert.Equal((S(4), S(6), S(10)), (first.Start, first.End, second.End));
        // Each half overhangs into the other.
        Assert.True(first.RightOverhang >= second.Duration);
        Assert.True(second.LeftOverhang >= first.Duration);
    }

    [Fact]
    public void The_second_half_jumps_to_another_lane()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 10);

        t.History.Execute(new SplitClipCommand(a.Id, S(4)));

        var layout = ChainLayout.Compute(t.Project);
        Assert.Equal(0, layout.Clips[0].Lane);
        Assert.Equal(1, layout.Clips[1].Lane);
    }

    [Fact]
    public void Split_moves_the_fade_out_to_the_second_half()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 10);
        t.History.Execute(new SetClipFadesCommand(a.Id, S(1), S(2)));
        var split = new SplitClipCommand(a.Id, S(5));

        t.History.Execute(split);

        var second = t.Project.FindChainClip(split.NewClipId)!;
        Assert.Equal((S(1), TimeSpan.Zero), (a.FadeInFromBlack, a.FadeOutToBlack));
        Assert.Equal((TimeSpan.Zero, S(2)), (second.FadeInFromBlack, second.FadeOutToBlack));

        t.History.Undo();
        Assert.Single(t.Project.Main.Clips);
        Assert.Equal((S(10), S(2)), (a.Out, a.FadeOutToBlack));
    }

    [Fact]
    public void Split_outside_or_near_the_edge_is_rejected_and_changes_nothing()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 4);
        var before = t.History.UndoDescription;

        Assert.Throws<EditException>(() => t.History.Execute(new SplitClipCommand(a.Id, S(4.05))));
        Assert.Throws<EditException>(() => t.History.Execute(new SplitClipCommand(a.Id, S(0.01))));

        Assert.Single(t.Project.Main.Clips);
        Assert.Equal(S(4), a.Out);
        Assert.Equal(before, t.History.UndoDescription);
    }

    [Fact]
    public void Split_of_an_aux_clip_keeps_both_halves_in_place()
    {
        var t = new TestProject();
        var track = t.AddTrack();
        var clip = t.AddAux(track, 20, 2, 10, start: 5);
        var split = new SplitClipCommand(clip.Id, S(8));

        t.History.Execute(split);

        var second = t.Project.FindAuxClip(split.NewClipId)!.Value.Clip;
        Assert.Equal((S(5), S(8)), (clip.Start, clip.End));
        Assert.Equal((S(8), S(13)), (second.Start, second.End));
        Assert.Equal(S(5), second.In);
    }
}

public class MoveTests
{
    [Fact]
    public void Moving_a_chain_clip_reorders_the_chain_and_ripples()
    {
        var t = new TestProject();
        var a = t.AddChain(1, 0, 1);
        var b = t.AddChain(2, 0, 2);
        var c = t.AddChain(3, 0, 3);

        t.History.Execute(new MoveChainClipCommand(a.Id, 2));

        Assert.Equal(new[] { b.Id, c.Id, a.Id }, t.Project.Main.Clips.Select(x => x.Id));
        var layout = ChainLayout.Compute(t.Project);
        Assert.Equal(new[] { S(0), S(2), S(5) }, layout.Clips.Select(x => x.Start));

        t.History.Undo();
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, t.Project.Main.Clips.Select(x => x.Id));
    }

    [Fact]
    public void Move_index_is_clamped()
    {
        var t = new TestProject();
        var a = t.AddChain(1, 0, 1);
        var b = t.AddChain(2, 0, 2);

        t.History.Execute(new MoveChainClipCommand(b.Id, -5));
        Assert.Equal(new[] { b.Id, a.Id }, t.Project.Main.Clips.Select(x => x.Id));

        t.History.Execute(new MoveChainClipCommand(b.Id, 99));
        Assert.Equal(new[] { a.Id, b.Id }, t.Project.Main.Clips.Select(x => x.Id));
    }

    [Fact]
    public void Aux_clips_move_freely_but_not_before_zero_and_across_tracks()
    {
        var t = new TestProject();
        var music = t.AddTrack(AuxTrackKind.Music);
        var voice = t.AddTrack(AuxTrackKind.Voice);
        var clip = t.AddAux(music, 10, 0, 5, start: 3);

        t.History.Execute(new MoveAuxClipCommand(clip.Id, S(-2), voice.Id));

        Assert.Equal(S(0), clip.Start);
        Assert.Empty(music.Clips);
        Assert.Single(voice.Clips);

        t.History.Undo();
        Assert.Equal(S(3), clip.Start);
        Assert.Single(music.Clips);
        Assert.Empty(voice.Clips);
    }

    [Fact]
    public void Removing_a_chain_clip_closes_the_gap_and_undo_restores_it()
    {
        var t = new TestProject();
        var a = t.AddChain(1, 0, 1);
        var b = t.AddChain(2, 0, 2);
        var c = t.AddChain(3, 0, 3);

        t.History.Execute(new RemoveClipCommand(b.Id));
        Assert.Equal(S(1), ChainLayout.Compute(t.Project).Clips[1].Start);

        t.History.Undo();
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, t.Project.Main.Clips.Select(x => x.Id));
    }
}

public class ValidationTests
{
    [Fact]
    public void Audio_only_sources_are_rejected_on_the_main_track()
    {
        var t = new TestProject();
        var audio = t.AddSource(5, video: false);

        Assert.Throws<EditException>(() =>
            t.History.Execute(new InsertChainClipCommand(InsertChainClipCommand.FullClip(audio))));
        Assert.Empty(t.Project.Main.Clips);
    }

    [Fact]
    public void Clip_ranges_outside_the_source_are_rejected()
    {
        var t = new TestProject();
        var source = t.AddSource(5);

        Assert.Throws<EditException>(() => t.History.Execute(
            new InsertChainClipCommand(new ChainClip { SourceId = source.Id, In = S(1), Out = S(6) })));
    }

    [Fact]
    public void Gain_and_fades_are_clamped()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 4);

        t.History.Execute(new SetClipGainCommand(a.Id, 100));
        Assert.Equal(EditLimits.MaxGainDb, a.GainDb);

        t.History.Execute(new SetClipFadesCommand(a.Id, S(3), S(3)));
        Assert.Equal((S(3), S(1)), (a.FadeInFromBlack, a.FadeOutToBlack));
    }

    [Fact]
    public void Composite_command_rolls_back_when_a_part_fails()
    {
        var t = new TestProject();
        var source = new MediaSource { Path = "x.mp4", Duration = S(5), HasVideo = true };
        var bad = new ChainClip { SourceId = source.Id, In = S(0), Out = S(9) };
        var composite = new CompositeCommand("Import", new IEditCommand[] { new AddSourceCommand(source), new InsertChainClipCommand(bad) });

        Assert.Throws<EditException>(() => t.History.Execute(composite));

        Assert.Empty(t.Project.Sources);
        Assert.False(t.History.CanUndo);
    }
}
