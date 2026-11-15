using System.Text.Json.Serialization;

namespace Ghostcut.Core.Model;

/// <summary>
/// An imported media file. Clips reference sources by <see cref="Id"/>; the metadata comes from
/// ffprobe at import time and is stored in the project so it opens without re-probing.
/// </summary>
public sealed class MediaSource
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Absolute path on the local machine.</summary>
    public string Path { get; set; } = "";

    public TimeSpan Duration { get; set; }

    public bool HasVideo { get; set; }

    public bool HasAudio { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public double FrameRate { get; set; }

    [JsonIgnore]
    public string Name => System.IO.Path.GetFileName(Path);
}
