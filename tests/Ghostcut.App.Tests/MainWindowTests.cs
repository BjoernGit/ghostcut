using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Ghostcut.App.Timeline;
using Ghostcut.Core.Commands;
using Ghostcut.Core.Model;
using Ghostcut.Core.Timeline;
using Ghostcut.Media;

[assembly: AvaloniaTestApplication(typeof(Ghostcut.App.Tests.TestApp))]

namespace Ghostcut.App.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Drives the real main window headless: import generated media, then trim, snap, reorder, split
/// and undo with simulated mouse and keyboard. Renders PNGs for a visual check (folder in
/// <see cref="CaptureDirectory"/>). Skipped when ffmpeg is not installed.
/// </summary>
public sealed class MainWindowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ghostcut-uitest", Guid.NewGuid().ToString("N"));

    public MainWindowTests()
    {
        Directory.CreateDirectory(_dir);
        // Keep the real autosave and cache untouched.
        Environment.SetEnvironmentVariable(AppPaths.DataDirVariable, Path.Combine(_dir, "data"));
    }

    public static string CaptureDirectory => Path.Combine(Path.GetTempPath(), "ghostcut-ui-captures");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataDirVariable, null);
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private async Task<string> Make(Ffmpeg ffmpeg, string name, double seconds, int hz, bool video)
    {
        var path = Path.Combine(_dir, name);
        var d = seconds.ToString(CultureInfo.InvariantCulture);
        var args = new List<string> { "-v", "error", "-y" };
        if (video)
            args.AddRange(new[] { "-f", "lavfi", "-i", $"testsrc2=size=640x360:rate=30:duration={d}" });
        args.AddRange(new[] { "-f", "lavfi", "-i", $"aevalsrc=0.4*sin(2*PI*{hz}*t):s=48000:d={d}" });
        if (video)
            args.AddRange(new[] { "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest" });
        args.Add(path);
        await Ffmpeg.RunAsync(ffmpeg.FfmpegPath, args);
        return path;
    }

    private static void Capture(Window window, string name)
    {
        Directory.CreateDirectory(CaptureDirectory);
        window.CaptureRenderedFrame()?.Save(Path.Combine(CaptureDirectory, name));
    }

    private static async Task Settle(Func<bool>? until = null, int timeoutMs = 5000)
    {
        var start = Environment.TickCount64;
        do
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(30);
        }
        while (until != null && !until() && Environment.TickCount64 - start < timeoutMs);
        Dispatcher.UIThread.RunJobs();
    }

    private static Point InWindow(Window window, Control control, Point p) => control.TranslatePoint(p, window)!.Value;

    private static void Drag(Window window, Point from, Point to, Action? whileHeld = null)
    {
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        window.MouseMove(to);
        whileHeld?.Invoke();
        window.MouseUp(to, MouseButton.Left);
    }

    [AvaloniaFact]
    public async Task Ghost_timeline_editing_end_to_end()
    {
        var ffmpeg = Ffmpeg.TryLocate();
        Assert.SkipWhen(ffmpeg == null, "ffmpeg not installed");

        var a = await Make(ffmpeg!, "a.mp4", 6, 437, video: true);
        var b = await Make(ffmpeg!, "b.mp4", 4, 661, video: true);
        var music = await Make(ffmpeg!, "music.wav", 8, 223, video: false);

        var window = new MainWindow(audioOutput: false) { Width = 1400, Height = 900 };
        window.Show();
        var session = window.Session;
        var timeline = window.FindControl<TimelineControl>("Timeline")!;

        await session.ImportAsync(new[] { a, b, music }, new ImportTarget.Auto(TimeSpan.Zero));
        var project = session.Project;
        Assert.Equal(2, project.Main.Clips.Count);
        Assert.Single(project.AuxTracks);
        timeline.ZoomToFit();
        await Settle(() => window.PreviewFramesShown > 0);
        Assert.True(window.PreviewFramesShown > 0, "the preview shows a decoded frame");
        Capture(window, "01-imported.png");

        var clipA = project.Main.Clips[0];
        var clipB = project.Main.Clips[1];

        // 1) Trim A's right edge onto the playhead: it snaps exactly, B ripples along.
        session.Playhead = S(3);
        var from = InWindow(window, timeline, timeline.PointOnClip(clipA.Id, +1));
        var to = new Point(InWindow(window, timeline, new Point(timeline.XOf(S(3)) + 4, 0)).X, from.Y);
        TimeSpan? snapDuringDrag = null;
        Drag(window, from, to, () =>
        {
            snapDuringDrag = timeline.SnapLine;
            Capture(window, "02-trim-snapping.png");
        });
        Assert.Equal(S(3), snapDuringDrag);
        Assert.Equal(S(3), clipA.Out);
        Assert.Equal(S(3), ChainLayout.Compute(project).Clips[1].Start);
        Assert.Equal("Trim clip", session.History.UndoDescription);

        // 2) Trim B's left edge by ~1 s: B stays attached to A, its content moves.
        from = InWindow(window, timeline, timeline.PointOnClip(clipB.Id, -1));
        to = from + new Point(timeline.XOf(S(1)) - timeline.XOf(S(0)), 0);
        Drag(window, from, to);
        Assert.InRange(clipB.In.TotalSeconds, 0.9, 1.1);
        Assert.Equal(S(3), ChainLayout.Compute(project).Clips[1].Start);

        // 3) Reorder: drag B in front of A.
        from = InWindow(window, timeline, timeline.PointOnClip(clipB.Id, 0));
        to = new Point(InWindow(window, timeline, new Point(timeline.XOf(S(0.2)), 0)).X, from.Y);
        Drag(window, from, to);
        Assert.Equal(new[] { clipB.Id, clipA.Id }, project.Main.Clips.Select(c => c.Id));

        // 4) Split B at the playhead with the S key: the second half lands on another lane.
        session.Playhead = S(1);
        session.SelectedClipId = clipB.Id;
        window.KeyPress(Key.S, RawInputModifiers.None, PhysicalKey.S, "s");
        await Settle();
        Assert.Equal(3, project.Main.Clips.Count);
        var layout = ChainLayout.Compute(project);
        Assert.NotEqual(layout.Clips[0].Lane, layout.Clips[1].Lane);
        Capture(window, "03-after-edits.png");

        // 5) Undo everything (split, reorder, two trims) with Ctrl+Z.
        for (var i = 0; i < 4; i++)
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
        await Settle();
        Assert.Equal(new[] { clipA.Id, clipB.Id }, project.Main.Clips.Select(c => c.Id));
        Assert.Equal((S(0), S(6)), (clipA.In, clipA.Out));
        Assert.Equal((S(0), S(4)), (clipB.In, clipB.Out));

        // 6) Redo brings it back.
        for (var i = 0; i < 4; i++)
            window.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, "y");
        await Settle();
        Assert.Equal(3, project.Main.Clips.Count);

        // 7) Play across the cuts at 1 s and 3 s: the playhead runs and decoded frames keep coming.
        session.Playhead = S(0.5);
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        await Settle(() => session.Playhead > S(3.3), timeoutMs: 8000);
        Assert.True(window.IsPlaying);
        Assert.True(session.Playhead > S(3.3), $"playhead only reached {session.Playhead}");
        Assert.True(window.PlaybackFramesShown >= 45, $"only {window.PlaybackFramesShown} frames shown in ~2.8 s");
        Capture(window, "04-playing.png");
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        await Settle();
        Assert.False(window.IsPlaying);

        session.History.MarkSaved(); // no unsaved-changes prompt on close
        window.Close();
    }
}
