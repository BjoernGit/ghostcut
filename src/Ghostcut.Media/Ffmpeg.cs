using System.Diagnostics;
using System.Text;

namespace Ghostcut.Media;

/// <summary>Finds ffmpeg/ffprobe and runs them.</summary>
public sealed class Ffmpeg
{
    /// <summary>Environment variable pointing to a folder with ffmpeg.exe and ffprobe.exe.</summary>
    public const string PathVariable = "GHOSTCUT_FFMPEG";

    private Ffmpeg(string ffmpegPath, string ffprobePath)
    {
        FfmpegPath = ffmpegPath;
        FfprobePath = ffprobePath;
    }

    public string FfmpegPath { get; }

    public string FfprobePath { get; }

    /// <summary>
    /// Looks in <see cref="PathVariable"/>, next to the app (<c>ffmpeg/</c>), on PATH and in the
    /// winget package folder (a fresh winget install is not on PATH until the next login).
    /// </summary>
    public static Ffmpeg? TryLocate()
    {
        var exe = OperatingSystem.IsWindows() ? ".exe" : "";
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } custom)
            candidates.Add(custom);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "ffmpeg"));
        candidates.Add(AppContext.BaseDirectory);
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(dir.Trim('"'));
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(local, "Microsoft", "WinGet", "Links"));
            var packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(packages))
            {
                foreach (var package in Directory.EnumerateDirectories(packages, "*FFmpeg*"))
                    foreach (var bin in SafeEnumerate(package, "bin"))
                        candidates.Add(bin);
            }
        }

        foreach (var dir in candidates)
        {
            var ffmpeg = Path.Combine(dir, "ffmpeg" + exe);
            var ffprobe = Path.Combine(dir, "ffprobe" + exe);
            if (File.Exists(ffmpeg) && File.Exists(ffprobe))
                return new Ffmpeg(ffmpeg, ffprobe);
        }
        return null;
    }

    private static IEnumerable<string> SafeEnumerate(string root, string name)
    {
        try
        {
            return Directory.EnumerateDirectories(root, name, SearchOption.AllDirectories).ToList();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public static ProcessStartInfo StartInfo(string exe, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        return info;
    }

    /// <summary>Runs a tool to completion and returns stdout; throws with stderr on failure.</summary>
    public static async Task<string> RunAsync(string exe, IEnumerable<string> args, CancellationToken cancel = default)
    {
        using var process = new Process { StartInfo = StartInfo(exe, args) };
        process.Start();
        using var registration = cancel.Register(() => TryKill(process));
        var stdout = process.StandardOutput.ReadToEndAsync(cancel);
        var stderr = process.StandardError.ReadToEndAsync(cancel);
        await process.WaitForExitAsync(cancel).ConfigureAwait(false);
        var output = await stdout.ConfigureAwait(false);
        var errors = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new FfmpegException($"{Path.GetFileNameWithoutExtension(exe)} failed (exit {process.ExitCode}): {LastLines(errors, 6)}");
        return output;
    }

    public static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    public static string LastLines(string text, int count)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" | ", lines.Skip(Math.Max(0, lines.Length - count)));
    }

    /// <summary>Formats seconds for ffmpeg arguments (invariant, dot as decimal separator).</summary>
    public static string Seconds(TimeSpan time) =>
        time.TotalSeconds.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class FfmpegException(string message) : Exception(message);
