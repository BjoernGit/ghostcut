using Ghostcut.Core.Timeline;
using static Ghostcut.Core.Tests.TestProject;

namespace Ghostcut.Core.Tests;

public class SnappingTests
{
    [Fact]
    public void Snaps_to_the_nearest_target_within_the_threshold()
    {
        var result = Snapper.Snap(S(4.95), new[] { S(4), S(5), S(6) }, S(0.1));

        Assert.True(result.Snapped);
        Assert.Equal(S(5), result.Value);
    }

    [Fact]
    public void Stays_put_outside_the_threshold()
    {
        var result = Snapper.Snap(S(4.5), new[] { S(4), S(5) }, S(0.1));

        Assert.False(result.Snapped);
        Assert.Equal(S(4.5), result.Value);
    }

    [Fact]
    public void A_span_snaps_by_its_closer_edge()
    {
        // start 1.0 is 0.08 from 0.92; end 3.0 is 0.02 from 3.02 → the end wins
        var result = Snapper.SnapSpan(S(1), S(2), new[] { S(0.92), S(3.02) }, S(0.1));

        Assert.Equal(S(1.02), result.Value);
        Assert.Equal(S(3.02), result.Target);
    }

    [Fact]
    public void Targets_are_real_clip_edges_not_overhang_ends()
    {
        var t = new TestProject();
        t.AddChain(10, 2, 4);  // footprint -2..8, real 0..2
        var layout = ChainLayout.Compute(t.Project);

        var targets = SnapTargets.Collect(t.Project, layout, playhead: S(1.5));

        Assert.Contains(S(0), targets);
        Assert.Contains(S(2), targets);
        Assert.Contains(S(1.5), targets);
        Assert.DoesNotContain(S(8), targets);
        Assert.DoesNotContain(S(-2), targets);
    }
}
