namespace Ghostcut.Media;

/// <summary>Local, machine-only data (caches, autosave). Never inside the repo or the project folder.</summary>
public static class AppPaths
{
    /// <summary>Overrides the data root, e.g. for tests that must not touch the real autosave.</summary>
    public const string DataDirVariable = "GHOSTCUT_DATA_DIR";

    public static string DataRoot =>
        Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ghostcut");

    public static string PcmCache => Path.Combine(DataRoot, "cache", "pcm");

    public static string Autosave => Path.Combine(DataRoot, "autosave");
}
