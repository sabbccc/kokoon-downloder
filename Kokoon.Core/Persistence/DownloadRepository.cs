using Kokoon.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Kokoon.Core.Persistence;

/// <summary>
/// Defines the contract for persisting and querying <see cref="DownloadItemEntity"/>
/// records in the Kokoon database.
/// </summary>
public interface IDownloadRepository
{
    /// <summary>
    /// Retrieves a download item by its unique identifier.
    /// </summary>
    /// <param name="id">The download item identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching entity, or <c>null</c> if not found.</returns>
    Task<DownloadItemEntity?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Retrieves all download items.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of all entities; empty if none exist.</returns>
    Task<List<DownloadItemEntity>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// Retrieves all download items with the specified status.
    /// </summary>
    /// <param name="status">The status to filter by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of matching entities; empty if none match.</returns>
    Task<List<DownloadItemEntity>> GetByStatusAsync(DownloadStatus status, CancellationToken ct);

    /// <summary>
    /// Adds a new download item to the database.
    /// </summary>
    /// <param name="item">The entity to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(DownloadItemEntity item, CancellationToken ct);

    /// <summary>
    /// Updates all fields of an existing download item.
    /// </summary>
    /// <param name="item">The entity with updated values.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(DownloadItemEntity item, CancellationToken ct);

    /// <summary>
    /// Updates only the JSON segments column for the specified download item,
    /// without touching other fields.
    /// </summary>
    /// <param name="id">The download item identifier.</param>
    /// <param name="segments">The updated list of segments.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateSegmentsAsync(Guid id, List<Segment> segments, CancellationToken ct);

    /// <summary>
    /// Updates only the status of the specified download item.
    /// </summary>
    /// <param name="id">The download item identifier.</param>
    /// <param name="status">The new status.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateStatusAsync(Guid id, DownloadStatus status, CancellationToken ct);

    /// <summary>
    /// Updates only the total/downloaded byte counts for the specified download item.
    /// Unlike <see cref="UpdateSegmentsAsync"/> (which derives <c>DownloadedBytes</c> from
    /// a per-segment breakdown), this is for downloads with no segment list of their own —
    /// e.g. yt-dlp full downloads — that track both values as plain running totals instead.
    /// </summary>
    /// <param name="id">The download item identifier.</param>
    /// <param name="totalBytes">The total size in bytes, or 0 if still unknown.</param>
    /// <param name="downloadedBytes">Bytes downloaded so far.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateProgressAsync(Guid id, long totalBytes, long downloadedBytes, CancellationToken ct);

    /// <summary>
    /// Removes a download item from the database by its identifier.
    /// </summary>
    /// <param name="id">The download item identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Retrieves all download items that are in an incomplete state
    /// (queued, connecting, downloading, assembling, or paused).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of incomplete entities; empty if none exist.</returns>
    Task<List<DownloadItemEntity>> GetIncompleteAsync(CancellationToken ct);
}

/// <summary>
/// SQLite-backed implementation of <see cref="IDownloadRepository"/> using
/// <see cref="IDbContextFactory{TContext}"/> for thread-safe access from
/// background scheduler threads.
/// </summary>
public class DownloadRepository : IDownloadRepository
{
    private static readonly DownloadStatus[] IncompleteStatuses =
    {
        DownloadStatus.Queued,
        DownloadStatus.Connecting,
        DownloadStatus.Downloading,
        DownloadStatus.Assembling,
        DownloadStatus.Paused
    };

    private readonly IDbContextFactory<KokoonDbContext> _contextFactory;

    /// <summary>
    /// Initializes a new instance of <see cref="DownloadRepository"/>.
    /// </summary>
    /// <param name="contextFactory">Factory for creating scoped <see cref="KokoonDbContext"/> instances.</param>
    public DownloadRepository(IDbContextFactory<KokoonDbContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    /// <inheritdoc />
    public async Task<DownloadItemEntity?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await context.Downloads
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<List<DownloadItemEntity>> GetAllAsync(CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await context.Downloads
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<List<DownloadItemEntity>> GetByStatusAsync(DownloadStatus status, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await context.Downloads
            .Where(d => d.Status == status)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(DownloadItemEntity item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        await using var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        context.Downloads.Add(item);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(DownloadItemEntity item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);

        await using var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        context.Downloads.Update(item);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateSegmentsAsync(Guid id, List<Segment> segments, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(segments);

        await using var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await context.Downloads
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return;
        }

        entity.Segments = segments.Select(SegmentEntity.FromDomainModel).ToList();
        entity.DownloadedBytes = segments.Sum(s => s.BytesDownloaded);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(Guid id, DownloadStatus status, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await context.Downloads
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return;
        }

        entity.Status = status;

        if (status == DownloadStatus.Completed)
        {
            entity.CompletedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateProgressAsync(Guid id, long totalBytes, long downloadedBytes, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await context.Downloads
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return;
        }

        entity.TotalBytes = totalBytes;
        entity.DownloadedBytes = downloadedBytes;
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await context.Downloads
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is not null)
        {
            context.Downloads.Remove(entity);
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<List<DownloadItemEntity>> GetIncompleteAsync(CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await context.Downloads
            .Where(d => IncompleteStatuses.Contains(d.Status))
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
