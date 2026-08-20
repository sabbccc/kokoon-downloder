using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Kokoon.Core.Engine;
using Kokoon.Core.Models;
using Kokoon.VideoGrabber;
using Kokoon.VideoGrabber.Models;
using Kokoon.UI.Helpers;
using Kokoon.UI.Settings;

namespace Kokoon.UI.Views;

public sealed partial class AddDownloadDialog : ContentDialog
{
    private DownloadItem? _probedItem;
    private VideoInfo? _videoInfo;
    private List<VideoFormat> _selectableFormats = new();
    private readonly DownloadEngine _engine;
    private readonly YtDlpProbe _ytDlpProbe;
    private readonly YtDlpUrlExtractor _ytDlpExtractor;
    private readonly ISettingsService _settingsService;

    public DownloadItem? Result => _probedItem;
    public bool StartImmediately { get; private set; }

    /// <summary>
    /// Pre-fills the URL field, e.g. when opening this dialog from the
    /// empty-state "Paste URL" action with a clipboard value already in hand.
    /// </summary>
    public void SetInitialUrl(string url)
    {
        UrlTextBox.Text = url;
    }

    public AddDownloadDialog()
    {
        this.InitializeComponent();
        _engine = App.Services.GetRequiredService<DownloadEngine>();
        _ytDlpProbe = App.Services.GetRequiredService<YtDlpProbe>();
        _ytDlpExtractor = App.Services.GetRequiredService<YtDlpUrlExtractor>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();

        var settings = _settingsService.Current;
        SavePathTextBox.Text = !string.IsNullOrEmpty(settings.DefaultSavePath)
            ? settings.DefaultSavePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        SegmentSlider.Value = Math.Clamp(settings.DefaultSegmentCount, 1, 32);
        YtDlpFallbackToggle.IsOn = settings.YtDlpFallbackEnabled;
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            ShowProbeError("Please enter a valid HTTP or HTTPS URL.");
            return;
        }

        ProbeLoadingPanel.Visibility = Visibility.Visible;
        ProbeResultsPanel.Visibility = Visibility.Collapsed;
        VideoInfoPanel.Visibility = Visibility.Collapsed;
        ProbeErrorPanel.Visibility = Visibility.Collapsed;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;
        _videoInfo = null;
        _probedItem = null;

        // Try HTTP probe first (works for direct file links).
        try
        {
            _probedItem = await _engine.ProbeAsync(url, CancellationToken.None);

            if (_probedItem.TotalBytes > 0 || _probedItem.MimeType is not null)
            {
                ShowHttpProbeResults();
                return;
            }
        }
        catch
        {
            // HTTP probe failed — fall through to yt-dlp probe.
        }

        // Try yt-dlp probe (works for video sites) — unless the user has
        // disabled automatic video-URL detection in Settings.
        if (!_settingsService.Current.AutoDetectVideoUrls)
        {
            ShowProbeError("Could not read this URL as a direct file link. Enable \"Auto-detect video URLs\" in Settings to try video-site detection.");
            ProbeLoadingPanel.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            _videoInfo = await _ytDlpProbe.ProbeAsync(url, CancellationToken.None);
            ShowVideoProbeResults(url);
        }
        catch (Exception ex)
        {
            ShowProbeError(ex.Message);
            _probedItem = null;
            _videoInfo = null;
        }
        finally
        {
            ProbeLoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowHttpProbeResults()
    {
        if (_probedItem is null) return;

        FileNameTextBox.Text = _probedItem.FileName;
        FileSizeText.Text = _probedItem.TotalBytes > 0
            ? FormatHelper.FormatBytes(_probedItem.TotalBytes)
            : "Unknown";

        var isLight = ActualTheme == ElementTheme.Light;

        if (_probedItem.SupportsRanges)
        {
            RangeSupportText.Text = "Supported";
            RangeSupportBadge.Background = isLight
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0x22, 0xC5, 0x5E))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0x1A, 0x4A, 0xDE, 0x80));
            RangeSupportText.Foreground = isLight
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x16, 0xA3, 0x4A))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x4A, 0xDE, 0x80));
        }
        else
        {
            RangeSupportText.Text = "Not Supported";
            RangeSupportBadge.Background = isLight
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0xD9, 0x94, 0x18))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0xF2, 0xA9, 0x3B));
            RangeSupportText.Foreground = isLight
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xD9, 0x94, 0x18))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF2, 0xA9, 0x3B));
        }

        SegmentSlider.IsEnabled = _probedItem.SupportsRanges;
        if (!_probedItem.SupportsRanges) SegmentSlider.Value = 1;

        ProbeLoadingPanel.Visibility = Visibility.Collapsed;
        ProbeResultsPanel.Visibility = Visibility.Visible;
        VideoInfoPanel.Visibility = Visibility.Collapsed;
        IsPrimaryButtonEnabled = true;
        IsSecondaryButtonEnabled = true;
    }

    private void ShowVideoProbeResults(string url)
    {
        if (_videoInfo is null) return;

        VideoTitleText.Text = _videoInfo.Title;
        VideoUploaderText.Text = _videoInfo.Uploader ?? "";

        if (_videoInfo.Duration.HasValue)
        {
            VideoDurationText.Text = FormatDuration(_videoInfo.Duration.Value);
            VideoDurationBadge.Visibility = Visibility.Visible;
        }
        else
        {
            VideoDurationBadge.Visibility = Visibility.Collapsed;
        }

        if (_videoInfo.ThumbnailUrl is not null)
        {
            try
            {
                VideoThumbnail.Source = new BitmapImage(new Uri(_videoInfo.ThumbnailUrl));
            }
            catch
            {
                VideoThumbnail.Source = null;
            }
        }
        else
        {
            VideoThumbnail.Source = null;
        }

        // Build selectable formats: prefer formats with both video+audio, then video-only.
        _selectableFormats = VideoProbeHelper.GetSelectableFormats(_videoInfo);

        FormatComboBox.Items.Clear();
        foreach (var fmt in _selectableFormats)
        {
            FormatComboBox.Items.Add(fmt.DisplayLabel);
        }

        if (FormatComboBox.Items.Count > 0)
            FormatComboBox.SelectedIndex = 0;

        // Create a preliminary DownloadItem for the video.
        _probedItem = VideoProbeHelper.BuildPreliminaryItem(url, _videoInfo, SavePathTextBox.Text);

        ProbeLoadingPanel.Visibility = Visibility.Collapsed;
        ProbeResultsPanel.Visibility = Visibility.Collapsed;
        VideoInfoPanel.Visibility = Visibility.Visible;
        IsPrimaryButtonEnabled = true;
        IsSecondaryButtonEnabled = true;
    }

    private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = FormatComboBox.SelectedIndex;
        if (idx < 0 || idx >= _selectableFormats.Count) return;

        var fmt = _selectableFormats[idx];
        VideoFileSizeText.Text = fmt.FileSize is > 0
            ? FormatHelper.FormatBytes(fmt.FileSize.Value)
            : "Unknown";

        if (_probedItem is not null && _videoInfo is not null)
        {
            VideoProbeHelper.ApplySelectedFormat(_probedItem, _videoInfo, fmt);
        }
    }

    private void ShowProbeError(string message)
    {
        ProbeErrorText.Text = message;
        ProbeErrorPanel.Visibility = Visibility.Visible;
        ProbeResultsPanel.Visibility = Visibility.Collapsed;
        VideoInfoPanel.Visibility = Visibility.Collapsed;
        AuthRetryPanel.Visibility = VideoProbeHelper.IsLikelyAuthError(message) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoginInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort — nothing more useful to do if the OS can't launch a browser for this URL.
        }
    }

    private async void RetryAfterLoginButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;

        ProbeLoadingPanel.Visibility = Visibility.Visible;
        ProbeErrorPanel.Visibility = Visibility.Collapsed;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;

        try
        {
            _videoInfo = await VideoProbeHelper.RetryProbeAfterLoginAsync(_ytDlpProbe, _settingsService, url);
            ShowVideoProbeResults(url);
        }
        catch (Exception ex)
        {
            ShowProbeError(ex.Message);
            _probedItem = null;
            _videoInfo = null;
        }
        finally
        {
            ProbeLoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ApplyUserOverrides();
        StartImmediately = true;
    }

    private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ApplyUserOverrides();
        StartImmediately = false;
    }

    private void ApplyUserOverrides()
    {
        if (_probedItem is null) return;

        // For video downloads, apply mode and format selection.
        if (_videoInfo is not null)
        {
            _probedItem.Mode = VideoProbeHelper.ResolveMode(YtDlpFallbackToggle.IsOn);
        }

        // For HTTP downloads, allow filename override.
        if (_videoInfo is null)
        {
            var editedFileName = FileNameTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(editedFileName))
                _probedItem.FileName = editedFileName;
        }

        var editedSavePath = SavePathTextBox.Text?.Trim();
        if (!string.IsNullOrEmpty(editedSavePath))
            _probedItem.SavePath = editedSavePath;

        _probedItem.SegmentCount = (int)SegmentSlider.Value;
    }

    private async void BrowseSavePathButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
        picker.FileTypeFilter.Add("*");

        var hwnd = GetCurrentWindowHandle();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            SavePathTextBox.Text = folder.Path;
        }
    }

    private static nint GetCurrentWindowHandle()
    {
        var window = App.MainWindow
            ?? throw new InvalidOperationException("Cannot locate the main application window.");
        return WinRT.Interop.WindowNative.GetWindowHandle(window);
    }

    private static string FormatDuration(TimeSpan duration) => VideoProbeHelper.FormatDuration(duration);
}
