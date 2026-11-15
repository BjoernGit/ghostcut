using System.Globalization;
using System.Text.Json;
using Ghostcut.Core.Model;

namespace Ghostcut.Media;

/// <summary>Reads media metadata with ffprobe.</summary>
public static class MediaProbe
{
    public static readonly string[] VideoExtensions = { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".wmv" };
    public static readonly string[] AudioExtensions = { ".wav", ".mp3", ".flac", ".ogg", ".m4a", ".aac", ".opus", ".wma" };

    public static bool IsSupportedFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return VideoExtensions.Contains(ext) || AudioExtensions.Contains(ext);
    }

    public static async Task<MediaSource> ProbeAsync(Ffmpeg ffmpeg, string path, CancellationToken cancel = default)
    {
        var json = await Ffmpeg.RunAsync(ffmpeg.FfprobePath, new[]
        {
            "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path,
        }, cancel).ConfigureAwait(false);
        return Parse(json, Path.GetFullPath(path));
    }

    /// <summary>Parses ffprobe's JSON output (separate for tests).</summary>
    public static MediaSource Parse(string ffprobeJson, string path)
    {
        using var doc = JsonDocument.Parse(ffprobeJson);
        var root = doc.RootElement;
        var source = new MediaSource { Path = path };
        double duration = 0;
        if (root.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var fd))
            duration = ParseDouble(fd.GetString());

        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var type = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString() : null;
                if (type == "video" && !source.HasVideo && !IsCoverArt(stream))
                {
                    source.HasVideo = true;
                    source.Width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                    source.Height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                    source.FrameRate = ParseRate(stream.TryGetProperty("avg_frame_rate", out var r) ? r.GetString() : null);
                    if (source.FrameRate <= 0)
                        source.FrameRate = ParseRate(stream.TryGetProperty("r_frame_rate", out var rr) ? rr.GetString() : null);
                    if (duration <= 0 && stream.TryGetProperty("duration", out var sd))
                        duration = ParseDouble(sd.GetString());
                }
                else if (type == "audio" && !source.HasAudio)
                {
                    source.HasAudio = true;
                    if (duration <= 0 && stream.TryGetProperty("duration", out var sd))
                        duration = ParseDouble(sd.GetString());
                }
            }
        }

        if (!source.HasVideo && !source.HasAudio)
            throw new FfmpegException($"{Path.GetFileName(path)} has neither picture nor sound.");
        if (duration <= 0)
            throw new FfmpegException($"{Path.GetFileName(path)} has no readable duration.");
        source.Duration = TimeSpan.FromSeconds(duration);
        return source;
    }

    private static bool IsCoverArt(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var a) && a.GetInt32() == 1;

    private static double ParseDouble(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double ParseRate(string? rate)
    {
        if (string.IsNullOrEmpty(rate))
            return 0;
        var parts = rate.Split('/');
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0)
            return n / d;
        return ParseDouble(rate);
    }
}
