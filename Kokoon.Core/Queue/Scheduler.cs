using System.Collections.Concurrent;
using Kokoon.Core.Engine;
using Kokoon.Core.Models;
using Kokoon.Core.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kokoon.Core.Queue;

/// <summary>
/// Configuration options for the <see cref="DownloadScheduler"/>.
/// </summary>
public class SchedulerOptions
{
    /// <summary>Maximum number of downloads that can run concurrently. Default: 3.</summary>
    public int MaxConcurrentDownloads { get; set; } = 3;

    /// <summary>
    /// Global speed limit in bytes per second. 0 means unlimited.
    /// </summary>
    public long MaxGlobalSpeedBps { get; set; }

    /// <summary>
    /// Whether to automatically reload and re-enqueue incomplete downloads
    /// from the database when the scheduler starts. Default: <c>true</c>.
    /// </summary>
    public bool AutoResumeOnStartup { get; set; } = true;

    /// <summary>
    /// Optional scheduled start time. If set, downloads will only begin after
    /// this time of day. <c>null</c> means downloads start immediately.
    /// </summary>
    public TimeOnly? ScheduledStartTime { get; set; }
}

/// <summary>
/// Background service that manages the download lifecycle: dequeues items from
/// <see cref="IDownloadQueue"/>, runs them against <see cref="DownloadEngine"/>
/// with concurrency control, persists state, and exposes aggregate statistics.
/// </summary>
public class DownloadScheduler : IHostedService, IDisposable
{
    private readonly IDownloadQueue _queue;
    private readonly DownloadEngine _engine;
    private readonly IExternalDownloader? _externalDownloader;
    private readonly IDownloadRepository _repository;
    private readonly ILogger<DownloadScheduler> _logger;
    private readonly SchedulerOptions _options;

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeDownloads = new();
    private readonly ConcurrentDictionary<Guid, double> _activeSpeeds = new();

    /// <summary>
    /// Live item references for active downloads, keyed by id. YtDlpFull-mode downloads
    /// track TotalBytes/DownloadedBytes as plain in-memory running totals (they have no
    /// segment list to derive them from), so <see cref="SaveAllActiveProgressAsync"/> needs
    /// a way to read those live values at shutdown instead of the stale ones already in the
    /// database.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, DownloadItem> _activeItems = new();

    /// <summary>
    /// Ids for which <see cref="PauseDownload"/> (as opposed to <see cref="CancelDownload"/> or a
    /// scheduler/app shutdown) requested cancellation of the in-flight transfer. Consulted by
    /// <see cref="StartDownloadAsync"/>'s cancellation handling to decide whether the item should be
    /// re-registered with <see cref="_queue"/> as resumable, versus left as a plain cancelled/shutdown
    /// item the way it already was.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, byte> _pauseRequested = new();

    private CancellationTokenSource? _schedulerCts;
    private Task? _schedulerLoop;

    /// <summary>
    /// Fired when a download completes successfully.
    /// </summary>
    public event Action<DownloadItem>? OnDownloadCompleted;

    /// <summary>
    /// Fired when a download fails after exhausting retries.
    /// </summary>
    public event Action<DownloadItem, Exception>? OnDownloadFailed;

    /// <summary>
    /// Fired on every progress update from an active download segment.
    /// Provides the parent download item and the segment progress snapshot.
    /// Subscribers should not perform blocking work on this callback.
    /// </summary>
    public event Action<DownloadItem, SegmentProgress>? OnProgressUpdated;

    /// <summary>
    /// Sum of all active download speeds in bytes per second.
    /// </summary>
    public double TotalSpeedBps => _activeSpeeds.Values.Sum();

    /// <summary>
    /// Number of downloads currently in progress.
    /// </summary>
    public int ActiveCount => _activeDownloads.Count;

    /// <summary>
    /// Initializes a new instance of <see cref="DownloadScheduler"/>.
    /// </summary>
    public DownloadScheduler(
        IDownloadQueue queue,
        DownloadEngine engine,
        IDownloadRepository repository,
        ILogger<DownloadScheduler> logger,
        IOptions<SchedulerOptions> options,
        IExternalDownloader? externalDownloader = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _externalDownloader = externalDownloader;
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DownloadScheduler starting. MaxConcurrent={Max}, AutoResume={AutoResume}",
            _options.MaxConcurrentDownloads, _options.AutoResumeOnStartup);

        if (_options.AutoResumeOnStartup)
        {
            await ResumeIncompleteDownloadsAsync(cancellationToken).ConfigureAwait(false);
        }

        _schedulerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _schedulerLoop = Task.Run(() => RunSchedulerLoopAsync(_schedulerCts.Token), _schedulerCts.Token);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DownloadScheduler stopping. Saving progress for {Count} active downloads", _activeDownloads.Count);

        // Signal the scheduler loop to stop.
        _schedulerCts?.Cancel();

        // Save progress for all active downloads before cancelling them.
        await SaveAllActiveProgressAsync(cancellationToken).ConfigureAwait(false);

        // Cancel all active downloads.
        foreach (var (id, cts) in _activeDownloads)
        {
            _logger.LogInformation("Cancelling active download {Id}", id);
            cts.Cancel();
        }

        // Wait for the scheduler loop to exit.
        if (_schedulerLoop is not null)
        {
            try
            {
                await _schedulerLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        _activeDownloads.Clear();
        _activeSpeeds.Clear();
    }

    /// <summary>
    /// Cancels a specific active download by its identifier.
    /// </summary>
    /// <param name="id">The download identifier to cancel.</param>
    public void CancelDownload(Guid id)
    {
        if (_activeDownloads.TryRemove(id, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _activeSpeeds.TryRemove(id, out _);
            _logger.LogInformation("Download {Id} cancelled by user", id);
        }
    }

    /// <summary>
    /// Pauses a specific actively-downloading item by cancelling its in-flight segment transfers.
    /// Unlike <see cref="CancelDownload"/>, the item's partial data and persisted segment ranges are
    /// preserved and it is re-registered with <see cref="_queue"/> as resumable (see the
    /// <c>OperationCanceledException</c> handling in <see cref="StartDownloadAsync"/>), so it can
    /// continue from where it left off via <see cref="IDownloadQueue.Resume"/>. No-op if the item is
    /// not currently active (e.g. it is still sitting in the queue — see <see cref="IDownloadQueue.Pause"/>
    /// for that case).
    /// </summary>
    /// <param name="id">The download identifier to pause.</param>
    public void PauseDownload(Guid id)
    {
        if (_activeDownloads.TryRemove(id, out var cts))
        {
            _pauseRequested[id] = 0;
            cts.Cancel();
            cts.Dispose();
            _activeSpeeds.TryRemove(id, out _);
            _logger.LogInformation("Download {Id} paused by user", id);
        }
    }

    private async Task RunSchedulerLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Scheduler loop started");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);

                if (!IsWithinScheduledWindow())
                {
                    continue;
                }

                // Fill available slots.
                while (_activeDownloads.Count < _options.MaxConcurrentDownloads
                       && _queue.TryDequeue(out var item))
                {
                    if (item is null)
                    {
                        break;
                    }

                    _ = StartDownloadAsync(item, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in scheduler loop");
            }
        }

        _logger.LogDebug("Scheduler loop exited");
    }

    private async Task StartDownloadAsync(DownloadItem item, CancellationToken schedulerCt)
    {
        var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(schedulerCt);

        if (!_activeDownloads.TryAdd(item.Id, downloadCts))
        {
            downloadCts.Dispose();
            _logger.LogWarning("Download {Id} is already active, skipping", item.Id);
            return;
        }

        _activeItems[item.Id] = item;

        _logger.LogInformation("Starting download {Id}: {FileName} from {Url}", item.Id, item.FileName, item.Url);

        try
        {
            // Persist that we've started downloading.
            await _repository.UpdateStatusAsync(item.Id, DownloadStatus.Downloading, schedulerCt).ConfigureAwait(false);

            var progress = new Progress<SegmentProgress>(p =>
            {
                _activeSpeeds[item.Id] = p.SpeedBps;
                OnProgressUpdated?.Invoke(item, p);
            });

            if (item.Mode == DownloadMode.YtDlpFull)
            {
                if (_externalDownloader is null)
                    throw new InvalidOperationException("No external downloader registered for YtDlpFull mode.");

                if (item.Status is DownloadStatus.Paused or DownloadStatus.Failed)
                    await _externalDownloader.ResumeAsync(item, progress, downloadCts.Token).ConfigureAwait(false);
                else
                    await _externalDownloader.DownloadAsync(item, progress, downloadCts.Token).ConfigureAwait(false);
            }
            else
            {
                // Http and YtDlpExtracted both use the segment engine (extracted URLs are direct links).
                // Segments are only built once a download actually begins (see DownloadEngine.StartAsync /
                // BuildSegments), so a non-empty Segments list means this item was previously downloading
                // and must resume from its persisted per-segment byte offsets rather than rebuilding and
                // re-downloading every segment from scratch. We check Segments.Count rather than
                // item.Status here because IDownloadQueue.Resume() resets Status to Queued before
                // re-enqueuing a paused item (queued-then-paused and previously-active-then-paused items
                // alike), so Status alone can no longer distinguish a fresh start from a resume.
                if (item.Segments.Count > 0)
                    await _engine.ResumeAsync(item, progress, downloadCts.Token).ConfigureAwait(false);
                else
                    await _engine.StartAsync(item, progress, downloadCts.Token).ConfigureAwait(false);
            }

            // Download completed successfully.
            // For YtDlpFull downloads, yt-dlp reports per-stream sizes in its progress
            // output — when downloading a merged format (e.g. "137+bestaudio"), the last
            // progress line belongs to the audio stream, so item.TotalBytes would be the
            // audio size, not the final muxed file. Use the actual file on disk instead.
            if (item.Mode == DownloadMode.YtDlpFull)
            {
                var finalPath = Path.Combine(item.SavePath, item.FileName);
                if (File.Exists(finalPath))
                {
                    var fileSize = new FileInfo(finalPath).Length;
                    item.TotalBytes = fileSize;
                    item.DownloadedBytes = fileSize;
                }
            }

            await _repository.UpdateStatusAsync(item.Id, DownloadStatus.Completed, schedulerCt).ConfigureAwait(false);
            await PersistBytesAsync(item, schedulerCt).ConfigureAwait(false);

            _logger.LogInformation("Download {Id} completed: {FileName}", item.Id, item.FileName);
            OnDownloadCompleted?.Invoke(item);
        }
        catch (OperationCanceledException)
        {
            item.Status = DownloadStatus.Paused;
            await PersistProgressSafelyAsync(item, CancellationToken.None).ConfigureAwait(false);

            // Only re-register with the queue as resumable if this cancellation came from
            // PauseDownload. A plain CancelDownload (full cancel/remove) or a scheduler/app
            // shutdown cancels the same token without setting _pauseRequested, and must not
            // leave the item sitting in the queue's paused set.
            if (_pauseRequested.TryRemove(item.Id, out _))
            {
                _queue.MarkPaused(item);
            }

            _logger.LogInformation("Download {Id} was cancelled/paused", item.Id);
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Failed;
            await _repository.UpdateStatusAsync(item.Id, DownloadStatus.Failed, CancellationToken.None).ConfigureAwait(false);
            await PersistBytesAsync(item, CancellationToken.None).ConfigureAwait(false);

            _logger.LogError(ex, "Download {Id} failed: {FileName}", item.Id, item.FileName);
            OnDownloadFailed?.Invoke(item, ex);
        }
        finally
        {
            _activeDownloads.TryRemove(item.Id, out _);
            _activeItems.TryRemove(item.Id, out _);
            _activeSpeeds.TryRemove(item.Id, out _);
            downloadCts.Dispose();
        }
    }

    /// <summary>
    /// Persists byte-count progress using whichever representation matches how
    /// <paramref name="item"/> actually tracks it: segment-engine downloads (Http/
    /// YtDlpExtracted) via their per-segment breakdown, YtDlpFull downloads via their
    /// plain running totals (they have no segment list to derive DownloadedBytes from,
    /// and TotalBytes is only ever known in memory once yt-dlp's own progress output
    /// reports it — see YtDlpFullDownloadHandler).
    /// </summary>
    private async Task PersistBytesAsync(DownloadItem item, CancellationToken ct)
    {
        if (item.Mode == DownloadMode.YtDlpFull)
            await _repository.UpdateProgressAsync(item.Id, item.TotalBytes, item.DownloadedBytes, ct).ConfigureAwait(false);
        else
            await _repository.UpdateSegmentsAsync(item.Id, item.Segments, ct).ConfigureAwait(false);
    }

    private async Task ResumeIncompleteDownloadsAsync(CancellationToken ct)
    {
        try
        {
            var incompleteItems = await _repository.GetIncompleteAsync(ct).ConfigureAwait(false);

            _logger.LogInformation("Found {Count} incomplete downloads to resume", incompleteItems.Count);

            foreach (var entity in incompleteItems)
            {
                var domainItem = entity.ToDomainModel();
                _queue.Enqueue(domainItem);
                _logger.LogDebug("Re-enqueued incomplete download {Id}: {FileName}", domainItem.Id, domainItem.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume incomplete downloads from database");
        }
    }

    private bool IsWithinScheduledWindow()
    {
        if (_options.ScheduledStartTime is null)
        {
            return true;
        }

        var now = TimeOnly.FromDateTime(DateTime.Now);
        return now >= _options.ScheduledStartTime.Value;
    }

    private async Task SaveAllActiveProgressAsync(CancellationToken ct)
    {
        foreach (var (id, _) in _activeDownloads)
        {
            try
            {
                var entity = await _repository.GetByIdAsync(id, ct).ConfigureAwait(false);
                if (entity is not null)
                {
                    entity.Status = DownloadStatus.Paused;

                    // The entity just loaded from the database has whatever byte counts were
                    // last persisted — stale for a YtDlpFull download, since it only tracks
                    // TotalBytes/DownloadedBytes in memory otherwise. Pull the live values in
                    // before this save is the only chance to record them for a clean resume.
                    if (_activeItems.TryGetValue(id, out var liveItem) && liveItem.Mode == DownloadMode.YtDlpFull)
                    {
                        entity.TotalBytes = liveItem.TotalBytes;
                        entity.DownloadedBytes = liveItem.DownloadedBytes;
                    }

                    await _repository.UpdateAsync(entity, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save progress for download {Id} during shutdown", id);
            }
        }
    }

    private async Task PersistProgressSafelyAsync(DownloadItem item, CancellationToken ct)
    {
        try
        {
            await _repository.UpdateStatusAsync(item.Id, item.Status, ct).ConfigureAwait(false);
            await PersistBytesAsync(item, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist progress for download {Id}", item.Id);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _schedulerCts?.Cancel();
        _schedulerCts?.Dispose();

        foreach (var (_, cts) in _activeDownloads)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _activeDownloads.Clear();
        _activeSpeeds.Clear();

        GC.SuppressFinalize(this);
    }
}
