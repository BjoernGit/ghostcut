namespace Ghostcut.Core.Model;

/// <summary>The main track: the ghost chain of video clips.</summary>
public sealed class MainTrack
{
    public List<ChainClip> Clips { get; set; } = new();

    /// <summary>Mutes the game sound of all chain clips.</summary>
    public bool Muted { get; set; }
}

public enum AuxTrackKind
{
    Voice,
    Music,
}

/// <summary>An additional audio track (voice or music) with freely placed clips.</summary>
public sealed class AuxTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public AuxTrackKind Kind { get; set; }

    public string Name { get; set; } = "";

    public bool Muted { get; set; }

    public List<AuxClip> Clips { get; set; } = new();
}
