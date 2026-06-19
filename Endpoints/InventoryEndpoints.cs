using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ParallelECommerce.DTOs;
using ParallelECommerce.Services;

namespace ParallelECommerce.Endpoints;

public static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this WebApplication app)
    {
        // Get product by id
        app.MapGet("/products/{id:int}", (int id, InventoryService inventoryService) =>
        {
            var product = inventoryService.GetProduct(id);

            if (product is null)
            {
                return Results.NotFound(new
                {
                    message = "Product not found"
                });
            }

            return Results.Ok(product);
        })
        .WithName("GetProduct")
        .WithTags("Products");

        app.MapGet("/products", (InventoryService inventoryService) =>
        {
            return Results.Ok(inventoryService.GetAllProducts());
        })
        .WithName("GetProducts")
        .WithTags("Products");

        // Reset stock manually
        app.MapPost("/inventory/reset", async (
            PurchaseRequest request,
            InventoryService inventoryService,
            CachingCatalogService cachingService,
            CancellationToken cancellationToken) =>
        {
            inventoryService.ResetStock(request.ProductId, request.Quantity);
            await cachingService.InvalidateProductAsync(request.ProductId, cancellationToken);

            return Results.Ok(new
            {
                message = "Stock has been reset and product-related cache keys were invalidated.",
                product = inventoryService.GetProduct(request.ProductId),
                cacheMetrics = cachingService.GetMetrics()
            });
        })
        .WithName("ResetStock")
        .WithTags("Inventory");

        // BEFORE: unsafe purchase endpoint
        app.MapPost("/purchase/before", async (PurchaseRequest request, InventoryService inventoryService) =>
        {
            var success = await inventoryService.PurchaseBeforeAsync(request.ProductId, request.Quantity);

            return Results.Ok(new
            {
                mode = "BEFORE - Unsafe",
                success,
                product = inventoryService.GetProduct(request.ProductId)
            });
        })
        .WithName("PurchaseBefore")
        .WithTags("Race Condition Demo");

        // AFTER: safe purchase endpoint
        app.MapPost("/purchase/after", async (
            PurchaseRequest request,
            InventoryService inventoryService,
            CachingCatalogService cachingService,
            CancellationToken cancellationToken) =>
        {
            var success = inventoryService.PurchaseAfter(request.ProductId, request.Quantity);

            if (success)
            {
                await cachingService.InvalidateProductAsync(request.ProductId, cancellationToken);
            }

            return Results.Ok(new
            {
                mode = "AFTER - Safe with lock",
                success,
                product = inventoryService.GetProduct(request.ProductId),
                cacheInvalidated = success
            });
        })
        .WithName("PurchaseAfter")
        .WithTags("Race Condition Demo");

        // DEMO BEFORE: simulate many users buying at the same time without protection
        app.MapPost("/demo/race/before", async (InventoryService inventoryService) =>
        {
            const int productId = 1;
            const int initialStock = 10;
            const int concurrentUsers = 20;

            inventoryService.ResetStock(productId, initialStock);

            var tasks = Enumerable.Range(1, concurrentUsers)
                .Select(_ => inventoryService.PurchaseBeforeAsync(productId, 1))
                .ToList();

            var results = await Task.WhenAll(tasks);

            var successCount = results.Count(result => result);
            var failedCount = results.Count(result => !result);

            return Results.Ok(new
            {
                mode = "BEFORE - Race Condition",
                initialStock,
                concurrentUsers,
                successCount,
                failedCount,
                finalProduct = inventoryService.GetProduct(productId),
                problem = "More purchases may succeed than available stock because requests read the same stock before updating it."
            });
        })
        .WithName("DemoRaceBefore")
        .WithTags("Race Condition Demo");

        // DEMO AFTER: simulate many users buying at the same time with lock protection
        app.MapPost("/demo/race/after", async (InventoryService inventoryService, CachingCatalogService cachingService, CancellationToken cancellationToken) =>
        {
            const int productId = 1;
            const int initialStock = 10;
            const int concurrentUsers = 20;

            inventoryService.ResetStock(productId, initialStock);
            await cachingService.InvalidateProductAsync(productId, cancellationToken);

            var tasks = Enumerable.Range(1, concurrentUsers)
                .Select(_ => Task.Run(() => inventoryService.PurchaseAfter(productId, 1), cancellationToken))
                .ToList();

            var results = await Task.WhenAll(tasks);
            await cachingService.InvalidateProductAsync(productId, cancellationToken);

            var successCount = results.Count(result => result);
            var failedCount = results.Count(result => !result);

            return Results.Ok(new
            {
                mode = "AFTER - Protected with lock",
                initialStock,
                concurrentUsers,
                successCount,
                failedCount,
                finalProduct = inventoryService.GetProduct(productId),
                cacheInvalidated = true,
                explanation = "Only 10 purchases succeed because the lock allows one thread at a time to update the stock."
            });
        })
        .WithName("DemoRaceAfter")
        .WithTags("Race Condition Demo");
    }
}
