namespace Ghostcut.Core.Model;

/// <summary>Fixed value ranges for edits (not per project).</summary>
public static class EditLimits
{
    public const double MinGainDb = -60;
    public const double MaxGainDb = 24;

    /// <summary>Gain at or below <see cref="MinGainDb"/> is treated as silence.</summary>
    public static double DbToLinear(double db) => db <= MinGainDb ? 0 : Math.Pow(10, db / 20);

    public static readonly TimeSpan MaxAudioEdgeFade = TimeSpan.FromMilliseconds(500);
}
