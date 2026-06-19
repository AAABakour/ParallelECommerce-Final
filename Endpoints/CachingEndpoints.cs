using ParallelECommerce.Services;

namespace ParallelECommerce.Endpoints;

public static class CachingEndpoints
{
    public static void MapCachingEndpoints(this WebApplication app)
    {
        app.MapPost("/cache/reset", async (CachingCatalogService cachingService, CancellationToken cancellationToken) =>
        {
            var metrics = await cachingService.ResetAsync(cancellationToken);

            return Results.Ok(new
            {
                message = "Caching metrics and known cache keys were reset.",
                metrics
            });
        })
        .WithName("ResetCachingMetrics")
        .WithTags("Caching / Distributed Cache");

        app.MapGet("/cache/metrics", (CachingCatalogService cachingService) =>
        {
            return Results.Ok(cachingService.GetMetrics());
        })
        .WithName("GetCachingMetrics")
        .WithTags("Caching / Distributed Cache");

        app.MapPost("/cache/invalidate/product/{productId:int}", async (
            int productId,
            CachingCatalogService cachingService,
            CancellationToken cancellationToken) =>
        {
            await cachingService.InvalidateProductAsync(productId, cancellationToken);

            return Results.Ok(new
            {
                message = "Product-related cache keys were invalidated.",
                productId,
                metrics = cachingService.GetMetrics()
            });
        })
        .WithName("InvalidateProductCache")
        .WithTags("Caching / Distributed Cache");

        app.MapGet("/cache/before/products/{productId:int}", async (
            int productId,
            CachingCatalogService cachingService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await cachingService.GetProductDirectAsync(productId, cancellationToken));
        })
        .WithName("GetProductBeforeCache")
        .WithTags("Caching / Distributed Cache");

        app.MapGet("/cache/after/products/{productId:int}", async (
            int productId,
            CachingCatalogService cachingService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await cachingService.GetProductCachedAsync(productId, cancellationToken));
        })
        .WithName("GetProductAfterCache")
        .WithTags("Caching / Distributed Cache");

        app.MapGet("/cache/before/popular-products", async (
            int? count,
            CachingCatalogService cachingService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await cachingService.GetPopularProductsDirectAsync(count ?? 3, cancellationToken));
        })
        .WithName("GetPopularProductsBeforeCache")
        .WithTags("Caching / Distributed Cache");

        app.MapGet("/cache/after/popular-products", async (
            int? count,
            CachingCatalogService cachingService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await cachingService.GetPopularProductsCachedAsync(count ?? 3, cancellationToken));
        })
        .WithName("GetPopularProductsAfterCache")
        .WithTags("Caching / Distributed Cache");

        app.MapGet("/cache/before/stock/{productId:int}", async (
            int productId,
            CachingCatalogService cachingService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await cachingService.GetStockDirectAsync(productId, cancellationToken));
        })
        .WithName("GetStockBeforeCache")
        .WithTags("Caching / Distributed Cache");

        app.MapGet("/cache/after/stock/{productId:int}", async (
            int productId,
            CachingCatalogService cachingService,
            CancellationToken cancellationToken) =>
        {
            return Results.Ok(await cachingService.GetStockCachedAsync(productId, cancellationToken));
        })
        .WithName("GetStockAfterCache")
        .WithTags("Caching / Distributed Cache");

        app.MapPost("/demo/cache/before", async (CachingCatalogService cachingService, CancellationToken cancellationToken) =>
        {
            await cachingService.ResetAsync(cancellationToken);
            var startedAt = DateTime.UtcNow;

            for (var i = 0; i < 30; i++)
            {
                await cachingService.GetProductDirectAsync(1, cancellationToken);
            }

            for (var i = 0; i < 10; i++)
            {
                await cachingService.GetPopularProductsDirectAsync(3, cancellationToken);
            }

            for (var i = 0; i < 10; i++)
            {
                await cachingService.GetStockDirectAsync(1, cancellationToken);
            }

            var finishedAt = DateTime.UtcNow;

            return Results.Ok(new
            {
                mode = "BEFORE - Repeated reads without cache",
                totalProductReads = 30,
                totalPopularProductsReads = 10,
                totalStockReads = 10,
                durationMs = Math.Round((finishedAt - startedAt).TotalMilliseconds, 2),
                metrics = cachingService.GetMetrics(),
                problem = "Every repeated read went to the backing data source."
            });
        })
        .WithName("DemoCacheBefore")
        .WithTags("Caching Demo");

        app.MapPost("/demo/cache/after", async (CachingCatalogService cachingService, CancellationToken cancellationToken) =>
        {
            await cachingService.ResetAsync(cancellationToken);
            var startedAt = DateTime.UtcNow;

            for (var i = 0; i < 30; i++)
            {
                await cachingService.GetProductCachedAsync(1, cancellationToken);
            }

            for (var i = 0; i < 10; i++)
            {
                await cachingService.GetPopularProductsCachedAsync(3, cancellationToken);
            }

            for (var i = 0; i < 10; i++)
            {
                await cachingService.GetStockCachedAsync(1, cancellationToken);
            }

            var finishedAt = DateTime.UtcNow;

            return Results.Ok(new
            {
                mode = "AFTER - Repeated reads with cache-aside",
                totalProductReads = 30,
                totalPopularProductsReads = 10,
                totalStockReads = 10,
                durationMs = Math.Round((finishedAt - startedAt).TotalMilliseconds, 2),
                metrics = cachingService.GetMetrics(),
                explanation = "Only the first request for each cache key missed. Repeated requests were served from cache."
            });
        })
        .WithName("DemoCacheAfter")
        .WithTags("Caching Demo");
    }
}
