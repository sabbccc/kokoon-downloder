using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Kokoon.Core.Engine;
using Kokoon.Core.Models;
using Kokoon.Core.Persistence;
using Kokoon.Core.Queue;
using Kokoon.UI.Models;
using Kokoon.UI.Settings;

namespace Kokoon.UI.ViewModels;

/// <summary>
/// Top-level view model for the main window. Owns the full download list,
/// filter state, aggregate statistics, and commands for global actions.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly DownloadEngine _engine;
    private readonly IDownloadQueue _queue;
    private readonly DownloadScheduler _scheduler;
    private readonly IDownloadRepository _repository;

    [ObservableProperty]
    private ObservableCollection<DownloadItemViewModel> allDownloads = new();

    [ObservableProperty]
    private ObservableCollection<DownloadItemViewModel> filteredDownloads = new();

    [ObservableProperty]
    private string totalSpeedText = "—";

    [ObservableProperty]
    private int activeCount;

    [ObservableProperty]
    private int queuedCount;

    [ObservableProperty]
    private int completedCount;

    [ObservableProperty]
    private int failedCount;

    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private string storageUsedText = "—";

    [ObservableProperty]
    private bool isEngineRunning;

    [ObservableProperty]
    private DownloadFilterMode currentFilter = DownloadFilterMode.All;

    [ObservableProperty]
    private ObservableCollection<DownloadItemViewModel> selectedDownloads = new();

    [ObservableProperty]
    private bool showSegmentColors = true;

    /// <summary>
    /// Set by the view to provide a XamlRoot for dialogs. The VM never
    /// references UI types directly beyond this delegate.
    /// </summary>
    public Func<XamlRoot>? GetXamlRoot { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="MainViewModel"/>.
    /// </summary>
    public MainViewModel(
        DownloadEngine engine,
        IDownloadQueue queue,
        DownloadScheduler scheduler,
        IDownloadRepository repository,
        ISettingsService settingsService)
    {
        _engine     = engine ?? throw new ArgumentNullException(nameof(engine));
        _queue      = queue ?? throw new ArgumentNullException(nameof(queue));
        _scheduler  = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        ShowSegmentColors = (settingsService ?? throw new ArgumentNullException(nameof(settingsService)))
            .Current.ShowSegmentColors;
    }

    /// <summary>
    /// Subscribes to a download item view model's <see cref="DownloadItemViewModel.RemoveRequested"/>
    /// event so the main VM can handle remove-and-delete operations.
    /// </summary>
    public void TrackViewModel(DownloadItemViewModel vm)
    {
        vm.RemoveRequested += OnRemoveRequested;
    }

    private async void OnRemoveRequested(DownloadItemViewModel vm, bool deleteFile)
    {
        await RemoveDownloadAsync(vm, deleteFile);
    }

    /// <summary>
    /// Removes a single download from the database and UI list,
    /// optionally deleting the downloaded file.
    /// </summary>
    public async Task RemoveDownloadAsync(DownloadItemViewModel vm, bool deleteFile)
    {
        if (vm.IsActive)
        {
            _scheduler.CancelDownload(vm.Id);
            _queue.Cancel(vm.Id);
        }

        if (deleteFile)
        {
            TryDeleteFile(vm.SavePath, vm.FileName);
        }

        await _repository.DeleteAsync(vm.Id, CancellationToken.None);

        AllDownloads.Remove(vm);
        SelectedDownloads.Remove(vm);
        UpdateCounts();
        RefreshFilteredDownloads();
    }

    /// <summary>
    /// Removes all currently selected downloads from the database and list.
    /// </summary>
    [RelayCommand]
    private async Task RemoveSelected()
    {
        var items = SelectedDownloads.ToList();
        foreach (var vm in items)
        {
            if (vm.IsActive)
            {
                _scheduler.CancelDownload(vm.Id);
                _queue.Cancel(vm.Id);
            }

            await _repository.DeleteAsync(vm.Id, CancellationToken.None);
            AllDownloads.Remove(vm);
        }

        SelectedDownloads.Clear();
        UpdateCounts();
        RefreshFilteredDownloads();
    }

    /// <summary>
    /// Removes all currently selected downloads and also deletes their files.
    /// </summary>
    [RelayCommand]
    private async Task DeleteSelectedWithFile()
    {
        var items = SelectedDownloads.ToList();
        foreach (var vm in items)
        {
            if (vm.IsActive)
            {
                _scheduler.CancelDownload(vm.Id);
                _queue.Cancel(vm.Id);
            }

            TryDeleteFile(vm.SavePath, vm.FileName);
            await _repository.DeleteAsync(vm.Id, CancellationToken.None);
            AllDownloads.Remove(vm);
        }

        SelectedDownloads.Clear();
        UpdateCounts();
        RefreshFilteredDownloads();
    }

    /// <summary>
    /// Clears all completed items and deletes their downloaded files.
    /// </summary>
    [RelayCommand]
    private void ClearCompletedWithFiles()
    {
        var completed = AllDownloads.Where(d => d.IsCompleted).ToList();

        foreach (var item in completed)
        {
            TryDeleteFile(item.SavePath, item.FileName);
            AllDownloads.Remove(item);
        }

        UpdateCounts();
        RefreshFilteredDownloads();
    }

    private static void TryDeleteFile(string savePath, string fileName)
    {
        try
        {
            var filePath = Path.Combine(savePath, fileName);
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // Best-effort — file may be in use or already gone.
        }
    }

    /// <summary>
    /// Called by the source generator when <see cref="CurrentFilter"/> changes.
    /// Rebuilds the filtered downloads collection.
    /// </summary>
    partial void OnCurrentFilterChanged(DownloadFilterMode value)
    {
        SelectedDownloads.Clear();
        RefreshFilteredDownloads();
    }

    [RelayCommand]
    private async Task AddDownload()
    {
        var xamlRoot = GetXamlRoot?.Invoke();
        if (xamlRoot is null) return;

        var dialog = new Views.AddDownloadDialog { XamlRoot = xamlRoot };
        await ShowAddDownloadDialogAsync(dialog);
    }

    /// <summary>
    /// Empty-state "Paste URL" action: reads the clipboard, and if it looks
    /// like an http/https URL, opens <see cref="Views.AddDownloadDialog"/>
    /// pre-filled with it; otherwise just opens the dialog empty (same as
    /// "+ Add Download").
    /// </summary>
    [RelayCommand]
    private async Task PasteUrl()
    {
        var xamlRoot = GetXamlRoot?.Invoke();
        if (xamlRoot is null) return;

        var dialog = new Views.AddDownloadDialog { XamlRoot = xamlRoot };

        try
        {
            var clipboardContent = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (clipboardContent is not null &&
                clipboardContent.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                var text = (await clipboardContent.GetTextAsync())?.Trim();
                if (!string.IsNullOrEmpty(text) &&
                    Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "http" || uri.Scheme == "https"))
                {
                    dialog.SetInitialUrl(text);
                }
            }
        }
        catch
        {
            // Clipboard access can fail (empty clipboard, non-text content,
            // access denied) — fall back to opening the dialog empty.
        }

        await ShowAddDownloadDialogAsync(dialog);
    }

    /// <summary>
    /// Shows an already-constructed <see cref="Views.AddDownloadDialog"/> and,
    /// on a successful probe + accept, persists and enqueues the resulting
    /// <see cref="DownloadItem"/>. Shared by <see cref="AddDownload"/> and
    /// <see cref="PasteUrl"/>.
    /// </summary>
    private async Task ShowAddDownloadDialogAsync(Views.AddDownloadDialog dialog)
    {
        var result = await dialog.ShowAsync();

        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.None || dialog.Result is null)
            return;

        await PersistAndEnqueueAsync(dialog.Result, dialog.StartImmediately);
    }

    /// <summary>
    /// Persists a probed <see cref="DownloadItem"/> and enqueues it, then adds
    /// its view model to <see cref="AllDownloads"/>. Shared tail-end of both
    /// <see cref="ShowAddDownloadDialogAsync"/> (Add Download dialog) and
    /// <see cref="OpenVideoGrabber"/> (Video Grabber dialog) so there is one
    /// implementation of the persist/enqueue/track sequence.
    /// </summary>
    private async Task PersistAndEnqueueAsync(DownloadItem item, bool startImmediately)
    {
        var entity = DownloadItemEntity.FromDomainModel(item);
        await _repository.AddAsync(entity, CancellationToken.None);

        // "Download Now" jumps ahead of anything already sitting in the queue;
        // "Add to Queue" takes normal (lowest) priority.
        if (startImmediately)
            _queue.EnqueueWithPriority(item, priority: -1);
        else
            _queue.Enqueue(item);

        var vm = new DownloadItemViewModel(_queue, _scheduler);
        vm.UpdateFromModel(item);
        TrackViewModel(vm);
        AllDownloads.Add(vm);
        UpdateCounts();
        RefreshFilteredDownloads();
    }

    /// <summary>
    /// Pauses all currently active downloads by invoking each item's own
    /// pause command, so the queue/scheduler are kept in sync (matches
    /// what clicking an individual download's Pause button does).
    /// </summary>
    [RelayCommand]
    private void PauseAll()
    {
        foreach (var download in AllDownloads.Where(d => d.IsActive).ToList())
        {
            download.PauseCommand.Execute(null);
        }

        UpdateCounts();
        RefreshFilteredDownloads();
    }

    /// <summary>
    /// Resumes all currently paused downloads by invoking each item's own
    /// resume command, so the queue/scheduler are kept in sync (matches
    /// what clicking an individual download's Resume button does).
    /// </summary>
    [RelayCommand]
    private void ResumeAll()
    {
        foreach (var download in AllDownloads.Where(d => d.IsPaused).ToList())
        {
            download.ResumeCommand.Execute(null);
        }

        UpdateCounts();
        RefreshFilteredDownloads();
    }

    /// <summary>
    /// Removes all completed downloads from the list.
    /// </summary>
    [RelayCommand]
    private void ClearCompleted()
    {
        var completed = AllDownloads.Where(d => d.IsCompleted).ToList();

        foreach (var item in completed)
        {
            AllDownloads.Remove(item);
        }

        UpdateCounts();
        RefreshFilteredDownloads();
    }

    /// <summary>
    /// Opens the dedicated Video Grabber dialog (sidebar 🎬 icon) and, on a
    /// successful probe + accept, persists and enqueues the resulting video
    /// <see cref="DownloadItem"/> the same way <see cref="AddDownload"/> does
    /// for a video URL detected there.
    /// </summary>
    [RelayCommand]
    private async Task OpenVideoGrabber()
    {
        var xamlRoot = GetXamlRoot?.Invoke();
        if (xamlRoot is null) return;

        var dialog = new Views.VideoGrabberDialog { XamlRoot = xamlRoot };

        var result = await dialog.ShowAsync();

        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.None || dialog.Result is null)
            return;

        await PersistAndEnqueueAsync(dialog.Result, dialog.StartImmediately);
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        var xamlRoot = GetXamlRoot?.Invoke();
        if (xamlRoot is null) return;

        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "Settings",
            Content = new Views.SettingsPage(),
            CloseButtonText = "Close",
            XamlRoot = xamlRoot,
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close
        };

        // ContentDialog is hosted in its own Popup, which is not a visual
        // descendant of MainWindow's root Grid — so it does NOT inherit the
        // RequestedTheme that App.ApplyTheme sets there, and would otherwise
        // always render Dark (its unset default) regardless of the app's
        // current Dark/Light/System selection. Stamp the dialog with the
        // window's currently-resolved theme (ActualTheme already resolves
        // System mode to the concrete OS-effective value) so it opens
        // matching whatever the app is showing right now.
        if (App.MainWindow?.Content is FrameworkElement rootElement)
        {
            dialog.RequestedTheme = rootElement.ActualTheme;
        }

        await dialog.ShowAsync();
    }

    /// <summary>
    /// Applies the specified filter mode and refreshes the filtered collection.
    /// </summary>
    /// <param name="mode">The filter mode to apply.</param>
    [RelayCommand]
    private void ApplyFilter(DownloadFilterMode mode)
    {
        CurrentFilter = mode;
    }

    /// <summary>
    /// Rebuilds <see cref="FilteredDownloads"/> based on the current
    /// <see cref="CurrentFilter"/> value.
    /// </summary>
    /// <remarks>
    /// Also orders the result into status groups (active, then paused/queued, then
    /// completed, then failed) and flags the first completed item via
    /// <see cref="DownloadItemViewModel.IsFirstCompletedInGroup"/> so the "COMPLETED"
    /// section label renders exactly once, above that item.
    /// <see cref="Enumerable.OrderBy{TSource,TKey}"/> is a stable sort, so items keep
    /// their original relative order within each status group.
    /// </remarks>
    public void RefreshFilteredDownloads()
    {
        var filtered = CurrentFilter switch
        {
            DownloadFilterMode.Active    => AllDownloads.Where(d => d.IsActive),
            DownloadFilterMode.Queued    => AllDownloads.Where(d => d.Status == DownloadStatus.Queued),
            DownloadFilterMode.Completed => AllDownloads.Where(d => d.IsCompleted),
            DownloadFilterMode.Failed    => AllDownloads.Where(d => d.IsFailed),
            _                            => AllDownloads.AsEnumerable(),
        };

        var list = filtered.OrderBy(GetStatusGroupRank).ToList();

        FilteredDownloads.Clear();

        var completedGroupStarted = false;

        foreach (var item in list)
        {
            item.IsFirstCompletedInGroup = item.IsCompleted && !completedGroupStarted;
            if (item.IsFirstCompletedInGroup)
                completedGroupStarted = true;

            FilteredDownloads.Add(item);
        }
    }

    /// <summary>
    /// Status-group sort rank for the card list: active first, then paused/queued
    /// (anything that is none of active/completed/failed), then completed, then failed.
    /// </summary>
    private static int GetStatusGroupRank(DownloadItemViewModel item)
    {
        if (item.IsActive) return 0;
        if (item.IsCompleted) return 2;
        if (item.IsFailed) return 3;
        return 1;
    }

    /// <summary>
    /// Recalculates aggregate counts from <see cref="AllDownloads"/>.
    /// </summary>
    public void UpdateCounts()
    {
        ActiveCount    = AllDownloads.Count(d => d.IsActive);
        QueuedCount    = AllDownloads.Count(d => d.Status == DownloadStatus.Queued);
        CompletedCount = AllDownloads.Count(d => d.IsCompleted);
        FailedCount    = AllDownloads.Count(d => d.IsFailed);
        TotalCount     = AllDownloads.Count;
    }

    /// <summary>
    /// Loads all persisted downloads from the repository, creates view models,
    /// and populates <see cref="AllDownloads"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadDownloadsAsync(CancellationToken ct)
    {
        var entities = await _repository.GetAllAsync(ct).ConfigureAwait(false);

        AllDownloads.Clear();

        foreach (var entity in entities)
        {
            var domainItem = entity.ToDomainModel();
            var vm = new DownloadItemViewModel(_queue, _scheduler);
            vm.UpdateFromModel(domainItem);
            TrackViewModel(vm);
            AllDownloads.Add(vm);
        }

        UpdateCounts();
        RefreshFilteredDownloads();
    }
}
