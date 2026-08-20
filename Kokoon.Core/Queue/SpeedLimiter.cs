namespace Kokoon.Core.Queue;

/// <summary>
/// Token-bucket speed limiter that enforces a global bytes-per-second cap
/// across multiple concurrent consumers (e.g., 8 segment download threads
/// sharing a single rate limit). Thread-safe via <see cref="SemaphoreSlim"/>.
/// </summary>
/// <remarks>
/// Tokens are replenished on a 100ms timer. Each call to
/// <see cref="ThrottleAsync"/> blocks until enough tokens are available
/// to consume the requested byte count. Setting the limit to 0 disables
/// throttling entirely.
/// </remarks>
public sealed class SpeedLimiter : IDisposable
{
    private const int RefillIntervalMs = 100;

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Timer _refillTimer;

    private long _limitBytesPerSecond;
    private long _availableTokens;
    private long _tokensPerRefill;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="SpeedLimiter"/> with no limit.
    /// </summary>
    public SpeedLimiter()
    {
        _refillTimer = new Timer(RefillTokens, null, RefillIntervalMs, RefillIntervalMs);
    }

    /// <summary>
    /// Gets the currently configured speed limit in bytes per second.
    /// 0 means unlimited.
    /// </summary>
    public long LimitBytesPerSecond => Interlocked.Read(ref _limitBytesPerSecond);

    /// <summary>
    /// Sets the speed limit. Pass 0 to remove the limit.
    /// </summary>
    /// <param name="bytesPerSecond">Maximum bytes per second; 0 = unlimited.</param>
    public void SetLimit(long bytesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytesPerSecond);

        Interlocked.Exchange(ref _limitBytesPerSecond, bytesPerSecond);

        var newTokensPerRefill = bytesPerSecond == 0
            ? 0
            : Math.Max(1, bytesPerSecond / (1000 / RefillIntervalMs));

        Interlocked.Exchange(ref _tokensPerRefill, newTokensPerRefill);

        // Seed the bucket so new limit takes effect immediately.
        if (bytesPerSecond > 0)
        {
            Interlocked.Exchange(ref _availableTokens, newTokensPerRefill);
        }
    }

    /// <summary>
    /// Waits until the specified number of bytes can be consumed under the
    /// current rate limit. Returns immediately if the limit is 0 (unlimited).
    /// </summary>
    /// <param name="bytesToConsume">Number of bytes the caller wants to write/read.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ThrottleAsync(int bytesToConsume, CancellationToken ct)
    {
        if (bytesToConsume <= 0)
        {
            return;
        }

        // No limit set — pass through immediately.
        if (Interlocked.Read(ref _limitBytesPerSecond) == 0)
        {
            return;
        }

        while (bytesToConsume > 0)
        {
            ct.ThrowIfCancellationRequested();

            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var available = Interlocked.Read(ref _availableTokens);

                if (available > 0)
                {
                    var consume = (int)Math.Min(available, bytesToConsume);
                    Interlocked.Add(ref _availableTokens, -consume);
                    bytesToConsume -= consume;

                    if (bytesToConsume <= 0)
                    {
                        return;
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }

            // Not enough tokens — wait for next refill cycle.
            await Task.Delay(RefillIntervalMs, ct).ConfigureAwait(false);
        }
    }

    private void RefillTokens(object? state)
    {
        if (_disposed)
        {
            return;
        }

        var limit = Interlocked.Read(ref _limitBytesPerSecond);
        if (limit == 0)
        {
            return;
        }

        var tokensPerRefill = Interlocked.Read(ref _tokensPerRefill);
        var current = Interlocked.Read(ref _availableTokens);

        // Cap available tokens at the per-refill amount to prevent unbounded accumulation
        // during idle periods (burst = 1 refill window).
        var newValue = Math.Min(current + tokensPerRefill, tokensPerRefill * 2);
        Interlocked.Exchange(ref _availableTokens, newValue);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refillTimer.Dispose();
        _semaphore.Dispose();
    }
}
