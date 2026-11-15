using System.Text.Json;
using System.Text.Json.Serialization;
using Ghostcut.Core.Model;

namespace Ghostcut.Core.Persistence;

/// <summary>Reads and writes projects as readable JSON (<c>.ghostcut</c>).</summary>
public static class ProjectSerializer
{
    public const string FileExtension = ".ghostcut";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(Project project) => JsonSerializer.Serialize(project, Options);

    public static Project Deserialize(string json)
    {
        Project? project;
        try
        {
            project = JsonSerializer.Deserialize<Project>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Not a valid Ghostcut project: {ex.Message}", ex);
        }
        if (project == null)
            throw new InvalidDataException("Empty project file.");
        if (project.FormatVersion > Project.CurrentFormatVersion)
            throw new InvalidDataException(
                $"The project was saved by a newer Ghostcut (format {project.FormatVersion}, this version reads up to {Project.CurrentFormatVersion}).");
        Validate(project);
        project.FormatVersion = Project.CurrentFormatVersion;
        return project;
    }

    /// <summary>
    /// Writes atomically: temp file first, then replace. The previous file is kept as
    /// <c>&lt;name&gt;.ghostcut.bak</c>.
    /// </summary>
    public static void Save(Project project, string path)
    {
        var json = Serialize(project);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temp, json);
        if (File.Exists(path))
            File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(temp, path);
    }

    public static Project Load(string path) => Deserialize(File.ReadAllText(path));

    private static void Validate(Project project)
    {
        var sourceIds = new HashSet<Guid>(project.Sources.Select(s => s.Id));
        var clipIds = new HashSet<Guid>();
        IEnumerable<Clip> allClips = project.Main.Clips.Cast<Clip>().Concat(project.AuxTracks.SelectMany(t => t.Clips));
        foreach (var clip in allClips)
        {
            if (!sourceIds.Contains(clip.SourceId))
                throw new InvalidDataException($"Clip {clip.Id} references unknown source {clip.SourceId}.");
            if (!clipIds.Add(clip.Id))
                throw new InvalidDataException($"Duplicate clip id {clip.Id}.");
            if (clip.Out <= clip.In || clip.In < TimeSpan.Zero)
                throw new InvalidDataException($"Clip {clip.Id} has an invalid range.");
        }
    }
}
