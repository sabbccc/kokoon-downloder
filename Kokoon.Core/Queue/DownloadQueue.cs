using Kokoon.Core.Models;

namespace Kokoon.Core.Queue;

/// <summary>
/// Defines the contract for a thread-safe, priority-ordered download queue.
/// </summary>
public interface IDownloadQueue
{
    /// <summary>Enqueues a download item with default priority (0).</summary>
    /// <param name="item">The download item to enqueue.</param>
    void Enqueue(DownloadItem item);

    /// <summary>Enqueues a download item with the specified priority (lower = higher priority).</summary>
    /// <param name="item">The download item to enqueue.</param>
    /// <param name="priority">Priority value; lower values are dequeued first.</param>
    void EnqueueWithPriority(DownloadItem item, int priority);

    /// <summary>Attempts to dequeue the highest-priority item.</summary>
    /// <param name="item">The dequeued item, or <c>null</c> if the queue is empty.</param>
    /// <returns><c>true</c> if an item was dequeued; otherwise <c>false</c>.</returns>
    bool TryDequeue(out DownloadItem? item);

    /// <summary>Moves the specified download to the paused set.</summary>
    /// <param name="id">Identifier of the download to pause.</param>
    void Pause(Guid id);

    /// <summary>Moves the specified download from the paused set back to the queue.</summary>
    /// <param name="id">Identifier of the download to resume.</param>
    void Resume(Guid id);

    /// <summary>
    /// Registers <paramref name="item"/> directly into the paused set, without requiring
    /// it to currently be present in the queue. Used by <see cref="Kokoon.Core.Queue.DownloadScheduler"/>
    /// when an actively-downloading item is paused: such an item was already dequeued (and so is not
    /// found by <see cref="Pause"/>), but its partial progress must still become resumable via
    /// <see cref="Resume"/> the same way a queued-then-paused item is.
    /// </summary>
    /// <param name="item">The item to mark as paused. Its <see cref="DownloadItem.Status"/> is set to
    /// <see cref="DownloadStatus.Paused"/>.</param>
    void MarkPaused(DownloadItem item);

    /// <summary>Removes the specified download from both the queue and paused set.</summary>
    /// <param name="id">Identifier of the download to cancel.</param>
    void Cancel(Guid id);

    /// <summary>Increases the priority of the specified download (lower priority value).</summary>
    /// <param name="id">Identifier of the download to move up.</param>
    void MoveUp(Guid id);

    /// <summary>Decreases the priority of the specified download (higher priority value).</summary>
    /// <param name="id">Identifier of the download to move down.</param>
    void MoveDown(Guid id);

    /// <summary>Returns an immutable snapshot of all queued (non-paused) items in priority order.</summary>
    IReadOnlyList<DownloadItem> GetSnapshot();

    /// <summary>Gets the total number of items in the queue (excluding paused items).</summary>
    int Count { get; }
}

/// <summary>
/// Thread-safe, priority-ordered download queue backed by
/// <see cref="PriorityQueue{TElement,TPriority}"/> with a
/// <see cref="ReaderWriterLockSlim"/> for concurrent access.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PriorityQueue{TElement,TPriority}"/> does not support in-place priority
/// mutation. <see cref="MoveUp"/> and <see cref="MoveDown"/> therefore rebuild the
/// internal queue with adjusted priorities. This is acceptable because queue sizes
/// are typically small (tens to low hundreds of items).
/// </para>
/// </remarks>
public class DownloadQueue : IDownloadQueue, IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new();
    private PriorityQueue<DownloadItem, int> _queue = new();

    /// <summary>Tracks priorities so we can rebuild when mutating.</summary>
    private readonly Dictionary<Guid, int> _priorities = new();

    /// <summary>Items that have been paused, keyed by download ID.</summary>
    private readonly Dictionary<Guid, DownloadItem> _pausedItems = new();

    /// <inheritdoc />
    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _queue.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <inheritdoc />
    public void Enqueue(DownloadItem item)
    {
        EnqueueWithPriority(item, 0);
    }

    /// <inheritdoc />
    public void EnqueueWithPriority(DownloadItem item, int priority)
    {
        ArgumentNullException.ThrowIfNull(item);

        _lock.EnterWriteLock();
        try
        {
            _queue.Enqueue(item, priority);
            _priorities[item.Id] = priority;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public bool TryDequeue(out DownloadItem? item)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_queue.Count == 0)
            {
                item = null;
                return false;
            }

            item = _queue.Dequeue();
            _priorities.Remove(item.Id);
            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public void Pause(Guid id)
    {
        _lock.EnterWriteLock();
        try
        {
            var (items, priorities) = DrainAll();
            DownloadItem? target = null;

            foreach (var (item, priority) in items.Zip(priorities))
            {
                if (item.Id == id)
                {
                    target = item;
                    // Do not re-enqueue; move to paused set instead.
                }
                else
                {
                    _queue.Enqueue(item, priority);
                }
            }

            if (target is not null)
            {
                target.Status = DownloadStatus.Paused;
                _pausedItems[id] = target;
                _priorities.Remove(id);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public void Resume(Guid id)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_pausedItems.Remove(id, out var item))
            {
                item.Status = DownloadStatus.Queued;
                var priority = 0; // Resumed items get default priority.
                _queue.Enqueue(item, priority);
                _priorities[id] = priority;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public void MarkPaused(DownloadItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _lock.EnterWriteLock();
        try
        {
            item.Status = DownloadStatus.Paused;
            _pausedItems[item.Id] = item;
            _priorities.Remove(item.Id);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public void Cancel(Guid id)
    {
        _lock.EnterWriteLock();
        try
        {
            // Remove from paused set if present.
            if (_pausedItems.Remove(id, out var pausedItem))
            {
                pausedItem.Status = DownloadStatus.Cancelled;
                return;
            }

            // Remove from queue — requires rebuild.
            var (items, priorities) = DrainAll();

            foreach (var (item, priority) in items.Zip(priorities))
            {
                if (item.Id == id)
                {
                    item.Status = DownloadStatus.Cancelled;
                    _priorities.Remove(id);
                }
                else
                {
                    _queue.Enqueue(item, priority);
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public void MoveUp(Guid id)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_priorities.ContainsKey(id))
            {
                return;
            }

            _priorities[id]--;
            RebuildQueue();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public void MoveDown(Guid id)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_priorities.ContainsKey(id))
            {
                return;
            }

            _priorities[id]++;
            RebuildQueue();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DownloadItem> GetSnapshot()
    {
        _lock.EnterReadLock();
        try
        {
            // UnorderedItems gives us access without draining; sort by priority.
            return _queue.UnorderedItems
                .OrderBy(entry => entry.Priority)
                .Select(entry => entry.Element)
                .ToList()
                .AsReadOnly();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Drains all items from the priority queue, returning parallel lists of items
    /// and their priorities. Must be called under a write lock.
    /// </summary>
    private (List<DownloadItem> Items, List<int> Priorities) DrainAll()
    {
        var items = new List<DownloadItem>(_queue.Count);
        var priorities = new List<int>(_queue.Count);

        while (_queue.TryDequeue(out var item, out var priority))
        {
            items.Add(item);
            priorities.Add(priority);
        }

        return (items, priorities);
    }

    /// <summary>
    /// Rebuilds the priority queue from the current <see cref="_priorities"/> dictionary.
    /// Must be called under a write lock.
    /// </summary>
    private void RebuildQueue()
    {
        var (items, _) = DrainAll();
        foreach (var item in items)
        {
            if (_priorities.TryGetValue(item.Id, out var priority))
            {
                _queue.Enqueue(item, priority);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
