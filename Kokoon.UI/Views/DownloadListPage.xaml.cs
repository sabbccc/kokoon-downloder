using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using Kokoon.UI.ViewModels;

namespace Kokoon.UI.Views;

/// <summary>
/// A standalone Page that displays the download list with detailed item cards.
/// Can be navigated to via a Frame or hosted directly in any container.
/// The <see cref="Downloads"/> collection is set externally by the parent.
/// </summary>
public sealed partial class DownloadListPage : Page
{
    /// <summary>
    /// The observable collection of download items displayed in the list.
    /// Populated externally by the hosting view or view model.
    /// </summary>
    public ObservableCollection<DownloadItemViewModel> Downloads { get; set; } = new();

    public DownloadListPage()
    {
        this.InitializeComponent();
    }
}
