using System.Diagnostics;

namespace Ghostcut.Media;

/// <summary>Size of decoded preview frames; all frames are letterboxed to it (BGRA, 4 bytes per pixel).</summary>
public readonly record struct FrameSize(int Width, int Height)
{
    public int ByteCount => Width * Height * 4;

    internal string ScaleFilter =>
        $"scale={Width}:{Height}:force_original_aspect_ratio=decrease:flags=bilinear," +
        $"pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2:color=black,format=bgra";
}

/// <summary>Decodes preview frames by piping raw BGRA from ffmpeg.</summary>
public static class FrameDecoder
{
    /// <summary>Decodes the frame at <paramref name="sourceTime"/>; null if there is none (e.g. past the end).</summary>
    public static async Task<byte[]?> DecodeFrameAsync(Ffmpeg ffmpeg, string path, TimeSpan sourceTime, FrameSize size, CancellationToken cancel = default)
    {
        var args = new[]
        {
            "-v", "error", "-nostdin", "-ss", Ffmpeg.Seconds(sourceTime), "-i", path,
            "-an", "-sn", "-dn", "-frames:v", "1", "-vf", size.ScaleFilter, "-f", "rawvideo", "pipe:1",
        };
        using var process = new Process { StartInfo = Ffmpeg.StartInfo(ffmpeg.FfmpegPath, args) };
        process.Start();
        using var registration = cancel.Register(() => Ffmpeg.TryKill(process));
        _ = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var buffer = new byte[size.ByteCount];
        var read = await ReadFullyAsync(process.StandardOutput.BaseStream, buffer, cancel).ConfigureAwait(false);
        await process.WaitForExitAsync(cancel).ConfigureAwait(false);
        return read == buffer.Length ? buffer : null;
    }

    internal static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken cancel)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), cancel).ConfigureAwait(false);
            if (n == 0)
                break;
            total += n;
        }
        return total;
    }
}

/// <summary>
/// Streams consecutive frames of a source range at a fixed frame rate (for playback). Frame k
/// shows source time <c>start + k / fps</c>.
/// </summary>
public sealed class FrameStream : IDisposable
{
    private readonly Process _process;
    private readonly Stream _output;

    private FrameStream(Process process)
    {
        _process = process;
        _output = process.StandardOutput.BaseStream;
        _ = process.StandardError.ReadToEndAsync(CancellationToken.None);
    }

    public static FrameStream Start(Ffmpeg ffmpeg, string path, TimeSpan sourceStart, TimeSpan duration, int fps, FrameSize size)
    {
        var args = new[]
        {
            "-v", "error", "-nostdin", "-ss", Ffmpeg.Seconds(sourceStart), "-i", path, "-t", Ffmpeg.Seconds(duration),
            "-an", "-sn", "-dn", "-vf", $"fps={fps}," + size.ScaleFilter, "-f", "rawvideo", "pipe:1",
        };
        var process = new Process { StartInfo = Ffmpeg.StartInfo(ffmpeg.FfmpegPath, args) };
        process.Start();
        return new FrameStream(process);
    }

    /// <summary>Reads the next frame into <paramref name="buffer"/>; false at the end of the range.</summary>
    public bool ReadFrame(byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = _output.Read(buffer, total, buffer.Length - total);
            if (n == 0)
                return false;
            total += n;
        }
        return true;
    }

    public void Dispose()
    {
        Ffmpeg.TryKill(_process);
        _process.Dispose();
    }
}
