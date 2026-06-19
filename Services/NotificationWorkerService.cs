namespace ParallelECommerce.Services;

public class NotificationWorkerService : BackgroundService
{
    private readonly NotificationQueueService _queueService;
    private readonly ILogger<NotificationWorkerService> _logger;

    public NotificationWorkerService(
        NotificationQueueService queueService,
        ILogger<NotificationWorkerService> logger)
    {
        _queueService = queueService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification background worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _queueService.DequeueAsync(stoppingToken);

                try
                {
                    _logger.LogInformation(
                        "Processing notification job {JobId} for {Email}",
                        job.JobId,
                        job.CustomerEmail);

                    // Simulates a slow invoice/email/warehouse-notification task.
                    await Task.Delay(3000, stoppingToken);

                    _queueService.MarkAsProcessed();

                    _logger.LogInformation(
                        "Notification job {JobId} processed successfully.",
                        job.JobId);
                }
                catch (OperationCanceledException)
                {
                    _queueService.MarkAsFailed();
                    throw;
                }
                catch (Exception ex)
                {
                    _queueService.MarkAsFailed();
                    _logger.LogError(ex, "Notification job {JobId} failed.", job.JobId);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal during application shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while reading from notification queue.");
            }
        }
    }
}
