using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public interface IDistributedLockService
{
    Task<DistributedLockLease?> TryAcquireAsync(
        string resource,
        TimeSpan leaseTime,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken = default);

    DistributedLockMetricsSnapshot GetMetrics();

    void ResetMetrics();
}
