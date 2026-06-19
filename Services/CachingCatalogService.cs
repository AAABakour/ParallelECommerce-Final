using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public class CachingCatalogService
{
    private const int SimulatedProductDatabaseDelayMs = 80;
    private const int SimulatedPopularProductsDatabaseDelayMs = 140;
    private const int SimulatedStockDatabaseDelayMs = 50;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly InventoryService _inventoryService;
    private readonly IConfiguration _configuration;

    private long _productCacheHits;
    private long _productCacheMisses;
    private long _popularProductsCacheHits;
    private long _popularProductsCacheMisses;
    private long _stockCacheHits;
    private long _stockCacheMisses;
    private long _directDatabaseReads;
    private long _cacheBackedDatabaseReads;
    private long _cacheInvalidations;
    private DateTime _lastResetAtUtc = DateTime.UtcNow;

    public CachingCatalogService(
        IDistributedCache cache,
        InventoryService inventoryService,
        IConfiguration configuration)
    {
        _cache = cache;
        _inventoryService = inventoryService;
        _configuration = configuration;
    }

    public async Task<object> GetProductDirectAsync(int productId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var product = await ReadProductFromDatabaseAsync(productId, countAsDirectRead: true, cancellationToken);
        stopwatch.Stop();

        return new
        {
            mode = "BEFORE - Direct database read without cache",
            productId,
            found = product is not null,
            product,
            durationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
            problem = "Every request pays the simulated database-read cost even if the same product is requested many times."
        };
    }

    public async Task<object> GetProductCachedAsync(int productId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var key = ProductKey(productId);
        var cachedJson = await _cache.GetStringAsync(key, cancellationToken);

        if (!string.IsNullOrWhiteSpace(cachedJson))
        {
            Interlocked.Increment(ref _productCacheHits);
            stopwatch.Stop();

            return new
            {
                mode = "AFTER - Cache-aside product read",
                productId,
                cacheResult = "HIT",
                product = JsonSerializer.Deserialize<Product>(cachedJson, SerializerOptions),
                durationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                explanation = "The product was served from IDistributedCache, so the database read was skipped."
            };
        }

        Interlocked.Increment(ref _productCacheMisses);
        var product = await ReadProductFromDatabaseAsync(productId, countAsDirectRead: false, cancellationToken);

        if (product is not null)
        {
            await _cache.SetStringAsync(
                key,
                JsonSerializer.Serialize(product, SerializerOptions),
                ProductCacheOptions(),
                cancellationToken);
        }

        stopwatch.Stop();

        return new
        {
            mode = "AFTER - Cache-aside product read",
            productId,
            cacheResult = "MISS",
            found = product is not null,
            product,
            durationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
            explanation = "The first request missed the cache, read from the database source, then stored the result for following requests."
        };
    }

    public async Task<object> GetPopularProductsDirectAsync(int count = 3, CancellationToken cancellationToken = default)
    {
        count = NormalizeCount(count);
        var stopwatch = Stopwatch.StartNew();
        var products = await ReadPopularProductsFromDatabaseAsync(count, countAsDirectRead: true, cancellationToken);
        stopwatch.Stop();

        return new
        {
            mode = "BEFORE - Direct popular-products query without cache",
            count,
            products,
            durationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
            problem = "The popular-products query is repeated even though its result is read-heavy and changes less often than orders."
        };
    }

    public async Task<object> GetPopularProductsCachedAsync(int count = 3, CancellationToken cancellationToken = default)
    {
        count = NormalizeCount(count);
        var stopwatch = Stopwatch.StartNew();
        var key = PopularProductsKey(count);
        var cachedJson = await _cache.GetStringAsync(key, cancellationToken);

        if (!string.IsNullOrWhiteSpace(cachedJson))
        {
            Interlocked.Increment(ref _popularProductsCacheHits);
            stopwatch.Stop();

            return new
            {
                mode = "AFTER - Cache-aside popular-products query",
                count,
                cacheResult = "HIT",
                products = JsonSerializer.Deserialize<List<Product>>(cachedJson, SerializerOptions) ?? new List<Product>(),
                durationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                explanation = "The popular-products list was served from cache instead of recomputing the same read-heavy query."
            };
        }

        Interlocked.Increment(ref _popularProductsCacheMisses);
        var products = await ReadPopularProductsFromDatabaseAsync(count, countAsDirectRead: false, cancellationToken);

        await _cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(products, SerializerOptions),
            PopularProductsCacheOptions(),
            cancellationToken);

        stopwatch.Stop();

        return new
        {
            mode = "AFTER - Cache-aside popular-products query",
            count,
            cacheResult = "MISS",
            products,
            durationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
            explanation = "The list was loaded once, then cached because it is read frequently by many users."
        };
    }

    public async Task<object> GetStockDirectAsync(int productId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var stock = await ReadStockFromDatabaseAsync(productId, countAsDirectRead: true, cancellationToken);
        stopwatch.Stop();

        return new
        {
            mode = "BEFORE - Direct stock read without cache",
            productId,
            found = stock is not null,
            stock,
            durationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
            problem = "Repeated stock lookups hit the backing store every time."
        };
    }

    public async Task<object> GetStockCachedAsync(int productId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var key = StockKey(productId);
        var cachedJson = await _cache.GetStringAsync(key, cancellationToken);

        if (!string.IsNullOrWhiteSpace(cachedJson))
        {
            Interlocked.Increment(ref _stockCacheHits);
            stopwatch.Stop();

            return new
            {
                mode = "AFTER - Short-lived stock cache",
                productId,
                cacheResult = "HIT",
                stock = JsonSerializer.Deserialize<StockSnapshot>(cachedJson, SerializerOptions),
                durationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                explanation = "The stock snapshot was served from a short-lived cache entry and is invalidated after stock-changing operations."
            };
        }

        Interlocked.Increment(ref _stockCacheMisses);
        var stock = await ReadStockFromDatabaseAsync(productId, countAsDirectRead: false, cancellationToken);

        if (stock is not null)
        {
            await _cache.SetStringAsync(
                key,
                JsonSerializer.Serialize(stock, SerializerOptions),
                StockCacheOptions(),
                cancellationToken);
        }

        stopwatch.Stop();

        return new
        {
            mode = "AFTER - Short-lived stock cache",
            productId,
            cacheResult = "MISS",
            found = stock is not null,
            stock,
            durationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
            explanation = "The stock was read from the backing store once, then cached briefly to reduce repeated reads."
        };
    }

    public async Task InvalidateProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(ProductKey(productId), cancellationToken);
        await _cache.RemoveAsync(StockKey(productId), cancellationToken);
        await RemovePopularProductKeysAsync(cancellationToken);

        Interlocked.Add(ref _cacheInvalidations, 22);
    }

    public async Task<CacheMetricsSnapshot> ResetAsync(CancellationToken cancellationToken = default)
    {
        foreach (var product in _inventoryService.GetAllProducts())
        {
            await _cache.RemoveAsync(ProductKey(product.Id), cancellationToken);
            await _cache.RemoveAsync(StockKey(product.Id), cancellationToken);
        }

        await RemovePopularProductKeysAsync(cancellationToken);

        Interlocked.Exchange(ref _productCacheHits, 0);
        Interlocked.Exchange(ref _productCacheMisses, 0);
        Interlocked.Exchange(ref _popularProductsCacheHits, 0);
        Interlocked.Exchange(ref _popularProductsCacheMisses, 0);
        Interlocked.Exchange(ref _stockCacheHits, 0);
        Interlocked.Exchange(ref _stockCacheMisses, 0);
        Interlocked.Exchange(ref _directDatabaseReads, 0);
        Interlocked.Exchange(ref _cacheBackedDatabaseReads, 0);
        Interlocked.Exchange(ref _cacheInvalidations, 0);
        _lastResetAtUtc = DateTime.UtcNow;

        return GetMetrics();
    }

    public CacheMetricsSnapshot GetMetrics()
    {
        var useRedis = bool.TryParse(_configuration["Cache:UseRedis"], out var parsedUseRedis) && parsedUseRedis;
        var redisConnectionString = _configuration["Cache:RedisConnectionString"];
        var redisConfigured = useRedis && !string.IsNullOrWhiteSpace(redisConnectionString);

        return new CacheMetricsSnapshot
        {
            ProviderName = redisConfigured
                ? "Redis through IDistributedCache"
                : "DistributedMemoryCache fallback through IDistributedCache",
            RedisConfigured = redisConfigured,
            ProductCacheHits = Interlocked.Read(ref _productCacheHits),
            ProductCacheMisses = Interlocked.Read(ref _productCacheMisses),
            PopularProductsCacheHits = Interlocked.Read(ref _popularProductsCacheHits),
            PopularProductsCacheMisses = Interlocked.Read(ref _popularProductsCacheMisses),
            StockCacheHits = Interlocked.Read(ref _stockCacheHits),
            StockCacheMisses = Interlocked.Read(ref _stockCacheMisses),
            DirectDatabaseReads = Interlocked.Read(ref _directDatabaseReads),
            CacheBackedDatabaseReads = Interlocked.Read(ref _cacheBackedDatabaseReads),
            CacheInvalidations = Interlocked.Read(ref _cacheInvalidations),
            LastResetAtUtc = _lastResetAtUtc,
            Explanation = "Cache-aside is used in multiple read-heavy places: product details, popular-products list, and short-lived stock snapshots. Stock-changing operations invalidate related keys."
        };
    }

    private async Task<Product?> ReadProductFromDatabaseAsync(
        int productId,
        bool countAsDirectRead,
        CancellationToken cancellationToken)
    {
        await Task.Delay(SimulatedProductDatabaseDelayMs, cancellationToken);
        IncrementDatabaseReadCounter(countAsDirectRead);
        return _inventoryService.GetProduct(productId);
    }

    private async Task<List<Product>> ReadPopularProductsFromDatabaseAsync(
        int count,
        bool countAsDirectRead,
        CancellationToken cancellationToken)
    {
        await Task.Delay(SimulatedPopularProductsDatabaseDelayMs, cancellationToken);
        IncrementDatabaseReadCounter(countAsDirectRead);
        return _inventoryService.GetPopularProducts(count);
    }

    private async Task<StockSnapshot?> ReadStockFromDatabaseAsync(
        int productId,
        bool countAsDirectRead,
        CancellationToken cancellationToken)
    {
        await Task.Delay(SimulatedStockDatabaseDelayMs, cancellationToken);
        IncrementDatabaseReadCounter(countAsDirectRead);

        var product = _inventoryService.GetProduct(productId);

        return product is null
            ? null
            : new StockSnapshot
            {
                ProductId = product.Id,
                ProductName = product.Name,
                StockQuantity = product.StockQuantity,
                SnapshotAtUtc = DateTime.UtcNow
            };
    }

    private void IncrementDatabaseReadCounter(bool countAsDirectRead)
    {
        if (countAsDirectRead)
        {
            Interlocked.Increment(ref _directDatabaseReads);
        }
        else
        {
            Interlocked.Increment(ref _cacheBackedDatabaseReads);
        }
    }

    private static DistributedCacheEntryOptions ProductCacheOptions()
    {
        return new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
            SlidingExpiration = TimeSpan.FromSeconds(30)
        };
    }

    private static DistributedCacheEntryOptions PopularProductsCacheOptions()
    {
        return new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            SlidingExpiration = TimeSpan.FromMinutes(1)
        };
    }

    private static DistributedCacheEntryOptions StockCacheOptions()
    {
        return new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20),
            SlidingExpiration = TimeSpan.FromSeconds(10)
        };
    }

    private static int NormalizeCount(int count) => Math.Clamp(count, 1, 20);

    private static string ProductKey(int productId) => $"products:details:{productId}";

    private static string StockKey(int productId) => $"inventory:stock:{productId}";

    private static string PopularProductsKey(int count) => $"products:popular:top:{NormalizeCount(count)}";

    private async Task RemovePopularProductKeysAsync(CancellationToken cancellationToken)
    {
        for (var count = 1; count <= 20; count++)
        {
            await _cache.RemoveAsync(PopularProductsKey(count), cancellationToken);
        }
    }
}
