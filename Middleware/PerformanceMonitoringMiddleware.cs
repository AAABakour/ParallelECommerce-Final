using System.Diagnostics;
using ParallelECommerce.Services;

namespace ParallelECommerce.Middleware;

public class PerformanceMonitoringMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PerformanceMonitoringMiddleware> _logger;

    public PerformanceMonitoringMiddleware(
        RequestDelegate next,
        ILogger<PerformanceMonitoringMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, PerformanceMetricsService metricsService)
    {
        var stopwatch = Stopwatch.StartNew();
        var failed = false;

        metricsService.RequestStarted();

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            failed = true;
            _logger.LogError(
                ex,
                "Unhandled exception while processing {Method} {Path}",
                context.Request.Method,
                context.Request.Path.Value);

            throw;
        }
        finally
        {
            stopwatch.Stop();

            metricsService.RequestFinished(
                context.Request.Method,
                context.Request.Path.Value ?? "/",
                context.Response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds,
                failed);
        }
    }
}
