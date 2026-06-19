using System.Globalization;
using System.Text;
using ParallelECommerce.Models;
using ParallelECommerce.Services;

namespace ParallelECommerce.Endpoints;

public static class BottleneckBenchmarkingEndpoints
{
    public static void MapBottleneckBenchmarkingEndpoints(this WebApplication app)
    {
        app.MapPost("/benchmark/reset", async (BottleneckBenchmarkingService benchmarkingService, CancellationToken cancellationToken) =>
        {
            var snapshot = await benchmarkingService.ResetAsync(cancellationToken);
            return Results.Ok(new
            {
                message = "Requirement 10 benchmark state was reset.",
                snapshot
            });
        })
        .WithName("ResetBenchmark")
        .WithTags("Requirement 10 - Bottleneck Analysis & Benchmarking");

        app.MapPost("/demo/benchmark/before", async (BottleneckBenchmarkingService benchmarkingService, CancellationToken cancellationToken) =>
        {
            var snapshot = await benchmarkingService.RunBeforeBenchmarkAsync(cancellationToken);
            return Results.Ok(snapshot);
        })
        .WithName("RunBenchmarkBefore")
        .WithTags("Requirement 10 - Bottleneck Analysis & Benchmarking");

        app.MapPost("/demo/benchmark/after", async (BottleneckBenchmarkingService benchmarkingService, CancellationToken cancellationToken) =>
        {
            var snapshot = await benchmarkingService.RunAfterBenchmarkAsync(cancellationToken);
            return Results.Ok(snapshot);
        })
        .WithName("RunBenchmarkAfter")
        .WithTags("Requirement 10 - Bottleneck Analysis & Benchmarking");

        app.MapPost("/demo/benchmark/full", async (BottleneckBenchmarkingService benchmarkingService, CancellationToken cancellationToken) =>
        {
            var snapshot = await benchmarkingService.RunFullBenchmarkAsync(cancellationToken);
            return Results.Ok(snapshot);
        })
        .WithName("RunFullBenchmark")
        .WithTags("Requirement 10 - Bottleneck Analysis & Benchmarking");

        app.MapGet("/benchmark/metrics", (BottleneckBenchmarkingService benchmarkingService) =>
        {
            return Results.Ok(benchmarkingService.GetLastSnapshot());
        })
        .WithName("GetBenchmarkMetrics")
        .WithTags("Requirement 10 - Bottleneck Analysis & Benchmarking");

        app.MapGet("/benchmark/report", (BottleneckBenchmarkingService benchmarkingService) =>
        {
            var snapshot = benchmarkingService.GetLastSnapshot();
            return Results.Text(BuildMarkdownReport(snapshot), "text/markdown; charset=utf-8");
        })
        .WithName("GetBenchmarkMarkdownReport")
        .WithTags("Requirement 10 - Bottleneck Analysis & Benchmarking");

        app.MapGet("/benchmark/chart/svg", (BottleneckBenchmarkingService benchmarkingService) =>
        {
            var snapshot = benchmarkingService.GetLastSnapshot();
            return Results.Text(BuildBenchmarkChartSvg(snapshot), "image/svg+xml");
        })
        .WithName("GetBenchmarkSvgChart")
        .WithTags("Requirement 10 - Bottleneck Analysis & Benchmarking");
    }

    private static string BuildMarkdownReport(BenchmarkMetricsSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Requirement 10 - Bottleneck Analysis & Benchmarking");
        builder.AppendLine();
        builder.AppendLine($"- Benchmark passed: **{snapshot.BenchmarkPassed}**");
        builder.AppendLine($"- Identified bottleneck: **{snapshot.IdentifiedBottleneck}**");
        builder.AppendLine($"- Root cause: {snapshot.RootCause}");
        builder.AppendLine($"- Applied optimization: {snapshot.AppliedOptimization}");
        builder.AppendLine($"- Before total duration: {snapshot.BeforeTotalDurationMs} ms");
        builder.AppendLine($"- After total duration: {snapshot.AfterTotalDurationMs} ms");
        builder.AppendLine($"- Overall latency reduction: {snapshot.OverallLatencyReductionPercent}%");
        builder.AppendLine();
        builder.AppendLine("| Operation | Before avg ms | After avg ms | Reduction % | Speedup | Optimization |");
        builder.AppendLine("|---|---:|---:|---:|---:|---|");

        foreach (var comparison in snapshot.Comparisons)
        {
            builder.AppendLine($"| {comparison.OperationName} | {comparison.BeforeAverageLatencyMs} | {comparison.AfterAverageLatencyMs} | {comparison.LatencyReductionPercent} | {comparison.SpeedupFactor}x | {comparison.OptimizationApplied} |");
        }

        builder.AppendLine();
        builder.AppendLine("Conclusion:");
        builder.AppendLine(snapshot.Explanation);
        return builder.ToString();
    }

    private static string BuildBenchmarkChartSvg(BenchmarkMetricsSnapshot snapshot)
    {
        const int width = 1100;
        const int rowHeight = 70;
        const int top = 95;
        const int left = 310;
        const int maxBarWidth = 640;
        var height = Math.Max(360, top + snapshot.Comparisons.Count * rowHeight + 110);
        var maxValue = Math.Max(1, snapshot.Comparisons.Select(comparison => comparison.BeforeAverageLatencyMs).DefaultIfEmpty(1).Max());

        var builder = new StringBuilder();
        builder.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        builder.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>");
        builder.AppendLine("<text x=\"30\" y=\"36\" font-family=\"Arial\" font-size=\"24\" font-weight=\"700\">Requirement 10 - Before/After Benchmark</text>");
        builder.AppendLine($"<text x=\"30\" y=\"65\" font-family=\"Arial\" font-size=\"15\">Bottleneck: {Escape(snapshot.IdentifiedBottleneck)} | Overall reduction: {snapshot.OverallLatencyReductionPercent.ToString("0.##", CultureInfo.InvariantCulture)}% | Passed: {snapshot.BenchmarkPassed}</text>");

        if (snapshot.Comparisons.Count == 0)
        {
            builder.AppendLine("<text x=\"30\" y=\"120\" font-family=\"Arial\" font-size=\"16\">No benchmark data yet. Run POST /demo/benchmark/full first.</text>");
            builder.AppendLine("</svg>");
            return builder.ToString();
        }

        builder.AppendLine("<circle cx=\"60\" cy=\"88\" r=\"6\" fill=\"#ef4444\"/><text x=\"74\" y=\"93\" font-family=\"Arial\" font-size=\"13\">Before avg latency</text>");
        builder.AppendLine("<circle cx=\"220\" cy=\"88\" r=\"6\" fill=\"#22c55e\"/><text x=\"234\" y=\"93\" font-family=\"Arial\" font-size=\"13\">After avg latency</text>");

        for (var i = 0; i < snapshot.Comparisons.Count; i++)
        {
            var comparison = snapshot.Comparisons[i];
            var y = top + i * rowHeight;
            var beforeWidth = comparison.BeforeAverageLatencyMs / maxValue * maxBarWidth;
            var afterWidth = comparison.AfterAverageLatencyMs / maxValue * maxBarWidth;

            builder.AppendLine($"<text x=\"30\" y=\"{y + 18}\" font-family=\"Arial\" font-size=\"13\" font-weight=\"700\">{Escape(comparison.OperationName)}</text>");
            builder.AppendLine($"<rect x=\"{left}\" y=\"{y}\" width=\"{beforeWidth.ToString("0.##", CultureInfo.InvariantCulture)}\" height=\"20\" rx=\"4\" fill=\"#ef4444\"/>");
            builder.AppendLine($"<text x=\"{left + beforeWidth + 8}\" y=\"{y + 15}\" font-family=\"Arial\" font-size=\"12\">{comparison.BeforeAverageLatencyMs} ms</text>");
            builder.AppendLine($"<rect x=\"{left}\" y=\"{y + 28}\" width=\"{afterWidth.ToString("0.##", CultureInfo.InvariantCulture)}\" height=\"20\" rx=\"4\" fill=\"#22c55e\"/>");
            builder.AppendLine($"<text x=\"{left + afterWidth + 8}\" y=\"{y + 43}\" font-family=\"Arial\" font-size=\"12\">{comparison.AfterAverageLatencyMs} ms | -{comparison.LatencyReductionPercent}% | {comparison.SpeedupFactor}x</text>");
        }

        builder.AppendLine($"<rect x=\"30\" y=\"{height - 70}\" width=\"1010\" height=\"45\" rx=\"10\" fill=\"#f8fafc\" stroke=\"#cbd5e1\"/>");
        builder.AppendLine($"<text x=\"50\" y=\"{height - 42}\" font-family=\"Arial\" font-size=\"14\">Before total: {snapshot.BeforeTotalDurationMs} ms | After total: {snapshot.AfterTotalDurationMs} ms | Root cause: {Escape(snapshot.RootCause)}</text>");
        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return System.Security.SecurityElement.Escape(value) ?? string.Empty;
    }
}
