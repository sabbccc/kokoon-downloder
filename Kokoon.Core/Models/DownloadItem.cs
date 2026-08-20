using System.Diagnostics.CodeAnalysis;

namespace Kokoon.Core.Models;

/// <summary>
/// Represents a single download job tracked by the engine, including all of its
/// segments, destination metadata, and lifecycle state.
/// </summary>
public class DownloadItem
{
    /// <summary>
    /// Parameterless constructor required by XAML type metadata generation.
    /// Prefer the object-initializer form that sets <see cref="Url"/>,
    /// <see cref="FileName"/>, and <see cref="SavePath"/> explicitly.
    /// </summary>
    [SetsRequiredMembers]
    public DownloadItem()
    {
        Url = string.Empty;
        FileName = string.Empty;
        SavePath = string.Empty;
    }

    /// <summary>Unique identifier for this download job.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Source URL of the file being downloaded.</summary>
    public required string Url { get; set; }

    /// <summary>File name the download will be saved as.</summary>
    public required string FileName { get; set; }

    /// <summary>Absolute path to the destination directory or file on disk.</summary>
    public required string SavePath { get; set; }

    /// <summary>Total size of the remote file in bytes, or 0 if unknown.</summary>
    public long TotalBytes { get; set; }

    /// <summary>Sum of bytes downloaded across all segments.</summary>
    public long DownloadedBytes { get; set; }

    /// <summary>Current lifecycle status of the download.</summary>
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;

    /// <summary>UTC timestamp the job was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp the job completed, if it has.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Desired number of parallel segments (default 8, configurable 1-32).</summary>
    public int SegmentCount { get; set; } = 8;

    /// <summary>Segments that make up this download.</summary>
    public List<Segment> Segments { get; set; } = new();

    /// <summary>Whether the remote server supports byte-range requests.</summary>
    public bool SupportsRanges { get; set; }

    /// <summary>MIME type reported by the server, if known.</summary>
    public string? MimeType { get; set; }

    /// <summary>Referer header to send with segment requests, if required by the source.</summary>
    public string? Referer { get; set; }

    /// <summary>Download mode: standard HTTP, yt-dlp extracted URL, or full yt-dlp download.</summary>
    public DownloadMode Mode { get; set; } = DownloadMode.Http;

    /// <summary>Video title from yt-dlp metadata, if this is a video download.</summary>
    public string? VideoTitle { get; set; }

    /// <summary>Video thumbnail URL from yt-dlp metadata.</summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>Video duration from yt-dlp metadata.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Selected yt-dlp format ID for video downloads.</summary>
    public string? FormatId { get; set; }

    /// <summary>
    /// Fractional completion of the overall download in the range [0, 1].
    /// Returns 0 when <see cref="TotalBytes"/> is unknown.
    /// </summary>
    public double Progress => TotalBytes == 0 ? 0 : (double)DownloadedBytes / TotalBytes;

    /// <summary>Whether the download is currently in a state that represents active work.</summary>
    public bool IsActive => Status is DownloadStatus.Connecting or DownloadStatus.Downloading or DownloadStatus.Assembling;
}
