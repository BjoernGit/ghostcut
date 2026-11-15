using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using Ghostcut.Core.Commands;
using Ghostcut.Core.Model;
using Ghostcut.Core.Timeline;

namespace Ghostcut.App.Timeline;

/// <summary>
/// The ghost timeline, drawn by hand. Top: ruler. Then the chain lanes (alternating clips with
/// greyed-out overhangs), then one row per voice/music track. Every edit is a command; drags run as
/// history gestures, so a drag is one undo step while the model updates live.
/// </summary>
public sealed class TimelineControl : Control
{
    // ---- Tunables ----------------------------------------------------------------------------
    public double HeaderWidth { get; set; } = 130;
    public double RulerHeight { get; set; } = 28;
    public double ChainLaneHeight { get; set; } = 44;
    public double AuxRowHeight { get; set; } = 40;
    public double SectionGap { get; set; } = 12;
    public double EdgeGrabPixels { get; set; } = 7;
    public double SnapPixels { get; set; } = 8;
    public double DragStartPixels { get; set; } = 4;
    public double MinPixelsPerSecond { get; set; } = 2;
    public double MaxPixelsPerSecond { get; set; } = 2000;
    public double ZoomStep { get; set; } = 1.2;

    // ---- Look --------------------------------------------------------------------------------
    private static readonly IBrush Background = new ImmutableSolidColorBrush(Color.Parse("#1B1C1F"));
    private static readonly IBrush RulerBrush = new ImmutableSolidColorBrush(Color.Parse("#26282D"));
    private static readonly IBrush HeaderBrush = new ImmutableSolidColorBrush(Color.Parse("#232529"));
    private static readonly IBrush ChainAreaBrush = new ImmutableSolidColorBrush(Color.Parse("#202226"));
    private static readonly IBrush AuxRowBrush = new ImmutableSolidColorBrush(Color.Parse("#1F2124"));
    private static readonly IBrush TextBrush = new ImmutableSolidColorBrush(Color.Parse("#E6E6E6"));
    private static readonly IBrush DimTextBrush = new ImmutableSolidColorBrush(Color.Parse("#9DA1A8"));
    private static readonly IBrush GhostBrush = new ImmutableSolidColorBrush(Color.FromArgb(70, 150, 153, 160));
    private static readonly IPen GhostPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(140, 160, 163, 170)), 1, new ImmutableDashStyle(new double[] { 3, 3 }, 0));
    private static readonly IPen LanePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#2C2F35")), 1);
    private static readonly IPen CutPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1);
    private static readonly IPen TickPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#6B6F76")), 1);
    private static readonly IPen SelectedPen = new ImmutablePen(Brushes.White, 2);
    private static readonly IPen ClipPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 1);
    private static readonly IPen PlayheadPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#FF4D4D")), 1.5);
    private static readonly IBrush PlayheadBrush = new ImmutableSolidColorBrush(Color.Parse("#FF4D4D"));
    private static readonly ImmutableSolidColorBrush SnapBrush = new(Color.Parse("#FFE14D"));
    private static readonly IPen SnapPen = new ImmutablePen(SnapBrush, 2, new ImmutableDashStyle(new double[] { 4, 3 }, 0));
    private static readonly IPen InsertPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#FFE14D")), 3);
    private static readonly IBrush DragGhostBrush = new ImmutableSolidColorBrush(Color.FromArgb(90, 255, 225, 77));
    private static readonly IBrush FadeBrush = new ImmutableSolidColorBrush(Color.FromArgb(150, 0, 0, 0));
    private static readonly IBrush ButtonBrush = new ImmutableSolidColorBrush(Color.Parse("#34373D"));
    private static readonly IBrush MutedBrush = new ImmutableSolidColorBrush(Color.Parse("#C0392B"));

    private static readonly IBrush[] LaneBrushes =
    {
        new ImmutableSolidColorBrush(Color.Parse("#3D7FD9")),
        new ImmutableSolidColorBrush(Color.Parse("#2FA38C")),
        new ImmutableSolidColorBrush(Color.Parse("#8A63D2")),
        new ImmutableSolidColorBrush(Color.Parse("#C77D2E")),
        new ImmutableSolidColorBrush(Color.Parse("#C2497A")),
    };

    private static readonly IBrush VoiceBrush = new ImmutableSolidColorBrush(Color.Parse("#D98A3D"));
    private static readonly IBrush MusicBrush = new ImmutableSolidColorBrush(Color.Parse("#4CAF50"));

    private static readonly Typeface Font = new(FontFamily.Default);

    private static readonly double[] TickSteps = { 1.0 / 30, 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 };

    private EditorSession? _session;
    private double _pixelsPerSecond = 60;
    private double _scrollSeconds = -0.5;
    private Drag? _drag;
    private TimeSpan? _snapLine;
    private TimeSpan? _dropLine;

    public TimelineControl()
    {
        Focusable = true;
        ClipToBounds = true;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => { _dropLine = null; InvalidateVisual(); });
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public EditorSession? Session
    {
        get => _session;
        set
        {
            if (_session != null)
            {
                _session.ProjectChanged -= OnProjectChanged;
                _session.PlayheadChanged -= InvalidateVisual;
                _session.SelectionChanged -= InvalidateVisual;
            }
            _session = value;
            if (_session != null)
            {
                _session.ProjectChanged += OnProjectChanged;
                _session.PlayheadChanged += InvalidateVisual;
                _session.SelectionChanged += InvalidateVisual;
            }
            OnProjectChanged();
        }
    }

    public double PixelsPerSecond
    {
        get => _pixelsPerSecond;
        set
        {
            _pixelsPerSecond = Math.Clamp(value, MinPixelsPerSecond, MaxPixelsPerSecond);
            InvalidateVisual();
        }
    }

    public double ScrollSeconds
    {
        get => _scrollSeconds;
        set
        {
            _scrollSeconds = Math.Max(MinScroll(), value);
            InvalidateVisual();
        }
    }

    /// <summary>True while the user drags something; the window keeps the preview on the playhead.</summary>
    public bool IsDragging => _drag != null;

    private Project? Project => _session?.Project;

    // ---- Coordinates -------------------------------------------------------------------------

    private double X(TimeSpan time) => HeaderWidth + (time.TotalSeconds - _scrollSeconds) * _pixelsPerSecond;

    private TimeSpan T(double x) => TimeSpan.FromSeconds((x - HeaderWidth) / _pixelsPerSecond + _scrollSeconds);

    private TimeSpan SnapThreshold => TimeSpan.FromSeconds(SnapPixels / _pixelsPerSecond);

    private double ChainTop => RulerHeight;

    private double ChainHeight(ChainLayout layout) => layout.LaneCount * ChainLaneHeight;

    private double AuxTop(ChainLayout layout, int index) => ChainTop + ChainHeight(layout) + SectionGap + index * (AuxRowHeight + 2);

    private double LaneTop(int lane) => ChainTop + lane * ChainLaneHeight;

    private ChainLayout Layout() => ChainLayout.Compute(Project!, _drag?.FrozenLanes);

    private double MinScroll()
    {
        var project = Project;
        var overhang = project is { Main.Clips.Count: > 0 } ? project.Main.Clips[0].In.TotalSeconds : 0;
        return -overhang - 0.5;
    }

    // ---- Public view helpers -----------------------------------------------------------------

    public void ZoomBy(double factor, double? anchorX = null)
    {
        var x = anchorX ?? HeaderWidth + (Bounds.Width - HeaderWidth) / 2;
        var anchor = T(x).TotalSeconds;
        _pixelsPerSecond = Math.Clamp(_pixelsPerSecond * factor, MinPixelsPerSecond, MaxPixelsPerSecond);
        ScrollSeconds = anchor - (x - HeaderWidth) / _pixelsPerSecond;
    }

    public void ZoomToFit()
    {
        var duration = Math.Max(1, Project?.Duration.TotalSeconds ?? 0);
        var width = Math.Max(100, Bounds.Width - HeaderWidth - 40);
        _pixelsPerSecond = Math.Clamp(width / (duration + 1), MinPixelsPerSecond, MaxPixelsPerSecond);
        ScrollSeconds = -0.5;
    }

    /// <summary>Scrolls so that <paramref name="time"/> is visible (used during playback).</summary>
    public void EnsureVisible(TimeSpan time)
    {
        var x = X(time);
        if (x < HeaderWidth || x > Bounds.Width - 20)
            ScrollSeconds = time.TotalSeconds - 0.1 * (Bounds.Width - HeaderWidth) / _pixelsPerSecond;
    }

    /// <summary>Aborts a running drag and restores the state before it.</summary>
    public void CancelDrag()
    {
        if (_drag == null || _session == null)
            return;
        _session.History.CancelGesture(_drag.Token);
        _drag = null;
        _snapLine = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnProjectChanged()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
        if (Project == null)
            return new Size(width, 200);
        var layout = Layout();
        return new Size(width, AuxTop(layout, Project.AuxTracks.Count) + 24);
    }

    // ---- Rendering ---------------------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);
        var project = Project;
        if (project == null)
            return;
        var layout = Layout();
        var chainRect = new Rect(0, ChainTop, bounds.Width, ChainHeight(layout));

        context.FillRectangle(ChainAreaBrush, chainRect);
        for (var lane = 1; lane < layout.LaneCount; lane++)
            context.DrawLine(LanePen, new Point(HeaderWidth, LaneTop(lane)), new Point(bounds.Width, LaneTop(lane)));
        for (var i = 0; i < project.AuxTracks.Count; i++)
            context.FillRectangle(AuxRowBrush, new Rect(0, AuxTop(layout, i), bounds.Width, AuxRowHeight));

        using (context.PushClip(new Rect(HeaderWidth, 0, Math.Max(0, bounds.Width - HeaderWidth), bounds.Height)))
        {
            DrawChain(context, project, layout);
            DrawAuxTracks(context, project, layout);
            DrawRuler(context, bounds);
            DrawOverlays(context, project, layout, bounds);
        }
        DrawHeaders(context, project, layout);
    }

    private void DrawChain(DrawingContext context, Project project, ChainLayout layout)
    {
        var selected = _session!.SelectedClipId;
        var dimmed = project.Main.Muted;
        var reorder = _drag is { Mode: DragMode.Reorder } ? _drag.ClipId : (Guid?)null;
        var visibleStart = T(HeaderWidth);
        var visibleEnd = T(Bounds.Width);

        // Overhangs first, so real clip parts are always on top.
        foreach (var c in layout.Clips)
        {
            if (c.FootprintEnd < visibleStart || c.FootprintStart > visibleEnd)
                continue;
            var top = LaneTop(c.Lane) + 3;
            var height = ChainLaneHeight - 6;
            if (c.LeftOverhang > TimeSpan.Zero)
                DrawGhost(context, new Rect(X(c.FootprintStart), top + 4, X(c.Start) - X(c.FootprintStart), height - 8));
            if (c.RightOverhang > TimeSpan.Zero)
                DrawGhost(context, new Rect(X(c.End), top + 4, X(c.FootprintEnd) - X(c.End), height - 8));
        }

        foreach (var c in layout.Clips)
        {
            if (c.End < visibleStart || c.Start > visibleEnd)
                continue;
            var rect = new Rect(X(c.Start), LaneTop(c.Lane) + 3, Math.Max(1, X(c.End) - X(c.Start)), ChainLaneHeight - 6);
            var brush = LaneBrushes[c.Lane % LaneBrushes.Length];
            using (context.PushOpacity(dimmed || reorder == c.Clip.Id ? 0.45 : 1))
            {
                context.DrawRectangle(brush, ClipPen, rect, 3, 3);
                DrawFades(context, rect, c.Clip, c.Duration);
                var name = project.FindSource(c.Clip.SourceId)?.Name ?? "?";
                DrawLabel(context, rect, name, $"{Format(c.Duration)}{GainSuffix(c.Clip.GainDb)}");
            }
            if (selected == c.Clip.Id)
                context.DrawRectangle(null, SelectedPen, rect.Deflate(1), 3, 3);
        }

        // The alignment line: every cut is a straight edge through all chain lanes.
        var chainBottom = ChainTop + ChainHeight(layout);
        for (var i = 1; i < layout.Clips.Count; i++)
        {
            var x = X(layout.Clips[i].Start);
            context.DrawLine(CutPen, new Point(x, ChainTop), new Point(x, chainBottom));
        }
    }

    private static void DrawGhost(DrawingContext context, Rect rect)
    {
        if (rect.Width <= 0)
            return;
        context.DrawRectangle(GhostBrush, GhostPen, rect, 2, 2);
    }

    private void DrawFades(DrawingContext context, Rect rect, ChainClip clip, TimeSpan duration)
    {
        if (clip.FadeInFromBlack > TimeSpan.Zero)
        {
            var w = Math.Min(rect.Width, Math.Min(clip.FadeInFromBlack.TotalSeconds, duration.TotalSeconds) * _pixelsPerSecond);
            context.DrawGeometry(FadeBrush, null, new PolylineGeometry(new[] { rect.TopLeft, new Point(rect.Left + w, rect.Top), rect.BottomLeft }, true));
        }
        if (clip.FadeOutToBlack > TimeSpan.Zero)
        {
            var w = Math.Min(rect.Width, Math.Min(clip.FadeOutToBlack.TotalSeconds, duration.TotalSeconds) * _pixelsPerSecond);
            context.DrawGeometry(FadeBrush, null, new PolylineGeometry(new[] { rect.TopRight, new Point(rect.Right - w, rect.Top), rect.BottomRight }, true));
        }
    }

    private void DrawAuxTracks(DrawingContext context, Project project, ChainLayout layout)
    {
        var selected = _session!.SelectedClipId;
        for (var i = 0; i < project.AuxTracks.Count; i++)
        {
            var track = project.AuxTracks[i];
            var top = AuxTop(layout, i);
            var brush = track.Kind == AuxTrackKind.Voice ? VoiceBrush : MusicBrush;
            foreach (var clip in track.Clips)
            {
                var source = project.FindSource(clip.SourceId);
                var sourceDuration = source?.Duration ?? clip.Out;
                var ghostTop = top + 7;
                var ghostHeight = AuxRowHeight - 14;
                if (clip.In > TimeSpan.Zero)
                    DrawGhost(context, new Rect(X(clip.Start - clip.In), ghostTop, X(clip.Start) - X(clip.Start - clip.In), ghostHeight));
                if (sourceDuration > clip.Out)
                    DrawGhost(context, new Rect(X(clip.End), ghostTop, X(clip.End + (sourceDuration - clip.Out)) - X(clip.End), ghostHeight));
            }
            foreach (var clip in track.Clips)
            {
                var rect = new Rect(X(clip.Start), top + 3, Math.Max(1, X(clip.End) - X(clip.Start)), AuxRowHeight - 6);
                using (context.PushOpacity(track.Muted ? 0.45 : 1))
                {
                    context.DrawRectangle(brush, ClipPen, rect, 3, 3);
                    DrawLabel(context, rect, project.FindSource(clip.SourceId)?.Name ?? "?", $"{Format(clip.Duration)}{GainSuffix(clip.GainDb)}");
                }
                if (selected == clip.Id)
                    context.DrawRectangle(null, SelectedPen, rect.Deflate(1), 3, 3);
            }
        }
    }

    private static void DrawLabel(DrawingContext context, Rect rect, string title, string detail)
    {
        if (rect.Width < 18)
            return;
        using (context.PushClip(rect.Deflate(4)))
        {
            context.DrawText(Text(title, 12, TextBrush), new Point(rect.Left + 6, rect.Top + 3));
            if (rect.Height > 30)
                context.DrawText(Text(detail, 10, TextBrush), new Point(rect.Left + 6, rect.Top + 19));
        }
    }

    private void DrawRuler(DrawingContext context, Rect bounds)
    {
        context.FillRectangle(RulerBrush, new Rect(0, 0, bounds.Width, RulerHeight));
        var step = TickSteps.FirstOrDefault(s => s * _pixelsPerSecond >= 70, TickSteps[^1]);
        var first = Math.Floor(T(HeaderWidth).TotalSeconds / step) * step;
        var last = T(bounds.Width).TotalSeconds;
        for (var s = first; s <= last; s += step)
        {
            var x = X(TimeSpan.FromSeconds(s));
            context.DrawLine(TickPen, new Point(x, RulerHeight - 8), new Point(x, RulerHeight));
            var minor = x + step * _pixelsPerSecond / 2;
            context.DrawLine(TickPen, new Point(minor, RulerHeight - 4), new Point(minor, RulerHeight));
            if (s >= -1e-9)
                context.DrawText(Text(FormatRuler(s, step), 10, DimTextBrush), new Point(x + 3, 4));
        }
    }

    private void DrawOverlays(DrawingContext context, Project project, ChainLayout layout, Rect bounds)
    {
        var chainBottom = ChainTop + ChainHeight(layout);
        if (_drag is { Mode: DragMode.Reorder } reorder)
        {
            var clip = project.FindChainClip(reorder.ClipId);
            if (clip != null)
            {
                var start = reorder.CurrentTime - reorder.GrabOffset;
                context.FillRectangle(DragGhostBrush, new Rect(X(start), ChainTop + 2, clip.Duration.TotalSeconds * _pixelsPerSecond, ChainHeight(layout) - 4));
            }
            if (reorder.InsertTime is { } insert)
                context.DrawLine(InsertPen, new Point(X(insert), ChainTop), new Point(X(insert), chainBottom));
        }
        if (_dropLine is { } drop)
            context.DrawLine(InsertPen, new Point(X(drop), RulerHeight), new Point(X(drop), bounds.Height));

        var playheadX = X(_session!.Playhead);
        context.DrawLine(PlayheadPen, new Point(playheadX, 0), new Point(playheadX, bounds.Height));
        context.DrawGeometry(PlayheadBrush, null, new PolylineGeometry(new[]
        {
            new Point(playheadX - 6, 0), new Point(playheadX + 6, 0), new Point(playheadX, 10),
        }, true));

        // Snap indicator on top of everything (it often coincides with the playhead).
        if (_snapLine is { } snap)
        {
            var x = X(snap);
            context.DrawLine(SnapPen, new Point(x, 0), new Point(x, bounds.Height));
            context.DrawGeometry(SnapBrush, null, new PolylineGeometry(new[]
            {
                new Point(x, RulerHeight - 12), new Point(x + 6, RulerHeight - 6), new Point(x, RulerHeight), new Point(x - 6, RulerHeight - 6),
            }, true));
        }
    }

    private void DrawHeaders(DrawingContext context, Project project, ChainLayout layout)
    {
        context.FillRectangle(HeaderBrush, new Rect(0, 0, HeaderWidth, Bounds.Height));
        context.DrawText(Text(Format(_session!.Playhead, precise: true), 12, TextBrush), new Point(8, 6));

        var chainRect = new Rect(0, ChainTop, HeaderWidth, ChainHeight(layout));
        context.DrawText(Text("Main", 13, TextBrush), new Point(10, chainRect.Top + 6));
        context.DrawText(Text($"{layout.Clips.Count} clips · {layout.LaneCount} lanes", 10, DimTextBrush), new Point(10, chainRect.Top + 24));
        DrawButton(context, MuteRect(chainRect), "M", project.Main.Muted);

        for (var i = 0; i < project.AuxTracks.Count; i++)
        {
            var track = project.AuxTracks[i];
            var row = new Rect(0, AuxTop(layout, i), HeaderWidth, AuxRowHeight);
            context.DrawText(Text(track.Name, 12, TextBrush), new Point(10, row.Top + 4));
            context.DrawText(Text(track.Kind.ToString(), 10, DimTextBrush), new Point(10, row.Top + 21));
            DrawButton(context, MuteRect(row), "M", track.Muted);
            DrawButton(context, RemoveRect(row), "×", false);
        }
    }

    private static void DrawButton(DrawingContext context, Rect rect, string label, bool active)
    {
        context.DrawRectangle(active ? MutedBrush : ButtonBrush, null, rect, 3, 3);
        var text = Text(label, 11, TextBrush);
        context.DrawText(text, new Point(rect.Center.X - text.Width / 2, rect.Center.Y - text.Height / 2));
    }

    private Rect MuteRect(Rect header) => new(HeaderWidth - 50, header.Top + 6, 20, 18);

    private Rect RemoveRect(Rect header) => new(HeaderWidth - 26, header.Top + 6, 20, 18);

    private static FormattedText Text(string text, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, brush);

    public static string Format(TimeSpan time, bool precise = false)
    {
        var sign = time < TimeSpan.Zero ? "-" : "";
        time = time.Duration();
        var text = time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
        return sign + text + (precise ? $".{time.Milliseconds:000}" : $".{time.Milliseconds / 100}");
    }

    private static string FormatRuler(double seconds, double step)
    {
        var time = TimeSpan.FromSeconds(Math.Round(seconds, 3));
        var mmss = $"{(int)time.TotalMinutes}:{time.Seconds:00}";
        return step < 1 ? mmss + "." + (time.Milliseconds / 10).ToString("00", CultureInfo.InvariantCulture) : mmss;
    }

    private static string GainSuffix(double gainDb) =>
        Math.Abs(gainDb) < 0.05 ? "" : $"  {gainDb.ToString("+0.0;-0.0", CultureInfo.InvariantCulture)} dB";

    // ---- Hit testing -------------------------------------------------------------------------

    private enum HitKind { None, Ruler, Header, ChainLeft, ChainRight, ChainBody, ChainGhost, AuxLeft, AuxRight, AuxBody, Empty }

    private readonly record struct Hit(HitKind Kind, Guid ClipId = default, Guid TrackId = default, int Index = -1);

    private Hit HitTest(Point p)
    {
        var project = Project!;
        if (p.X < HeaderWidth)
            return new Hit(HitKind.Header);
        if (p.Y < RulerHeight)
            return new Hit(HitKind.Ruler);
        var layout = Layout();
        if (p.Y < ChainTop + ChainHeight(layout))
        {
            var lane = (int)((p.Y - ChainTop) / ChainLaneHeight);
            foreach (var c in layout.Clips)
            {
                if (c.Lane != lane)
                    continue;
                var edge = EdgeHit(p.X, X(c.Start), X(c.End));
                if (edge != 0)
                    return new Hit(edge < 0 ? HitKind.ChainLeft : HitKind.ChainRight, c.Clip.Id, Index: c.Index);
                if (p.X >= X(c.Start) && p.X < X(c.End))
                    return new Hit(HitKind.ChainBody, c.Clip.Id, Index: c.Index);
            }
            foreach (var c in layout.Clips)
                if (c.Lane == lane && p.X >= X(c.FootprintStart) && p.X < X(c.FootprintEnd))
                    return new Hit(HitKind.ChainGhost, c.Clip.Id, Index: c.Index);
            return new Hit(HitKind.Empty);
        }
        var row = AuxRowAt(layout, p.Y);
        if (row >= 0)
        {
            var track = project.AuxTracks[row];
            for (var i = track.Clips.Count - 1; i >= 0; i--)
            {
                var clip = track.Clips[i];
                var edge = EdgeHit(p.X, X(clip.Start), X(clip.End));
                if (edge != 0)
                    return new Hit(edge < 0 ? HitKind.AuxLeft : HitKind.AuxRight, clip.Id, track.Id);
                if (p.X >= X(clip.Start) && p.X < X(clip.End))
                    return new Hit(HitKind.AuxBody, clip.Id, track.Id);
            }
            return new Hit(HitKind.Empty, TrackId: track.Id);
        }
        return new Hit(HitKind.Empty);
    }

    /// <summary>-1 on the left edge, +1 on the right edge, 0 elsewhere.</summary>
    private int EdgeHit(double x, double left, double right)
    {
        var grab = Math.Min(EdgeGrabPixels, (right - left) / 3);
        if (Math.Abs(x - left) <= grab)
            return -1;
        if (Math.Abs(x - right) <= grab)
            return 1;
        return 0;
    }

    private int AuxRowAt(ChainLayout layout, double y)
    {
        var project = Project!;
        for (var i = 0; i < project.AuxTracks.Count; i++)
        {
            var top = AuxTop(layout, i);
            if (y >= top && y < top + AuxRowHeight)
                return i;
        }
        return -1;
    }

    // ---- Pointer -----------------------------------------------------------------------------

    private enum DragMode { Scrub, TrimChainLeft, TrimChainRight, ReorderPending, Reorder, TrimAuxLeft, TrimAuxRight, MoveAuxPending, MoveAux }

    private sealed class Drag
    {
        public required DragMode Mode;
        public required Guid ClipId;
        public required Point StartPoint;
        public required TimeSpan StartTime;
        public required Dictionary<Guid, int> FrozenLanes;
        public object Token { get; } = new();
        public TimeSpan OrigIn, OrigOut, OrigStart, OrigEnd, GrabOffset, CurrentTime;
        public int OrigIndex;
        public Guid TrackId;
        public List<TimeSpan> Targets = new();
        public TimeSpan? InsertTime;
        public int InsertIndex;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_session == null)
            return;
        Focus();
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;
        var p = point.Position;
        var hit = HitTest(p);
        var time = T(p.X);
        var layout = Layout();
        var project = _session.Project;

        switch (hit.Kind)
        {
            case HitKind.Header:
                HandleHeaderClick(p, layout);
                return;
            case HitKind.Ruler:
            case HitKind.Empty:
                if (hit.Kind == HitKind.Empty)
                    _session.SelectedClipId = null;
                StartDrag(e, DragMode.Scrub, Guid.Empty, p, time, layout);
                _session.Playhead = Clamp(time);
                return;
            case HitKind.ChainGhost:
                _session.SelectedClipId = hit.ClipId;
                return;
        }

        _session.SelectedClipId = hit.ClipId;
        var mode = hit.Kind switch
        {
            HitKind.ChainLeft => DragMode.TrimChainLeft,
            HitKind.ChainRight => DragMode.TrimChainRight,
            HitKind.ChainBody => DragMode.ReorderPending,
            HitKind.AuxLeft => DragMode.TrimAuxLeft,
            HitKind.AuxRight => DragMode.TrimAuxRight,
            _ => DragMode.MoveAuxPending,
        };
        var drag = StartDrag(e, mode, hit.ClipId, p, time, layout);
        drag.TrackId = hit.TrackId;

        if (mode is DragMode.TrimChainLeft or DragMode.TrimChainRight or DragMode.ReorderPending)
        {
            var c = layout.Clips[hit.Index];
            drag.OrigIn = c.Clip.In;
            drag.OrigOut = c.Clip.Out;
            drag.OrigStart = c.Start;
            drag.OrigEnd = c.End;
            drag.OrigIndex = c.Index;
            drag.GrabOffset = time - c.Start;
            // Edges that do not move during this drag: everything before the clip, aux clips, playhead.
            drag.Targets = SnapTargets.Collect(project, layout, _session.Playhead, other => other.Index < c.Index);
        }
        else
        {
            var clip = project.FindAuxClip(hit.ClipId)!.Value.Clip;
            drag.OrigIn = clip.In;
            drag.OrigOut = clip.Out;
            drag.OrigStart = clip.Start;
            drag.OrigEnd = clip.End;
            drag.GrabOffset = time - clip.Start;
            drag.Targets = SnapTargets.Collect(project, layout, _session.Playhead, null, other => other.Id != clip.Id);
        }
    }

    private Drag StartDrag(PointerPressedEventArgs e, DragMode mode, Guid clipId, Point p, TimeSpan time, ChainLayout layout)
    {
        _drag = new Drag
        {
            Mode = mode,
            ClipId = clipId,
            StartPoint = p,
            StartTime = time,
            CurrentTime = time,
            FrozenLanes = layout.LaneMap(),
        };
        e.Pointer.Capture(this);
        return _drag;
    }

    private void HandleHeaderClick(Point p, ChainLayout layout)
    {
        var project = _session!.Project;
        var chainRect = new Rect(0, ChainTop, HeaderWidth, ChainHeight(layout));
        if (MuteRect(chainRect).Contains(p))
        {
            _session.TryExecute(new SetTrackMuteCommand(null, !project.Main.Muted));
            return;
        }
        var row = AuxRowAt(layout, p.Y);
        if (row < 0)
            return;
        var track = project.AuxTracks[row];
        var rowRect = new Rect(0, AuxTop(layout, row), HeaderWidth, AuxRowHeight);
        if (MuteRect(rowRect).Contains(p))
            _session.TryExecute(new SetTrackMuteCommand(track.Id, !track.Muted));
        else if (RemoveRect(rowRect).Contains(p))
            _session.TryExecute(new RemoveAuxTrackCommand(track.Id));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_session == null)
            return;
        var p = e.GetPosition(this);
        if (_drag == null)
        {
            UpdateCursor(p);
            return;
        }
        var drag = _drag;
        var time = T(p.X);
        drag.CurrentTime = time;
        var delta = time - drag.StartTime;
        _snapLine = null;

        switch (drag.Mode)
        {
            case DragMode.Scrub:
                _session.Playhead = Clamp(time);
                break;

            case DragMode.TrimChainLeft:
            {
                // The edge stays attached to the predecessor, so the content moves instead of the
                // edge: the clip drifts away from the mouse. Snapping uses the virtual edge position.
                var snap = Snap(drag.OrigStart + delta, drag);
                _session.TryExecuteGesture(new TrimClipCommand(drag.ClipId, TrimEdge.Left, drag.OrigIn + (snap - drag.OrigStart)), drag.Token);
                break;
            }

            case DragMode.TrimChainRight:
            {
                var snap = Snap(drag.OrigEnd + delta, drag);
                _session.TryExecuteGesture(new TrimClipCommand(drag.ClipId, TrimEdge.Right, drag.OrigOut + (snap - drag.OrigEnd)), drag.Token);
                break;
            }

            case DragMode.TrimAuxLeft:
            {
                var snap = Snap(drag.OrigStart + delta, drag);
                _session.TryExecuteGesture(new TrimClipCommand(drag.ClipId, TrimEdge.Left, drag.OrigIn + (snap - drag.OrigStart)), drag.Token);
                break;
            }

            case DragMode.TrimAuxRight:
            {
                var snap = Snap(drag.OrigEnd + delta, drag);
                _session.TryExecuteGesture(new TrimClipCommand(drag.ClipId, TrimEdge.Right, drag.OrigOut + (snap - drag.OrigEnd)), drag.Token);
                break;
            }

            case DragMode.ReorderPending:
            case DragMode.MoveAuxPending:
                if (Math.Abs(p.X - drag.StartPoint.X) + Math.Abs(p.Y - drag.StartPoint.Y) >= DragStartPixels)
                    drag.Mode = drag.Mode == DragMode.ReorderPending ? DragMode.Reorder : DragMode.MoveAux;
                break;

            case DragMode.Reorder:
                UpdateReorderTarget(drag, time);
                break;

            case DragMode.MoveAux:
            {
                var start = drag.OrigStart + delta;
                if (start < TimeSpan.Zero)
                    start = TimeSpan.Zero;
                var snap = Snapper.SnapSpan(start, drag.OrigEnd - drag.OrigStart, drag.Targets, SnapThreshold);
                _snapLine = snap.Target;
                var layout = Layout();
                var row = AuxRowAt(layout, p.Y);
                Guid? target = row >= 0 ? _session.Project.AuxTracks[row].Id : null;
                _session.TryExecuteGesture(new MoveAuxClipCommand(drag.ClipId, snap.Value, target), drag.Token);
                break;
            }
        }
        InvalidateVisual();
    }

    private TimeSpan Snap(TimeSpan candidate, Drag drag)
    {
        var result = Snapper.Snap(candidate, drag.Targets, SnapThreshold);
        _snapLine = result.Target;
        return result.Value;
    }

    private void UpdateReorderTarget(Drag drag, TimeSpan time)
    {
        var layout = Layout();
        var others = layout.Clips.Where(c => c.Clip.Id != drag.ClipId).ToList();
        var dragged = layout.Find(drag.ClipId);
        // Centre of the dragged clip decides where it goes.
        var centre = time - drag.GrabOffset + (dragged?.Duration ?? TimeSpan.Zero) / 2;
        var index = others.Count(o => o.Start + o.Duration / 2 < centre);
        drag.InsertIndex = index;
        drag.InsertTime = index < others.Count ? others[index].Start : others.Count > 0 ? others[^1].End : TimeSpan.Zero;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        FinishDrag();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        FinishDrag();
    }

    private void FinishDrag()
    {
        var drag = _drag;
        if (drag == null || _session == null)
            return;
        _drag = null;
        _snapLine = null;
        if (drag.Mode == DragMode.Reorder && drag.InsertIndex != drag.OrigIndex)
            _session.TryExecute(new MoveChainClipCommand(drag.ClipId, drag.InsertIndex));
        _session.History.EndGesture();
        // Lanes were frozen during the drag; recompute now.
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void UpdateCursor(Point p)
    {
        if (Project == null)
            return;
        var hit = HitTest(p);
        Cursor = hit.Kind switch
        {
            HitKind.ChainLeft or HitKind.ChainRight or HitKind.AuxLeft or HitKind.AuxRight => new Cursor(StandardCursorType.SizeWestEast),
            HitKind.ChainBody or HitKind.AuxBody => new Cursor(StandardCursorType.Hand),
            _ => Cursor.Default,
        };
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ZoomBy(e.Delta.Y > 0 ? ZoomStep : 1 / ZoomStep, e.GetPosition(this).X);
            e.Handled = true;
        }
        else if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
            ScrollSeconds -= delta * 80 / _pixelsPerSecond;
            e.Handled = true;
        }
    }

    private TimeSpan Clamp(TimeSpan time)
    {
        var end = Project?.Duration ?? TimeSpan.Zero;
        return time < TimeSpan.Zero ? TimeSpan.Zero : time > end ? end : time;
    }

    // ---- Test hooks ----------------------------------------------------------------------------

    /// <summary>A point on a clip in control coordinates: edge -1 = just inside the left edge, +1 = right edge, 0 = centre.</summary>
    internal Point PointOnClip(Guid clipId, int edge)
    {
        var layout = Layout();
        double left, right, y;
        if (layout.Find(clipId) is { } chain)
        {
            left = X(chain.Start);
            right = X(chain.End);
            y = LaneTop(chain.Lane) + ChainLaneHeight / 2;
        }
        else
        {
            var (clip, track) = Project!.FindAuxClip(clipId) ?? throw new ArgumentException("Unknown clip", nameof(clipId));
            left = X(clip.Start);
            right = X(clip.End);
            y = AuxTop(layout, Project.AuxTracks.IndexOf(track)) + AuxRowHeight / 2;
        }
        var x = edge < 0 ? left + 1 : edge > 0 ? right - 1 : (left + right) / 2;
        return new Point(x, y);
    }

    /// <summary>Horizontal position of a timeline time in control coordinates.</summary>
    internal double XOf(TimeSpan time) => X(time);

    internal double RulerCentreY => RulerHeight / 2;

    internal TimeSpan? SnapLine => _snapLine;

    // ---- Drop from the file manager ----------------------------------------------------------

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (Project == null || !HasFiles(e))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Copy;
        var target = DropTargetAt(e.GetPosition(this));
        _dropLine = target switch
        {
            ImportTarget.Chain chain => ChainBoundary(chain.Index),
            ImportTarget.Aux aux => aux.Start,
            _ => null,
        };
        InvalidateVisual();
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        _dropLine = null;
        InvalidateVisual();
        if (_session == null)
            return;
        var paths = DroppedPaths(e);
        if (paths.Count == 0)
            return;
        var target = DropTargetAt(e.GetPosition(this));
        await _session.ImportAsync(paths, target);
    }

    private static bool HasFiles(DragEventArgs e) => e.DataTransfer.Contains(DataFormat.File);

    public static List<string> DroppedPaths(DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles();
        if (items == null)
            return new List<string>();
        var paths = new List<string>();
        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();
            if (path == null)
                continue;
            if (Directory.Exists(path))
                paths.AddRange(Directory.EnumerateFiles(path).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            else
                paths.Add(path);
        }
        return paths;
    }

    private ImportTarget DropTargetAt(Point p)
    {
        var project = Project!;
        var layout = Layout();
        var time = T(p.X);
        if (p.Y >= ChainTop && p.Y < ChainTop + ChainHeight(layout) && p.X >= HeaderWidth)
        {
            var index = layout.Clips.Count(c => c.Start + c.Duration / 2 < time);
            return new ImportTarget.Chain(index);
        }
        var row = AuxRowAt(layout, p.Y);
        if (row >= 0)
            return new ImportTarget.Aux(project.AuxTracks[row].Id, time < TimeSpan.Zero ? TimeSpan.Zero : time);
        return new ImportTarget.Auto(_session!.Playhead);
    }

    private TimeSpan ChainBoundary(int index)
    {
        var layout = Layout();
        return index < layout.Clips.Count ? layout.Clips[index].Start : layout.Duration;
    }
}
