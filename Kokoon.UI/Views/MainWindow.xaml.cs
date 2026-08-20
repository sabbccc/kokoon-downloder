using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Kokoon.UI.Models;
using Kokoon.UI.ViewModels;

namespace Kokoon.UI.Views;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        this.InitializeComponent();

        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        ViewModel.GetXamlRoot = () => this.Content.XamlRoot;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        Title = "Kokoon Downloader";
    }

    // ── Sidebar navigation click handlers ──────────────────────────────

    private void NavButton_AllDownloads_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyFilterCommand.Execute(DownloadFilterMode.All);
    }

    private void NavButton_Active_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyFilterCommand.Execute(DownloadFilterMode.Active);
    }

    private void NavButton_Queued_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyFilterCommand.Execute(DownloadFilterMode.Queued);
    }

    private void NavButton_Completed_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyFilterCommand.Execute(DownloadFilterMode.Completed);
    }

    private void NavButton_Failed_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyFilterCommand.Execute(DownloadFilterMode.Failed);
    }

    /// <summary>
    /// Opens the dedicated Video Grabber dialog.
    /// </summary>
    private void NavButton_VideoGrabber_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenVideoGrabberCommand.Execute(null);
    }

    // ── Download list selection handlers ──────────────────────────────

    /// <summary>
    /// Synchronises the ListView's selected items with the ViewModel's
    /// SelectedDownloads collection and toggles the batch toolbar.
    /// </summary>
    private void DownloadListView_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        var listView = sender as Microsoft.UI.Xaml.Controls.ListView;
        if (listView is null) return;

        ViewModel.SelectedDownloads.Clear();

        foreach (var item in listView.SelectedItems)
        {
            if (item is DownloadItemViewModel vm)
            {
                ViewModel.SelectedDownloads.Add(vm);
            }
        }

        var count = ViewModel.SelectedDownloads.Count;
        SelectionCountText.Text = $"{count} selected";
        BatchToolbar.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Download card template handlers ─────────────────────────────────

    /// <summary>
    /// Starts the looping diagonal-stripe animation on an active download card's
    /// progress-bar overlay. WinUI has no declarative "autoplay on load" trigger for
    /// a plain element's brush transform, so this is wired via the overlay
    /// ProgressBar's Loaded event (fired once per realized list item) instead of a
    /// VisualStateManager storyboard. Purely decorative — no functional binding.
    /// </summary>
    private void StripeOverlay_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ProgressBar { Foreground: LinearGradientBrush { Transform: TranslateTransform translate } })
            return;

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 14, // matches the brush's repeating tile size (EndPoint 14,14)
            Duration = new Duration(TimeSpan.FromSeconds(1)),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Storyboard.SetTarget(animation, translate);
        Storyboard.SetTargetProperty(animation, "X");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    /// <summary>
    /// Opens the "⋯" overflow menu (Cancel/Copy Link/Remove/Delete with file) on an
    /// active download card. Plain <see cref="Button"/> has no built-in Flyout
    /// property in WinUI (unlike DropDownButton/SplitButton), so the menu is set via
    /// <c>FlyoutBase.AttachedFlyout</c> in XAML and shown explicitly here.
    /// </summary>
    private void MoreActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            FlyoutBase.ShowAttachedFlyout(element);
    }
}
