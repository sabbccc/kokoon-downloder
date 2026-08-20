namespace Kokoon.UI.Helpers;

/// <summary>
/// Utility methods for formatting byte sizes, speeds, and durations
/// into human-readable strings for the UI.
/// </summary>
public static class FormatHelper
{
    private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB" };

    /// <summary>
    /// Formats a byte count into a human-readable size string.
    /// Example: 1_500_000 → "1.43 MB"
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";

        var unitIndex = 0;
        var size = (double)bytes;

        while (size >= 1024 && unitIndex < SizeUnits.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{size:F0} {SizeUnits[unitIndex]}"
            : $"{size:F1} {SizeUnits[unitIndex]}";
    }

    /// <summary>
    /// Formats a speed in bytes per second into a human-readable string.
    /// Example: 13_000_000 → "12.4 MB/s"
    /// </summary>
    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "—";
        return $"{FormatBytes((long)bytesPerSecond)}/s";
    }

    /// <summary>
    /// Formats an ETA given remaining bytes and current speed.
    /// Returns "—" if speed is zero or calculation is not possible.
    /// </summary>
    public static string FormatEta(long remainingBytes, double speedBps)
    {
        if (speedBps <= 0 || remainingBytes <= 0) return "—";

        var seconds = remainingBytes / speedBps;

        if (seconds < 60)
            return $"ETA: {seconds:F0}s";
        if (seconds < 3600)
            return $"ETA: {seconds / 60:F0}m {seconds % 60:F0}s";

        var hours = (int)(seconds / 3600);
        var minutes = (int)((seconds % 3600) / 60);
        return $"ETA: {hours}h {minutes}m";
    }

    /// <summary>
    /// Formats a progress value with downloaded/total bytes.
    /// Example: "45.2 MB / 100 MB"
    /// </summary>
    public static string FormatProgress(long downloadedBytes, long totalBytes)
    {
        if (totalBytes <= 0)
            return FormatBytes(downloadedBytes);

        return $"{FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes)}";
    }

    /// <summary>
    /// Extracts the domain from a URL for display purposes.
    /// Example: "https://releases.ubuntu.com/22.04/ubuntu.iso" → "releases.ubuntu.com"
    /// </summary>
    public static string ExtractDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "—";

        try
        {
            return new Uri(url).Host;
        }
        catch
        {
            return "—";
        }
    }

    /// <summary>
    /// Gets a file extension icon glyph (Segoe Fluent Icons) based on extension.
    /// </summary>
    public static string GetFileIconGlyph(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return ""; // Generic file

        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch
        {
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "", // Archive
            ".exe" or ".msi" or ".appx"                  => "", // App
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv"=> "", // Video
            ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg"=> "", // Audio
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" => "", // Image
            ".pdf"                                        => "", // PDF
            ".doc" or ".docx"                             => "", // Document
            ".xls" or ".xlsx"                             => "", // Spreadsheet
            ".iso" or ".img"                              => "", // Disc
            ".torrent"                                    => "", // Network
            _                                             => ""  // Generic file
        };
    }
}
