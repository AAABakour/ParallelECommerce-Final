using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ParallelECommerce.Models;
using ParallelECommerce.Services;

namespace ParallelECommerce.Endpoints;

public static class StressTestingEndpoints
{
    public static void MapStressTestingEndpoints(this WebApplication app)
    {
        app.MapPost("/stress-test/reset", async (StressTestingService stressTestingService, CancellationToken cancellationToken) =>
        {
            var snapshot = await stressTestingService.ResetAsync(cancellationToken);
            return Results.Ok(new
            {
                message = "Requirement 09 stress-test state was reset.",
                snapshot
            });
        })
        .WithName("ResetStressTest")
        .WithTags("Requirement 09 - Stress Testing");

        app.MapPost("/demo/stress/100-users", async (StressTestingService stressTestingService, CancellationToken cancellationToken) =>
        {
            var snapshot = await stressTestingService.RunOneHundredUsersAsync(cancellationToken);
            return Results.Ok(snapshot);
        })
        .WithName("RunOneHundredUserStressTest")
        .WithTags("Requirement 09 - Stress Testing");

        app.MapGet("/stress-test/metrics", (StressTestingService stressTestingService) =>
        {
            return Results.Ok(stressTestingService.GetLastSnapshot());
        })
        .WithName("GetStressTestMetrics")
        .WithTags("Requirement 09 - Stress Testing");

        app.MapGet("/stress-test/resources/svg", (PerformanceMetricsService metricsService) =>
        {
            var svg = BuildResourceChartSvg(metricsService.GetResourceSamples());
            return Results.Text(svg, "image/svg+xml");
        })
        .WithName("GetStressResourceSvgChart")
        .WithTags("Requirement 09 - Stress Testing");
    }

    private static string BuildResourceChartSvg(List<ResourceSample> samples)
    {
        const int width = 1000;
        const int height = 460;
        const int left = 70;
        const int top = 60;
        const int plotWidth = 850;
        const int plotHeight = 290;
        const int bottom = top + plotHeight;

        var builder = new StringBuilder();
        builder.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        builder.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
        builder.AppendLine("<text x=\"30\" y=\"34\" font-family=\"Arial\" font-size=\"22\" font-weight=\"700\">Requirement 09 - Resource usage during 100-user stress test</text>");

        if (samples.Count < 2)
        {
            builder.AppendLine("<text x=\"30\" y=\"95\" font-family=\"Arial\" font-size=\"16\">No resource samples yet. Run POST /demo/stress/100-users, then refresh this chart.</text>");
            builder.AppendLine("</svg>");
            return builder.ToString();
        }

        var ordered = samples.OrderBy(sample => sample.TimestampUtc).ToList();
        var first = ordered.First().TimestampUtc;
        var last = ordered.Last().TimestampUtc;
        var totalSeconds = Math.Max((last - first).TotalSeconds, 1);
        var maxMemory = Math.Max(ordered.Max(sample => sample.WorkingSetMemoryMb), 1);
        var maxBusyWorkers = Math.Max(ordered.Max(sample => sample.ThreadPoolBusyWorkers), 1);

        builder.AppendLine($"<line x1=\"{left}\" y1=\"{bottom}\" x2=\"{left + plotWidth}\" y2=\"{bottom}\" stroke=\"#222\"/>");
        builder.AppendLine($"<line x1=\"{left}\" y1=\"{top}\" x2=\"{left}\" y2=\"{bottom}\" stroke=\"#222\"/>");

        for (var i = 0; i <= 5; i++)
        {
            var y = top + i * plotHeight / 5.0;
            var value = 100 - i * 20;
            builder.AppendLine($"<line x1=\"{left}\" y1=\"{y.ToString("0.##", CultureInfo.InvariantCulture)}\" x2=\"{left + plotWidth}\" y2=\"{y.ToString("0.##", CultureInfo.InvariantCulture)}\" stroke=\"#e5e7eb\"/>");
            builder.AppendLine($"<text x=\"20\" y=\"{(y + 4).ToString("0.##", CultureInfo.InvariantCulture)}\" font-family=\"Arial\" font-size=\"12\">{value}%</text>");
        }

        string BuildPolyline(Func<ResourceSample, double> selector, double maxValue)
        {
            var points = ordered.Select(sample =>
            {
                var x = left + ((sample.TimestampUtc - first).TotalSeconds / totalSeconds) * plotWidth;
                var normalized = Math.Clamp(selector(sample) / maxValue, 0, 1);
                var y = bottom - normalized * plotHeight;
                return $"{x.ToString("0.##", CultureInfo.InvariantCulture)},{y.ToString("0.##", CultureInfo.InvariantCulture)}";
            });

            return string.Join(' ', points);
        }

        var cpuPoints = BuildPolyline(sample => sample.CpuUsagePercent, 100);
        var memoryPoints = BuildPolyline(sample => sample.WorkingSetMemoryMb, maxMemory);
        var workerPoints = BuildPolyline(sample => sample.ThreadPoolBusyWorkers, maxBusyWorkers);

        builder.AppendLine($"<polyline fill=\"none\" stroke=\"#2563eb\" stroke-width=\"3\" points=\"{cpuPoints}\"/>");
        builder.AppendLine($"<polyline fill=\"none\" stroke=\"#16a34a\" stroke-width=\"3\" points=\"{memoryPoints}\"/>");
        builder.AppendLine($"<polyline fill=\"none\" stroke=\"#f97316\" stroke-width=\"3\" points=\"{workerPoints}\"/>");

        var peakCpu = ordered.Max(sample => sample.CpuUsagePercent);
        var avgCpu = ordered.Average(sample => sample.CpuUsagePercent);
        var peakMemory = ordered.Max(sample => sample.WorkingSetMemoryMb);
        var peakBusyWorkers = ordered.Max(sample => sample.ThreadPoolBusyWorkers);
        var failedRequests = ordered.Max(sample => sample.FailedHttpRequests);

        builder.AppendLine("<rect x=\"70\" y=\"374\" width=\"850\" height=\"58\" rx=\"10\" fill=\"#f8fafc\" stroke=\"#cbd5e1\"/>");
        builder.AppendLine("<circle cx=\"95\" cy=\"394\" r=\"6\" fill=\"#2563eb\"/><text x=\"110\" y=\"399\" font-family=\"Arial\" font-size=\"13\">CPU %</text>");
        builder.AppendLine("<circle cx=\"190\" cy=\"394\" r=\"6\" fill=\"#16a34a\"/><text x=\"205\" y=\"399\" font-family=\"Arial\" font-size=\"13\">Working set memory, normalized</text>");
        builder.AppendLine("<circle cx=\"420\" cy=\"394\" r=\"6\" fill=\"#f97316\"/><text x=\"435\" y=\"399\" font-family=\"Arial\" font-size=\"13\">ThreadPool busy workers, normalized</text>");
        builder.AppendLine($"<text x=\"90\" y=\"423\" font-family=\"Arial\" font-size=\"13\">Samples: {samples.Count} | Peak CPU: {peakCpu:0.##}% | Avg CPU: {avgCpu:0.##}% | Peak memory: {peakMemory:0.##} MB | Peak busy workers: {peakBusyWorkers} | Failed HTTP requests: {failedRequests}</text>");
        builder.AppendLine($"<text x=\"{left}\" y=\"{bottom + 28}\" font-family=\"Arial\" font-size=\"12\">Start: {SecurityElementEscape(first.ToString("HH:mm:ss"))}</text>");
        builder.AppendLine($"<text x=\"{left + plotWidth - 80}\" y=\"{bottom + 28}\" font-family=\"Arial\" font-size=\"12\">End: {SecurityElementEscape(last.ToString("HH:mm:ss"))}</text>");
        builder.AppendLine("</svg>");

        return builder.ToString();
    }

    private static string SecurityElementEscape(string value)
    {
        return System.Security.SecurityElement.Escape(value) ?? string.Empty;
    }
}
