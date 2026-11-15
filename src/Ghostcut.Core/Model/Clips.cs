using System.Text.Json.Serialization;

namespace Ghostcut.Core.Model;

/// <summary>
/// A used range [<see cref="In"/>, <see cref="Out"/>) of a source. Everything of the source outside
/// that range is the clip's overhang ("ghost"): it stays visible and can be pulled back.
/// </summary>
public abstract class Clip
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SourceId { get; set; }

    /// <summary>Source time where the used range starts.</summary>
    public TimeSpan In { get; set; }

    /// <summary>Source time where the used range ends (exclusive).</summary>
    public TimeSpan Out { get; set; }

    /// <summary>Clip volume in decibels, 0 = unchanged.</summary>
    public double GainDb { get; set; }

    [JsonIgnore]
    public TimeSpan Duration => Out - In;
}

/// <summary>
/// A clip of the main track (picture + game sound). Its timeline position is not stored: chain
/// clips always sit gap-free one after another (ripple is always on), so the position follows from
/// the order and the durations of the clips before it.
/// </summary>
public sealed class ChainClip : Clip
{
    /// <summary>Fade from black at the start of the clip; zero = hard cut.</summary>
    public TimeSpan FadeInFromBlack { get; set; }

    /// <summary>Fade to black at the end of the clip; zero = hard cut.</summary>
    public TimeSpan FadeOutToBlack { get; set; }

    public ChainClip Clone() => (ChainClip)MemberwiseClone();
}

/// <summary>A freely placed clip on a voice or music track.</summary>
public sealed class AuxClip : Clip
{
    /// <summary>Timeline time where <see cref="Clip.In"/> plays.</summary>
    public TimeSpan Start { get; set; }

    [JsonIgnore]
    public TimeSpan End => Start + Duration;

    public AuxClip Clone() => (AuxClip)MemberwiseClone();
}
