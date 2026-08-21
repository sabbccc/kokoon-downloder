using System.Collections.Concurrent;
using Kokoon.Core.Models;
using Kokoon.Core.Queue;
using Kokoon.UI.Helpers;
using Kokoon.UI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace Kokoon.UI.Services;

/// <summary>
/// Bridges download engine progress events to the UI layer. Subscribes to
/// <see cref="DownloadScheduler.OnProgressUpdated"/>, computes rolling-window
/// speed and ETA, then marshals updates to the correct
/// <see cref="DownloadItemViewModel"/> via <see cref="DispatcherQueue"/>.
/// </summary>
/// <remarks>
/// All speed calculations use a 3-second rolling window of <c>(timestamp, totalBytes)</c>
/// samples per download, ensuring the displayed speed is smooth and responsive
/// without blocking the download engine thread.
/// </remarks>
public sealed class DownloadProgressBridge : IDisposable
{
    /// <summary>Rolling window entry for speed calculation.</summary>
    private readonly record struct SpeedSample(long Timestamp, long TotalBytes);

    private const int RollingWindowSeconds = 3;
    private const int MaxSamplesPerWindow = 30; // ~10 samples/s × 3s

    private readonly DownloadScheduler _scheduler;
    private readonly MainViewModel _mainViewModel;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILogger<DownloadProgressBridge> _logger;

    /// <summary>Per-download rolling speed samples, keyed by download ID.</summary>
    private readonly ConcurrentDictionary<Guid, List<SpeedSample>> _speedWindows = new();

    /// <summary>Last known downloaded bytes per download, for speed timer ticks.</summary>
    private readonly ConcurrentDictionary<Guid, long> _lastBytes = new();

    /// <summary>
    /// Last <see cref="Environment.TickCount64"/> a UI dispatch was actually queued for a
    /// given download, used to throttle <see cref="HandleProgressUpdated"/>. SegmentDownloader
    /// reports progress every 500ms OR every 5 buffers (400KB) — on a fast connection the
    /// buffer-count trigger dominates and can fire well over 20 times/sec per segment, times up
    /// to 8 concurrent segments per download. That's far more UI updates than a progress readout
    /// needs and was contributing to visible jank, so UI dispatch (not the underlying byte/speed
    /// tracking, which stays accurate) is capped to <see cref="UiUpdateThrottleMs"/>.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, long> _lastUiDispatchTicks = new();

    private const long UiUpdateThrottleMs = 200; // ~5 Hz cap per download

    private bool _subscribed;
    private CancellationTokenSource? _timerCts;

    /// <summary>
    /// Initializes a new instance of <see cref="DownloadProgressBridge"/>.
    /// </summary>
    /// <param name="scheduler">The scheduler that emits progress events.</param>
    /// <param name="mainViewModel">The main VM whose <see cref="MainViewModel.AllDownloads"/>
    /// collection contains the VMs to update.</param>
    /// <param name="dispatcherQueue">The UI thread dispatcher for marshalling updates.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public DownloadProgressBridge(
        DownloadScheduler scheduler,
        MainViewModel mainViewModel,
        DispatcherQueue dispatcherQueue,
        ILogger<DownloadProgressBridge> logger)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Subscribes to scheduler events. Call once after DI is configured and
    /// the main window is created. Safe to call multiple times — subsequent
    /// calls are no-ops.
    /// </summary>
    public void Start()
    {
        if (_subscribed) return;

        _scheduler.OnProgressUpdated += HandleProgressUpdated;
        _scheduler.OnDownloadCompleted += HandleDownloadCompleted;
        _scheduler.OnDownloadFailed += HandleDownloadFailed;
        _subscribed = true;

        _logger.LogInformation("DownloadProgressBridge started");

        // Start the 1-second speed update loop.
        _timerCts = new CancellationTokenSource();
        _ = SpeedUpdateLoopAsync(_timerCts.Token);
    }

    private void HandleProgressUpdated(DownloadItem item, SegmentProgress progress)
    {
        // Compute speed on the thread-pool thread to avoid UI work.
        var speedBps = ComputeRollingSpeed(item.Id, item.DownloadedBytes);
        var remainingBytes = item.TotalBytes - item.DownloadedBytes;

        _lastBytes[item.Id] = item.DownloadedBytes;

        // Throttle UI dispatch to ~5 Hz per download — see _lastUiDispatchTicks doc comment.
        // Byte tracking and rolling-speed sampling above still run on every raw progress event.
        var now = Environment.TickCount64;
        var lastDispatch = _lastUiDispatchTicks.GetOrAdd(item.Id, 0L);
        if (now - lastDispatch < UiUpdateThrottleMs)
        {
            return;
        }

        _lastUiDispatchTicks[item.Id] = now;

        // Marshal the actual VM update to the UI thread. TryEnqueue is
        // non-blocking: if the UI thread is busy, the update is queued.
        _dispatcherQueue.TryEnqueue(() =>
        {
            var vm = FindViewModel(item.Id);
            if (vm is null) return;

            vm.Progress = item.Progress;
            vm.ProgressText = FormatHelper.FormatProgress(item.DownloadedBytes, item.TotalBytes);
            vm.SpeedText = speedBps > 0 ? FormatHelper.FormatSpeed(speedBps) : "—";
            vm.EtaText = FormatHelper.FormatEta(remainingBytes, speedBps);
            vm.Status = item.Status;
            if (item.TotalBytes > 0)
                vm.FileSizeText = FormatHelper.FormatBytes(item.TotalBytes);

            // Synchronise segment-level progress.
            UpdateSegments(vm, item, speedBps);

            // Update aggregate stats on the main VM.
            _mainViewModel.TotalSpeedText = FormatHelper.FormatSpeed(_scheduler.TotalSpeedBps);
            _mainViewModel.ActiveCount = _scheduler.ActiveCount;
        });
    }

    private void HandleDownloadCompleted(DownloadItem item)
    {
        // Remove speed history for this download.
        _speedWindows.TryRemove(item.Id, out _);
        _lastBytes.TryRemove(item.Id, out _);
        _lastUiDispatchTicks.TryRemove(item.Id, out _);

        _dispatcherQueue.TryEnqueue(() =>
        {
            var vm = FindViewModel(item.Id);
            if (vm is null) return;

            vm.Status = DownloadStatus.Completed;
            vm.Progress = 1.0;
            vm.SpeedText = "—";
            vm.EtaText = "—";
            vm.ProgressText = FormatHelper.FormatProgress(item.TotalBytes, item.TotalBytes);
            if (item.TotalBytes > 0)
                vm.FileSizeText = FormatHelper.FormatBytes(item.TotalBytes);

            _mainViewModel.UpdateCounts();
            _mainViewModel.RefreshFilteredDownloads();
            _mainViewModel.TotalSpeedText = FormatHelper.FormatSpeed(_scheduler.TotalSpeedBps);
        });
    }

    private void HandleDownloadFailed(DownloadItem item, Exception ex)
    {
        _speedWindows.TryRemove(item.Id, out _);
        _lastBytes.TryRemove(item.Id, out _);
        _lastUiDispatchTicks.TryRemove(item.Id, out _);

        _dispatcherQueue.TryEnqueue(() =>
        {
            var vm = FindViewModel(item.Id);
            if (vm is null) return;

            vm.Status = DownloadStatus.Failed;
            vm.SpeedText = "—";
            vm.EtaText = "—";

            _mainViewModel.UpdateCounts();
            _mainViewModel.RefreshFilteredDownloads();
            _mainViewModel.TotalSpeedText = FormatHelper.FormatSpeed(_scheduler.TotalSpeedBps);
        });
    }

    /// <summary>
    /// Calculates speed using a rolling 3-second window of (timestamp, totalBytes) samples.
    /// </summary>
    private double ComputeRollingSpeed(Guid downloadId, long currentBytes)
    {
        var now = Environment.TickCount64;
        var samples = _speedWindows.GetOrAdd(downloadId, _ => new List<SpeedSample>());

        lock (samples)
        {
            samples.Add(new SpeedSample(now, currentBytes));

            // Evict samples older than the rolling window.
            var cutoff = now - (RollingWindowSeconds * 1000L);
            samples.RemoveAll(s => s.Timestamp < cutoff);

            // Trim if we have too many samples to bound memory.
            while (samples.Count > MaxSamplesPerWindow)
            {
                samples.RemoveAt(0);
            }

            if (samples.Count < 2)
            {
                return 0;
            }

            var oldest = samples[0];
            var newest = samples[^1];
            var elapsedMs = newest.Timestamp - oldest.Timestamp;

            if (elapsedMs <= 0) return 0;

            var bytesDelta = newest.TotalBytes - oldest.TotalBytes;
            return bytesDelta / (elapsedMs / 1000.0);
        }
    }

    /// <summary>
    /// Updates segment-level VMs on the download item view model.
    /// Must be called on the UI thread.
    /// </summary>
    private static void UpdateSegments(DownloadItemViewModel vm, DownloadItem item, double totalSpeedBps)
    {
        // Ensure the segment VM collection is the right size.
        while (vm.Segments.Count > item.Segments.Count)
        {
            vm.Segments.RemoveAt(vm.Segments.Count - 1);
        }

        while (vm.Segments.Count < item.Segments.Count)
        {
            vm.Segments.Add(new SegmentViewModel());
        }

        // Distribute speed evenly across active segments for display.
        var activeSegments = item.Segments.Count(s => s.Status == SegmentStatus.Downloading);
        var perSegmentSpeed = activeSegments > 0 ? totalSpeedBps / activeSegments : 0;

        for (var i = 0; i < item.Segments.Count; i++)
        {
            var segment = item.Segments[i];
            var segSpeed = segment.Status == SegmentStatus.Downloading ? perSegmentSpeed : 0;
            vm.Segments[i].Update(segment, segSpeed);
        }
    }

    private DownloadItemViewModel? FindViewModel(Guid downloadId)
    {
        // Linear scan is fine for the typical download count (tens to low hundreds).
        foreach (var vm in _mainViewModel.AllDownloads)
        {
            if (vm.Id == downloadId) return vm;
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();

        if (_subscribed)
        {
            _scheduler.OnProgressUpdated -= HandleProgressUpdated;
            _scheduler.OnDownloadCompleted -= HandleDownloadCompleted;
            _scheduler.OnDownloadFailed -= HandleDownloadFailed;
            _subscribed = false;
        }

        _speedWindows.Clear();
        _lastBytes.Clear();
        _lastUiDispatchTicks.Clear();
    }

    /// <summary>
    /// Background loop that updates total and per-download speeds every second,
    /// ensuring the UI refresh...ets at a steady 1 Hz even between progress events.
    /// </summary>
    private async Task SpeedUpdateLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, ct).ConfigureAwait(false);

                var totalSpeed = _scheduler.TotalSpeedBps;

                _dispatcherQueue.TryEnqueue(() =>
                {
                    _mainViewModel.TotalSpeedText = totalSpeed > 0
                        ? FormatHelper.FormatSpeed(totalSpeed)
                        : "—";

                    foreach (var vm in _mainViewModel.AllDownloads)
                    {
                        if (!vm.IsActive) continue;

                        var speed = ComputeRollingSpeed(
                            vm.Id,
                            _lastBytes.GetValueOrDefault(vm.Id, 0));

                        vm.SpeedText = speed > 0
                            ? FormatHelper.FormatSpeed(speed)
                            : "—";
                    }
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
