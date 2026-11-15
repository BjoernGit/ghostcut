using System.Globalization;
using System.Text.Json;
using Ghostcut.Core.Commands;
using Ghostcut.Core.Model;
using Ghostcut.Core.Timeline;
using Ghostcut.Media;
using static Ghostcut.Core.Tests.TestProject;

namespace Ghostcut.Core.Tests;

/// <summary>
/// End-to-end: generated test media → probe → cut → export → ffprobe. Skipped when ffmpeg is not
/// installed. All media is created at runtime in the temp folder.
/// </summary>
public sealed class ExportIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ghostcut-it", Guid.NewGuid().ToString("N"));
    private readonly Ffmpeg? _ffmpeg = Ffmpeg.TryLocate();

    public ExportIntegrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // aevalsrc instead of sine: louder (0.5) and no forced zero crossings at round cut times.
    private static string Tone(int hz, double seconds) =>
        $"aevalsrc=0.5*sin(2*PI*{hz}*t):s=48000:d={seconds.ToString(CultureInfo.InvariantCulture)}";

    private async Task<string> MakeVideo(string name, double seconds, int toneHz)
    {
        var path = Path.Combine(_dir, name);
        await Ffmpeg.RunAsync(_ffmpeg!.FfmpegPath, new[]
        {
            "-v", "error", "-y",
            "-f", "lavfi", "-i", $"testsrc2=size=640x360:rate=30:duration={seconds.ToString(CultureInfo.InvariantCulture)}",
            "-f", "lavfi", "-i", Tone(toneHz, seconds),
            "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", path,
        });
        return path;
    }

    private async Task<string> MakeAudio(string name, double seconds, int toneHz)
    {
        var path = Path.Combine(_dir, name);
        await Ffmpeg.RunAsync(_ffmpeg!.FfmpegPath, new[]
        {
            "-v", "error", "-y", "-f", "lavfi",
            "-i", Tone(toneHz, seconds),
            path,
        });
        return path;
    }

    [SkippableTheory]
    [InlineData(10, false)]
    [InlineData(0, true)] // control: without edge fades the hard cuts must be detected as clicks
    public async Task Export_has_the_expected_duration_resolution_and_streams_and_no_clicks(int edgeFadeMs, bool expectClicks)
    {
        Skip.If(_ffmpeg == null, "ffmpeg not installed");

        var a = await MediaProbe.ProbeAsync(_ffmpeg!, await MakeVideo("a.mp4", 4, 437));
        var b = await MediaProbe.ProbeAsync(_ffmpeg!, await MakeVideo("b.mp4", 4, 661));
        var music = await MediaProbe.ProbeAsync(_ffmpeg!, await MakeAudio("music.wav", 3, 223));
        Assert.True(a.HasVideo && a.HasAudio);
        Assert.Equal(640, a.Width);
        Assert.Equal(30, a.FrameRate, 2);
        Assert.False(music.HasVideo);

        var t = new TestProject();
        t.Project.Settings.AudioEdgeFade = TimeSpan.FromMilliseconds(edgeFadeMs);
        var probed = new[] { a, b };
        t.History.Execute(ImportPlanner.Plan(t.Project, probed, new ImportTarget.Auto(TimeSpan.Zero))!);
        var clipA = t.Project.Main.Clips[0];
        var clipB = t.Project.Main.Clips[1];
        t.History.Execute(new TrimClipCommand(clipA.Id, TrimEdge.Left, S(0.53)));
        t.History.Execute(new TrimClipCommand(clipA.Id, TrimEdge.Right, S(2.53)));  // A: 2 s
        t.History.Execute(new TrimClipCommand(clipB.Id, TrimEdge.Right, S(3)));     // B: 3 s
        t.History.Execute(new SplitClipCommand(clipB.Id, S(3.5)));                  // B1 1.5 s, B2 1.5 s
        t.History.Execute(new MoveChainClipCommand(clipA.Id, 2));                   // B1 B2 A
        t.History.Execute(new SetClipFadesCommand(clipA.Id, TimeSpan.Zero, S(0.5)));
        var track = new AuxTrack { Kind = AuxTrackKind.Music, Name = "Music 1" };
        t.History.Execute(new AddAuxTrackCommand(track));
        t.History.Execute(ImportPlanner.Plan(t.Project, new[] { music }, new ImportTarget.Aux(track.Id, S(1)))!);
        t.History.Execute(new SetClipGainCommand(track.Clips[0].Id, -12));
        Assert.InRange(t.Project.ChainDuration.TotalSeconds, 4.999, 5.001);

        var output = Path.Combine(_dir, "export.mp4");
        using var pcm = new PcmLibrary(_ffmpeg!, Path.Combine(_dir, "pcm"));
        var reports = new List<double>();
        await Exporter.ExportAsync(_ffmpeg!, t.Project, pcm, ExportPreset.Draft720p30, output, new SyncProgress(reports.Add));

        var info = JsonDocument.Parse(await Ffmpeg.RunAsync(_ffmpeg!.FfprobePath, new[]
        {
            "-v", "error", "-print_format", "json", "-show_format", "-show_streams", output,
        })).RootElement;
        var duration = double.Parse(info.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        var streams = info.GetProperty("streams").EnumerateArray().ToList();
        var video = streams.Single(s => s.GetProperty("codec_type").GetString() == "video");
        var audio = streams.Single(s => s.GetProperty("codec_type").GetString() == "audio");

        Assert.InRange(duration, 4.95, 5.1);
        Assert.Equal("h264", video.GetProperty("codec_name").GetString());
        Assert.Equal(1280, video.GetProperty("width").GetInt32());
        Assert.Equal(720, video.GetProperty("height").GetInt32());
        Assert.Equal("30/1", video.GetProperty("avg_frame_rate").GetString());
        Assert.Equal(150, int.Parse(video.GetProperty("nb_frames").GetString()!, CultureInfo.InvariantCulture));
        Assert.Equal("aac", audio.GetProperty("codec_name").GetString());
        Assert.Equal("48000", audio.GetProperty("sample_rate").GetString());
        Assert.Equal(2, audio.GetProperty("channels").GetInt32());
        Assert.Equal(1.0, reports.Last(), 3);
        Assert.True(reports.Zip(reports.Skip(1), (x, y) => y >= x - 1e-9).All(ok => ok), "progress must not go backwards");

        // No clicks: decode the exported sound and look for jumps. Sines of amplitude 0.5 at ≤661 Hz move at most
        // ~0.045 per sample; a hard cut between unrelated phases jumps up to ~1.
        var raw = Path.Combine(_dir, "export.f32");
        await Ffmpeg.RunAsync(_ffmpeg!.FfmpegPath, new[] { "-v", "error", "-y", "-i", output, "-ac", "1", "-ar", "48000", "-f", "f32le", raw });
        var bytes = await File.ReadAllBytesAsync(raw);
        var samples = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 4);
        var maxStep = 0f;
        for (var i = 1; i < samples.Length; i++)
            maxStep = Math.Max(maxStep, Math.Abs(samples[i] - samples[i - 1]));
        Assert.True((maxStep >= 0.15f) == expectClicks, $"largest sample step {maxStep} (fade {edgeFadeMs} ms)");
    }

    [SkippableFact]
    public async Task A_frame_can_be_decoded_for_the_preview()
    {
        Skip.If(_ffmpeg == null, "ffmpeg not installed");
        var path = await MakeVideo("p.mp4", 2, 440);
        var size = new FrameSize(320, 180);

        var frame = await FrameDecoder.DecodeFrameAsync(_ffmpeg!, path, S(1), size);
        Assert.NotNull(frame);
        Assert.Equal(size.ByteCount, frame!.Length);

        using var stream = FrameStream.Start(_ffmpeg!, path, S(0.5), S(1), 30, size);
        var buffer = new byte[size.ByteCount];
        var count = 0;
        while (stream.ReadFrame(buffer))
            count++;
        Assert.InRange(count, 29, 31);
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        private readonly object _lock = new();

        public void Report(double value)
        {
            lock (_lock)
                report(value);
        }
    }
}
