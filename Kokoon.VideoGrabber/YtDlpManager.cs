using System.Diagnostics;
using Kokoon.VideoGrabber.Models;

namespace Kokoon.VideoGrabber;

public class YtDlpManager
{
    private readonly string _appDirectory;

    public YtDlpManager()
    {
        _appDirectory = AppContext.BaseDirectory;
    }

    public string GetExecutablePath() =>
        Path.Combine(_appDirectory, "tools", "yt-dlp.exe");

    public string GetFfmpegPath() =>
        Path.Combine(_appDirectory, "tools", "ffmpeg.exe");

    public bool IsAvailable() =>
        File.Exists(GetExecutablePath());

    public bool IsFfmpegAvailable() =>
        File.Exists(GetFfmpegPath());

    public async Task<YtDlpUpdateResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (!IsAvailable())
        {
            return new YtDlpUpdateResult
            {
                Status = YtDlpUpdateStatus.Failed,
                ErrorMessage = "yt-dlp is not available."
            };
        }

        var exePath = GetExecutablePath();

        var (exitCode, stdout, stderr) = await RunProcessAsync(exePath, new[] { "-U" }, ct).ConfigureAwait(false);

        if (stdout.Contains("up to date", StringComparison.OrdinalIgnoreCase))
        {
            return new YtDlpUpdateResult { Status = YtDlpUpdateStatus.UpToDate };
        }

        var updatedIndex = stdout.IndexOf("Updated to", StringComparison.OrdinalIgnoreCase);
        if (updatedIndex >= 0)
        {
            return new YtDlpUpdateResult
            {
                Status = YtDlpUpdateStatus.Updated,
                NewVersion = ExtractUpdatedVersion(stdout, updatedIndex)
            };
        }

        return new YtDlpUpdateResult
        {
            Status = YtDlpUpdateStatus.Failed,
            ErrorMessage = !string.IsNullOrEmpty(stderr) ? stderr : stdout
        };
    }

    private static string ExtractUpdatedVersion(string stdout, int updatedIndex)
    {
        var afterPrefix = stdout[(updatedIndex + "Updated to".Length)..].TrimStart();
        var lineEnd = afterPrefix.IndexOfAny(new[] { '\r', '\n' });
        var line = lineEnd >= 0 ? afterPrefix[..lineEnd] : afterPrefix;

        var fromIndex = line.IndexOf(" from", StringComparison.OrdinalIgnoreCase);
        if (fromIndex >= 0)
            return line[..fromIndex].Trim();

        var whitespaceIndex = line.IndexOf(' ');
        return (whitespaceIndex >= 0 ? line[..whitespaceIndex] : line).Trim();
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName, IEnumerable<string> arguments, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in arguments)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return (process.ExitCode, stdout.Trim(), stderr.Trim());
    }
}
