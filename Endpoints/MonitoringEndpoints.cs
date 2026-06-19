using System.Text;
using ParallelECommerce.Services;

namespace ParallelECommerce.Endpoints;

public static class MonitoringEndpoints
{
    public static void MapMonitoringEndpoints(this WebApplication app)
    {
        app.MapGet("/monitoring/metrics", (PerformanceMetricsService metricsService) =>
        {
            return Results.Ok(metricsService.GetSummary());
        })
        .WithName("GetPerformanceMetrics")
        .WithTags("Monitoring / AOP");

        app.MapGet("/monitoring/resources", (PerformanceMetricsService metricsService) =>
        {
            return Results.Ok(metricsService.GetResourceSamples());
        })
        .WithName("GetResourceSamples")
        .WithTags("Monitoring / AOP");

        app.MapGet("/monitoring/resources/csv", (PerformanceMetricsService metricsService) =>
        {
            var samples = metricsService.GetResourceSamples();
            var csv = new StringBuilder();

            csv.AppendLine("TimestampUtc,CpuUsagePercent,ManagedMemoryMb,WorkingSetMemoryMb,ProcessThreadCount,ThreadPoolBusyWorkers,ThreadPoolAvailableWorkers,ActiveHttpRequests,TotalHttpRequests,FailedHttpRequests,AverageHttpLatencyMs");

            foreach (var sample in samples)
            {
                csv.AppendLine(string.Join(',', new object[]
                {
                    sample.TimestampUtc.ToString("O"),
                    sample.CpuUsagePercent,
                    sample.ManagedMemoryMb,
                    sample.WorkingSetMemoryMb,
                    sample.ProcessThreadCount,
                    sample.ThreadPoolBusyWorkers,
                    sample.ThreadPoolAvailableWorkers,
                    sample.ActiveHttpRequests,
                    sample.TotalHttpRequests,
                    sample.FailedHttpRequests,
                    sample.AverageHttpLatencyMs
                }));
            }

            return Results.Text(csv.ToString(), "text/csv");
        })
        .WithName("GetResourceSamplesCsv")
        .WithTags("Monitoring / AOP");

        app.MapPost("/monitoring/reset", (PerformanceMetricsService metricsService) =>
        {
            metricsService.Reset();

            return Results.Ok(new
            {
                message = "Monitoring metrics and resource samples were reset."
            });
        })
        .WithName("ResetMonitoring")
        .WithTags("Monitoring / AOP");
    }
}
