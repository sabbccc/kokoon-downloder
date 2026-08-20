using Kokoon.Core.Engine;
using Kokoon.Core.Models;
using Kokoon.VideoGrabber.Models;
using Microsoft.Extensions.Logging;

namespace Kokoon.VideoGrabber;

/// <summary>
/// Implements <see cref="IExternalDownloader"/> by delegating to yt-dlp
/// for HLS/DASH streams that the segment engine cannot handle directly.
/// </summary>
public class YtDlpFullDownloadHandler : IExternalDownloader
{
    private readonly YtDlpDownloader _downloader;
    private readonly YtDlpProbe _probe;
    private readonly ILogger<YtDlpFullDownloadHandler> _logger;

    public YtDlpFullDownloadHandler(YtDlpDownloader downloader, YtDlpProbe probe, ILogger<YtDlpFullDownloadHandler> logger)
    {
        _downloader = downloader;
        _probe = probe;
        _logger = logger;
    }

    public async Task DownloadAsync(DownloadItem item, IProgress<SegmentProgress> progress, CancellationToken ct)
    {
        var outputPath = Path.Combine(item.SavePath, item.FileName);
        _logger.LogInformation("Starting yt-dlp full download {Id}: {Url} -> {Output}", item.Id, item.Url, outputPath);

        item.Status = DownloadStatus.Downloading;

        var adapter = new Progress<YtDlpProgress>(p =>
        {
            item.TotalBytes = p.TotalBytes ?? item.TotalBytes;
            item.DownloadedBytes = p.DownloadedBytes;

            progress.Report(new SegmentProgress(
                item.Id,
                SegmentIndex: 0,
                p.DownloadedBytes,
                p.TotalBytes ?? 0,
                p.SpeedBps ?? 0));
        });

        await _downloader.DownloadAsync(item.Url, outputPath, item.FormatId, adapter, ct).ConfigureAwait(false);
    }

    public async Task ResumeAsync(DownloadItem item, IProgress<SegmentProgress> progress, CancellationToken ct)
    {
        // An abrupt app close (crash/kill, not a graceful pause) can lose the in-memory
        // TotalBytes before it's ever persisted, and a --continue resume on an
        // already-mostly-downloaded file can finish before emitting enough progress
        // lines to re-derive it — leaving the UI stuck at a permanent "0 B". Re-probe
        // once up front when the size is unknown so this download gets a real total.
        if (item.TotalBytes <= 0)
        {
            try
            {
                var info = await _probe.ProbeAsync(item.Url, ct).ConfigureAwait(false);
                var estimated = EstimateTotalBytes(info, item.FormatId);
                if (estimated > 0)
                    item.TotalBytes = estimated;
            }
            catch
            {
                // Best-effort — progress parsing during the download itself still
                // gets a chance to determine the size if this probe fails.
            }
        }

        // yt-dlp handles resume via --continue flag internally
        await DownloadAsync(item, progress, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sums the probed file sizes for each "+"-joined component of a merge format id
    /// (e.g. "137+bestaudio"). Selector keywords like "bestaudio"/"bestvideo" aren't
    /// literal format ids and won't match anything in <paramref name="info"/>, so
    /// those are approximated with the largest available format of the matching type
    /// instead of being left uncounted.
    /// </summary>
    private static long EstimateTotalBytes(VideoInfo info, string? formatId)
    {
        if (string.IsNullOrEmpty(formatId))
            return 0;

        long total = 0;
        foreach (var part in formatId.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var exact = info.Formats.FirstOrDefault(f => f.FormatId == part);
            if (exact?.FileSize is long size)
            {
                total += size;
                continue;
            }

            if (part.Contains("audio", StringComparison.OrdinalIgnoreCase))
                total += info.Formats.Where(f => f.IsAudioOnly).Select(f => f.FileSize ?? 0).DefaultIfEmpty(0).Max();
            else if (part.Contains("video", StringComparison.OrdinalIgnoreCase))
                total += info.Formats.Where(f => f.IsVideoOnly || f.HasVideo).Select(f => f.FileSize ?? 0).DefaultIfEmpty(0).Max();
        }

        return total;
    }
}
