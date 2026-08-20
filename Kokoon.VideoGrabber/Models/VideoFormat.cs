namespace Kokoon.VideoGrabber.Models;

public class VideoFormat
{
    public string FormatId { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public long? FileSize { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public string? DirectUrl { get; set; }
    public double? Fps { get; set; }
    public int? AudioBitrate { get; set; }
    public int? VideoBitrate { get; set; }
    public string? FormatNote { get; set; }

    public bool HasVideo => VideoCodec is not null && VideoCodec != "none";
    public bool HasAudio => AudioCodec is not null && AudioCodec != "none";
    public bool IsVideoOnly => HasVideo && !HasAudio;
    public bool IsAudioOnly => !HasVideo && HasAudio;

    public string DisplayLabel
    {
        get
        {
            var parts = new List<string>();
            if (Resolution is not null) parts.Add(Resolution);
            if (FormatNote is not null) parts.Add(FormatNote);
            parts.Add(Extension);
            if (FileSize is > 0) parts.Add(FormatFileSize(FileSize.Value));
            if (IsVideoOnly) parts.Add("video only");
            if (IsAudioOnly) parts.Add("audio only");
            return string.Join(" · ", parts);
        }
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
            >= 1024L * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            >= 1024L => $"{bytes / 1024.0:F0} KB",
            _ => $"{bytes} B"
        };
    }
}
