namespace ParallelECommerce.Models;

public sealed class DistributedLockLease : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task> _releaseAsync;
    private int _released;

    public DistributedLockLease(
        string resource,
        string token,
        DateTime acquiredAtUtc,
        TimeSpan leaseTime,
        Func<CancellationToken, Task> releaseAsync)
    {
        Resource = resource;
        Token = token;
        AcquiredAtUtc = acquiredAtUtc;
        LeaseTime = leaseTime;
        _releaseAsync = releaseAsync;
    }

    public string Resource { get; }

    public string Token { get; }

    public DateTime AcquiredAtUtc { get; }

    public TimeSpan LeaseTime { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            await _releaseAsync(CancellationToken.None);
        }
    }
}
