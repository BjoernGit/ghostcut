using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Ghostcut.App.Timeline;
using Ghostcut.Media;

namespace Ghostcut.App;

public partial class ExportWindow : Window
{
    private readonly EditorSession _session;
    private CancellationTokenSource? _cancel;

    public ExportWindow()
        : this(new EditorSession())
    {
    }

    public ExportWindow(EditorSession session)
    {
        _session = session;
        InitializeComponent();
        PresetBox.ItemsSource = ExportPreset.All;
        PresetBox.SelectedIndex = 0;
        PresetBox.SelectionChanged += (_, _) => UpdatePresetInfo();
        var folder = _session.FilePath != null
            ? Path.GetDirectoryName(_session.FilePath)!
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        OutputBox.Text = Path.Combine(folder, _session.DisplayName + ".mp4");
        UpdatePresetInfo();
    }

    private ExportPreset Preset => (ExportPreset)PresetBox.SelectedItem!;

    private void UpdatePresetInfo()
    {
        var p = Preset;
        PresetInfo.Text = $"{p.Width}×{p.Height}, {p.FrameRate} fps, H.264 + AAC {p.AudioBitrate} — length {TimelineControl.Format(_session.Project.Duration, true)}";
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to",
            SuggestedFileName = Path.GetFileName(OutputBox.Text),
            DefaultExtension = "mp4",
            FileTypeChoices = new[] { new FilePickerFileType("MP4 video") { Patterns = new[] { "*.mp4" } } },
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is { } path)
            OutputBox.Text = path;
    }

    private async void OnStart(object? sender, RoutedEventArgs e)
    {
        var output = OutputBox.Text?.Trim();
        if (string.IsNullOrEmpty(output))
            return;
        if (!output.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            output += ".mp4";
        StartButton.IsEnabled = false;
        PresetBox.IsEnabled = false;
        CancelButton.Content = "Cancel";
        _cancel = new CancellationTokenSource();
        var started = DateTime.Now;
        var progress = new Progress<double>(p =>
        {
            Progress.Value = p;
            StatusText.Text = $"Exporting… {p:P0}";
        });
        try
        {
            await Exporter.ExportAsync(_session.Ffmpeg!, _session.Project, _session.Pcm!, Preset, output, progress, _cancel.Token);
            var size = new FileInfo(output).Length / (1024.0 * 1024.0);
            StatusText.Text = $"Done in {(DateTime.Now - started).TotalSeconds:0} s: {output} ({size:0.0} MB)";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Export cancelled.";
            TryDelete(output);
        }
        catch (Exception ex) when (ex is FfmpegException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
        }
        finally
        {
            _cancel = null;
            StartButton.IsEnabled = true;
            PresetBox.IsEnabled = true;
            CancelButton.Content = "Close";
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        if (_cancel != null)
            _cancel.Cancel();
        else
            Close();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
