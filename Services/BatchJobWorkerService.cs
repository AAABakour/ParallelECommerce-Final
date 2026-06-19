namespace ParallelECommerce.Services;

public class BatchJobWorkerService : BackgroundService
{
    private readonly BatchProcessingService _batchProcessingService;
    private readonly ILogger<BatchJobWorkerService> _logger;

    public BatchJobWorkerService(
        BatchProcessingService batchProcessingService,
        ILogger<BatchJobWorkerService> logger)
    {
        _batchProcessingService = batchProcessingService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Batch job background worker started.");

        try
        {
            await _batchProcessingService.RunWorkerAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Batch job background worker stopped.");
        }
    }
}
