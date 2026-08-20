using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kokoon.Core.Models;
using Kokoon.Core.Queue;
using Kokoon.UI.Helpers;
using Windows.UI;

namespace Kokoon.UI.ViewModels;

public partial class DownloadItemViewModel : ObservableObject
{
    private readonly IDownloadQueue _queue;
    private readonly DownloadScheduler _scheduler;

    public DownloadItemViewModel(IDownloadQueue queue, DownloadScheduler scheduler)
    {
        _queue = queue;
        _scheduler = scheduler;
    }
    [ObservableProperty]
    private Guid id;

    [ObservableProperty]
    private string fileName = "";

    [ObservableProperty]
    private string url = "";

    [ObservableProperty]
    private string savePath = "";

    [ObservableProperty]
    private DownloadStatus status;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private string progressText = "";

    [ObservableProperty]
    private string speedText = "—";

    [ObservableProperty]
    private string etaText = "—";

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private string domainText = "";

    [ObservableProperty]
    private string fileSizeText = "";

    [ObservableProperty]
    private string fileIconGlyph = "";

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private bool isCompleted;

    [ObservableProperty]
    private bool isFailed;

    /// <summary>
    /// Set by <c>MainViewModel.RefreshFilteredDownloads</c> to mark the first completed
    /// item in the (status-sorted) filtered list, so the "COMPLETED" section label
    /// can render above just that one row.
    /// </summary>
    [ObservableProperty]
    private bool isFirstCompletedInGroup;

    [ObservableProperty]
    private string? videoTitle;

    [ObservableProperty]
    private string durationText = "";

    [ObservableProperty]
    private bool isVideoDownload;

    [ObservableProperty]
    private DownloadMode downloadMode;

    /// <summary>
    /// Observable collection of segment view models for the multi-segment progress display.
    /// </summary>
    public ObservableCollection<SegmentViewModel> Segments { get; } = new();

    /// <summary>
    /// True for the "compact row" card template — paused items, and queued items
    /// (which don't set <see cref="IsActive"/>/<see cref="IsPaused"/>/
    /// <see cref="IsCompleted"/>/<see cref="IsFailed"/>, so without this they'd match
    /// none of the four per-status templates and render as an invisible row).
    /// </summary>
    public bool IsCompactRow => IsPaused || (!IsActive && !IsCompleted && !IsFailed);

    /// <summary>
    /// Returns a neon color based on the current download status.
    /// </summary>
    public Color StatusColor => Status switch
    {
        DownloadStatus.Downloading or DownloadStatus.Connecting
            => Color.FromArgb(255, 0x4C, 0xA6, 0xFF),   // blue
        DownloadStatus.Completed
            => Color.FromArgb(255, 0x35, 0xD4, 0x8A),   // green
        DownloadStatus.Failed or DownloadStatus.Cancelled
            => Color.FromArgb(255, 0xF2, 0x55, 0x5A),   // pink
        DownloadStatus.Paused or DownloadStatus.Queued
            => Color.FromArgb(255, 0xF2, 0xA9, 0x3B),   // amber
        DownloadStatus.Assembling
            => Color.FromArgb(255, 0x8B, 0x7C, 0xF0),   // purple
        _ => Color.FromArgb(255, 0xF2, 0xA9, 0x3B),      // amber fallback
    };

    [RelayCommand]
    private void Pause()
    {
        // Handles an item still sitting in the queue (not yet dequeued).
        _queue.Pause(Id);
        // Handles an item that is actively downloading: stops its in-flight segment
        // transfers and re-registers it with the queue as resumable. No-op if the
        // item isn't currently active (i.e. the call above already handled it).
        _scheduler.PauseDownload(Id);
        Status = DownloadStatus.Paused;
    }

    [RelayCommand]
    private void Resume()
    {
        _queue.Resume(Id);
        Status = DownloadStatus.Queued;
    }

    [RelayCommand]
    private void Cancel()
    {
        _scheduler.CancelDownload(Id);
        _queue.Cancel(Id);
        Status = DownloadStatus.Cancelled;
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var filePath = Path.Combine(SavePath, FileName);
        if (File.Exists(filePath))
        {
            Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        else if (Directory.Exists(SavePath))
        {
            Process.Start("explorer.exe", $"\"{SavePath}\"");
        }
    }

    [RelayCommand]
    private void OpenFile()
    {
        var filePath = Path.Combine(SavePath, FileName);
        if (File.Exists(filePath))
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void Retry()
    {
        _queue.Resume(Id);
        Status = DownloadStatus.Queued;
    }

    [RelayCommand]
    private void CopyLink()
    {
        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(Url);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
    }

    /// <summary>
    /// Raised when the user wants to remove this download from the list.
    /// The bool parameter is <c>true</c> to also delete the downloaded file.
    /// </summary>
    public event Action<DownloadItemViewModel, bool>? RemoveRequested;

    [RelayCommand]
    private void Remove()
    {
        RemoveRequested?.Invoke(this, false);
    }

    [RelayCommand]
    private void DeleteWithFile()
    {
        RemoveRequested?.Invoke(this, true);
    }

    /// <summary>
    /// Called by the source generator when <see cref="Status"/> changes.
    /// Updates derived state flags and the human-readable status text.
    /// </summary>
    partial void OnStatusChanged(DownloadStatus value)
    {
        StatusText = value switch
        {
            DownloadStatus.Queued      => "Queued",
            DownloadStatus.Connecting  => "Connecting",
            DownloadStatus.Downloading => "Downloading",
            DownloadStatus.Assembling  => "Assembling",
            DownloadStatus.Completed   => "Completed",
            DownloadStatus.Paused      => "Paused",
            DownloadStatus.Failed      => "Failed",
            DownloadStatus.Cancelled   => "Cancelled",
            _ => "Unknown",
        };

        IsActive    = value is DownloadStatus.Downloading or DownloadStatus.Connecting or DownloadStatus.Assembling;
        IsPaused    = value is DownloadStatus.Paused;
        IsCompleted = value is DownloadStatus.Completed;
        IsFailed    = value is DownloadStatus.Failed or DownloadStatus.Cancelled;

        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(IsCompactRow));
    }

    /// <summary>
    /// Maps all properties from a domain <see cref="DownloadItem"/> and optional
    /// aggregate speed, synchronising the segments collection.
    /// </summary>
    /// <param name="item">The domain model to map from.</param>
    /// <param name="speedBps">Aggregate download speed in bytes per second (default 0).</param>
    public void UpdateFromModel(DownloadItem item, double speedBps = 0)
    {
        Id           = item.Id;
        FileName     = item.FileName;
        Url          = item.Url;
        SavePath     = item.SavePath;
        Status       = item.Status;
        Progress     = item.Progress;
        ProgressText = FormatHelper.FormatProgress(item.DownloadedBytes, item.TotalBytes);
        SpeedText    = speedBps > 0 ? FormatHelper.FormatSpeed(speedBps) : "—";
        EtaText      = FormatHelper.FormatEta(item.TotalBytes - item.DownloadedBytes, speedBps);
        DomainText   = FormatHelper.ExtractDomain(item.Url);
        FileSizeText = FormatHelper.FormatBytes(item.TotalBytes);
        FileIconGlyph = FormatHelper.GetFileIconGlyph(item.FileName);

        VideoTitle     = item.VideoTitle;
        DownloadMode   = item.Mode;
        IsVideoDownload = item.Mode is DownloadMode.YtDlpExtracted or DownloadMode.YtDlpFull;
        DurationText   = item.Duration.HasValue ? FormatDuration(item.Duration.Value) : "";

        SyncSegments(item.Segments, speedBps);
    }

    /// <summary>
    /// Adds, removes, or updates <see cref="SegmentViewModel"/> entries to match
    /// the current set of domain segments.
    /// </summary>
    private void SyncSegments(List<Segment> domainSegments, double totalSpeedBps)
    {
        while (Segments.Count > domainSegments.Count)
        {
            Segments.RemoveAt(Segments.Count - 1);
        }

        while (Segments.Count < domainSegments.Count)
        {
            Segments.Add(new SegmentViewModel());
        }

        var activeSegmentCount = domainSegments.Count(s => s.Status == SegmentStatus.Downloading);
        var perSegmentSpeed = activeSegmentCount > 0 ? totalSpeedBps / activeSegmentCount : 0;

        for (var i = 0; i < domainSegments.Count; i++)
        {
            var segment = domainSegments[i];
            var segmentSpeed = segment.Status == SegmentStatus.Downloading ? perSegmentSpeed : 0;
            Segments[i].Update(segment, segmentSpeed);
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        return $"{duration.Minutes}:{duration.Seconds:D2}";
    }
}
