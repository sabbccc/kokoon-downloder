using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kokoon.VideoGrabber.Models;
using Microsoft.Extensions.Logging;

namespace Kokoon.VideoGrabber;

public class YtDlpProbe
{
    private readonly YtDlpManager _manager;
    private readonly ILogger<YtDlpProbe> _logger;
    private readonly IVideoCookieAuthProvider _cookieAuthProvider;

    public YtDlpProbe(YtDlpManager manager, ILogger<YtDlpProbe> logger, IVideoCookieAuthProvider cookieAuthProvider)
    {
        _manager = manager;
        _logger = logger;
        _cookieAuthProvider = cookieAuthProvider;
    }

    public async Task<VideoInfo> ProbeAsync(string url, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);

        if (!_manager.IsAvailable())
            throw new InvalidOperationException("yt-dlp is not available. Ensure yt-dlp.exe is in the tools directory.");

        var exePath = _manager.GetExecutablePath();
        var ffmpegDir = Path.GetDirectoryName(_manager.GetFfmpegPath());

        // Quick sanity check — if even --version hangs, the problem is
        // launching yt-dlp itself, not the network call.
        _logger.LogInformation("Running yt-dlp --version sanity check...");
        try
        {
            var (vExit, vOut, vErr) = await RunProcessAsync(exePath, new[] { "--version" }, ct).ConfigureAwait(false);
            _logger.LogInformation("yt-dlp --version: exit={Exit} stdout={Out} stderr={Err}", vExit, vOut, vErr);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "yt-dlp --version failed — process launch is broken");
            throw;
        }

        var args = new List<string> { "--dump-json", "--no-download", "--no-playlist", "--no-warnings", "--no-check-certificates", "--no-update", "--socket-timeout", "15", "--verbose" };

        if (ffmpegDir is not null && _manager.IsFfmpegAvailable())
            args.AddRange(new[] { "--ffmpeg-location", ffmpegDir });

        var cookiesFromBrowser = _cookieAuthProvider.GetCookiesFromBrowser(url);
        if (!string.IsNullOrEmpty(cookiesFromBrowser))
            args.AddRange(new[] { "--cookies-from-browser", cookiesFromBrowser });

        args.Add(url);

        _logger.LogInformation("Probing URL: {Url}", url);

        var (exitCode, stdout, stderr) = await RunProcessAsync(exePath, args, ct).ConfigureAwait(false);

        if (exitCode != 0)
        {
            _logger.LogError("yt-dlp probe failed (exit {ExitCode}): {StdErr}", exitCode, stderr);
            throw new InvalidOperationException($"yt-dlp failed: {stderr}");
        }

        return ParseVideoInfo(stdout, url);
    }

    public async Task<VideoInfo> ProbePlaylistAsync(string url, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);

        if (!_manager.IsAvailable())
            throw new InvalidOperationException("yt-dlp is not available.");

        var exePath = _manager.GetExecutablePath();
        var args = new List<string> { "--flat-playlist", "--dump-json", "--no-download", "--no-warnings", url };

        var (exitCode, stdout, stderr) = await RunProcessAsync(exePath, args, ct).ConfigureAwait(false);

        if (exitCode != 0)
            throw new InvalidOperationException($"yt-dlp playlist probe failed: {stderr}");

        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return new VideoInfo
        {
            OriginalUrl = url,
            IsPlaylist = true,
            PlaylistCount = lines.Length,
            Title = $"Playlist ({lines.Length} videos)"
        };
    }

    private VideoInfo ParseVideoInfo(string json, string originalUrl)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        var raw = JsonSerializer.Deserialize<YtDlpJsonOutput>(json, options);
        if (raw is null)
            throw new InvalidOperationException("Failed to parse yt-dlp JSON output.");

        var info = new VideoInfo
        {
            Title = raw.Title ?? "Unknown",
            Uploader = raw.Uploader,
            Duration = raw.Duration.HasValue ? TimeSpan.FromSeconds(raw.Duration.Value) : null,
            ThumbnailUrl = raw.Thumbnail,
            Description = raw.Description,
            OriginalUrl = originalUrl,
            WebpageUrl = raw.WebpageUrl,
            Extractor = raw.Extractor,
            IsPlaylist = false
        };

        if (raw.Formats is not null)
        {
            foreach (var f in raw.Formats)
            {
                info.Formats.Add(new VideoFormat
                {
                    FormatId = f.FormatId ?? "",
                    Extension = f.Ext ?? "mp4",
                    Resolution = f.Resolution,
                    Width = f.Width,
                    Height = f.Height,
                    FileSize = f.Filesize ?? f.FilesizeApprox,
                    VideoCodec = f.Vcodec,
                    AudioCodec = f.Acodec,
                    DirectUrl = f.Url,
                    Fps = f.Fps,
                    AudioBitrate = f.Abr.HasValue ? (int)f.Abr.Value : null,
                    VideoBitrate = f.Vbr.HasValue ? (int)f.Vbr.Value : null,
                    FormatNote = f.FormatNote
                });
            }
        }

        return info;
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName, IEnumerable<string> arguments, CancellationToken ct)
    {
        // Use temp files for output instead of pipes. When running inside a
        // Chrome native messaging host, pipe handle inheritance causes
        // yt-dlp (PyInstaller) to hang indefinitely.
        var stdoutFile = Path.GetTempFileName();
        var stderrFile = Path.GetTempFileName();

        try
        {
            var escapedArgs = string.Join(' ', arguments.Select(QuoteArg));

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{fileName}\" {escapedArgs} >\"{stdoutFile}\" 2>\"{stderrFile}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true
            };

            _logger.LogDebug("Launching: {FileName} {Args}", fileName, escapedArgs);

            process.Start();
            process.StandardInput.Close();

            _logger.LogDebug("Process started (PID {Pid})", process.Id);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Read whatever yt-dlp wrote before timing out
                var partialStdout = "";
                var partialStderr = "";
                try { partialStdout = File.ReadAllText(stdoutFile); } catch { }
                try { partialStderr = File.ReadAllText(stderrFile); } catch { }
                _logger.LogError("Process timed out after 60s (PID {Pid}) — killing. stderr={StdErr}", process.Id, partialStderr);
                if (!string.IsNullOrEmpty(partialStdout))
                    _logger.LogDebug("Partial stdout ({Len} chars): {Out}", partialStdout.Length, partialStdout[..Math.Min(500, partialStdout.Length)]);
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("yt-dlp timed out after 60 seconds");
            }

            var stdout = await File.ReadAllTextAsync(stdoutFile, ct).ConfigureAwait(false);
            var stderr = await File.ReadAllTextAsync(stderrFile, ct).ConfigureAwait(false);

            _logger.LogDebug("Process exited (PID {Pid}, exit code {ExitCode}, stdout {StdOutLen} chars, stderr {StdErrLen} chars)",
                process.Id, process.ExitCode, stdout.Length, stderr.Length);

            return (process.ExitCode, stdout.Trim(), stderr.Trim());
        }
        finally
        {
            try { File.Delete(stdoutFile); } catch { }
            try { File.Delete(stderrFile); } catch { }
        }
    }

    private static string QuoteArg(string arg) =>
        arg.Contains(' ') || arg.Contains('&') || arg.Contains('"') || arg.Contains('^')
            ? $"\"{arg.Replace("\"", "\\\"")}\""
            : arg;

    // Internal model for deserializing yt-dlp --dump-json output
    private class YtDlpJsonOutput
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("uploader")]
        public string? Uploader { get; set; }

        [JsonPropertyName("duration")]
        public double? Duration { get; set; }

        [JsonPropertyName("thumbnail")]
        public string? Thumbnail { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("webpage_url")]
        public string? WebpageUrl { get; set; }

        [JsonPropertyName("extractor")]
        public string? Extractor { get; set; }

        [JsonPropertyName("formats")]
        public List<YtDlpFormat>? Formats { get; set; }
    }

    private class YtDlpFormat
    {
        [JsonPropertyName("format_id")]
        public string? FormatId { get; set; }

        [JsonPropertyName("ext")]
        public string? Ext { get; set; }

        [JsonPropertyName("resolution")]
        public string? Resolution { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        [JsonPropertyName("filesize")]
        public long? Filesize { get; set; }

        [JsonPropertyName("filesize_approx")]
        public long? FilesizeApprox { get; set; }

        [JsonPropertyName("vcodec")]
        public string? Vcodec { get; set; }

        [JsonPropertyName("acodec")]
        public string? Acodec { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("fps")]
        public double? Fps { get; set; }

        [JsonPropertyName("abr")]
        public double? Abr { get; set; }

        [JsonPropertyName("vbr")]
        public double? Vbr { get; set; }

        [JsonPropertyName("format_note")]
        public string? FormatNote { get; set; }
    }
}
