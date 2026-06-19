namespace ParallelECommerce.Models;

public class DistributedLockDemoMetricsSnapshot
{
    public string ProviderName { get; set; } = string.Empty;
    public bool RedisConfigured { get; set; }
    public long CouponBeforeAttempts { get; set; }
    public long CouponBeforeSuccesses { get; set; }
    public long CouponAfterAttempts { get; set; }
    public long CouponAfterSuccesses { get; set; }
    public long PaymentBeforeAttempts { get; set; }
    public long PaymentBeforeCaptures { get; set; }
    public long PaymentAfterAttempts { get; set; }
    public long PaymentAfterCaptures { get; set; }
    public int ProcessedPaymentReferences { get; set; }
    public List<CouponStateSnapshot> Coupons { get; set; } = new();
    public DistributedLockMetricsSnapshot LockMetrics { get; set; } = new();
    public DateTime LastResetAtUtc { get; set; }
    public string Explanation { get; set; } = string.Empty;
}
