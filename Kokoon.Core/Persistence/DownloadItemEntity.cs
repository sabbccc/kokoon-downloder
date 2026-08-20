using Kokoon.Core.Models;

namespace Kokoon.Core.Persistence;

/// <summary>
/// EF Core entity that maps a <see cref="DownloadItem"/> to the <c>DownloadItems</c>
/// table. Segments are stored as a JSON column via EF Core 8 owned-type JSON mapping.
/// </summary>
public class DownloadItemEntity
{
    /// <summary>Primary key — matches <see cref="DownloadItem.Id"/>.</summary>
    public Guid Id { get; set; }

    /// <summary>Source URL of the file being downloaded.</summary>
    public required string Url { get; set; }

    /// <summary>File name the download will be saved as.</summary>
    public required string FileName { get; set; }

    /// <summary>Absolute path to the destination directory on disk.</summary>
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

    /// <summary>Desired number of parallel segments (default 8).</summary>
    public int SegmentCount { get; set; } = 8;

    /// <summary>Whether the remote server supports byte-range requests.</summary>
    public bool SupportsRanges { get; set; }

    /// <summary>MIME type reported by the server, if known.</summary>
    public string? MimeType { get; set; }

    /// <summary>Referer header to send with segment requests, if required by the source.</summary>
    public string? Referer { get; set; }

    /// <summary>Download mode: Http, YtDlpExtracted, or YtDlpFull.</summary>
    public DownloadMode Mode { get; set; } = DownloadMode.Http;

    /// <summary>Video title from yt-dlp metadata.</summary>
    public string? VideoTitle { get; set; }

    /// <summary>Video thumbnail URL.</summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>Video duration in ticks (nullable).</summary>
    public long? DurationTicks { get; set; }

    /// <summary>Selected yt-dlp format ID.</summary>
    public string? FormatId { get; set; }

    /// <summary>Segments stored as a JSON column in SQLite.</summary>
    public List<SegmentEntity> Segments { get; set; } = new();

    /// <summary>
    /// Maps this entity to a domain <see cref="DownloadItem"/>.
    /// </summary>
    public DownloadItem ToDomainModel()
    {
        return new DownloadItem
        {
            Id = Id,
            Url = Url,
            FileName = FileName,
            SavePath = SavePath,
            TotalBytes = TotalBytes,
            DownloadedBytes = DownloadedBytes,
            Status = Status,
            CreatedAt = CreatedAt,
            CompletedAt = CompletedAt,
            SegmentCount = SegmentCount,
            SupportsRanges = SupportsRanges,
            MimeType = MimeType,
            Referer = Referer,
            Mode = Mode,
            VideoTitle = VideoTitle,
            ThumbnailUrl = ThumbnailUrl,
            Duration = DurationTicks.HasValue ? TimeSpan.FromTicks(DurationTicks.Value) : null,
            FormatId = FormatId,
            Segments = Segments.Select(s => s.ToDomainModel()).ToList()
        };
    }

    /// <summary>
    /// Creates a new entity from a domain <see cref="DownloadItem"/>.
    /// </summary>
    public static DownloadItemEntity FromDomainModel(DownloadItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new DownloadItemEntity
        {
            Id = item.Id,
            Url = item.Url,
            FileName = item.FileName,
            SavePath = item.SavePath,
            TotalBytes = item.TotalBytes,
            DownloadedBytes = item.DownloadedBytes,
            Status = item.Status,
            CreatedAt = item.CreatedAt,
            CompletedAt = item.CompletedAt,
            SegmentCount = item.SegmentCount,
            SupportsRanges = item.SupportsRanges,
            MimeType = item.MimeType,
            Referer = item.Referer,
            Mode = item.Mode,
            VideoTitle = item.VideoTitle,
            ThumbnailUrl = item.ThumbnailUrl,
            DurationTicks = item.Duration?.Ticks,
            FormatId = item.FormatId,
            Segments = item.Segments.Select(SegmentEntity.FromDomainModel).ToList()
        };
    }
}

/// <summary>
/// EF Core owned entity representing a single download segment,
/// stored inside the parent <see cref="DownloadItemEntity"/> JSON column.
/// </summary>
public class SegmentEntity
{
    /// <summary>Zero-based ordinal position of this segment.</summary>
    public int Index { get; set; }

    /// <summary>Inclusive start byte offset within the source file.</summary>
    public long StartByte { get; set; }

    /// <summary>Inclusive end byte offset within the source file.</summary>
    public long EndByte { get; set; }

    /// <summary>Number of bytes downloaded so far.</summary>
    public long BytesDownloaded { get; set; }

    /// <summary>Current lifecycle status of the segment.</summary>
    public SegmentStatus Status { get; set; } = SegmentStatus.Pending;

    /// <summary>Absolute path to the temporary file backing this segment.</summary>
    public string TempFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Maps this entity to a domain <see cref="Segment"/>.
    /// </summary>
    public Segment ToDomainModel()
    {
        return new Segment
        {
            Index = Index,
            StartByte = StartByte,
            EndByte = EndByte,
            BytesDownloaded = BytesDownloaded,
            Status = Status,
            TempFilePath = TempFilePath
        };
    }

    /// <summary>
    /// Creates a new entity from a domain <see cref="Segment"/>.
    /// </summary>
    public static SegmentEntity FromDomainModel(Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        return new SegmentEntity
        {
            Index = segment.Index,
            StartByte = segment.StartByte,
            EndByte = segment.EndByte,
            BytesDownloaded = segment.BytesDownloaded,
            Status = segment.Status,
            TempFilePath = segment.TempFilePath
        };
    }
}
