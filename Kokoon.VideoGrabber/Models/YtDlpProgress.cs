namespace Kokoon.VideoGrabber.Models;

public class YtDlpProgress
{
    public double Percent { get; set; }
    public long DownloadedBytes { get; set; }
    public long? TotalBytes { get; set; }
    public double? SpeedBps { get; set; }
    public TimeSpan? Eta { get; set; }
    public string? Status { get; set; }
}
