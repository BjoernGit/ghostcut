using Ghostcut.Core.Timeline;
using static Ghostcut.Core.Tests.TestProject;

namespace Ghostcut.Core.Tests;

public class ChainLayoutTests
{
    [Fact]
    public void Clips_form_a_gap_free_chain()
    {
        var t = new TestProject();
        t.AddChain(10, 2, 5);  // 3 s
        t.AddChain(10, 0, 4);  // 4 s
        t.AddChain(10, 1, 2);  // 1 s

        var layout = ChainLayout.Compute(t.Project);

        Assert.Equal(new[] { S(0), S(3), S(7) }, layout.Clips.Select(c => c.Start));
        Assert.Equal(new[] { S(3), S(7), S(8) }, layout.Clips.Select(c => c.End));
        Assert.Equal(S(8), layout.Duration);
    }

    [Fact]
    public void Overhangs_are_the_unused_source_parts()
    {
        var t = new TestProject();
        t.AddChain(10, 2, 5);

        var clip = ChainLayout.Compute(t.Project).Clips[0];

        Assert.Equal(S(2), clip.LeftOverhang);
        Assert.Equal(S(5), clip.RightOverhang);
        Assert.Equal(S(-2), clip.FootprintStart);
        Assert.Equal(S(8), clip.FootprintEnd);
    }

    [Fact]
    public void Lanes_alternate_when_overhangs_do_not_collide()
    {
        var t = new TestProject();
        // Untrimmed clips have no overhang, so lanes 0 and 1 are always free.
        for (var i = 0; i < 4; i++)
            t.AddChain(2, 0, 2);

        var layout = ChainLayout.Compute(t.Project);

        Assert.Equal(new[] { 0, 1, 0, 1 }, layout.Clips.Select(c => c.Lane));
        Assert.Equal(2, layout.LaneCount);
    }

    [Fact]
    public void Colliding_overhangs_open_another_lane()
    {
        var t = new TestProject();
        t.AddChain(10, 0, 2);  // lane 0, right overhang reaches to 10 s
        t.AddChain(2, 0, 2);   // lane 1, 2..4
        t.AddChain(2, 0, 2);   // 4..6 would collide with clip 0's overhang on lane 0 → lane 2

        var layout = ChainLayout.Compute(t.Project);

        Assert.Equal(new[] { 0, 1, 2 }, layout.Clips.Select(c => c.Lane));
        Assert.Equal(3, layout.LaneCount);
    }

    [Fact]
    public void A_clip_never_shares_the_lane_of_its_predecessor()
    {
        var t = new TestProject();
        t.AddChain(10, 0, 2);  // lane 0
        t.AddChain(10, 0, 2);  // lane 1 (overhang to 12)
        t.AddChain(2, 0, 2);   // lanes 0 and 1 collide → lane 2
        t.AddChain(2, 0, 2);   // must not be lane 2; lanes 0/1 still collide → lane 3

        var layout = ChainLayout.Compute(t.Project);

        Assert.Equal(new[] { 0, 1, 2, 3 }, layout.Clips.Select(c => c.Lane));
    }

    [Fact]
    public void Touching_footprints_do_not_collide()
    {
        var t = new TestProject();
        t.AddChain(4, 0, 2);   // lane 0, footprint 0..4
        t.AddChain(2, 0, 2);   // lane 1, 2..4
        t.AddChain(2, 0, 2);   // 4..6 touches clip 0's footprint end → lane 0

        var layout = ChainLayout.Compute(t.Project);

        Assert.Equal(0, layout.Clips[2].Lane);
    }

    [Fact]
    public void Frozen_lanes_keep_their_assignment()
    {
        var t = new TestProject();
        var a = t.AddChain(2, 0, 2);
        var b = t.AddChain(2, 0, 2);
        var frozen = new Dictionary<Guid, int> { [a.Id] = 1, [b.Id] = 0 };

        var layout = ChainLayout.Compute(t.Project, frozen);

        Assert.Equal(new[] { 1, 0 }, layout.Clips.Select(c => c.Lane));
    }

    [Fact]
    public void ClipAt_uses_start_inclusive_end_exclusive()
    {
        var t = new TestProject();
        var a = t.AddChain(2, 0, 2);
        var b = t.AddChain(2, 0, 2);
        var layout = ChainLayout.Compute(t.Project);

        Assert.Equal(a.Id, layout.ClipAt(S(0))!.Value.Clip.Id);
        Assert.Equal(b.Id, layout.ClipAt(S(2))!.Value.Clip.Id);
        Assert.Null(layout.ClipAt(S(4)));
        Assert.Equal(S(1.5), layout.ClipAt(S(3.5))!.Value.ToSourceTime(S(3.5)));
    }
}
