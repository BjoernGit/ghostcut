using Ghostcut.Core.Audio;
using Ghostcut.Core.Commands;
using Ghostcut.Core.Model;
using static Ghostcut.Core.Tests.TestProject;

namespace Ghostcut.Core.Tests;

public class AudioTests
{
    /// <summary>PCM provider where each source is a constant level (DC), which makes envelopes easy to read.</summary>
    private sealed class ConstantPcm : IPcmProvider
    {
        private readonly Dictionary<Guid, IPcmData> _data = new();

        public void Add(MediaSource source, float level)
        {
            var frames = AudioFormat.ToFrames(source.Duration);
            var samples = new float[frames * AudioFormat.Channels];
            Array.Fill(samples, level);
            _data[source.Id] = new MemoryPcmData(samples);
        }

        public IPcmData? TryGet(Guid sourceId) => _data.GetValueOrDefault(sourceId);
    }

    private static float[] Render(Project project, ConstantPcm pcm, TimeSpan from, TimeSpan length)
    {
        var mixer = new AudioMixer(pcm);
        mixer.Update(project);
        var buffer = new float[AudioFormat.ToFrames(length) * AudioFormat.Channels];
        mixer.Mix(AudioFormat.ToFrames(from), buffer);
        return buffer.Where((_, i) => i % 2 == 0).ToArray(); // left channel
    }

    [Fact]
    public void A_butt_edge_with_overhang_material_crossfades_to_constant_level()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 2);   // right overhang available
        var b = t.AddChain(10, 1, 3);   // left overhang available
        var pcm = new ConstantPcm();
        foreach (var source in t.Project.Sources)
            pcm.Add(source, 0.5f);

        var left = Render(t.Project, pcm, S(1.9), S(0.2));

        // Equal-sum raised-cosine crossfade: the level never dips at the cut.
        Assert.All(left, v => Assert.InRange(v, 0.499f, 0.501f));
    }

    [Fact]
    public void A_crossfade_is_centred_on_the_cut_and_as_long_as_the_edge_fade()
    {
        var t = new TestProject();
        var a = t.AddChain(10, 0, 2);
        t.AddChain(10, 1, 3);

        var segments = AudioPlan.Build(t.Project);
        var cut = AudioFormat.ToFrames(S(2));
        var fade = AudioFormat.ToFrames(t.Project.Settings.AudioEdgeFade);

        Assert.Equal(cut + fade / 2, segments[0].PlayEnd);
        Assert.Equal(cut - fade / 2, segments[1].PlayStart);
        Assert.Equal(segments[0].FadeOutStart, segments[1].FadeInStart);
        Assert.Equal(segments[0].FadeOutEnd, segments[1].FadeInEnd);
    }

    [Fact]
    public void Without_overhang_material_the_clip_fades_to_zero_at_the_cut()
    {
        var t = new TestProject();
        t.AddChain(2, 0, 2);   // no right overhang
        t.AddChain(2, 0, 2);   // no left overhang
        var pcm = new ConstantPcm();
        foreach (var source in t.Project.Sources)
            pcm.Add(source, 1f);

        var segments = AudioPlan.Build(t.Project);
        var cut = AudioFormat.ToFrames(S(2));
        Assert.Equal(cut, segments[0].PlayEnd);
        Assert.Equal(cut, segments[1].PlayStart);

        var around = Render(t.Project, pcm, S(2) - TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
        Assert.All(around, v => Assert.True(v < 0.1f, $"level {v} at the cut"));
    }

    [Fact]
    public void Clip_edges_never_jump()
    {
        // A full-scale constant source is the worst case for clicks: any hard edge is a jump of 1.0.
        var t = new TestProject();
        t.AddChain(3, 0, 1);
        t.AddChain(3, 0.5, 2);
        t.AddChain(1, 0, 1);
        var music = t.AddTrack();
        t.AddAux(music, 5, 0, 1.7, start: 0.3);
        var pcm = new ConstantPcm();
        foreach (var source in t.Project.Sources)
            pcm.Add(source, 0.45f);

        var samples = Render(t.Project, pcm, S(0), t.Project.Duration + S(0.1));

        var maxStep = samples.Zip(samples.Skip(1), (x, y) => Math.Abs(y - x)).Max();
        Assert.True(maxStep < 0.01f, $"largest sample step {maxStep}");
    }

    [Fact]
    public void Gain_mute_and_fade_from_black_shape_the_level()
    {
        var t = new TestProject();
        var a = t.AddChain(4, 0, 4);
        var music = t.AddTrack();
        t.AddAux(music, 4, 0, 4, start: 0);
        var pcm = new ConstantPcm();
        foreach (var source in t.Project.Sources)
            pcm.Add(source, 0.25f);

        t.History.Execute(new SetClipGainCommand(a.Id, -6.0206)); // half
        t.History.Execute(new SetTrackMuteCommand(music.Id, true));
        Assert.InRange(Render(t.Project, pcm, S(2), S(0.01))[0], 0.124f, 0.126f);

        t.History.Execute(new SetClipFadesCommand(a.Id, S(1), TimeSpan.Zero));
        Assert.InRange(Render(t.Project, pcm, S(0.5), S(0.001))[0], 0.06f, 0.065f); // halfway through the fade

        t.History.Execute(new SetTrackMuteCommand(null, true));
        Assert.All(Render(t.Project, pcm, S(0), S(4)), v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Wav_header_matches_the_mix()
    {
        var t = new TestProject();
        t.AddChain(1, 0, 1);
        var pcm = new ConstantPcm();
        pcm.Add(t.Project.Sources[0], 0.5f);
        var mixer = new AudioMixer(pcm);
        mixer.Update(t.Project);
        using var stream = new MemoryStream();

        WavWriter.RenderMix(mixer, AudioFormat.SampleRate, stream);

        var bytes = stream.ToArray();
        Assert.Equal(44 + AudioFormat.SampleRate * 8, bytes.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(3, BitConverter.ToInt16(bytes, 20)); // IEEE float
        Assert.Equal(0.5f, BitConverter.ToSingle(bytes, 44 + 8 * 24000), 3);
    }
}
