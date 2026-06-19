using System.Diagnostics;
using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public class ResourceMonitoringService : BackgroundService
{
    private readonly PerformanceMetricsService _metricsService;
    private readonly ILogger<ResourceMonitoringService> _logger;

    private TimeSpan _previousTotalProcessorTime;
    private DateTime _previousTimestampUtc;

    public ResourceMonitoringService(
        PerformanceMetricsService metricsService,
        ILogger<ResourceMonitoringService> logger)
    {
        _metricsService = metricsService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Resource monitoring service started.");

        var process = Process.GetCurrentProcess();
        process.Refresh();
        _previousTotalProcessorTime = process.TotalProcessorTime;
        _previousTimestampUtc = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                CaptureSample();
            }
            catch (OperationCanceledException)
            {
                // Normal when the application is shutting down.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to capture resource monitoring sample.");
            }
        }
    }

    private void CaptureSample()
    {
        var process = Process.GetCurrentProcess();
        process.Refresh();

        var nowUtc = DateTime.UtcNow;
        var currentTotalProcessorTime = process.TotalProcessorTime;

        var cpuTimeDeltaMs = (currentTotalProcessorTime - _previousTotalProcessorTime).TotalMilliseconds;
        var elapsedMs = (nowUtc - _previousTimestampUtc).TotalMilliseconds;

        var cpuUsagePercent = elapsedMs <= 0
            ? 0
            : cpuTimeDeltaMs / (elapsedMs * Environment.ProcessorCount) * 100;

        _previousTotalProcessorTime = currentTotalProcessorTime;
        _previousTimestampUtc = nowUtc;

        ThreadPool.GetAvailableThreads(out var availableWorkers, out _);
        ThreadPool.GetMaxThreads(out var maxWorkers, out _);

        var sample = new ResourceSample
        {
            TimestampUtc = nowUtc,
            CpuUsagePercent = Math.Round(Math.Clamp(cpuUsagePercent, 0, 100), 2),
            ManagedMemoryMb = Math.Round(GC.GetTotalMemory(forceFullCollection: false) / 1024d / 1024d, 2),
            WorkingSetMemoryMb = Math.Round(process.WorkingSet64 / 1024d / 1024d, 2),
            ProcessThreadCount = process.Threads.Count,
            ThreadPoolBusyWorkers = maxWorkers - availableWorkers,
            ThreadPoolAvailableWorkers = availableWorkers,
            ThreadPoolMaxWorkers = maxWorkers,
            ActiveHttpRequests = _metricsService.ActiveHttpRequests,
            TotalHttpRequests = _metricsService.TotalHttpRequests,
            FailedHttpRequests = _metricsService.FailedHttpRequests,
            AverageHttpLatencyMs = _metricsService.GetAverageHttpLatencyMs()
        };

        _metricsService.AddResourceSample(sample);
    }
}
