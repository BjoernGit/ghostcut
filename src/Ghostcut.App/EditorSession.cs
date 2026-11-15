using Avalonia.Threading;
using Ghostcut.Core.Commands;
using Ghostcut.Core.Model;
using Ghostcut.Core.Persistence;
using Ghostcut.Media;

namespace Ghostcut.App;

/// <summary>
/// The open project and everything around it that is not drawing: history, playhead, selection,
/// import, save/load and autosave. All edits go through <see cref="History"/>.
/// </summary>
public sealed class EditorSession : IDisposable
{
    /// <summary>How often a dirty project is written to the autosave folder.</summary>
    public static readonly TimeSpan AutosaveInterval = TimeSpan.FromSeconds(60);

    private readonly DispatcherTimer _autosaveTimer;
    private TimeSpan _playhead;
    private Guid? _selectedClipId;
    private bool _changedSinceAutosave;

    public EditorSession()
    {
        Ffmpeg = Media.Ffmpeg.TryLocate();
        if (Ffmpeg != null)
        {
            Pcm = new PcmLibrary(Ffmpeg, PcmLibrary.DefaultCacheDirectory);
            Pcm.Loaded += _ => Dispatcher.UIThread.Post(() => AudioLoaded?.Invoke());
            Pcm.Failed += (_, ex) => Dispatcher.UIThread.Post(() => Status?.Invoke($"Audio decoding failed: {ex.Message}"));
        }
        History = new History(Project);
        History.Changed += OnHistoryChanged;
        _autosaveTimer = new DispatcherTimer { Interval = AutosaveInterval };
        _autosaveTimer.Tick += (_, _) => Autosave();
        _autosaveTimer.Start();
    }

    public Ffmpeg? Ffmpeg { get; }

    public PcmLibrary? Pcm { get; }

    public Project Project { get; private set; } = new();

    public History History { get; private set; }

    /// <summary>Path of the project file; null while untitled.</summary>
    public string? FilePath { get; private set; }

    public static string AutosaveDirectory => AppPaths.Autosave;

    public static string AutosavePath => Path.Combine(AutosaveDirectory, "autosave" + ProjectSerializer.FileExtension);

    /// <summary>Any model change (edit, undo, redo, new/loaded project).</summary>
    public event Action? ProjectChanged;

    /// <summary>A new project instance was loaded or created.</summary>
    public event Action? ProjectReplaced;

    public event Action? PlayheadChanged;

    public event Action? SelectionChanged;

    /// <summary>Some source's audio finished decoding.</summary>
    public event Action? AudioLoaded;

    /// <summary>A message for the status bar.</summary>
    public event Action<string>? Status;

    public TimeSpan Playhead
    {
        get => _playhead;
        set
        {
            var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value;
            if (clamped == _playhead)
                return;
            _playhead = clamped;
            PlayheadChanged?.Invoke();
        }
    }

    public Guid? SelectedClipId
    {
        get => _selectedClipId;
        set
        {
            if (value == _selectedClipId)
                return;
            _selectedClipId = value;
            SelectionChanged?.Invoke();
        }
    }

    public string DisplayName => FilePath != null ? Path.GetFileNameWithoutExtension(FilePath) : "Untitled";

    /// <summary>Runs a command and reports a rejected edit in the status bar instead of throwing.</summary>
    public bool TryExecute(IEditCommand command)
    {
        try
        {
            History.Execute(command);
            return true;
        }
        catch (EditException ex)
        {
            Status?.Invoke(ex.Message);
            return false;
        }
    }

    public bool TryExecuteGesture(IEditCommand command, object token)
    {
        try
        {
            History.ExecuteGesture(command, token);
            return true;
        }
        catch (EditException ex)
        {
            Status?.Invoke(ex.Message);
            return false;
        }
    }

    public async Task ImportAsync(IReadOnlyList<string> paths, ImportTarget target)
    {
        if (Ffmpeg == null)
        {
            Status?.Invoke("ffmpeg not found — import needs ffprobe. Install ffmpeg or set GHOSTCUT_FFMPEG.");
            return;
        }
        var files = paths.Where(MediaProbe.IsSupportedFile).ToList();
        var skipped = paths.Count - files.Count;
        if (files.Count == 0)
        {
            Status?.Invoke("No supported media files (video: mp4/mov/mkv/…, audio: wav/mp3/…).");
            return;
        }
        Status?.Invoke(files.Count == 1 ? $"Importing {Path.GetFileName(files[0])}…" : $"Importing {files.Count} files…");
        var probed = new List<MediaSource>();
        var errors = new List<string>();
        foreach (var file in files)
        {
            try
            {
                probed.Add(await MediaProbe.ProbeAsync(Ffmpeg, file));
            }
            catch (Exception ex) when (ex is FfmpegException or IOException)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        if (probed.Count > 0)
        {
            var command = ImportPlanner.Plan(Project, probed, target);
            if (command != null && TryExecute(command))
                Pcm?.EnsureAll(Project);
        }
        var message = $"Imported {probed.Count} file(s).";
        if (skipped > 0)
            message += $" Skipped {skipped} unsupported.";
        if (errors.Count > 0)
            message += " Failed: " + string.Join("; ", errors);
        Status?.Invoke(message);
    }

    public void NewProject() => Replace(new Project(), null);

    public void Load(string path) => Replace(ProjectSerializer.Load(path), path);

    /// <summary>Opens the last autosave as an untitled, unsaved project.</summary>
    public bool RecoverAutosave()
    {
        if (!File.Exists(AutosavePath))
            return false;
        Replace(ProjectSerializer.Load(AutosavePath), null);
        History.MarkUnsaved();
        ProjectChanged?.Invoke();
        return true;
    }

    public void Save(string path)
    {
        ProjectSerializer.Save(Project, path);
        FilePath = path;
        History.MarkSaved();
        ProjectChanged?.Invoke();
        Status?.Invoke($"Saved {Path.GetFileName(path)}.");
    }

    /// <summary>Writes the project to the autosave file if it changed since the last autosave.</summary>
    public void Autosave()
    {
        if (!_changedSinceAutosave || Project.Sources.Count == 0)
            return;
        try
        {
            ProjectSerializer.Save(Project, AutosavePath);
            _changedSinceAutosave = false;
        }
        catch (IOException ex)
        {
            Status?.Invoke($"Autosave failed: {ex.Message}");
        }
    }

    private void Replace(Project project, string? path)
    {
        Autosave();
        History.Changed -= OnHistoryChanged;
        Project = project;
        History = new History(project);
        History.Changed += OnHistoryChanged;
        FilePath = path;
        _selectedClipId = null;
        _playhead = TimeSpan.Zero;
        _changedSinceAutosave = false;
        Pcm?.EnsureAll(project);
        ProjectReplaced?.Invoke();
        ProjectChanged?.Invoke();
        SelectionChanged?.Invoke();
        PlayheadChanged?.Invoke();
        var missing = project.Sources.Where(s => !File.Exists(s.Path)).Select(s => s.Name).ToList();
        if (missing.Count > 0)
            Status?.Invoke("Missing media: " + string.Join(", ", missing));
    }

    private void OnHistoryChanged()
    {
        _changedSinceAutosave = true;
        if (_selectedClipId is { } id && Project.FindClip(id) == null)
        {
            _selectedClipId = null;
            SelectionChanged?.Invoke();
        }
        ProjectChanged?.Invoke();
    }

    public void Dispose()
    {
        _autosaveTimer.Stop();
        Autosave();
        Pcm?.Dispose();
    }
}
