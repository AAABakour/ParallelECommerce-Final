using ParallelECommerce.Services;

namespace ParallelECommerce.Endpoints;

public static class CapacityControlEndpoints
{
    public static void MapCapacityControlEndpoints(this WebApplication app)
    {
        app.MapPost("/capacity/reset", (CapacityControlService capacityService) =>
        {
            capacityService.ResetMetrics();

            return Results.Ok(new
            {
                message = "Capacity-control metrics were reset.",
                metrics = capacityService.GetMetrics()
            });
        })
        .WithName("ResetCapacityMetrics")
        .WithTags("Capacity Control Demo");

        app.MapGet("/capacity/metrics", (CapacityControlService capacityService) =>
        {
            return Results.Ok(capacityService.GetMetrics());
        })
        .WithName("GetCapacityMetrics")
        .WithTags("Capacity Control Demo");

        app.MapPost("/capacity/before/work", async (CapacityControlService capacityService) =>
        {
            var result = await capacityService.RunWithoutCapacityControlAsync();
            return Results.Ok(result);
        })
        .WithName("CapacityBeforeSingleWork")
        .WithTags("Capacity Control Demo");

        app.MapPost("/capacity/after/work", async (CapacityControlService capacityService) =>
        {
            var result = await capacityService.RunWithCapacityControlAsync();
            return Results.Ok(result);
        })
        .WithName("CapacityAfterSingleWork")
        .WithTags("Capacity Control Demo");

        app.MapPost("/demo/capacity/before", async (CapacityControlService capacityService) =>
        {
            const int totalRequests = 50;

            capacityService.ResetMetrics();

            var startedAt = DateTime.UtcNow;

            var tasks = Enumerable.Range(1, totalRequests)
                .Select(_ => capacityService.RunWithoutCapacityControlAsync())
                .ToList();

            await Task.WhenAll(tasks);

            var finishedAt = DateTime.UtcNow;
            var metrics = capacityService.GetMetrics();

            return Results.Ok(new
            {
                mode = "BEFORE - No Capacity Control",
                totalRequests,
                metrics.MaxActiveOperationsObserved,
                metrics.StartedOperations,
                metrics.CompletedOperations,
                durationMs = Math.Round((finishedAt - startedAt).TotalMilliseconds, 2),
                problem = "All operations were allowed to run at the same time, which can exhaust CPU, memory, threads, or external connections under heavy load."
            });
        })
        .WithName("DemoCapacityBefore")
        .WithTags("Capacity Control Demo");

        app.MapPost("/demo/capacity/after", async (CapacityControlService capacityService) =>
        {
            const int totalRequests = 50;
            const int maxAllowedParallelOperations = CapacityControlService.MaxParallelOperations;

            capacityService.ResetMetrics();

            var startedAt = DateTime.UtcNow;

            var tasks = Enumerable.Range(1, totalRequests)
                .Select(_ => capacityService.RunWithCapacityControlAsync())
                .ToList();

            await Task.WhenAll(tasks);

            var finishedAt = DateTime.UtcNow;
            var metrics = capacityService.GetMetrics();

            return Results.Ok(new
            {
                mode = "AFTER - Protected with SemaphoreSlim",
                totalRequests,
                maxAllowedParallelOperations,
                metrics.MaxActiveOperationsObserved,
                metrics.StartedOperations,
                metrics.CompletedOperations,
                durationMs = Math.Round((finishedAt - startedAt).TotalMilliseconds, 2),
                explanation = "SemaphoreSlim limited the number of heavy operations running at the same time. The response time may increase because extra work waits in a controlled queue, but resource usage stays bounded."
            });
        })
        .WithName("DemoCapacityAfter")
        .WithTags("Capacity Control Demo");
    }
}
