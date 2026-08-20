namespace Kokoon.VideoGrabber.Models;

public class ExtractedUrl
{
    public string DirectUrl { get; set; } = string.Empty;
    public string? AudioUrl { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long? FileSize { get; set; }
    public bool SupportsRanges { get; set; }
    public bool NeedsMuxing { get; set; }
    public string? FormatId { get; set; }
    public string Extension { get; set; } = "mp4";
}
