using System.Diagnostics;
using ParallelECommerce.Models;
using StackExchange.Redis;

namespace ParallelECommerce.Services;

public class RedisDistributedLockService : IDistributedLockService
{
    private const string KeyPrefix = "distributed-locks:";

    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration _configuration;

    private long _acquireSuccesses;
    private long _acquireFailures;
    private long _releases;
    private long _contentions;
    private DateTime _lastResetAtUtc = DateTime.UtcNow;

    public RedisDistributedLockService(IConnectionMultiplexer redis, IConfiguration configuration)
    {
        _redis = redis;
        _configuration = configuration;
    }

    public async Task<DistributedLockLease?> TryAcquireAsync(
        string resource,
        TimeSpan leaseTime,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken = default)
    {
        var database = _redis.GetDatabase();
        var key = ToRedisKey(resource);
        var token = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        var contentionRecorded = false;

        while (stopwatch.Elapsed <= waitTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Redis SET resource token NX PX is exposed by StackExchange.Redis as LockTakeAsync.
            // It is atomic, so two API instances cannot acquire the same logical lock at the same time.
            var acquired = await database.LockTakeAsync(key, token, leaseTime);

            if (acquired)
            {
                Interlocked.Increment(ref _acquireSuccesses);

                return new DistributedLockLease(
                    resource,
                    token,
                    DateTime.UtcNow,
                    leaseTime,
                    async releaseCancellationToken =>
                    {
                        await database.LockReleaseAsync(key, token);
                        Interlocked.Increment(ref _releases);
                    });
            }

            if (!contentionRecorded)
            {
                Interlocked.Increment(ref _contentions);
                contentionRecorded = true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        Interlocked.Increment(ref _acquireFailures);
        return null;
    }

    public DistributedLockMetricsSnapshot GetMetrics()
    {
        var useRedis = bool.TryParse(_configuration["Cache:UseRedis"], out var parsedUseRedis) && parsedUseRedis;
        var redisConnectionString = _configuration["Cache:RedisConnectionString"];
        var redisConfigured = useRedis && !string.IsNullOrWhiteSpace(redisConnectionString);

        return new DistributedLockMetricsSnapshot
        {
            ProviderName = redisConfigured
                ? "Redis distributed lock using SET NX through StackExchange.Redis"
                : "Redis distributed lock is not configured",
            RedisConfigured = redisConfigured,
            AcquireSuccesses = Interlocked.Read(ref _acquireSuccesses),
            AcquireFailures = Interlocked.Read(ref _acquireFailures),
            Releases = Interlocked.Read(ref _releases),
            Contentions = Interlocked.Read(ref _contentions),
            LastResetAtUtc = _lastResetAtUtc,
            Explanation = "The lock is stored in Redis, not in the database. It protects shared business resources that may be touched by multiple application instances."
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

    private static RedisKey ToRedisKey(string resource)
    {
        var normalized = resource.Trim().ToLowerInvariant().Replace(' ', ':');
        return $"{KeyPrefix}{normalized}";
    }
}
