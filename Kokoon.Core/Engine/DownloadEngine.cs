using System.Net;
using System.Net.Http.Headers;
using Kokoon.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kokoon.Core.Engine;

/// <summary>
/// Orchestrates the full lifecycle of a download: probing the remote server,
/// partitioning the file into segments, downloading them in parallel, and
/// assembling the result into the final output file.
/// </summary>
public class DownloadEngine
{
    private const long MinSegmentSizeBytes = 1024L * 1024L; // 1MB
    private const int DefaultMaxConcurrentSegments = 8;
    private const int MaxSegmentCount = 32;

    private readonly HttpClient _httpClient;
    private readonly SegmentDownloader _segmentDownloader;
    private readonly SegmentAssembler _segmentAssembler;
    private readonly ILogger<DownloadEngine> _logger;
    private readonly int _maxConcurrentSegments;

    /// <summary>
    /// Initializes a new instance of <see cref="DownloadEngine"/>.
    /// </summary>
    /// <param name="httpClient">HTTP client used for probing the remote server.</param>
    /// <param name="segmentDownloader">Downloader responsible for individual segments.</param>
    /// <param name="segmentAssembler">Assembler responsible for merging completed segments.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="maxConcurrentSegments">Maximum number of segments downloaded concurrently. Default 8.</param>
    public DownloadEngine(
        HttpClient httpClient,
        SegmentDownloader segmentDownloader,
        SegmentAssembler segmentAssembler,
        ILogger<DownloadEngine> logger,
        int maxConcurrentSegments = DefaultMaxConcurrentSegments)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _segmentDownloader = segmentDownloader ?? throw new ArgumentNullException(nameof(segmentDownloader));
        _segmentAssembler = segmentAssembler ?? throw new ArgumentNullException(nameof(segmentAssembler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxConcurrentSegments = maxConcurrentSegments < 1 ? DefaultMaxConcurrentSegments : maxConcurrentSegments;
    }

    /// <summary>
    /// Probes <paramref name="url"/> to determine file size, range support, and
    /// suggested file name, without downloading any content. Falls back to a
    /// ranged GET request if the server rejects HEAD requests.
    /// </summary>
    /// <param name="url">The URL to probe.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A new <see cref="DownloadItem"/> populated with probe results.</returns>
    public async Task<DownloadItem> ProbeAsync(string url, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);

        long totalBytes = 0;
        var supportsRanges = false;
        string? fileName = null;
        string? mimeType = null;
        var probed = false;

        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
            using var headResponse = await _httpClient
                .SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            headResponse.EnsureSuccessStatusCode();

            totalBytes = headResponse.Content.Headers.ContentLength ?? 0;
            supportsRanges = headResponse.Headers.AcceptRanges.Contains("bytes");
            fileName = ExtractFileName(headResponse);
            mimeType = headResponse.Content.Headers.ContentType?.MediaType;
            probed = true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HEAD probe failed for {Url}; falling back to ranged GET probe", url);
        }

        if (!probed)
        {
            try
            {
                using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
                getRequest.Headers.Range = new RangeHeaderValue(0, 0);

                using var getResponse = await _httpClient
                    .SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                getResponse.EnsureSuccessStatusCode();

                supportsRanges = getResponse.StatusCode == HttpStatusCode.PartialContent;
                totalBytes = getResponse.Content.Headers.ContentRange?.Length
                    ?? getResponse.Content.Headers.ContentLength
                    ?? 0;
                fileName = ExtractFileName(getResponse);
                mimeType = getResponse.Content.Headers.ContentType?.MediaType;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Fallback ranged GET probe also failed for {Url}", url);
            }
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = Path.GetFileName(new Uri(url).LocalPath);
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "download";
        }

        var defaultSavePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        return new DownloadItem
        {
            Url = url,
            FileName = fileName,
            SavePath = defaultSavePath,
            TotalBytes = totalBytes,
            SupportsRanges = supportsRanges,
            MimeType = mimeType,
            Status = DownloadStatus.Queued
        };
    }

    /// <summary>
    /// Starts downloading <paramref name="job"/> from scratch: partitions it into
    /// segments (or a single stream if ranges are unsupported), downloads them in
    /// parallel, and assembles the final file.
    /// </summary>
    /// <param name="job">The download job to start. Must have <see cref="DownloadItem.Url"/>,
    /// <see cref="DownloadItem.SavePath"/>, and <see cref="DownloadItem.FileName"/> set.</param>
    /// <param name="progress">Optional progress reporter; safe to pass null.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task StartAsync(
        DownloadItem job,
        IProgress<SegmentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        job.Status = DownloadStatus.Connecting;

        var tempDirectory = GetTempDirectory(job.Id);
        Directory.CreateDirectory(tempDirectory);

        if (job.Segments.Count == 0)
        {
            job.Segments = BuildSegments(job, tempDirectory);
        }

        await RunDownloadAndAssembleAsync(job, job.Segments, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resumes a previously started or interrupted download. Re-probes the URL to
    /// detect whether the server's range-support has changed, then re-downloads
    /// only the segments that have not yet completed, continuing from each
    /// segment's last downloaded byte offset.
    /// </summary>
    /// <param name="job">The download job to resume, including its existing segment state.</param>
    /// <param name="progress">Optional progress reporter; safe to pass null.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The updated <paramref name="job"/>.</returns>
    public async Task<DownloadItem> ResumeAsync(
        DownloadItem job,
        IProgress<SegmentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var probe = await ProbeAsync(job.Url, cancellationToken).ConfigureAwait(false);
        job.SupportsRanges = probe.SupportsRanges;

        if (job.TotalBytes <= 0)
        {
            job.TotalBytes = probe.TotalBytes;
        }

        var tempDirectory = GetTempDirectory(job.Id);
        Directory.CreateDirectory(tempDirectory);

        if (job.Segments.Count == 0)
        {
            job.Segments = BuildSegments(job, tempDirectory);
        }

        var pendingSegments = job.Segments.Where(s => s.Status != SegmentStatus.Completed).ToList();

        await RunDownloadAndAssembleAsync(job, pendingSegments, progress, cancellationToken).ConfigureAwait(false);

        return job;
    }

    private async Task RunDownloadAndAssembleAsync(
        DownloadItem job,
        IReadOnlyList<Segment> segmentsToDownload,
        IProgress<SegmentProgress>? progress,
        CancellationToken cancellationToken)
    {
        job.Status = DownloadStatus.Downloading;

        try
        {
            await DownloadSegmentsAsync(job, segmentsToDownload, progress, cancellationToken).ConfigureAwait(false);

            job.Status = DownloadStatus.Assembling;
            await _segmentAssembler.AssembleAsync(job, progress: null, cancellationToken).ConfigureAwait(false);

            job.Status = DownloadStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            job.Status = DownloadStatus.Cancelled;
            throw;
        }
        catch (Exception ex)
        {
            job.Status = DownloadStatus.Failed;
            _logger.LogError(ex, "Download {JobId} failed for {Url}", job.Id, job.Url);
            throw;
        }
    }

    private async Task DownloadSegmentsAsync(
        DownloadItem job,
        IReadOnlyList<Segment> segmentsToDownload,
        IProgress<SegmentProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (segmentsToDownload.Count == 0)
        {
            return;
        }

        using var semaphore = new SemaphoreSlim(Math.Min(_maxConcurrentSegments, segmentsToDownload.Count));
        var aggregatingProgress = new AggregatingProgress(job, progress);

        var tasks = segmentsToDownload.Select(async segment =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _segmentDownloader
                    .DownloadSegmentAsync(job, segment, aggregatingProgress, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static List<Segment> BuildSegments(DownloadItem job, string tempDirectory)
    {
        if (!job.SupportsRanges || job.TotalBytes <= 0)
        {
            return new List<Segment>
            {
                new()
                {
                    Index = 0,
                    StartByte = 0,
                    EndByte = Math.Max(job.TotalBytes - 1, 0),
                    TempFilePath = Path.Combine(tempDirectory, "part_0")
                }
            };
        }

        var requestedSegmentCount = Math.Clamp(job.SegmentCount, 1, MaxSegmentCount);
        var maxSegmentsBySize = Math.Max(1, (int)(job.TotalBytes / MinSegmentSizeBytes));
        var segmentCount = Math.Min(requestedSegmentCount, maxSegmentsBySize);

        var segments = new List<Segment>(segmentCount);
        var baseSegmentSize = job.TotalBytes / segmentCount;
        var start = 0L;

        for (var i = 0; i < segmentCount; i++)
        {
            var isLastSegment = i == segmentCount - 1;
            var end = isLastSegment ? job.TotalBytes - 1 : start + baseSegmentSize - 1;

            segments.Add(new Segment
            {
                Index = i,
                StartByte = start,
                EndByte = end,
                TempFilePath = Path.Combine(tempDirectory, $"part_{i}")
            });

            start = end + 1;
        }

        return segments;
    }

    private static string GetTempDirectory(Guid jobId) =>
        Path.Combine(Path.GetTempPath(), "KokoonDownloader", jobId.ToString());

    private static string? ExtractFileName(HttpResponseMessage response)
    {
        var contentDisposition = response.Content.Headers.ContentDisposition;
        var name = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
        return name?.Trim('"');
    }

    /// <summary>
    /// Wraps a caller-supplied progress reporter so that <see cref="DownloadItem.DownloadedBytes"/>
    /// stays in sync with the sum of all segment byte counts as segment progress is reported.
    /// </summary>
    private sealed class AggregatingProgress : IProgress<SegmentProgress>
    {
        private readonly DownloadItem _job;
        private readonly IProgress<SegmentProgress>? _inner;

        public AggregatingProgress(DownloadItem job, IProgress<SegmentProgress>? inner)
        {
            _job = job;
            _inner = inner;
        }

        public void Report(SegmentProgress value)
        {
            _job.DownloadedBytes = _job.Segments.Sum(s => s.BytesDownloaded);
            _inner?.Report(value);
        }
    }
}
