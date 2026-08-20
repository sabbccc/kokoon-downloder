namespace Kokoon.Core.Models;

/// <summary>
/// Point-in-time progress snapshot for a single segment, delivered via
/// <see cref="IProgress{T}"/> during a download operation.
/// </summary>
/// <param name="JobId">Identifier of the parent <see cref="DownloadItem"/>.</param>
/// <param name="SegmentIndex">Ordinal index of the segment this snapshot belongs to.</param>
/// <param name="BytesDownloaded">Total bytes downloaded so far for this segment.</param>
/// <param name="TotalBytes">Total bytes this segment is responsible for.</param>
/// <param name="SpeedBps">Instantaneous download speed for this segment, in bytes per second.</param>
public record SegmentProgress(
    Guid JobId,
    int SegmentIndex,
    long BytesDownloaded,
    long TotalBytes,
    double SpeedBps);
