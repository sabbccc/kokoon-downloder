namespace Kokoon.VideoGrabber.Models;

public class VideoInfo
{
    public string Title { get; set; } = string.Empty;
    public string? Uploader { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Description { get; set; }
    public List<VideoFormat> Formats { get; set; } = new();
    public bool IsPlaylist { get; set; }
    public int PlaylistCount { get; set; }
    public string OriginalUrl { get; set; } = string.Empty;
    public string? WebpageUrl { get; set; }
    public string? Extractor { get; set; }
}
