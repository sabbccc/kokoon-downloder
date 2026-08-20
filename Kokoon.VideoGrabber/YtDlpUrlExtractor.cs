using System.Diagnostics;
using Kokoon.VideoGrabber.Models;
using Microsoft.Extensions.Logging;

namespace Kokoon.VideoGrabber;

public class YtDlpUrlExtractor
{
    private readonly YtDlpManager _manager;
    private readonly ILogger<YtDlpUrlExtractor> _logger;

    public YtDlpUrlExtractor(YtDlpManager manager, ILogger<YtDlpUrlExtractor> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    public async Task<ExtractedUrl> ExtractAsync(
        string url, string? formatId = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);

        if (!_manager.IsAvailable())
            throw new InvalidOperationException("yt-dlp is not available.");

        var exePath = _manager.GetExecutablePath();

        var args = new List<string> { "-g", "--no-playlist", "--no-warnings" };

        if (!string.IsNullOrEmpty(formatId))
        {
            args.Add("-f");
            args.Add(formatId);
        }
        else
        {
            // Default: best video+audio, preferring mp4
            args.Add("-f");
            args.Add("bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best");
        }

        args.Add(url);

        _logger.LogInformation("Extracting direct URL for {Url} (format: {Format})", url, formatId ?? "auto");

        var (exitCode, stdout, stderr) = await RunProcessAsync(exePath, args, ct).ConfigureAwait(false);

        if (exitCode != 0)
        {
            _logger.LogError("yt-dlp URL extraction failed (exit {ExitCode}): {StdErr}", exitCode, stderr);
            throw new InvalidOperationException($"yt-dlp extraction failed: {stderr}");
        }

        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
            throw new InvalidOperationException("yt-dlp returned no URLs.");

        // Also get the filename
        var filenameArgs = new List<string> { "--get-filename", "--no-playlist", "--no-warnings" };
        if (!string.IsNullOrEmpty(formatId))
        {
            filenameArgs.Add("-f");
            filenameArgs.Add(formatId);
        }
        filenameArgs.Add(url);

        var (_, filenameOut, _) = await RunProcessAsync(exePath, filenameArgs, ct).ConfigureAwait(false);
        var fileName = filenameOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "video.mp4";

        var result = new ExtractedUrl
        {
            DirectUrl = lines[0],
            FileName = SanitizeFileName(fileName),
            FormatId = formatId,
            Extension = Path.GetExtension(fileName).TrimStart('.')
        };

        // Two URLs = separate video + audio streams that need muxing
        if (lines.Length >= 2)
        {
            result.AudioUrl = lines[1];
            result.NeedsMuxing = true;
        }

        return result;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
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
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in arguments)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return (process.ExitCode, stdout.Trim(), stderr.Trim());
    }
}
