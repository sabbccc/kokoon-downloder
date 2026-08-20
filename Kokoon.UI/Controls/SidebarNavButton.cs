using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Kokoon.UI.Controls;

public class SidebarNavButton : HandCursorButton
{
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(SidebarNavButton),
        new PropertyMetadata(false, OnIsSelectedChanged));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var button = (SidebarNavButton)d;
        var selected = (bool)e.NewValue;
        VisualStateManager.GoToState(button, selected ? "Selected" : "Unselected", true);
        button.Foreground = selected
            ? (Brush)Application.Current.Resources["NeonBlueBrush"]
            : (Brush)Application.Current.Resources["TextSecondary"];
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (IsSelected)
        {
            VisualStateManager.GoToState(this, "Selected", false);
            Foreground = (Brush)Application.Current.Resources["NeonBlueBrush"];
        }
    }
}
