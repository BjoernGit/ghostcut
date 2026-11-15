using System.Diagnostics;
using System.Globalization;
using System.Text;
using Ghostcut.Core.Audio;
using Ghostcut.Core.Model;
using Ghostcut.Core.Timeline;

namespace Ghostcut.Media;

/// <summary>An MP4 export setting (H.264 + AAC).</summary>
public sealed record ExportPreset(
    string Name,
    int Width,
    int Height,
    int FrameRate,
    IReadOnlyList<string> VideoArgs,
    string AudioBitrate)
{
    /// <summary>
    /// Steam store trailer. Steam asks for up to 1920x1080, 16:9, 30 or 60 fps, H.264 with a high bit
    /// rate (5,000+ kbps) and AAC at 44.1/48 kHz; it transcodes the upload itself, so we give it
    /// generous headroom: 16 Mbit/s VBR.
    /// </summary>
    public static readonly ExportPreset SteamTrailer = new(
        "Steam Trailer (1080p60, 16 Mbit/s)", 1920, 1080, 60,
        new[] { "-c:v", "libx264", "-preset", "slow", "-profile:v", "high", "-b:v", "16M", "-maxrate", "24M", "-bufsize", "32M" },
        "256k");

    public static readonly ExportPreset Hd1080p60 = new(
        "1080p60", 1920, 1080, 60,
        new[] { "-c:v", "libx264", "-preset", "medium", "-crf", "18" },
        "192k");

    public static readonly ExportPreset Hd1080p30 = new(
        "1080p30", 1920, 1080, 30,
        new[] { "-c:v", "libx264", "-preset", "medium", "-crf", "18" },
        "192k");

    /// <summary>Fast low-quality export to check a cut.</summary>
    public static readonly ExportPreset Draft720p30 = new(
        "Draft 720p30 (fast)", 1280, 720, 30,
        new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "26" },
        "128k");

    public static IReadOnlyList<ExportPreset> All { get; } = new[] { SteamTrailer, Hd1080p60, Hd1080p30, Draft720p30 };

    public override string ToString() => Name;
}

/// <summary>
/// Builds the ffmpeg call for an export. The picture is cut by ffmpeg (one input per chain clip,
/// frame-exact lengths, fades to black); the sound is the timeline mix rendered by our own
/// <see cref="AudioMixer"/> into a WAV, so the export sounds exactly like the preview.
/// </summary>
public static class ExportPlan
{
    public sealed record Result(IReadOnlyList<string> Arguments, string FilterGraph, TimeSpan Duration, long FrameCount);

    /// <param name="filterScriptPath">File the filter graph is written to (keeps the command line short).</param>
    public static Result Build(Project project, ExportPreset preset, string wavPath, string outputPath, string filterScriptPath)
    {
        var layout = ChainLayout.Compute(project);
        if (layout.Clips.Count == 0)
            throw new InvalidOperationException("The main track is empty — there is no picture to export.");

        var fps = preset.FrameRate;
        var args = new List<string> { "-v", "error", "-nostdin", "-y" };
        var graph = new StringBuilder();
        var labels = new List<string>();
        long chainFrames = 0;

        foreach (var clip in layout.Clips)
        {
            var startFrame = ToFrame(clip.Start, fps);
            var endFrame = ToFrame(clip.End, fps);
            var frames = endFrame - startFrame;
            if (frames <= 0)
                continue;
            var source = project.GetSource(clip.Clip.SourceId);
            var input = labels.Count;
            // Input seeking is fast and frame accurate; a little extra material covers rounding,
            // tpad covers a source that ends early.
            args.AddRange(new[] { "-ss", Ffmpeg.Seconds(clip.Clip.In), "-t", Ffmpeg.Seconds(clip.Duration + TimeSpan.FromSeconds(1)), "-i", source.Path });

            var seconds = frames / (double)fps;
            var fadeIn = Math.Min(clip.Clip.FadeInFromBlack.TotalSeconds, seconds);
            var fadeOut = Math.Min(clip.Clip.FadeOutToBlack.TotalSeconds, seconds - fadeIn);
            var label = $"v{input}";
            graph.Append(CultureInfo.InvariantCulture,
                $"[{input}:v:0]setpts=PTS-STARTPTS,fps={fps},scale={preset.Width}:{preset.Height}:force_original_aspect_ratio=decrease," +
                $"pad={preset.Width}:{preset.Height}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,format=yuv420p," +
                $"tpad=stop_mode=clone:stop_duration=2,trim=end_frame={frames},setpts=PTS-STARTPTS");
            if (fadeIn > 0)
                graph.Append(CultureInfo.InvariantCulture, $",fade=t=in:st=0:d={fadeIn:0.######}:color=black");
            if (fadeOut > 0)
                graph.Append(CultureInfo.InvariantCulture, $",fade=t=out:st={seconds - fadeOut:0.######}:d={fadeOut:0.######}:color=black");
            graph.Append($"[{label}];\n");
            labels.Add(label);
            chainFrames += frames;
        }

        var totalFrames = Math.Max(chainFrames, ToFrame(project.Duration, fps));
        foreach (var label in labels)
            graph.Append('[').Append(label).Append(']');
        graph.Append(CultureInfo.InvariantCulture, $"concat=n={labels.Count}:v=1:a=0[vc];\n");
        if (totalFrames > chainFrames)
            graph.Append(CultureInfo.InvariantCulture, $"[vc]tpad=stop_mode=add:stop={totalFrames - chainFrames}:color=black[vout]");
        else
            graph.Append("[vc]null[vout]");

        var duration = TimeSpan.FromSeconds(totalFrames / (double)fps);
        var audioInput = labels.Count;
        args.AddRange(new[] { "-i", wavPath });
        args.AddRange(new[] { "-/filter_complex", filterScriptPath });
        args.AddRange(new[] { "-map", "[vout]", "-map", $"{audioInput}:a:0" });
        args.AddRange(preset.VideoArgs);
        args.AddRange(new[] { "-pix_fmt", "yuv420p", "-r", fps.ToString(CultureInfo.InvariantCulture) });
        args.AddRange(new[] { "-c:a", "aac", "-b:a", preset.AudioBitrate, "-ar", AudioFormat.SampleRate.ToString(CultureInfo.InvariantCulture), "-ac", "2" });
        args.AddRange(new[] { "-t", Ffmpeg.Seconds(duration), "-movflags", "+faststart" });
        args.AddRange(new[] { "-progress", "pipe:1", "-nostats", outputPath });
        return new Result(args, graph.ToString(), duration, totalFrames);
    }

    private static long ToFrame(TimeSpan time, int fps) => (long)Math.Round(time.TotalSeconds * fps, MidpointRounding.AwayFromZero);
}

/// <summary>Runs an export: mix the sound, then let ffmpeg cut and encode.</summary>
public static class Exporter
{
    /// <summary>Share of the progress bar used by the audio mix.</summary>
    private const double AudioShare = 0.05;

    public static async Task ExportAsync(
        Ffmpeg ffmpeg, Project project, PcmLibrary pcm, ExportPreset preset, string outputPath,
        IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        // Make sure all sound is decoded, otherwise the mix would contain silence.
        await Task.WhenAll(project.Sources.Where(s => s.HasAudio).Select(s => pcm.EnsureAsync(s, cancel))).ConfigureAwait(false);

        var workDir = Path.Combine(Path.GetTempPath(), "ghostcut-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var wav = Path.Combine(workDir, "mix.wav");
            var script = Path.Combine(workDir, "graph.txt");
            var plan = ExportPlan.Build(project, preset, wav, outputPath, script);
            await File.WriteAllTextAsync(script, plan.FilterGraph, cancel).ConfigureAwait(false);

            var mixer = new AudioMixer(pcm);
            mixer.Update(project);
            await Task.Run(() =>
            {
                using var stream = new FileStream(wav, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                WavWriter.RenderMix(mixer, AudioFormat.ToFrames(plan.Duration), stream,
                    new Progress<double>(p => progress?.Report(p * AudioShare)), cancel);
            }, cancel).ConfigureAwait(false);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            await RunFfmpegAsync(ffmpeg, plan, progress, cancel).ConfigureAwait(false);
            progress?.Report(1);
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task RunFfmpegAsync(Ffmpeg ffmpeg, ExportPlan.Result plan, IProgress<double>? progress, CancellationToken cancel)
    {
        using var process = new Process { StartInfo = Ffmpeg.StartInfo(ffmpeg.FfmpegPath, plan.Arguments) };
        process.Start();
        using var registration = cancel.Register(() => Ffmpeg.TryKill(process));
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false)) != null)
        {
            // ffmpeg reports out_time_us (and the misnamed out_time_ms, also in microseconds).
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
                && long.TryParse(line.AsSpan("out_time_us=".Length), out var us) && plan.Duration > TimeSpan.Zero)
            {
                var fraction = Math.Clamp(us / 1e6 / plan.Duration.TotalSeconds, 0, 1);
                progress?.Report(AudioShare + fraction * (1 - AudioShare));
            }
        }
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        cancel.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new FfmpegException($"Export failed (exit {process.ExitCode}): {Ffmpeg.LastLines(await stderr.ConfigureAwait(false), 8)}");
    }
}
