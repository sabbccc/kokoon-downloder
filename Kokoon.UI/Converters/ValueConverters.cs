using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml;
using Kokoon.UI.Models;

namespace Kokoon.UI.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
        {
            if (parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase))
                return b ? Visibility.Collapsed : Visibility.Visible;

            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility v)
            return v == Visibility.Visible;
        return false;
    }
}

public sealed class ProgressToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double d)
            return d * 100.0;
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is double d)
            return d / 100.0;
        return 0.0;
    }
}

public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is Windows.UI.Color color)
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var count = value is int i ? i : 0;
        var invert = parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase);

        if (invert)
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;

        return count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}

public sealed class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is Kokoon.Core.Models.DownloadStatus status)
        {
            return status switch
            {
                Kokoon.Core.Models.DownloadStatus.Downloading or
                Kokoon.Core.Models.DownloadStatus.Connecting => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 0, 212, 255)),
                Kokoon.Core.Models.DownloadStatus.Completed => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 0, 255, 136)),
                Kokoon.Core.Models.DownloadStatus.Failed or
                Kokoon.Core.Models.DownloadStatus.Cancelled => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 255, 0, 128)),
                Kokoon.Core.Models.DownloadStatus.Paused or
                Kokoon.Core.Models.DownloadStatus.Queued => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 255, 184, 0)),
                Kokoon.Core.Models.DownloadStatus.Assembling => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 139, 92, 246)),
                _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(255, 122, 122, 154))
            };
        }
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Returns true when CurrentFilter matches the ConverterParameter string.
/// </summary>
public sealed class FilterMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DownloadFilterMode current && parameter is string target)
        {
            return Enum.TryParse<DownloadFilterMode>(target, out var mode) && current == mode;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
