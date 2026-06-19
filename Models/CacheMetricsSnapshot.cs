namespace ParallelECommerce.Models;

public class CacheMetricsSnapshot
{
    public string ProviderName { get; set; } = string.Empty;
    public bool RedisConfigured { get; set; }
    public long ProductCacheHits { get; set; }
    public long ProductCacheMisses { get; set; }
    public long PopularProductsCacheHits { get; set; }
    public long PopularProductsCacheMisses { get; set; }
    public long StockCacheHits { get; set; }
    public long StockCacheMisses { get; set; }
    public long DirectDatabaseReads { get; set; }
    public long CacheBackedDatabaseReads { get; set; }
    public long CacheInvalidations { get; set; }
    public DateTime LastResetAtUtc { get; set; }
    public string Explanation { get; set; } = string.Empty;
}
