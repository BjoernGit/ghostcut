using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Ghostcut.App.Playback;
using Ghostcut.App.Timeline;
using Ghostcut.Core.Commands;
using Ghostcut.Core.Model;
using Ghostcut.Core.Persistence;
using Ghostcut.Core.Timeline;
using Ghostcut.Media;

namespace Ghostcut.App;

public partial class MainWindow : Window
{
    /// <summary>Refresh interval of the preview while playing.</summary>
    private static readonly TimeSpan PlaybackTick = TimeSpan.FromMilliseconds(15);

    private static readonly FilePickerFileType ProjectFileType = new("Ghostcut project") { Patterns = new[] { "*" + ProjectSerializer.FileExtension } };

    private readonly EditorSession _session = new();
    private readonly PlaybackEngine _playback;
    private readonly ScrubDecoder? _scrub;
    private readonly DispatcherTimer _playTimer;
    private readonly WriteableBitmap _frame;
    private object _gainGesture = new();
    private bool _updatingPanel;
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        _frame = new WriteableBitmap(
            new PixelSize(PlaybackEngine.PreviewSize.Width, PlaybackEngine.PreviewSize.Height),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        PreviewImage.Source = _frame;
        _playback = new PlaybackEngine(_session.Pcm ?? (Core.Audio.IPcmProvider)new NoPcm(), _session.Ffmpeg);
        if (_session.Ffmpeg != null)
        {
            _scrub = new ScrubDecoder(_session.Ffmpeg, PlaybackEngine.PreviewSize);
            _scrub.FrameReady += pixels =>
            {
                PreviewFramesShown++;
                ShowPixels(pixels);
            };
        }
        _playTimer = new DispatcherTimer { Interval = PlaybackTick };
        _playTimer.Tick += (_, _) => OnPlaybackTick();

        Timeline.Session = _session;
        _session.ProjectChanged += OnProjectChanged;
        _session.ProjectReplaced += OnProjectReplaced;
        _session.PlayheadChanged += OnPlayheadChanged;
        _session.SelectionChanged += RefreshPanel;
        _session.Status += ShowStatus;
        _session.AudioLoaded += () => ShowStatus("Audio ready.");

        GainSlider.ValueChanged += (_, e) => OnGainChanged(e.NewValue);
        FadeInBox.ValueChanged += (_, _) => OnFadesChanged();
        FadeOutBox.ValueChanged += (_, _) => OnFadesChanged();
        EdgeFadeBox.ValueChanged += (_, _) => OnEdgeFadeChanged();

        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnWindowDrop);

        if (_session.Ffmpeg == null)
            ShowStatus("ffmpeg not found — install it (winget install Gyan.FFmpeg.Essentials) or set GHOSTCUT_FFMPEG to its folder.");
        RefreshAll();
    }

    internal EditorSession Session => _session;

    public void OpenOnStartup(string path) => Dispatcher.UIThread.Post(() => TryLoad(path));

    // ---- Model → view ------------------------------------------------------------------------

    private void OnProjectChanged()
    {
        _playback.UpdateMix(_session.Project);
        if (_playback.IsPlaying && !Timeline.IsDragging)
        {
            // The picture pump works on a snapshot; restart it on the edited project.
            _playback.Play(_session.Project, _playback.Position);
        }
        RefreshAll();
        if (!_playback.IsPlaying)
            RequestPreviewFrame();
    }

    private void OnProjectReplaced()
    {
        StopPlayback();
        _scrub?.Invalidate();
        Timeline.ZoomToFit();
    }

    private void OnPlayheadChanged()
    {
        TimecodeText.Text = TimelineControl.Format(_session.Playhead, precise: true);
        if (!_playback.IsPlaying)
            RequestPreviewFrame();
        else
            UpdateFadeOverlay(_session.Playhead);
    }

    private void RefreshAll()
    {
        Title = $"Ghostcut — {_session.DisplayName}{(_session.History.IsDirty ? " *" : "")}";
        UndoItem.Header = _session.History.UndoDescription is { } u ? $"_Undo {u}" : "_Undo";
        RedoItem.Header = _session.History.RedoDescription is { } r ? $"_Redo {r}" : "_Redo";
        PreviewHint.IsVisible = _session.Project.Main.Clips.Count == 0;
        RefreshPanel();
    }

    private void RefreshPanel()
    {
        _updatingPanel = true;
        try
        {
            var project = _session.Project;
            EdgeFadeBox.Value = (decimal)project.Settings.AudioEdgeFade.TotalMilliseconds;
            var clip = _session.SelectedClipId is { } id ? project.FindClip(id) : null;
            ClipEditor.IsEnabled = clip != null;
            if (clip == null)
            {
                ClipName.Text = "No clip selected";
                ClipRange.Text = "";
                FadePanel.IsVisible = true;
                return;
            }
            var source = project.FindSource(clip.SourceId);
            ClipName.Text = source?.Name ?? "?";
            ClipRange.Text = $"Source {TimelineControl.Format(clip.In, true)} – {TimelineControl.Format(clip.Out, true)}"
                             + $"  ({TimelineControl.Format(clip.Duration, true)} of {TimelineControl.Format(source?.Duration ?? TimeSpan.Zero, true)})";
            GainSlider.Value = clip.GainDb;
            GainText.Text = clip.GainDb.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture);
            if (clip is ChainClip chain)
            {
                FadePanel.IsVisible = true;
                FadeInBox.Value = (decimal)chain.FadeInFromBlack.TotalSeconds;
                FadeOutBox.Value = (decimal)chain.FadeOutToBlack.TotalSeconds;
            }
            else
            {
                FadePanel.IsVisible = false;
            }
        }
        finally
        {
            _updatingPanel = false;
        }
    }

    // ---- Panel → model -----------------------------------------------------------------------

    private void OnGainChanged(double value)
    {
        if (_updatingPanel || _session.SelectedClipId is not { } id)
            return;
        // Consecutive slider moves on one clip merge into one undo step.
        _session.TryExecuteGesture(new SetClipGainCommand(id, Math.Round(value, 1)), _gainGesture);
    }

    private void OnFadesChanged()
    {
        if (_updatingPanel || _session.SelectedClipId is not { } id || _session.Project.FindChainClip(id) == null)
            return;
        var fadeIn = TimeSpan.FromSeconds((double)(FadeInBox.Value ?? 0));
        var fadeOut = TimeSpan.FromSeconds((double)(FadeOutBox.Value ?? 0));
        _session.TryExecute(new SetClipFadesCommand(id, fadeIn, fadeOut));
    }

    private void OnEdgeFadeChanged()
    {
        if (_updatingPanel)
            return;
        var ms = (double)(EdgeFadeBox.Value ?? 0);
        if (Math.Abs(ms - _session.Project.Settings.AudioEdgeFade.TotalMilliseconds) < 0.5)
            return;
        _session.TryExecute(new SetAudioEdgeFadeCommand(TimeSpan.FromMilliseconds(ms)));
    }

    // ---- Preview -----------------------------------------------------------------------------

    private void RequestPreviewFrame()
    {
        var time = _session.Playhead;
        UpdateFadeOverlay(time);
        var layout = ChainLayout.Compute(_session.Project);
        var clip = layout.ClipAt(time);
        if (clip == null && layout.Clips.Count > 0 && time >= layout.Duration)
            clip = layout.Clips[^1]; // show the last frame at the very end
        if (clip is not { } c)
        {
            ClearPreview();
            return;
        }
        var source = _session.Project.FindSource(c.Clip.SourceId);
        if (source == null || _scrub == null || !File.Exists(source.Path))
        {
            ClearPreview();
            return;
        }
        var sourceTime = c.ToSourceTime(time < c.End ? time : c.End - TimeSpan.FromMilliseconds(1));
        _scrub.Request(source.Path, sourceTime);
    }

    private void UpdateFadeOverlay(TimeSpan time)
    {
        var layout = ChainLayout.Compute(_session.Project);
        var black = 0.0;
        if (layout.ClipAt(time) is { } c)
        {
            var intoClip = (time - c.Start).TotalSeconds;
            var untilEnd = (c.End - time).TotalSeconds;
            var fadeIn = c.Clip.FadeInFromBlack.TotalSeconds;
            var fadeOut = c.Clip.FadeOutToBlack.TotalSeconds;
            if (fadeIn > 0 && intoClip < fadeIn)
                black = Math.Max(black, 1 - intoClip / fadeIn);
            if (fadeOut > 0 && untilEnd < fadeOut)
                black = Math.Max(black, 1 - untilEnd / fadeOut);
        }
        else if (layout.Clips.Count > 0 && time >= layout.Duration && layout.Clips[^1].Clip.FadeOutToBlack > TimeSpan.Zero)
        {
            black = 1;
        }
        FadeOverlay.Opacity = black;
    }

    /// <summary>Test hook: number of decoded scrub frames shown.</summary>
    internal int PreviewFramesShown { get; private set; }

    private void ShowPixels(byte[] pixels)
    {
        using (var buffer = _frame.Lock())
        {
            var rowBytes = PlaybackEngine.PreviewSize.Width * 4;
            for (var y = 0; y < PlaybackEngine.PreviewSize.Height; y++)
                Marshal.Copy(pixels, y * rowBytes, buffer.Address + y * buffer.RowBytes, rowBytes);
        }
        PreviewImage.InvalidateVisual();
    }

    private void ClearPreview()
    {
        _scrub?.Invalidate();
        ShowPixels(new byte[PlaybackEngine.PreviewSize.ByteCount]);
    }

    // ---- Playback ----------------------------------------------------------------------------

    private void OnPlayPause(object? sender, RoutedEventArgs e) => TogglePlayback();

    private void TogglePlayback()
    {
        if (_playback.IsPlaying)
        {
            StopPlayback();
            return;
        }
        var duration = _session.Project.Duration;
        if (duration <= TimeSpan.Zero)
            return;
        if (_session.Playhead >= duration - TimeSpan.FromMilliseconds(50))
            _session.Playhead = TimeSpan.Zero;
        _playback.Play(_session.Project, _session.Playhead);
        if (_playback.AudioError is { } error)
            ShowStatus($"No audio output ({error}); playing without sound.");
        _playTimer.Start();
        PlayButton.Content = "❚❚ Pause";
    }

    private void StopPlayback()
    {
        if (!_playback.IsPlaying)
            return;
        var position = _playback.Position;
        _playback.Stop();
        _playTimer.Stop();
        PlayButton.Content = "▶ Play";
        _scrub?.Invalidate();
        _session.Playhead = position;
        RequestPreviewFrame();
    }

    private void OnPlaybackTick()
    {
        if (!_playback.IsPlaying)
            return;
        var position = _playback.Position;
        if (position >= _session.Project.Duration)
        {
            StopPlayback();
            _session.Playhead = _session.Project.Duration;
            return;
        }
        if (!Timeline.IsDragging)
        {
            _session.Playhead = position;
            Timeline.EnsureVisible(position);
        }
        var frame = _playback.TakeFrame(position);
        if (frame != null)
        {
            ShowPixels(frame.Pixels);
            _playback.Recycle(frame);
        }
    }

    // ---- Commands ----------------------------------------------------------------------------

    private void OnUndo(object? sender, RoutedEventArgs e) => _session.History.Undo();

    private void OnRedo(object? sender, RoutedEventArgs e) => _session.History.Redo();

    private void OnSplit(object? sender, RoutedEventArgs e) => SplitAtPlayhead();

    private void OnDelete(object? sender, RoutedEventArgs e) => DeleteSelected();

    private void SplitAtPlayhead()
    {
        var project = _session.Project;
        var time = _session.Playhead;
        Guid? target = null;
        if (_session.SelectedClipId is { } id)
        {
            if (ChainLayout.Compute(project).Find(id) is { } chain && time > chain.Start && time < chain.End)
                target = id;
            else if (project.FindAuxClip(id) is { } aux && time > aux.Clip.Start && time < aux.Clip.End)
                target = id;
        }
        target ??= ChainLayout.Compute(project).ClipAt(time)?.Clip.Id;
        if (target == null)
        {
            ShowStatus("Put the playhead inside a clip to split it.");
            return;
        }
        var command = new SplitClipCommand(target.Value, time);
        if (_session.TryExecute(command))
            _session.SelectedClipId = command.NewClipId;
    }

    private void DeleteSelected()
    {
        if (_session.SelectedClipId is { } id)
            _session.TryExecute(new RemoveClipCommand(id));
    }

    private void OnAddVoiceTrack(object? sender, RoutedEventArgs e) => AddTrack(AuxTrackKind.Voice);

    private void OnAddMusicTrack(object? sender, RoutedEventArgs e) => AddTrack(AuxTrackKind.Music);

    private void AddTrack(AuxTrackKind kind) =>
        _session.TryExecute(new AddAuxTrackCommand(new AuxTrack { Kind = kind, Name = ImportPlanner.NextTrackName(_session.Project, kind) }));

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Timeline.ZoomBy(Timeline.ZoomStep);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Timeline.ZoomBy(1 / Timeline.ZoomStep);

    private void OnZoomFit(object? sender, RoutedEventArgs e) => Timeline.ZoomToFit();

    // ---- Files -------------------------------------------------------------------------------

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        if (await ConfirmDiscardAsync())
            _session.NewProject();
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync())
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open project",
            AllowMultiple = false,
            FileTypeFilter = new[] { ProjectFileType },
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            TryLoad(path);
    }

    private void TryLoad(string path)
    {
        try
        {
            _session.Load(path);
            ShowStatus($"Opened {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowStatus($"Could not open {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e) => await SaveAsync(saveAs: false);

    private async void OnSaveAs(object? sender, RoutedEventArgs e) => await SaveAsync(saveAs: true);

    private async Task<bool> SaveAsync(bool saveAs)
    {
        var path = _session.FilePath;
        if (saveAs || path == null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save project",
                SuggestedFileName = _session.DisplayName + ProjectSerializer.FileExtension,
                DefaultExtension = ProjectSerializer.FileExtension.TrimStart('.'),
                FileTypeChoices = new[] { ProjectFileType },
                ShowOverwritePrompt = true,
            });
            path = file?.TryGetLocalPath();
            if (path == null)
                return false;
        }
        try
        {
            _session.Save(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Save failed: {ex.Message}");
            return false;
        }
    }

    private async void OnRecoverAutosave(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync())
            return;
        try
        {
            ShowStatus(_session.RecoverAutosave() ? "Autosave recovered — save it under a name." : "No autosave found.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            ShowStatus($"Autosave unreadable: {ex.Message}");
        }
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import media",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Media") { Patterns = MediaProbe.VideoExtensions.Concat(MediaProbe.AudioExtensions).Select(x => "*" + x).ToArray() },
            },
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0)
            await _session.ImportAsync(paths, new ImportTarget.Auto(_session.Playhead));
    }

    private async void OnWindowDrop(object? sender, DragEventArgs e)
    {
        if (e.Handled)
            return; // the timeline handled it with a precise target
        var paths = TimelineControl.DroppedPaths(e);
        if (paths.Count > 0)
            await _session.ImportAsync(paths, new ImportTarget.Auto(_session.Playhead));
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        StopPlayback();
        if (_session.Ffmpeg == null || _session.Pcm == null)
        {
            ShowStatus("Export needs ffmpeg.");
            return;
        }
        if (_session.Project.Main.Clips.Count == 0)
        {
            ShowStatus("The main track is empty — nothing to export.");
            return;
        }
        var dialog = new ExportWindow(_session);
        await dialog.ShowDialog(this);
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_session.History.IsDirty)
            return true;
        var choice = await ConfirmDialog.AskAsync(this, "Unsaved changes",
            $"Save changes to {_session.DisplayName}?", "Save", "Discard", "Cancel");
        return choice switch
        {
            0 => await SaveAsync(saveAs: false),
            1 => true,
            _ => false,
        };
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || !_session.History.IsDirty)
        {
            Shutdown();
            return;
        }
        e.Cancel = true;
        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void Shutdown()
    {
        _playTimer.Stop();
        _playback.Dispose();
        _session.Dispose();
    }

    // ---- Keyboard ----------------------------------------------------------------------------

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        // Leave typing keys to text boxes (e.g. the fade fields).
        var typing = FocusManager?.GetFocusedElement() is TextBox;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var handled = true;
        switch (e.Key)
        {
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                if (typing) { handled = false; break; }
                _session.History.Redo();
                break;
            case Key.Z when ctrl:
                if (typing) { handled = false; break; }
                _session.History.Undo();
                break;
            case Key.S when ctrl && shift:
                OnSaveAs(null, new RoutedEventArgs());
                break;
            case Key.S when ctrl:
                OnSave(null, new RoutedEventArgs());
                break;
            case Key.O when ctrl:
                OnOpen(null, new RoutedEventArgs());
                break;
            case Key.N when ctrl:
                OnNew(null, new RoutedEventArgs());
                break;
            case Key.I when ctrl:
                OnImport(null, new RoutedEventArgs());
                break;
            case Key.E when ctrl:
                OnExport(null, new RoutedEventArgs());
                break;
            case Key.OemPlus or Key.Add when ctrl:
                Timeline.ZoomBy(Timeline.ZoomStep);
                break;
            case Key.OemMinus or Key.Subtract when ctrl:
                Timeline.ZoomBy(1 / Timeline.ZoomStep);
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                Timeline.ZoomToFit();
                break;
            case Key.Escape when Timeline.IsDragging:
                Timeline.CancelDrag();
                break;
            case Key.Space when !typing:
                TogglePlayback();
                break;
            case Key.S when !typing && !ctrl:
                SplitAtPlayhead();
                break;
            case Key.Delete or Key.Back when !typing:
                DeleteSelected();
                break;
            case Key.Home when !typing:
                _session.Playhead = TimeSpan.Zero;
                break;
            case Key.End when !typing:
                _session.Playhead = _session.Project.Duration;
                break;
            case Key.Left when !typing:
                StepFrames(shift ? -10 : -1);
                break;
            case Key.Right when !typing:
                StepFrames(shift ? 10 : 1);
                break;
            default:
                handled = false;
                break;
        }
        if (handled)
            e.Handled = true;
    }

    private void StepFrames(int frames)
    {
        StopPlayback();
        var step = TimeSpan.FromSeconds(frames / (double)PlaybackEngine.PreviewFrameRate);
        var target = _session.Playhead + step;
        var end = _session.Project.Duration;
        _session.Playhead = target < TimeSpan.Zero ? TimeSpan.Zero : target > end ? end : target;
    }

    private void ShowStatus(string message) => StatusText.Text = message;
}
