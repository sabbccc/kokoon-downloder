using System.Net;
using Kokoon.Core.Exceptions;
using Kokoon.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kokoon.Core.Engine;

/// <summary>
/// Downloads a single byte-range segment of a file over HTTP, supporting resume,
/// retry with exponential backoff, and periodic progress reporting.
/// </summary>
public class SegmentDownloader
{
    private const int BufferSize = 81920;
    private const int MaxAttempts = 3;
    private static readonly TimeSpan ProgressReportInterval = TimeSpan.FromMilliseconds(500);
    private const int ProgressReportEveryNBuffers = 5;

    private readonly HttpClient _httpClient;
    private readonly ILogger<SegmentDownloader> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="SegmentDownloader"/>.
    /// </summary>
    /// <param name="httpClient">HTTP client used to issue range requests.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public SegmentDownloader(HttpClient httpClient, ILogger<SegmentDownloader> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Downloads the byte range described by <paramref name="segment"/>, writing to
    /// <see cref="Segment.TempFilePath"/>. Resumes automatically from
    /// <see cref="Segment.BytesDownloaded"/> when greater than zero, and retries
    /// transient failures up to 3 times with exponential backoff.
    /// </summary>
    /// <param name="job">The parent download job, used for URL and referer.</param>
    /// <param name="segment">The segment to download.</param>
    /// <param name="progress">Optional progress reporter; safe to pass null.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task DownloadSegmentAsync(
        DownloadItem job,
        Segment segment,
        IProgress<SegmentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(segment);

        segment.Status = SegmentStatus.Downloading;

        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await DownloadAttemptAsync(job, segment, progress, cancellationToken).ConfigureAwait(false);
                segment.Status = SegmentStatus.Completed;
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                lastException = ex;
                _logger.LogWarning(
                    ex,
                    "Segment {SegmentIndex} download attempt {Attempt}/{MaxAttempts} failed for {Url}",
                    segment.Index,
                    attempt,
                    MaxAttempts,
                    job.Url);

                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        segment.Status = SegmentStatus.Failed;
        throw new SegmentDownloadException(segment.Index, job.Url, lastException!);
    }

    private async Task DownloadAttemptAsync(
        DownloadItem job,
        Segment segment,
        IProgress<SegmentProgress>? progress,
        CancellationToken cancellationToken)
    {
        var rangeStart = segment.StartByte + segment.BytesDownloaded;

        using var request = new HttpRequestMessage(HttpMethod.Get, job.Url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(rangeStart, segment.EndByte);

        if (!string.IsNullOrEmpty(job.Referer))
        {
            request.Headers.Referrer = new Uri(job.Referer);
        }

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(
            segment.TempFilePath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true);

        if (segment.BytesDownloaded > 0)
        {
            fileStream.Seek(segment.BytesDownloaded, SeekOrigin.Begin);
        }

        var buffer = new byte[BufferSize];
        var buffersSinceLastReport = 0;
        var lastReportTime = DateTime.UtcNow;
        var bytesAtLastReport = segment.BytesDownloaded;

        int bytesRead;
        while ((bytesRead = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            segment.BytesDownloaded += bytesRead;
            buffersSinceLastReport++;

            var elapsed = DateTime.UtcNow - lastReportTime;
            if (progress is not null && (elapsed >= ProgressReportInterval || buffersSinceLastReport >= ProgressReportEveryNBuffers))
            {
                var speedBps = elapsed.TotalSeconds > 0
                    ? (segment.BytesDownloaded - bytesAtLastReport) / elapsed.TotalSeconds
                    : 0;

                progress.Report(new SegmentProgress(
                    job.Id,
                    segment.Index,
                    segment.BytesDownloaded,
                    segment.TotalBytes,
                    speedBps));

                lastReportTime = DateTime.UtcNow;
                bytesAtLastReport = segment.BytesDownloaded;
                buffersSinceLastReport = 0;
            }
        }

        await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
