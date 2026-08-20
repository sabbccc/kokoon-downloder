namespace Kokoon.Core.Exceptions;

/// <summary>
/// Thrown when a segment download fails after exhausting its retry policy,
/// wrapping the original transport-level exception with segment context.
/// </summary>
public class SegmentDownloadException : Exception
{
    /// <summary>Ordinal index of the segment that failed.</summary>
    public int SegmentIndex { get; }

    /// <summary>Source URL that was being downloaded when the failure occurred.</summary>
    public string Url { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="SegmentDownloadException"/>.
    /// </summary>
    /// <param name="segmentIndex">Ordinal index of the segment that failed.</param>
    /// <param name="url">Source URL that was being downloaded.</param>
    /// <param name="innerException">The underlying exception that caused the failure.</param>
    public SegmentDownloadException(int segmentIndex, string url, Exception innerException)
        : base($"Segment {segmentIndex} failed downloading '{url}': {innerException.Message}", innerException)
    {
        SegmentIndex = segmentIndex;
        Url = url;
    }
}
