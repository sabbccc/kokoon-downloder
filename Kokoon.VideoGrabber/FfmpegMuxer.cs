using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Kokoon.VideoGrabber.Models;
using Microsoft.Extensions.Logging;

namespace Kokoon.VideoGrabber;

public class FfmpegMuxer
{
    private readonly YtDlpManager _manager;
    private readonly ILogger<FfmpegMuxer> _logger;

    public FfmpegMuxer(YtDlpManager manager, ILogger<FfmpegMuxer> logger)
    {
        _manager = manager;
        _logger = logger;
    }

    public async Task MuxAsync(
        string videoPath,
        string audioPath,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(videoPath);
        ArgumentException.ThrowIfNullOrEmpty(audioPath);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        if (!_manager.IsFfmpegAvailable())
            throw new InvalidOperationException("ffmpeg is not available. Ensure ffmpeg.exe is in the tools directory.");

        var ffmpegPath = _manager.GetFfmpegPath();

        _logger.LogInformation("Muxing {Video} + {Audio} -> {Output}", videoPath, audioPath, outputPath);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(videoPath);
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(audioPath);
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("copy");
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add(outputPath);

        process.Start();
        process.StandardInput.Close();

        using var registration = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* process may have already exited */ }
        });

        // ffmpeg outputs progress on stderr
        var duration = TimeSpan.Zero;
        while (!process.StandardError.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await process.StandardError.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;

            if (duration == TimeSpan.Zero)
            {
                var durationMatch = Regex.Match(line, @"Duration:\s+(\d{2}:\d{2}:\d{2}\.\d{2})");
                if (durationMatch.Success && TimeSpan.TryParseExact(
                    durationMatch.Groups[1].Value, @"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture, out var d))
                {
                    duration = d;
                }
            }

            var timeMatch = Regex.Match(line, @"time=(\d{2}:\d{2}:\d{2}\.\d{2})");
            if (timeMatch.Success && duration > TimeSpan.Zero &&
                TimeSpan.TryParseExact(timeMatch.Groups[1].Value, @"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture, out var current))
            {
                progress?.Report(current / duration);
            }
        }

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            _logger.LogError("ffmpeg muxing failed (exit {ExitCode}): {StdErr}", process.ExitCode, stderr);
            throw new InvalidOperationException($"ffmpeg muxing failed: {stderr}");
        }

        _logger.LogInformation("Muxing completed: {Output}", outputPath);

        // Clean up source files
        TryDelete(videoPath);
        TryDelete(audioPath);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp file {Path}", path);
        }
    }
}
