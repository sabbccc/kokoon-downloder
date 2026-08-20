using Kokoon.UI.Settings;
using Kokoon.VideoGrabber;

namespace Kokoon.UI.Services;

/// <summary>
/// Resolves whether a URL's host is in the user's authorized cookie-domain list
/// and, if so, which browser to have yt-dlp pull cookies from.
/// </summary>
public class VideoCookieAuthProvider : IVideoCookieAuthProvider
{
    private readonly ISettingsService _settingsService;
    private readonly DefaultBrowserDetector _browserDetector;

    public VideoCookieAuthProvider(ISettingsService settingsService, DefaultBrowserDetector browserDetector)
    {
        _settingsService = settingsService;
        _browserDetector = browserDetector;
    }

    public string? GetCookiesFromBrowser(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        // Normalize "www." so a stored "youtube.com" matches both youtube.com and www.youtube.com.
        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];

        var authorized = _settingsService.Current.AuthorizedCookieDomains.Any(domain =>
            host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

        if (!authorized)
            return null;

        return _browserDetector.DetectYtDlpBrowserId();
    }
}
