namespace Kokoon.VideoGrabber.Models;

public enum YtDlpUpdateStatus
{
    UpToDate,
    Updated,
    Failed
}

public class YtDlpUpdateResult
{
    public YtDlpUpdateStatus Status { get; set; }
    public string? NewVersion { get; set; }
    public string? ErrorMessage { get; set; }
}
