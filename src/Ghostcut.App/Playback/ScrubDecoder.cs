using Avalonia.Threading;
using Ghostcut.Media;

namespace Ghostcut.App.Playback;

/// <summary>
/// Decodes single frames for scrubbing. Only one decode runs at a time; while it runs, newer requests
/// replace each other and only the latest is decoded next ("latest wins"), so dragging the playhead
/// never builds up a backlog.
/// </summary>
public sealed class ScrubDecoder(Ffmpeg ffmpeg, FrameSize size)
{
    private (string Path, TimeSpan Time)? _pending;
    private (string Path, TimeSpan Time)? _shown;
    private bool _running;

    /// <summary>Raised on the UI thread with the decoded pixels.</summary>
    public event Action<byte[]>? FrameReady;

    public void Request(string path, TimeSpan sourceTime)
    {
        var request = (path, sourceTime);
        if (_shown == request && !_running)
            return;
        _pending = request;
        if (!_running)
            _ = RunAsync();
    }

    /// <summary>Forget the last shown frame (e.g. after playback replaced the picture).</summary>
    public void Invalidate() => _shown = null;

    private async Task RunAsync()
    {
        _running = true;
        try
        {
            while (_pending is { } next)
            {
                _pending = null;
                byte[]? pixels = null;
                try
                {
                    pixels = await FrameDecoder.DecodeFrameAsync(ffmpeg, next.Path, next.Time, size);
                    // Past the last frame of a file ffmpeg returns nothing; step back a little.
                    if (pixels == null && next.Time > TimeSpan.FromSeconds(0.1))
                        pixels = await FrameDecoder.DecodeFrameAsync(ffmpeg, next.Path, next.Time - TimeSpan.FromSeconds(0.1), size);
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
                if (pixels != null)
                {
                    _shown = next;
                    Dispatcher.UIThread.Post(() => FrameReady?.Invoke(pixels));
                }
            }
        }
        finally
        {
            _running = false;
        }
    }
}
