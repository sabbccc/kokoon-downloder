using System.Diagnostics;
using Kokoon.UI.Helpers;
using Kokoon.UI.Settings;
using Kokoon.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kokoon.UI.Views;

/// <summary>
/// Settings page for configuring download defaults, speed limits,
/// video grabber, and appearance preferences.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private const string ThemeName = "NeonDark";

    private readonly ISettingsService _settingsService;

    /// <summary>
    /// Currently selected theme-mode pill index (0=Dark, 1=Light, 2=System).
    /// </summary>
    private int _selectedModeIndex;

    /// <summary>
    /// Default save path displayed in the text box; resolves to the user's Downloads folder.
    /// </summary>
    public string DefaultSavePath { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public SettingsPage()
    {
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        this.InitializeComponent();
        LoadSettings();

        // Pill colors are painted as plain snapshot brushes, not live {ThemeResource}
        // bindings, so they must be re-applied whenever the effective theme flips.
        this.ActualThemeChanged += (_, _) => UpdateThemePillVisuals();
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Current;

        // --- Download section ---
        if (!string.IsNullOrEmpty(settings.DefaultSavePath))
            DefaultSavePath = settings.DefaultSavePath;
        SavePathTextBox.Text = DefaultSavePath;

        SegmentCountSlider.Value = Math.Clamp(settings.DefaultSegmentCount, 1, 32);
        AutoStartToggle.IsOn = settings.AutoStartWithWindows;
        MinimizeToTrayToggle.IsOn = settings.MinimizeToTray;

        // --- Speed section ---
        SpeedLimitSlider.Value = Math.Clamp(settings.SpeedLimitBps / 1_000_000.0, 0, 100);

        // --- Video Grabber section ---
        var qualityTag = settings.PreferredVideoQuality.ToString();
        for (int i = 0; i < VideoQualityComboBox.Items.Count; i++)
        {
            if (VideoQualityComboBox.Items[i] is ComboBoxItem item &&
                item.Tag is string tag && tag == qualityTag)
            {
                VideoQualityComboBox.SelectedIndex = i;
                break;
            }
        }

        YtDlpFallbackToggle.IsOn = settings.YtDlpFallbackEnabled;
        AutoDetectVideoToggle.IsOn = settings.AutoDetectVideoUrls;

        // --- Appearance section ---
        SegmentColorsToggle.IsOn = settings.ShowSegmentColors;

        // Set the theme pill selection to match the saved base theme (without firing
        // a live preview — UpdateThemePillVisuals only updates visuals, it doesn't
        // call App.ApplyTheme).
        _selectedModeIndex = settings.BaseTheme switch
        {
            BaseThemeMode.Dark => 0,
            BaseThemeMode.Light => 1,
            BaseThemeMode.System => 2,
            _ => 0
        };
        UpdateThemePillVisuals();
    }

    // ── Save button ────────────────────────────────────────────────────

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var baseTheme = GetBaseThemeMode();
        var qualityTag = VideoQualityComboBox.SelectedItem is ComboBoxItem qItem
            ? qItem.Tag as string
            : null;

        var autoStartWithWindows = AutoStartToggle.IsOn;
        var minimizeToTray = MinimizeToTrayToggle.IsOn;
        var showSegmentColors = SegmentColorsToggle.IsOn;

        await _settingsService.SetAsync(s =>
        {
            // Download section
            s.DefaultSavePath = DefaultSavePath;
            s.DefaultSegmentCount = (int)SegmentCountSlider.Value;
            s.AutoStartWithWindows = autoStartWithWindows;
            s.MinimizeToTray = minimizeToTray;

            // Speed section
            s.SpeedLimitBps = (long)(SpeedLimitSlider.Value * 1_000_000);

            // Video Grabber section
            if (qualityTag != null && Enum.TryParse<PreferredVideoQuality>(qualityTag, out var quality))
                s.PreferredVideoQuality = quality;
            s.YtDlpFallbackEnabled = YtDlpFallbackToggle.IsOn;
            s.AutoDetectVideoUrls = AutoDetectVideoToggle.IsOn;

            // Appearance section
            s.Theme = ThemeName;
            s.BaseTheme = baseTheme;
            s.ShowSegmentColors = showSegmentColors;
        });

        AutoStartHelper.SetEnabled(autoStartWithWindows);

        // SetAsync uses ConfigureAwait(false) internally, so after the await we may be
        // on a threadpool thread — marshal back before touching WinRT/UI objects.
        DispatcherQueue.TryEnqueue(() =>
        {
            App.ApplyTheme(ThemeName, baseTheme);
            App.Services.GetRequiredService<MainViewModel>().ShowSegmentColors = showSegmentColors;
        });
    }

    // ── Live theme preview (no save) ───────────────────────────────────

    /// <summary>
    /// Handles a click on one of the Dark/Light/System theme pills: updates the
    /// selection and immediately live-previews via <see cref="App.ApplyTheme"/>.
    /// </summary>
    private void ThemeModePill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tagStr } && int.TryParse(tagStr, out var index))
        {
            _selectedModeIndex = index;
        }

        var mode = GetBaseThemeMode();
        App.ApplyTheme(ThemeName, mode);

        // App.ApplyTheme only updates MainWindow's root element. This page is hosted
        // inside a ContentDialog whose Popup is not a visual descendant of that root,
        // so it must be stamped with the resolved theme explicitly.
        if (FindAncestorContentDialog(this) is ContentDialog dialog &&
            App.MainWindow?.Content is FrameworkElement rootElement)
        {
            dialog.RequestedTheme = rootElement.ActualTheme;
        }

        // Must run after the theme switch above: UpdateThemePillVisuals reads
        // this.ActualTheme, which only reflects the new theme once RequestedTheme
        // has been applied.
        UpdateThemePillVisuals();
    }

    private static ContentDialog? FindAncestorContentDialog(DependencyObject start)
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is ContentDialog dialog)
                return dialog;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    /// <summary>
    /// Applies the selected/unselected visual treatment to the three theme pills.
    /// WinUI's default Button/ToggleButton "checked" visuals would pull
    /// <c>AccentFillColorDefaultBrush</c> (Windows accent color) rather than our
    /// purple <c>NeonBlueBrush</c>, so the selected look is applied manually here
    /// instead of relying on a control's built-in checked state.
    /// </summary>
    private void UpdateThemePillVisuals()
    {
        // Resolving these via Application.Current.Resources.MergedDictionaries /
        // ThemeDictionaries.TryGetValue at runtime is unreliable right after a theme
        // switch (WinUI ThemeDictionaries lazy-materialization), so the per-theme
        // values are mirrored here as literal Colors instead — keep in sync manually
        // with the Dark/Light ThemeDictionaries blocks in Kokoon.UI/Themes/NeonDark.xaml.
        bool isDark = ActualTheme == ElementTheme.Dark;

        var selectedBg     = isDark ? Color.FromArgb(0x1A, 0x6C, 0x5C, 0xE7) : Color.FromArgb(0x1A, 0x48, 0x34, 0xD4); // NeonBlue20
        var selectedAccent = isDark ? Color.FromArgb(0xFF, 0x6C, 0x5C, 0xE7) : Color.FromArgb(0xFF, 0x48, 0x34, 0xD4); // NeonBlueBrush
        var unselectedBg   = isDark ? Color.FromArgb(0xFF, 0x18, 0x18, 0x1D) : Color.FromArgb(0xFF, 0xFA, 0xFA, 0xFA); // BackgroundElevated
        var unselectedBorder = isDark ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0x00, 0x00, 0x00); // StrokeBrush
        var unselectedText = isDark ? Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x73, 0x00, 0x00, 0x00); // TextSecondary

        var pills = new[] { ThemePillDark, ThemePillLight, ThemePillSystem };
        for (int i = 0; i < pills.Length; i++)
        {
            var pill = pills[i];
            if (i == _selectedModeIndex)
            {
                pill.Background = new SolidColorBrush(selectedBg);
                pill.BorderBrush = new SolidColorBrush(selectedAccent);
                pill.Foreground = new SolidColorBrush(selectedAccent);
                pill.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            }
            else
            {
                pill.Background = new SolidColorBrush(unselectedBg);
                pill.BorderBrush = new SolidColorBrush(unselectedBorder);
                pill.Foreground = new SolidColorBrush(unselectedText);
                pill.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private BaseThemeMode GetBaseThemeMode() => _selectedModeIndex switch
    {
        1 => BaseThemeMode.Light,
        2 => BaseThemeMode.System,
        _ => BaseThemeMode.Dark,
    };

    // ── Folder picker ──────────────────────────────────────────────────

    /// <summary>
    /// Opens a folder picker so the user can choose a default download directory.
    /// Uses WinRT.Interop to attach the picker to the current window (required for WinUI 3).
    /// </summary>
    private async void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
        picker.FileTypeFilter.Add("*");

        // WinUI 3 requires initializing the picker with the window handle.
        var hwnd = GetCurrentWindowHandle();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            SavePathTextBox.Text = folder.Path;
            DefaultSavePath = folder.Path;
        }
    }

    /// <summary>
    /// Opens the current default save-path folder in File Explorer.
    /// </summary>
    private void OpenSavePathButton_Click(object sender, RoutedEventArgs e)
    {
        var folderPath = DefaultSavePath;

        if (string.IsNullOrEmpty(folderPath))
        {
            folderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        if (Directory.Exists(folderPath))
        {
            Process.Start("explorer.exe", $"\"{folderPath}\"");
        }
    }

    /// <summary>
    /// Retrieves the HWND of the application's main window.
    /// WinUI 3 pickers and dialogs require a window handle for initialization.
    /// </summary>
    private static nint GetCurrentWindowHandle()
    {
        var window = App.MainWindow
            ?? throw new InvalidOperationException("Cannot locate the main application window.");
        return WinRT.Interop.WindowNative.GetWindowHandle(window);
    }
}
