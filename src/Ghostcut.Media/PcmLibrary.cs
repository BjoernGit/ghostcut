using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using Ghostcut.Core.Audio;
using Ghostcut.Core.Model;

namespace Ghostcut.Media;

/// <summary>
/// Decodes the sound of each source once into a raw float cache file (48 kHz stereo) and serves it
/// memory-mapped to the mixer. The cache lives outside the project (local app data), keyed by path,
/// size and modification time.
/// </summary>
public sealed class PcmLibrary : IPcmProvider, IDisposable
{
    private readonly Ffmpeg _ffmpeg;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<Guid, MappedPcm> _loaded = new();
    private readonly ConcurrentDictionary<Guid, Task> _pending = new();

    public PcmLibrary(Ffmpeg ffmpeg, string cacheDirectory)
    {
        _ffmpeg = ffmpeg;
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(cacheDirectory);
    }

    public static string DefaultCacheDirectory => AppPaths.PcmCache;

    /// <summary>Raised (on a worker thread) when a source's audio became available.</summary>
    public event Action<Guid>? Loaded;

    /// <summary>Raised (on a worker thread) when decoding a source failed.</summary>
    public event Action<Guid, Exception>? Failed;

    public IPcmData? TryGet(Guid sourceId) => _loaded.GetValueOrDefault(sourceId);

    public bool IsLoaded(Guid sourceId) => _loaded.ContainsKey(sourceId);

    /// <summary>Starts decoding every source with sound that is not loaded yet.</summary>
    public void EnsureAll(Project project)
    {
        foreach (var source in project.Sources)
            if (source.HasAudio)
                _ = EnsureAsync(source);
    }

    public Task EnsureAsync(MediaSource source, CancellationToken cancel = default)
    {
        if (!source.HasAudio || _loaded.ContainsKey(source.Id))
            return Task.CompletedTask;
        return _pending.GetOrAdd(source.Id, id => Task.Run(async () =>
        {
            try
            {
                var file = await DecodeToCacheAsync(source, cancel).ConfigureAwait(false);
                _loaded[source.Id] = new MappedPcm(file);
                Loaded?.Invoke(source.Id);
            }
            catch (Exception ex)
            {
                Failed?.Invoke(source.Id, ex);
                throw;
            }
            finally
            {
                _pending.TryRemove(id, out var _);
            }
        }, cancel));
    }

    private async Task<string> DecodeToCacheAsync(MediaSource source, CancellationToken cancel)
    {
        var info = new FileInfo(source.Path);
        if (!info.Exists)
            throw new FileNotFoundException("Source file is missing.", source.Path);
        var key = CacheKey(info);
        var file = Path.Combine(_cacheDirectory, key + ".f32");
        if (File.Exists(file))
            return file;
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await Ffmpeg.RunAsync(_ffmpeg.FfmpegPath, new[]
            {
                "-v", "error", "-nostdin", "-y", "-i", source.Path, "-vn", "-sn", "-dn",
                "-ac", AudioFormat.Channels.ToString(), "-ar", AudioFormat.SampleRate.ToString(),
                "-f", "f32le", temp,
            }, cancel).ConfigureAwait(false);
            File.Move(temp, file, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
        return file;
    }

    private static string CacheKey(FileInfo info)
    {
        var raw = $"{info.FullName.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..32].ToLowerInvariant();
    }

    public void Dispose()
    {
        foreach (var pcm in _loaded.Values)
            pcm.Dispose();
        _loaded.Clear();
    }

    private sealed unsafe class MappedPcm : IPcmData, IDisposable
    {
        private readonly MemoryMappedFile? _file;
        private readonly MemoryMappedViewAccessor? _view;
        private readonly byte* _pointer;

        public MappedPcm(string path)
        {
            var length = new FileInfo(path).Length;
            FrameCount = length / (sizeof(float) * AudioFormat.Channels);
            if (FrameCount == 0)
                return;
            _file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            byte* pointer = null;
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            _pointer = pointer + _view.PointerOffset;
        }

        public long FrameCount { get; }

        public void Read(long startFrame, Span<float> destination)
        {
            destination.Clear();
            var frames = destination.Length / AudioFormat.Channels;
            var from = Math.Max(0, startFrame);
            var to = Math.Min(FrameCount, startFrame + frames);
            if (to <= from || _pointer == null)
                return;
            var source = new ReadOnlySpan<float>(
                (float*)_pointer + from * AudioFormat.Channels, (int)((to - from) * AudioFormat.Channels));
            source.CopyTo(destination[(int)((from - startFrame) * AudioFormat.Channels)..]);
        }

        public void Dispose()
        {
            if (_view != null)
            {
                _view.SafeMemoryMappedViewHandle.ReleasePointer();
                _view.Dispose();
            }
            _file?.Dispose();
        }
    }
}
