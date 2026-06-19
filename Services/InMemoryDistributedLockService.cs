using System.Collections.Concurrent;
using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public class InMemoryDistributedLockService : IDistributedLockService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private long _acquireSuccesses;
    private long _acquireFailures;
    private long _releases;
    private long _contentions;
    private DateTime _lastResetAtUtc = DateTime.UtcNow;

    public async Task<DistributedLockLease?> TryAcquireAsync(
        string resource,
        TimeSpan leaseTime,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(resource, _ => new SemaphoreSlim(1, 1));

        if (semaphore.CurrentCount == 0)
        {
            Interlocked.Increment(ref _contentions);
        }

        var acquired = await semaphore.WaitAsync(waitTimeout, cancellationToken);

        if (!acquired)
        {
            Interlocked.Increment(ref _acquireFailures);
            return null;
        }

        Interlocked.Increment(ref _acquireSuccesses);

        return new DistributedLockLease(
            resource,
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow,
            leaseTime,
            _ =>
            {
                semaphore.Release();
                Interlocked.Increment(ref _releases);
                return Task.CompletedTask;
            });
    }

    public DistributedLockMetricsSnapshot GetMetrics()
    {
        return new DistributedLockMetricsSnapshot
        {
            ProviderName = "In-memory fallback lock - local process only, not distributed",
            RedisConfigured = false,
            AcquireSuccesses = Interlocked.Read(ref _acquireSuccesses),
            AcquireFailures = Interlocked.Read(ref _acquireFailures),
            Releases = Interlocked.Read(ref _releases),
            Contentions = Interlocked.Read(ref _contentions),
            LastResetAtUtc = _lastResetAtUtc,
            Explanation = "This fallback protects only one application process. For the final requirement demo, run Redis and enable Cache:UseRedis=true."
        };
    }

    public void ResetMetrics()
    {
        Interlocked.Exchange(ref _acquireSuccesses, 0);
        Interlocked.Exchange(ref _acquireFailures, 0);
        Interlocked.Exchange(ref _releases, 0);
        Interlocked.Exchange(ref _contentions, 0);
        _lastResetAtUtc = DateTime.UtcNow;
    }
}
