using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ParallelECommerce.Services;

namespace ParallelECommerce.Endpoints;

public static class DatabaseConcurrencyEndpoints
{
    private const string SwaggerTag = "Requirement 07 - Real DB Optimistic Locking";

    public static void MapDatabaseConcurrencyEndpoints(this WebApplication app)
    {
        app.MapPost("/db-concurrency/reset", async (
            DatabaseConcurrencyService concurrencyService,
            CancellationToken cancellationToken) =>
        {
            var result = await concurrencyService.ResetAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("ResetDatabaseConcurrencyDemo")
        .WithTags(SwaggerTag);

        app.MapPost("/demo/db-concurrency/stock/after", async (
            DatabaseConcurrencyService concurrencyService,
            CancellationToken cancellationToken) =>
        {
            var result = await concurrencyService.RunOptimisticStockDemoAsync(cancellationToken);
            return Results.Ok(result);
        })
        .WithName("DemoDatabaseOptimisticStockAfter")
        .WithTags(SwaggerTag);
    }
}
