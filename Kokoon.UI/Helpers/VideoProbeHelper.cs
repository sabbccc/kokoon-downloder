using Kokoon.Core.Models;
using Kokoon.VideoGrabber;
using Kokoon.VideoGrabber.Models;
using Kokoon.UI.Settings;

namespace Kokoon.UI.Helpers;

/// <summary>
/// Shared yt-dlp probe-result -&gt; <see cref="DownloadItem"/> logic used by both
/// <see cref="Views.AddDownloadDialog"/> (auto-detected video URLs) and
/// <see cref="Views.VideoGrabberDialog"/> (explicit "Video Grabber" entry point),
/// so there is exactly one implementation of format selection / filename /
/// mode-resolution behavior instead of two copies that could drift apart.
/// </summary>
public static class VideoProbeHelper
{
    /// <summary>
    /// Builds the list of selectable formats from a probed <see cref="VideoInfo"/>:
    /// video-capable formats only, best resolution first, largest file size as tiebreak.
    /// </summary>
    public static List<VideoFormat> GetSelectableFormats(VideoInfo videoInfo)
    {
        return videoInfo.Formats
            .Where(f => f.HasVideo)
            .OrderByDescending(f => f.Height ?? 0)
            .ThenByDescending(f => f.FileSize ?? 0)
            .ToList();
    }

    /// <summary>
    /// Constructs the preliminary <see cref="DownloadItem"/> for a probed video,
    /// before a specific format has been selected.
    /// </summary>
    public static DownloadItem BuildPreliminaryItem(string url, VideoInfo videoInfo, string savePath)
    {
        return new DownloadItem
        {
            Url = url,
            FileName = SanitizeFileName(videoInfo.Title) + ".mp4",
            SavePath = savePath,
            VideoTitle = videoInfo.Title,
            ThumbnailUrl = videoInfo.ThumbnailUrl,
            Duration = videoInfo.Duration,
            Mode = DownloadMode.YtDlpExtracted
        };
    }

    /// <summary>
    /// Applies a user-selected <see cref="VideoFormat"/> to a preliminary
    /// <see cref="DownloadItem"/>: sets the format ID and updates the file
    /// name's extension to match.
    /// </summary>
    public static void ApplySelectedFormat(DownloadItem item, VideoInfo videoInfo, VideoFormat format)
    {
        // Video-only formats (common for YouTube's higher resolutions, which are DASH-only)
        // need to be paired with an audio stream, or yt-dlp downloads video with no audio track.
        item.FormatId = format.IsVideoOnly ? $"{format.FormatId}+bestaudio" : format.FormatId;
        item.FileName = SanitizeFileName(videoInfo.Title) + "." + format.Extension;

        // Set initial TotalBytes from probed format data so the UI shows a size
        // immediately. For video-only formats paired with bestaudio, sum both
        // streams for a closer estimate.
        long estimatedSize = format.FileSize ?? 0;
        if (format.IsVideoOnly)
        {
            var bestAudio = videoInfo.Formats
                .Where(f => f.IsAudioOnly && f.FileSize.HasValue)
                .OrderByDescending(f => f.FileSize!.Value)
                .FirstOrDefault();
            if (bestAudio?.FileSize is long audioSize)
                estimatedSize += audioSize;
        }
        if (estimatedSize > 0)
            item.TotalBytes = estimatedSize;
    }

    /// <summary>
    /// Resolves the final <see cref="DownloadMode"/> for a video download based on
    /// whether the user has opted into the full yt-dlp download fallback.
    /// </summary>
    public static DownloadMode ResolveMode(bool ytDlpFallbackEnabled)
        => ytDlpFallbackEnabled ? DownloadMode.YtDlpFull : DownloadMode.YtDlpExtracted;

    /// <summary>
    /// Formats a duration as "H:MM:SS" (or "M:SS" under an hour).
    /// </summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        return $"{duration.Minutes}:{duration.Seconds:D2}";
    }

    /// <summary>
    /// Replaces characters invalid in Windows file names with underscores.
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static readonly string[] AuthErrorKeywords =
    {
        "sign in", "log in", "login", "private video", "cookies", "not a bot",
        "403", "429", "age-restricted", "confirm your age", "members-only",
        "unauthorized", "authentication"
    };

    /// <summary>
    /// Heuristically detects whether a probe/download error message looks
    /// like it's caused by a login/auth wall rather than some other failure.
    /// </summary>
    public static bool IsLikelyAuthError(string message)
    {
        return AuthErrorKeywords.Any(keyword =>
            message.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Retries a yt-dlp probe after the user has logged into the site in their
    /// browser. The URL's host must be added to <c>AuthorizedCookieDomains</c>
    /// and saved BEFORE calling <see cref="YtDlpProbe.ProbeAsync"/> again,
    /// since <see cref="YtDlpProbe"/> only appends <c>--cookies-from-browser</c>
    /// when the host is already in that list — otherwise this retry would run
    /// without cookies and fail identically. If the retry still fails, the
    /// domain is removed again so a failed guess doesn't linger in settings.
    /// </summary>
    public static async Task<VideoInfo> RetryProbeAfterLoginAsync(
        YtDlpProbe ytDlpProbe, ISettingsService settingsService, string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return await ytDlpProbe.ProbeAsync(url, ct);

        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];

        var domains = settingsService.Current.AuthorizedCookieDomains;
        var alreadyAuthorized = domains.Any(d => host.Equals(d, StringComparison.OrdinalIgnoreCase));
        var added = false;

        if (!alreadyAuthorized)
        {
            domains.Add(host);
            added = true;
            await settingsService.SaveAsync(ct);
        }

        try
        {
            return await ytDlpProbe.ProbeAsync(url, ct);
        }
        catch
        {
            if (added)
            {
                domains.RemoveAll(d => host.Equals(d, StringComparison.OrdinalIgnoreCase));
                await settingsService.SaveAsync(ct);
            }
            throw;
        }
    }
}
