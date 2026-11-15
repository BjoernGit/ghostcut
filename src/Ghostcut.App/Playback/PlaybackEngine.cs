using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Ghostcut.Core.Audio;
using Ghostcut.Core.Model;
using Ghostcut.Core.Timeline;
using Ghostcut.Media;
using NAudio.Wave;

namespace Ghostcut.App.Playback;

/// <summary>A decoded preview frame at a timeline time.</summary>
public sealed class VideoFrame(TimeSpan time, byte[] pixels)
{
    public TimeSpan Time { get; } = time;

    public byte[] Pixels { get; } = pixels;
}

/// <summary>
/// Real-time preview: the sound plays through NAudio from the same <see cref="AudioMixer"/> the export
/// uses and is the master clock; a background pump decodes the picture ahead of the clock.
/// </summary>
public sealed class PlaybackEngine : IDisposable
{
    public static readonly FrameSize PreviewSize = new(960, 540);

    /// <summary>Frame rate of the preview picture (export uses the preset's rate).</summary>
    public const int PreviewFrameRate = 30;

    /// <summary>Frames the picture pump may decode ahead of the clock.</summary>
    private const int FrameQueueLength = 20;

    private readonly AudioMixer _mixer;
    private readonly Ffmpeg? _ffmpeg;
    private readonly ConcurrentBag<byte[]> _pool = new();
    private WaveOut? _output;
    private MixSampleProvider? _provider;
    private readonly Stopwatch _fallbackClock = new();
    private TimeSpan _startTime;
    private Channel<VideoFrame>? _frames;
    private CancellationTokenSource? _pumpCancel;

    private readonly bool _audioOutput;

    /// <param name="audioOutput">False runs the clock on a stopwatch without opening a sound device (tests).</param>
    public PlaybackEngine(IPcmProvider pcm, Ffmpeg? ffmpeg, bool audioOutput = true)
    {
        _mixer = new AudioMixer(pcm);
        _ffmpeg = ffmpeg;
        _audioOutput = audioOutput;
    }

    public bool IsPlaying { get; private set; }

    /// <summary>Set when no audio device could be opened; playback then runs on a stopwatch.</summary>
    public string? AudioError { get; private set; }

    public void UpdateMix(Project project) => _mixer.Update(project);

    public TimeSpan Position
    {
        get
        {
            if (!IsPlaying)
                return _startTime;
            if (_output != null && OperatingSystem.IsWindows())
            {
                var bytes = _output.GetPosition();
                var seconds = bytes / (double)_output.OutputWaveFormat.AverageBytesPerSecond;
                return _startTime + TimeSpan.FromSeconds(seconds);
            }
            return _startTime + _fallbackClock.Elapsed;
        }
    }

    public void Play(Project project, TimeSpan from)
    {
        Stop();
        _startTime = from;
        _mixer.Update(project);
        StartPicture(project, from);
        // NAudio's WaveOut is Windows-only; elsewhere the preview runs silently on a stopwatch.
        if (!_audioOutput || !OperatingSystem.IsWindows())
        {
            _fallbackClock.Restart();
            IsPlaying = true;
            return;
        }
        try
        {
            _provider = new MixSampleProvider(_mixer, AudioFormat.ToFrames(from));
            _output = new WaveOut { BufferMilliseconds = 40, NumberOfBuffers = 3 };
            _output.Init(_provider);
            _output.Play();
            AudioError = null;
        }
        catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException)
        {
            _output?.Dispose();
            _output = null;
            AudioError = ex.Message;
            _fallbackClock.Restart();
        }
        IsPlaying = true;
    }

    /// <summary>Stops and keeps the position where playback was.</summary>
    public void Stop()
    {
        if (IsPlaying)
            _startTime = Position;
        IsPlaying = false;
        _fallbackClock.Reset();
        if (_output != null && OperatingSystem.IsWindows())
        {
            _output.Stop();
            _output.Dispose();
            _output = null;
        }
        _pumpCancel?.Cancel();
        _pumpCancel = null;
        if (_frames != null)
        {
            while (_frames.Reader.TryRead(out var frame))
                Recycle(frame);
            _frames = null;
        }
    }

    /// <summary>The newest frame due at <paramref name="position"/>; older due frames are dropped.</summary>
    public VideoFrame? TakeFrame(TimeSpan position)
    {
        var reader = _frames?.Reader;
        if (reader == null)
            return null;
        VideoFrame? latest = null;
        while (reader.TryPeek(out var next) && next.Time <= position)
        {
            reader.TryRead(out next);
            if (latest != null)
                Recycle(latest);
            latest = next;
        }
        return latest;
    }

    public void Recycle(VideoFrame frame) => _pool.Add(frame.Pixels);

    private void StartPicture(Project project, TimeSpan from)
    {
        if (_ffmpeg == null)
            return;
        // Snapshot on the UI thread: the pump must not read the live model. An edit during playback
        // restarts playback.
        var segments = new List<(string Path, TimeSpan Start, TimeSpan End, TimeSpan SourceStart)>();
        foreach (var clip in ChainLayout.Compute(project).Clips)
        {
            if (clip.End <= from)
                continue;
            var source = project.FindSource(clip.Clip.SourceId);
            if (source == null || !File.Exists(source.Path))
                continue;
            var start = clip.Start < from ? from : clip.Start;
            segments.Add((source.Path, start, clip.End, clip.ToSourceTime(start)));
        }
        var channel = Channel.CreateBounded<VideoFrame>(new BoundedChannelOptions(FrameQueueLength)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _frames = channel;
        var cancel = new CancellationTokenSource();
        _pumpCancel = cancel;
        var ffmpeg = _ffmpeg;
        _ = Task.Run(() => PumpAsync(ffmpeg, segments, channel.Writer, cancel.Token));
    }

    private async Task PumpAsync(Ffmpeg ffmpeg, List<(string Path, TimeSpan Start, TimeSpan End, TimeSpan SourceStart)> segments,
        ChannelWriter<VideoFrame> writer, CancellationToken cancel)
    {
        try
        {
            foreach (var segment in segments)
            {
                cancel.ThrowIfCancellationRequested();
                using var stream = FrameStream.Start(ffmpeg, segment.Path, segment.SourceStart, segment.End - segment.Start,
                    PreviewFrameRate, PreviewSize);
                using var stop = cancel.Register(stream.Dispose);
                for (var k = 0; ; k++)
                {
                    var time = segment.Start + TimeSpan.FromSeconds(k / (double)PreviewFrameRate);
                    if (time >= segment.End)
                        break;
                    var buffer = _pool.TryTake(out var pooled) && pooled.Length == PreviewSize.ByteCount
                        ? pooled
                        : new byte[PreviewSize.ByteCount];
                    if (!stream.ReadFrame(buffer))
                    {
                        _pool.Add(buffer);
                        break;
                    }
                    await writer.WriteAsync(new VideoFrame(time, buffer), cancel).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The stream was killed on stop.
        }
        finally
        {
            writer.TryComplete();
        }
    }

    public void Dispose() => Stop();

    private sealed class MixSampleProvider(AudioMixer mixer, long startFrame) : ISampleProvider
    {
        private long _frame = startFrame;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, AudioFormat.Channels);

        public int Read(Span<float> buffer)
        {
            mixer.Mix(_frame, buffer);
            _frame += buffer.Length / AudioFormat.Channels;
            return buffer.Length;
        }
    }
}

/// <summary>Empty PCM source when ffmpeg is missing.</summary>
public sealed class NoPcm : IPcmProvider
{
    public IPcmData? TryGet(Guid sourceId) => null;
}
