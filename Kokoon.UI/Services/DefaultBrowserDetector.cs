using Microsoft.Win32;

namespace Kokoon.UI.Services;

/// <summary>
/// Detects the user's default HTTP browser from the Windows registry and maps
/// it to a yt-dlp <c>--cookies-from-browser</c> identifier.
/// </summary>
public class DefaultBrowserDetector
{
    private const string UserChoiceKeyPath =
        @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice";

    /// <summary>Returns the yt-dlp browser identifier for the current default browser, or null if it can't be determined.</summary>
    public string? DetectYtDlpBrowserId()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserChoiceKeyPath);
            var progId = key?.GetValue("ProgId") as string;
            if (string.IsNullOrEmpty(progId))
                return null;

            // Exact ProgId strings vary by vendor/version (and Edge's is an AppX
            // package id, not a human-readable name), so substring matching is intentional.
            if (progId.Contains("chrome", StringComparison.OrdinalIgnoreCase))
                return "chrome";
            if (progId.Contains("edge", StringComparison.OrdinalIgnoreCase) ||
                progId.Contains("msedge", StringComparison.OrdinalIgnoreCase))
                return "edge";
            if (progId.Contains("firefox", StringComparison.OrdinalIgnoreCase))
                return "firefox";
            if (progId.Contains("brave", StringComparison.OrdinalIgnoreCase))
                return "brave";
            if (progId.Contains("vivaldi", StringComparison.OrdinalIgnoreCase))
                return "vivaldi";
            if (progId.Contains("opera", StringComparison.OrdinalIgnoreCase))
                return "opera";

            return null;
        }
        catch
        {
            return null;
        }
    }
}
