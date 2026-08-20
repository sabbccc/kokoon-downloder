namespace Kokoon.Core.Models;

/// <summary>
/// Represents the lifecycle state of a <see cref="DownloadItem"/>.
/// </summary>
public enum DownloadStatus
{
    /// <summary>The item is waiting in the queue and has not started.</summary>
    Queued,

    /// <summary>The engine is probing the remote server and establishing connections.</summary>
    Connecting,

    /// <summary>One or more segments are actively transferring data.</summary>
    Downloading,

    /// <summary>All segments have completed and are being merged into the final file.</summary>
    Assembling,

    /// <summary>The download finished successfully and the final file is on disk.</summary>
    Completed,

    /// <summary>The download was paused by the user and can be resumed.</summary>
    Paused,

    /// <summary>The download failed after exhausting retries.</summary>
    Failed,

    /// <summary>The download was cancelled by the user.</summary>
    Cancelled
}
