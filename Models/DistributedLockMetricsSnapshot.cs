namespace ParallelECommerce.Models;

public class DistributedLockMetricsSnapshot
{
    public string ProviderName { get; set; } = string.Empty;
    public bool RedisConfigured { get; set; }
    public long AcquireSuccesses { get; set; }
    public long AcquireFailures { get; set; }
    public long Releases { get; set; }
    public long Contentions { get; set; }
    public DateTime LastResetAtUtc { get; set; }
    public string Explanation { get; set; } = string.Empty;
}
