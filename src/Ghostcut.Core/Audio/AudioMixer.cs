using Ghostcut.Core.Model;

namespace Ghostcut.Core.Audio;

/// <summary>Decoded audio of one source in <see cref="AudioFormat"/>.</summary>
public interface IPcmData
{
    long FrameCount { get; }

    /// <summary>
    /// Copies interleaved stereo frames starting at <paramref name="startFrame"/> into
    /// <paramref name="destination"/>; frames outside the data are silence.
    /// </summary>
    void Read(long startFrame, Span<float> destination);
}

/// <summary>Gives the mixer access to decoded audio; a source that is not decoded yet stays silent.</summary>
public interface IPcmProvider
{
    IPcmData? TryGet(Guid sourceId);
}

/// <summary>In-memory PCM, used by tests and for short material.</summary>
public sealed class MemoryPcmData(float[] interleaved) : IPcmData
{
    public float[] Samples { get; } = interleaved;

    public long FrameCount => Samples.Length / AudioFormat.Channels;

    public void Read(long startFrame, Span<float> destination)
    {
        destination.Clear();
        var frames = destination.Length / AudioFormat.Channels;
        var from = Math.Max(0, startFrame);
        var to = Math.Min(FrameCount, startFrame + frames);
        if (to <= from)
            return;
        var src = Samples.AsSpan((int)(from * AudioFormat.Channels), (int)((to - from) * AudioFormat.Channels));
        src.CopyTo(destination[(int)((from - startFrame) * AudioFormat.Channels)..]);
    }
}

/// <summary>
/// Renders the timeline's audio. Used both by the preview and by the export, so both sound the
/// same. <see cref="Update"/> may be called from the UI thread while <see cref="Mix"/> runs on the
/// audio thread: the plan is swapped atomically.
/// </summary>
public sealed class AudioMixer(IPcmProvider pcm)
{
    private volatile IReadOnlyList<AudioSegment> _segments = Array.Empty<AudioSegment>();
    private float[] _scratch = new float[4096];

    public IReadOnlyList<AudioSegment> Segments => _segments;

    public void Update(Project project) => _segments = AudioPlan.Build(project);

    /// <summary>
    /// Mixes interleaved stereo frames starting at <paramref name="timelineFrame"/>. The result is
    /// clamped to [-1, 1].
    /// </summary>
    public void Mix(long timelineFrame, Span<float> destination)
    {
        destination.Clear();
        var frames = destination.Length / AudioFormat.Channels;
        var end = timelineFrame + frames;
        foreach (var segment in _segments)
        {
            var from = Math.Max(timelineFrame, segment.PlayStart);
            var to = Math.Min(end, segment.PlayEnd);
            if (to <= from)
                continue;
            var data = pcm.TryGet(segment.SourceId);
            if (data == null)
                continue;
            var count = (int)(to - from);
            var needed = count * AudioFormat.Channels;
            if (_scratch.Length < needed)
                _scratch = new float[needed];
            var scratch = _scratch.AsSpan(0, needed);
            data.Read(segment.ToSourceFrame(from), scratch);
            var offset = (int)(from - timelineFrame) * AudioFormat.Channels;
            for (var i = 0; i < count; i++)
            {
                var gain = segment.EnvelopeAt(from + i);
                if (gain == 0)
                    continue;
                var d = offset + i * AudioFormat.Channels;
                var s = i * AudioFormat.Channels;
                destination[d] += scratch[s] * gain;
                destination[d + 1] += scratch[s + 1] * gain;
            }
        }
        for (var i = 0; i < destination.Length; i++)
            destination[i] = Math.Clamp(destination[i], -1f, 1f);
    }
}
