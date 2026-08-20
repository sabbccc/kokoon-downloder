using Kokoon.Core.Models;

namespace Kokoon.Core.Engine;

/// <summary>
/// Download handler for items that cannot use the built-in segment engine
/// (e.g. HLS/DASH streams handled by yt-dlp).
/// </summary>
public interface IExternalDownloader
{
    /// <summary>Starts an external download for the given item.</summary>
    Task DownloadAsync(DownloadItem item, IProgress<SegmentProgress> progress, CancellationToken ct);

    /// <summary>Resumes a previously paused or failed external download.</summary>
    Task ResumeAsync(DownloadItem item, IProgress<SegmentProgress> progress, CancellationToken ct);
}
