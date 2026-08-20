using System.Text.Json.Serialization;

namespace Kokoon.UI.Settings;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PreferredVideoQuality
{
    Best,
    FullHD1080,
    HD720,
    SD480,
    SD360,
    AudioOnly
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BaseThemeMode
{
    Dark,
    Light,
    System
}

/// <summary>
/// Application settings model persisted to <c>settings.json</c>.
/// Default values are applied when a property is missing from the file.
/// </summary>
public class AppSettings
{
    /// <summary>Default download destination folder.</summary>
    public string DefaultSavePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads", "Kokoon");

    /// <summary>Number of parallel segments per download (1-32). Default: 8.</summary>
    public int DefaultSegmentCount { get; set; } = 8;

    /// <summary>Global speed limit in bytes per second. 0 = unlimited.</summary>
    public long SpeedLimitBps { get; set; }

    /// <summary>Whether to minimize to system tray instead of closing.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Whether to auto-start with Windows login.</summary>
    public bool AutoStartWithWindows { get; set; }

    /// <summary>Active theme name.</summary>
    public string Theme { get; set; } = "NeonDark";

    /// <summary>Base theme mode (Dark, Light, or System follow).</summary>
    public BaseThemeMode BaseTheme { get; set; } = BaseThemeMode.Dark;

    /// <summary>Maximum concurrent downloads. Default: 3.</summary>
    public int MaxConcurrentDownloads { get; set; } = 3;

    /// <summary>Preferred video quality when downloading via yt-dlp.</summary>
    public PreferredVideoQuality PreferredVideoQuality { get; set; } = PreferredVideoQuality.Best;

    /// <summary>Allow yt-dlp to handle the full download for HLS/DASH streams that can't use the segment engine.</summary>
    public bool YtDlpFallbackEnabled { get; set; } = true;

    /// <summary>Automatically show the video download panel when a video URL is detected in AddDownloadDialog.</summary>
    public bool AutoDetectVideoUrls { get; set; } = true;

    /// <summary>Whether to show the per-segment progress visualization on each download card.</summary>
    public bool ShowSegmentColors { get; set; } = true;

    /// <summary>Domains authorized to use the caller's logged-in browser session for yt-dlp cookie auth.</summary>
    public List<string> AuthorizedCookieDomains { get; set; } = new();
}
