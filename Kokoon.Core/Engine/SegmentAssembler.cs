using Kokoon.Core.Models;

namespace Kokoon.Core.Engine;

/// <summary>
/// Merges completed segment temp files into a single pre-allocated final file,
/// writing each segment at its correct byte offset without concatenation.
/// </summary>
public class SegmentAssembler
{
    private const int CopyBufferSize = 1024 * 1024; // 1MB

    /// <summary>
    /// Assembles all segments of <paramref name="job"/> into the final output file at
    /// <c>Path.Combine(job.SavePath, job.FileName)</c>. Temp files and the temp directory
    /// are deleted only after the assembly completes successfully; on failure they are
    /// preserved so assembly can be retried.
    /// </summary>
    /// <param name="job">The download job whose segments should be assembled.</param>
    /// <param name="progress">Optional progress reporter (0.0-1.0); safe to pass null.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task AssembleAsync(
        DownloadItem job,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Segments.Count == 0)
        {
            throw new InvalidOperationException($"Download job {job.Id} has no segments to assemble.");
        }

        var finalFilePath = Path.Combine(job.SavePath, job.FileName);
        var directory = Path.GetDirectoryName(finalFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var orderedSegments = job.Segments.OrderBy(s => s.Index).ToList();

        await using (var finalStream = new FileStream(
            finalFilePath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            useAsync: true))
        {
            if (job.TotalBytes > 0)
            {
                finalStream.SetLength(job.TotalBytes);
            }

            var buffer = new byte[CopyBufferSize];
            var completedSegments = 0;

            foreach (var segment in orderedSegments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await using (var segmentStream = new FileStream(
                    segment.TempFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    CopyBufferSize,
                    useAsync: true))
                {
                    finalStream.Seek(segment.StartByte, SeekOrigin.Begin);

                    int bytesRead;
                    while ((bytesRead = await segmentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await finalStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    }
                }

                completedSegments++;
                progress?.Report((double)completedSegments / orderedSegments.Count);
            }

            await finalStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        CleanUpTempFiles(orderedSegments);
    }

    private static void CleanUpTempFiles(IReadOnlyList<Segment> segments)
    {
        string? tempDirectory = null;

        foreach (var segment in segments)
        {
            tempDirectory ??= Path.GetDirectoryName(segment.TempFilePath);

            if (File.Exists(segment.TempFilePath))
            {
                File.Delete(segment.TempFilePath);
            }
        }

        if (!string.IsNullOrEmpty(tempDirectory) && Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
