using CommunityToolkit.Mvvm.ComponentModel;
using Kokoon.Core.Models;
using Windows.UI;

namespace Kokoon.UI.ViewModels;

/// <summary>
/// View model for a single download segment, exposing progress, speed,
/// and a color-coded visual indicator that cycles through a neon palette.
/// </summary>
public partial class SegmentViewModel : ObservableObject
{
    private static readonly Color[] NeonPalette =
    {
        Color.FromArgb(255, 0x4C, 0xA6, 0xFF),   // 0: blue
        Color.FromArgb(255, 0x8B, 0x7C, 0xF0),   // 1: purple
        Color.FromArgb(255, 0x35, 0xD4, 0x8A),   // 2: green
        Color.FromArgb(255, 0xF2, 0x55, 0x5A),   // 3: pink
        Color.FromArgb(255, 0xF2, 0xA9, 0x3B),   // 4: amber
    };

    [ObservableProperty]
    private int index;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private SegmentStatus status;

    [ObservableProperty]
    private string speedText = "—";

    /// <summary>
    /// Returns a neon color that cycles through the palette based on the segment index.
    /// </summary>
    public Color SegmentColor => NeonPalette[((Index % NeonPalette.Length) + NeonPalette.Length) % NeonPalette.Length];

    /// <summary>
    /// Updates all properties from a domain <see cref="Segment"/> and its current speed.
    /// </summary>
    /// <param name="segment">The domain segment model.</param>
    /// <param name="speedBps">Current download speed for this segment in bytes per second.</param>
    public void Update(Segment segment, double speedBps)
    {
        Index = segment.Index;
        Progress = segment.Progress;
        Status = segment.Status;
        SpeedText = speedBps > 0
            ? Helpers.FormatHelper.FormatSpeed(speedBps)
            : "—";

        OnPropertyChanged(nameof(SegmentColor));
    }
}
