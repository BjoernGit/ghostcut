using Ghostcut.Core.Model;
using Ghostcut.Core.Timeline;

namespace Ghostcut.Core.Audio;

/// <summary>Internal mixing format: 48 kHz, stereo, 32-bit float, interleaved.</summary>
public static class AudioFormat
{
    public const int SampleRate = 48000;
    public const int Channels = 2;

    public static long ToFrames(TimeSpan time) => (long)Math.Round(time.Ticks * (double)SampleRate / TimeSpan.TicksPerSecond);

    public static TimeSpan ToTime(long frames) => TimeSpan.FromTicks((long)Math.Round(frames * (double)TimeSpan.TicksPerSecond / SampleRate));
}

/// <summary>
/// One clip's contribution to the mix, in timeline sample frames. The gain envelope is the product
/// of the anti-click edge fades and (for chain clips) the fade from/to black.
/// </summary>
public sealed record AudioSegment(
    Guid SourceId,
    Guid ClipId,
    long SourceOffset,
    long PlayStart,
    long PlayEnd,
    long FadeInStart,
    long FadeInEnd,
    long FadeOutStart,
    long FadeOutEnd,
    long BlackInStart,
    long BlackInEnd,
    long BlackOutStart,
    long BlackOutEnd,
    float Gain)
{
    /// <summary>Source frame that plays at a timeline frame.</summary>
    public long ToSourceFrame(long timelineFrame) => timelineFrame + SourceOffset;

    public float EnvelopeAt(long frame)
    {
        if (frame < PlayStart || frame >= PlayEnd)
            return 0;
        var env = Gain;
        env *= RampUp(frame, FadeInStart, FadeInEnd);
        env *= RampDown(frame, FadeOutStart, FadeOutEnd);
        env *= RampUp(frame, BlackInStart, BlackInEnd);
        env *= RampDown(frame, BlackOutStart, BlackOutEnd);
        return env;
    }

    /// <summary>Raised-cosine ramp 0→1 over [start, end). In- and out-ramps over the same window sum to 1.</summary>
    internal static float RampUp(long frame, long start, long end)
    {
        if (end <= start || frame >= end)
            return 1;
        if (frame < start)
            return 0;
        var x = (frame - start + 0.5) / (end - start);
        var s = Math.Sin(x * Math.PI / 2);
        return (float)(s * s);
    }

    /// <summary>Ramp 1→0 over [start, end); an empty window means "no fade" (always 1).</summary>
    internal static float RampDown(long frame, long start, long end) =>
        end <= start ? 1 : 1 - RampUp(frame, start, end);
}

/// <summary>
/// Turns the project into audio segments. Rules for the automatic edge fades (length
/// <see cref="ProjectSettings.AudioEdgeFade"/>):
/// <list type="bullet">
/// <item>At a butt edge (two clips meet), each side reaches up to half a fade into its overhang
/// material, so the two clips crossfade centred on the cut.</item>
/// <item>Without overhang material, or at a free edge, the clip fades to/from zero inside itself.</item>
/// </list>
/// </summary>
public static class AudioPlan
{
    /// <summary>Two aux clip edges closer than this count as a butt edge.</summary>
    public static readonly TimeSpan ButtTolerance = TimeSpan.FromMilliseconds(1);

    public static List<AudioSegment> Build(Project project)
    {
        var segments = new List<AudioSegment>();
        var fade = AudioFormat.ToFrames(project.Settings.AudioEdgeFade);

        if (!project.Main.Muted)
        {
            var layout = ChainLayout.Compute(project);
            for (var i = 0; i < layout.Clips.Count; i++)
            {
                var c = layout.Clips[i];
                var source = project.FindSource(c.Clip.SourceId);
                if (source is not { HasAudio: true })
                    continue;
                var durationTicks = c.Duration.Ticks;
                var blackIn = Math.Min(c.Clip.FadeInFromBlack.Ticks, durationTicks);
                var blackOut = Math.Min(c.Clip.FadeOutToBlack.Ticks, durationTicks - blackIn);
                segments.Add(Make(
                    c.Clip.SourceId, c.Clip.Id, c.Start, c.End, c.Clip.In, c.LeftOverhang, c.RightOverhang,
                    buttLeft: i > 0, buttRight: i < layout.Clips.Count - 1, fade, c.Clip.GainDb,
                    blackInTicks: blackIn, blackOutTicks: blackOut));
            }
        }

        foreach (var track in project.AuxTracks)
        {
            if (track.Muted)
                continue;
            foreach (var clip in track.Clips)
            {
                var source = project.FindSource(clip.SourceId);
                if (source is not { HasAudio: true })
                    continue;
                var buttLeft = track.Clips.Any(o => !ReferenceEquals(o, clip) && (o.End - clip.Start).Duration() <= ButtTolerance);
                var buttRight = track.Clips.Any(o => !ReferenceEquals(o, clip) && (o.Start - clip.End).Duration() <= ButtTolerance);
                segments.Add(Make(
                    clip.SourceId, clip.Id, clip.Start, clip.End, clip.In, clip.In, source.Duration - clip.Out,
                    buttLeft, buttRight, fade, clip.GainDb, 0, 0));
            }
        }

        return segments;
    }

    private static AudioSegment Make(
        Guid sourceId, Guid clipId, TimeSpan start, TimeSpan end, TimeSpan sourceIn,
        TimeSpan leftOverhang, TimeSpan rightOverhang, bool buttLeft, bool buttRight,
        long fade, double gainDb, long blackInTicks, long blackOutTicks)
    {
        var s = AudioFormat.ToFrames(start);
        var e = AudioFormat.ToFrames(end);
        var half = fade / 2;

        long fadeInStart, fadeInEnd, playStart;
        if (buttLeft)
        {
            var reach = Math.Min(half, AudioFormat.ToFrames(leftOverhang));
            playStart = s - reach;
            fadeInStart = playStart;
            fadeInEnd = playStart + fade;
        }
        else
        {
            playStart = s;
            fadeInStart = s;
            fadeInEnd = s + fade;
        }

        long fadeOutStart, fadeOutEnd, playEnd;
        if (buttRight)
        {
            var reach = Math.Min(half, AudioFormat.ToFrames(rightOverhang));
            playEnd = e + reach;
            fadeOutEnd = playEnd;
            fadeOutStart = playEnd - fade;
        }
        else
        {
            playEnd = e;
            fadeOutEnd = e;
            fadeOutStart = e - fade;
        }

        var blackIn = AudioFormat.ToFrames(TimeSpan.FromTicks(blackInTicks));
        var blackOut = AudioFormat.ToFrames(TimeSpan.FromTicks(blackOutTicks));

        return new AudioSegment(
            sourceId, clipId,
            SourceOffset: AudioFormat.ToFrames(sourceIn) - s,
            PlayStart: Math.Max(0, playStart),
            PlayEnd: playEnd,
            FadeInStart: fadeInStart,
            FadeInEnd: fadeInEnd,
            FadeOutStart: fadeOutStart,
            FadeOutEnd: fadeOutEnd,
            BlackInStart: s,
            BlackInEnd: s + blackIn,
            BlackOutStart: e - blackOut,
            BlackOutEnd: e,
            Gain: (float)EditLimits.DbToLinear(gainDb));
    }
}
