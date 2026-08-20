using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Kokoon.Core.Models;
using Kokoon.VideoGrabber;
using Kokoon.VideoGrabber.Models;
using Kokoon.UI.Helpers;
using Kokoon.UI.Settings;

namespace Kokoon.UI.Views;

/// <summary>
/// Dedicated "Video Grabber" dialog, opened from the sidebar's 🎬 icon — a
/// standalone, explicit "I know this is a video" entry point distinct from
/// the video panel that appears automatically inside
/// <see cref="AddDownloadDialog"/> when a video URL is auto-detected there.
///
/// Probing and format-selection logic lives in <see cref="VideoProbeHelper"/>
/// and is shared with <see cref="AddDownloadDialog"/> rather than duplicated here.
/// </summary>
public sealed partial class VideoGrabberDialog : ContentDialog
{
    private VideoInfo? _videoInfo;
    private List<VideoFormat> _selectableFormats = new();
    private DownloadItem? _probedItem;
    private readonly YtDlpProbe _ytDlpProbe;
    private readonly ISettingsService _settingsService;
    private readonly string _defaultSavePath;

    public DownloadItem? Result => _probedItem;
    public bool StartImmediately { get; private set; }

    /// <summary>
    /// Pre-fills the URL field, e.g. when opening this dialog with a known
    /// video URL already in hand.
    /// </summary>
    public void SetInitialUrl(string url)
    {
        UrlTextBox.Text = url;
    }

    public VideoGrabberDialog()
    {
        this.InitializeComponent();
        _ytDlpProbe = App.Services.GetRequiredService<YtDlpProbe>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();

        var settings = _settingsService.Current;
        _defaultSavePath = !string.IsNullOrEmpty(settings.DefaultSavePath)
            ? settings.DefaultSavePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        YtDlpFallbackToggle.IsOn = settings.YtDlpFallbackEnabled;
    }

    private async void ProbeButton_Click(object sender, RoutedEventArgs e)
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
        ProbeErrorPanel.Visibility = Visibility.Collapsed;
        VideoInfoPanel.Visibility = Visibility.Collapsed;
        QualitySection.Visibility = Visibility.Collapsed;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;
        _videoInfo = null;
        _probedItem = null;

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

    private void ShowVideoProbeResults(string url)
    {
        if (_videoInfo is null) return;

        VideoTitleText.Text = _videoInfo.Title;
        VideoUploaderText.Text = _videoInfo.Uploader ?? "";

        if (_videoInfo.Duration.HasValue)
        {
            VideoDurationText.Text = VideoProbeHelper.FormatDuration(_videoInfo.Duration.Value);
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
                ThumbnailPlaceholderText.Visibility = Visibility.Collapsed;
            }
            catch
            {
                VideoThumbnail.Source = null;
                ThumbnailPlaceholderText.Visibility = Visibility.Visible;
            }
        }
        else
        {
            VideoThumbnail.Source = null;
            ThumbnailPlaceholderText.Visibility = Visibility.Visible;
        }

        // Reuse the exact same format-list-building logic AddDownloadDialog uses.
        _selectableFormats = VideoProbeHelper.GetSelectableFormats(_videoInfo);

        QualityComboBox.ItemsSource = _selectableFormats.Select(f => f.DisplayLabel).ToList();

        if (_selectableFormats.Count > 0)
        {
            var best = _selectableFormats[0];
            VideoQualityText.Text = best.Resolution ?? (best.Height is > 0 ? $"{best.Height}p" : "");
            VideoQualityBadge.Visibility = string.IsNullOrEmpty(VideoQualityText.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;

            QualitySection.Visibility = Visibility.Visible;
            QualityComboBox.SelectedIndex = 0;
        }
        else
        {
            VideoQualityBadge.Visibility = Visibility.Collapsed;
            QualitySection.Visibility = Visibility.Collapsed;
        }

        // Reuse the exact same preliminary-DownloadItem construction as AddDownloadDialog.
        _probedItem = VideoProbeHelper.BuildPreliminaryItem(url, _videoInfo, _defaultSavePath);
        if (_selectableFormats.Count > 0)
        {
            VideoProbeHelper.ApplySelectedFormat(_probedItem, _videoInfo, _selectableFormats[0]);
        }

        ProbeLoadingPanel.Visibility = Visibility.Collapsed;
        VideoInfoPanel.Visibility = Visibility.Visible;
        IsPrimaryButtonEnabled = true;
        IsSecondaryButtonEnabled = true;
    }

    private void QualityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = QualityComboBox.SelectedIndex;
        if (idx < 0 || idx >= _selectableFormats.Count || _probedItem is null || _videoInfo is null)
            return;

        VideoProbeHelper.ApplySelectedFormat(_probedItem, _videoInfo, _selectableFormats[idx]);
    }

    private void ShowProbeError(string message)
    {
        ProbeErrorText.Text = message;
        ProbeErrorPanel.Visibility = Visibility.Visible;
        VideoInfoPanel.Visibility = Visibility.Collapsed;
        QualitySection.Visibility = Visibility.Collapsed;
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

        _probedItem.Mode = VideoProbeHelper.ResolveMode(YtDlpFallbackToggle.IsOn);
    }
}
