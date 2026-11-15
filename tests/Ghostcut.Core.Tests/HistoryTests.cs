using Ghostcut.Core.Commands;
using static Ghostcut.Core.Tests.TestProject;

namespace Ghostcut.Core.Tests;

public class HistoryTests
{
    [Fact]
    public void Undo_and_redo_walk_through_all_edits()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 10);
        t.History.Execute(new TrimClipCommand(a.Id, TrimEdge.Right, S(6)));
        t.History.Execute(new SplitClipCommand(a.Id, S(3)));

        Assert.Equal(2, t.Project.Main.Clips.Count);
        t.History.Undo();
        Assert.Single(t.Project.Main.Clips);
        Assert.Equal(S(6), a.Out);
        t.History.Undo();
        Assert.Equal(S(10), a.Out);

        t.History.Redo();
        t.History.Redo();
        Assert.Equal(2, t.Project.Main.Clips.Count);
        Assert.False(t.History.CanRedo);
    }

    [Fact]
    public void A_new_edit_clears_redo()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 10);
        t.History.Execute(new TrimClipCommand(a.Id, TrimEdge.Right, S(6)));
        t.History.Undo();

        t.History.Execute(new TrimClipCommand(a.Id, TrimEdge.Right, S(8)));

        Assert.False(t.History.CanRedo);
    }

    [Fact]
    public void A_gesture_is_one_undo_step()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 10);
        var gesture = new object();

        t.History.ExecuteGesture(new TrimClipCommand(a.Id, TrimEdge.Right, S(9)), gesture);
        t.History.ExecuteGesture(new TrimClipCommand(a.Id, TrimEdge.Right, S(7)), gesture);
        t.History.ExecuteGesture(new TrimClipCommand(a.Id, TrimEdge.Right, S(5)), gesture);
        t.History.EndGesture();
        Assert.Equal(S(5), a.Out);

        t.History.Undo();
        Assert.Equal(S(10), a.Out);
    }

    [Fact]
    public void Cancelling_a_gesture_restores_the_state_before_it()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 10);
        var gesture = new object();
        t.History.ExecuteGesture(new TrimClipCommand(a.Id, TrimEdge.Right, S(9)), gesture);
        t.History.ExecuteGesture(new TrimClipCommand(a.Id, TrimEdge.Right, S(4)), gesture);

        t.History.CancelGesture(gesture);

        Assert.Equal(S(10), a.Out);
        Assert.Equal("Add clip", t.History.UndoDescription);
    }

    [Fact]
    public void Dirty_follows_the_saved_state()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 10);
        t.History.MarkSaved();
        Assert.False(t.History.IsDirty);

        t.History.Execute(new TrimClipCommand(a.Id, TrimEdge.Right, S(6)));
        Assert.True(t.History.IsDirty);

        t.History.Undo();
        Assert.False(t.History.IsDirty);
    }

    [Fact]
    public void Changed_fires_for_every_change()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 10);
        var count = 0;
        t.History.Changed += () => count++;

        t.History.Execute(new TrimClipCommand(a.Id, TrimEdge.Right, S(6)));
        t.History.Undo();
        t.History.Redo();

        Assert.Equal(3, count);
    }

    [Fact]
    public void Capacity_limits_the_undo_stack()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 10);
        t.History.Capacity = 3;
        for (var i = 0; i < 6; i++)
            t.History.Execute(new SetClipGainCommand(a.Id, -i));

        var undone = 0;
        while (t.History.Undo())
            undone++;

        Assert.Equal(3, undone);
    }
}
