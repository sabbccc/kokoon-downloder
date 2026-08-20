namespace Kokoon.Core.Models;

/// <summary>
/// Status of an individual download segment.
/// </summary>
public enum SegmentStatus
{
    /// <summary>The segment has not started downloading.</summary>
    Pending,

    /// <summary>The segment is actively downloading.</summary>
    Downloading,

    /// <summary>The segment has downloaded all of its assigned byte range.</summary>
    Completed,

    /// <summary>The segment failed after exhausting retries.</summary>
    Failed
}

/// <summary>
/// Represents a contiguous byte range of a download that is fetched independently
/// of other segments, enabling multi-connection parallel downloading.
/// </summary>
public record class Segment
{
    /// <summary>Zero-based ordinal position of this segment within the parent download.</summary>
    public required int Index { get; init; }

    /// <summary>Inclusive start byte offset of this segment within the source file.</summary>
    public required long StartByte { get; init; }

    /// <summary>Inclusive end byte offset of this segment within the source file.</summary>
    public required long EndByte { get; init; }

    /// <summary>Number of bytes downloaded so far for this segment.</summary>
    public long BytesDownloaded { get; set; }

    /// <summary>Current lifecycle status of the segment.</summary>
    public SegmentStatus Status { get; set; } = SegmentStatus.Pending;

    /// <summary>Absolute path to the temporary file backing this segment's downloaded bytes.</summary>
    public required string TempFilePath { get; init; }

    /// <summary>Total number of bytes this segment is responsible for.</summary>
    public long TotalBytes => EndByte - StartByte + 1;

    /// <summary>Fractional completion of this segment in the range [0, 1].</summary>
    public double Progress => TotalBytes == 0 ? 0 : (double)BytesDownloaded / TotalBytes;
}
