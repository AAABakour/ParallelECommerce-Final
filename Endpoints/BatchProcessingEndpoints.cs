using ParallelECommerce.Services;

namespace ParallelECommerce.Endpoints;

public static class BatchProcessingEndpoints
{
    public static void MapBatchProcessingEndpoints(this WebApplication app)
    {
        app.MapPost("/batch/reset", (BatchProcessingService batchService) =>
        {
            return Results.Ok(batchService.Reset());
        })
        .WithName("ResetBatchMetrics")
        .WithTags("Batch Processing");

        app.MapGet("/batch/metrics", (BatchProcessingService batchService) =>
        {
            return Results.Ok(batchService.GetMetrics());
        })
        .WithName("GetBatchMetrics")
        .WithTags("Batch Processing");

        app.MapGet("/batch/jobs", (BatchProcessingService batchService) =>
        {
            return Results.Ok(batchService.GetRecentJobs());
        })
        .WithName("GetBatchJobs")
        .WithTags("Batch Processing");

        app.MapGet("/batch/jobs/{jobId:guid}", (Guid jobId, BatchProcessingService batchService) =>
        {
            var job = batchService.GetJob(jobId);
            return job is null ? Results.NotFound(new { message = "Batch job was not found." }) : Results.Ok(job);
        })
        .WithName("GetBatchJobById")
        .WithTags("Batch Processing");

        app.MapPost("/batch/before/sequential", async (BatchProcessingService batchService, int? totalRecords) =>
        {
            var result = await batchService.ProcessSequentiallyAsync(totalRecords ?? 100);
            return Results.Ok(result);
        })
        .WithName("BatchBeforeSequential")
        .WithTags("Batch Processing");

        app.MapPost("/batch/after/start-daily-sales-job", async (
            BatchProcessingService batchService,
            int? totalRecords,
            int? chunkSize,
            int? maxParallelChunks) =>
        {
            var job = await batchService.QueueDailySalesBatchJobAsync(
                totalRecords ?? 100,
                chunkSize ?? 10,
                maxParallelChunks ?? 4);

            return Results.Accepted($"/batch/jobs/{job.JobId}", new
            {
                message = "Daily sales batch job was queued and will be processed by the background worker.",
                job
            });
        })
        .WithName("StartDailySalesBatchJob")
        .WithTags("Batch Processing");

        // Backward-compatible demo endpoints used by the first JMeter file and by Swagger demos.
        app.MapPost("/demo/batch/before", async (BatchProcessingService batchService) =>
        {
            const int totalRecords = 100;
            var result = await batchService.ProcessSequentiallyAsync(totalRecords);
            return Results.Ok(result);
        })
        .WithName("DemoBatchBefore")
        .WithTags("Batch Processing Demo");

        app.MapPost("/demo/batch/after", async (BatchProcessingService batchService) =>
        {
            const int totalRecords = 100;
            const int chunkSize = 10;
            const int maxParallelChunks = 4;

            var result = await batchService.ProcessInParallelChunksAsync(totalRecords, chunkSize, maxParallelChunks);
            return Results.Ok(result);
        })
        .WithName("DemoBatchAfter")
        .WithTags("Batch Processing Demo");
    }
}
