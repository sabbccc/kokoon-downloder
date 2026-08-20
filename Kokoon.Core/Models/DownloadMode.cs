namespace Kokoon.Core.Models;

/// <summary>
/// Determines how the download engine processes a <see cref="DownloadItem"/>.
/// </summary>
public enum DownloadMode
{
    /// <summary>Standard HTTP download via the segment engine.</summary>
    Http,

    /// <summary>yt-dlp extracted a direct URL; the segment engine downloads it.</summary>
    YtDlpExtracted,

    /// <summary>Full yt-dlp download (HLS/DASH fallback).</summary>
    YtDlpFull
}
