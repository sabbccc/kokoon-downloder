namespace Kokoon.VideoGrabber;

/// <summary>
/// Resolves whether a URL's host is authorized to use the caller's logged-in
/// browser session for yt-dlp cookie auth, without VideoGrabber needing to know
/// about app settings or the Windows registry.
/// </summary>
public interface IVideoCookieAuthProvider
{
    /// <summary>
    /// Returns the yt-dlp <c>--cookies-from-browser</c> identifier (e.g. "chrome",
    /// "edge", "firefox") to use for <paramref name="url"/>, or null if this host
    /// isn't authorized or no supported default browser could be detected.
    /// </summary>
    string? GetCookiesFromBrowser(string url);
}
