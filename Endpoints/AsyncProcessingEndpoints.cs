using ParallelECommerce.DTOs;
using ParallelECommerce.Models;
using ParallelECommerce.Services;

namespace ParallelECommerce.Endpoints;

public static class AsyncProcessingEndpoints
{
    public static void MapAsyncProcessingEndpoints(this WebApplication app)
    {
        // BEFORE: the user waits for slow secondary work such as invoice/email sending.
        app.MapPost("/orders/before-sync", async (OrderRequest request) =>
        {
            var startedAt = DateTime.UtcNow;

            // Simulate the essential order creation path.
            await Task.Delay(300);

            // Simulate slow secondary work done synchronously.
            await Task.Delay(3000);

            var finishedAt = DateTime.UtcNow;

            return Results.Ok(new
            {
                mode = "BEFORE - Synchronous Processing",
                message = "Order created and notification sent synchronously.",
                request.ProductId,
                request.Quantity,
                request.CustomerEmail,
                durationMs = Math.Round((finishedAt - startedAt).TotalMilliseconds, 2),
                problem = "The user had to wait until the slow notification task finished."
            });
        })
        .WithName("CreateOrderBeforeSync")
        .WithTags("Async Queue Demo");

        // AFTER: the request enqueues the secondary work and returns quickly.
        app.MapPost("/orders/after-async", async (
            OrderRequest request,
            NotificationQueueService notificationQueue,
            CancellationToken cancellationToken) =>
        {
            var startedAt = DateTime.UtcNow;

            // Simulate the essential order creation path only.
            await Task.Delay(300, cancellationToken);

            var job = new NotificationJob
            {
                CustomerEmail = request.CustomerEmail,
                Message = $"Invoice for product {request.ProductId}, quantity {request.Quantity}"
            };

            await notificationQueue.EnqueueAsync(job, cancellationToken);

            var finishedAt = DateTime.UtcNow;

            return Results.Ok(new
            {
                mode = "AFTER - Asynchronous Queue Processing",
                message = "Order created. Notification job was queued and will be processed in the background.",
                request.ProductId,
                request.Quantity,
                request.CustomerEmail,
                queuedJobId = job.JobId,
                durationMs = Math.Round((finishedAt - startedAt).TotalMilliseconds, 2),
                queueStatus = notificationQueue.GetStatus(),
                explanation = "The user does not wait for the slow notification task. The background worker processes it after the response is returned."
            });
        })
        .WithName("CreateOrderAfterAsync")
        .WithTags("Async Queue Demo");

        app.MapPost("/notifications/reset", (NotificationQueueService notificationQueue) =>
        {
            return Results.Ok(new
            {
                message = "Notification queue metrics were reset.",
                status = notificationQueue.ResetMetrics()
            });
        })
        .WithName("ResetNotificationQueueMetrics")
        .WithTags("Async Queue Demo");

        app.MapGet("/notifications/status", (NotificationQueueService notificationQueue) =>
        {
            return Results.Ok(notificationQueue.GetStatus());
        })
        .WithName("GetNotificationQueueStatus")
        .WithTags("Async Queue Demo");
    }
}
