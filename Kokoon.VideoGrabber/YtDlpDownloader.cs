using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Kokoon.VideoGrabber.Models;
using Microsoft.Extensions.Logging;

namespace Kokoon.VideoGrabber;

public class YtDlpDownloader
{
    private readonly YtDlpManager _manager;
    private readonly ILogger<YtDlpDownloader> _logger;
    private readonly IVideoCookieAuthProvider _cookieAuthProvider;

    public YtDlpDownloader(YtDlpManager manager, ILogger<YtDlpDownloader> logger, IVideoCookieAuthProvider cookieAuthProvider)
    {
        _manager = manager;
        _logger = logger;
        _cookieAuthProvider = cookieAuthProvider;
    }

    public async Task DownloadAsync(
        string url,
        string outputPath,
        string? formatId = null,
        IProgress<YtDlpProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        if (!_manager.IsAvailable())
            throw new InvalidOperationException("yt-dlp is not available.");

        var exePath = _manager.GetExecutablePath();
        var ffmpegDir = Path.GetDirectoryName(_manager.GetFfmpegPath());

        var args = new List<string> { "--newline", "--progress", "--no-playlist", "--no-warnings" };

        if (ffmpegDir is not null && _manager.IsFfmpegAvailable())
            args.AddRange(new[] { "--ffmpeg-location", ffmpegDir });

        if (!string.IsNullOrEmpty(formatId))
        {
            args.Add("-f");
            args.Add(formatId);
        }

        // When formatId requests separate video+audio streams (e.g. "137+bestaudio"),
        // yt-dlp must mux them together. Without an explicit target, it silently upgrades
        // the container (e.g. to .mkv) whenever the chosen audio codec doesn't fit the
        // extension we asked for in -o (mp4 can't hold Opus via stream copy), and writes
        // the real merged file to "<outputPath>.mkv" instead of outputPath itself. Forcing
        // the merge format to match our extension keeps the final file at outputPath.
        var outputExt = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
        if (outputExt is "mp4" or "mkv" or "webm" or "ogg" or "flv")
        {
            args.Add("--merge-output-format");
            args.Add(outputExt);
        }

        var cookiesFromBrowser = _cookieAuthProvider.GetCookiesFromBrowser(url);
        if (!string.IsNullOrEmpty(cookiesFromBrowser))
            args.AddRange(new[] { "--cookies-from-browser", cookiesFromBrowser });

        args.Add("-o");
        args.Add(outputPath);
        args.Add("--continue");
        args.Add(url);

        _logger.LogInformation("Starting yt-dlp download: {Url} -> {Output}", url, outputPath);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        process.StandardInput.Close();

        using var registration = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* process may have already exited */ }
        });

        // Parse progress from stdout
        while (!process.StandardOutput.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;

            var parsed = ParseProgressLine(line);
            if (parsed is not null)
                progress?.Report(parsed);
        }

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            _logger.LogError("yt-dlp download failed (exit {ExitCode}): {StdErr}", process.ExitCode, stderr);
            throw new InvalidOperationException($"yt-dlp download failed: {stderr}");
        }

        _logger.LogInformation("yt-dlp download completed: {Output}", outputPath);
    }

    // yt-dlp progress line format:
    // [download]  42.3% of 125.67MiB at  5.23MiB/s ETA 00:15
    // Non-YouTube extractors (HLS/DASH/live streams routed through the full-download
    // fallback) frequently can't determine a total size or speed up front, so "of"/"at"
    // report "Unknown" instead of a number — those clauses are optional here so the line
    // still matches and reports a (partial) progress update instead of being dropped.
    // "of ~ 324.23MiB" (fragmented/HLS downloads report an approximate, growing total
    // prefixed with "~ " — note the space after the tilde, unlike the plain "of 314.82KiB"
    // case) — ~?\s* tolerates both, since a failed total match here also blocks speed/eta
    // from matching (the group boundaries are positional, not independently searchable).
    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+(?<pct>[\d.]+)%(?:\s+of\s+(?:~?\s*(?<total>[\d.]+)(?<unit>[KMG]iB)|Unknown))?(?:\s+at\s+(?:(?<speed>[\d.]+)(?<sunit>[KMG]iB)/s|Unknown\s+speed))?(?:\s+ETA\s+(?<eta>\S+))?",
        RegexOptions.Compiled);

    // Fragmented streams (HLS segments) with no byte size at all report fragment counts instead,
    // e.g. "[download] Downloading fragment 12 of 45".
    private static readonly Regex FragmentRegex = new(
        @"\[download\]\s+Downloading fragment\s+(?<frag>\d+)\s+of\s+(?<total>\d+)",
        RegexOptions.Compiled);

    private static YtDlpProgress? ParseProgressLine(string line)
    {
        var match = ProgressRegex.Match(line);
        if (match.Success)
        {
            var pct = double.TryParse(match.Groups["pct"].Value, CultureInfo.InvariantCulture, out var p) ? p : 0;

            long? totalBytes = null;
            if (match.Groups["total"].Success)
            {
                var totalVal = double.TryParse(match.Groups["total"].Value, CultureInfo.InvariantCulture, out var t) ? t : 0;
                totalBytes = (long)(totalVal * UnitMultiplier(match.Groups["unit"].Value));
            }

            double? speedBps = null;
            if (match.Groups["speed"].Success)
            {
                var speedVal = double.TryParse(match.Groups["speed"].Value, CultureInfo.InvariantCulture, out var s) ? s : 0;
                speedBps = speedVal * UnitMultiplier(match.Groups["sunit"].Value);
            }

            TimeSpan? eta = null;
            if (match.Groups["eta"].Success &&
                TimeSpan.TryParseExact(match.Groups["eta"].Value, new[] { @"mm\:ss", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var parsed))
                eta = parsed;

            return new YtDlpProgress
            {
                Percent = pct,
                TotalBytes = totalBytes,
                DownloadedBytes = totalBytes.HasValue ? (long)(totalBytes.Value * pct / 100.0) : 0,
                SpeedBps = speedBps,
                Eta = eta,
                Status = "downloading"
            };
        }

        var fragMatch = FragmentRegex.Match(line);
        if (fragMatch.Success)
        {
            var frag = int.Parse(fragMatch.Groups["frag"].Value, CultureInfo.InvariantCulture);
            var totalFrags = int.Parse(fragMatch.Groups["total"].Value, CultureInfo.InvariantCulture);

            return new YtDlpProgress
            {
                Percent = totalFrags > 0 ? frag * 100.0 / totalFrags : 0,
                TotalBytes = null,
                DownloadedBytes = 0,
                SpeedBps = null,
                Eta = null,
                Status = "downloading"
            };
        }

        return null;
    }

    private static double UnitMultiplier(string unit) => unit switch
    {
        "KiB" => 1024.0,
        "MiB" => 1024.0 * 1024,
        "GiB" => 1024.0 * 1024 * 1024,
        _ => 1.0
    };
}
