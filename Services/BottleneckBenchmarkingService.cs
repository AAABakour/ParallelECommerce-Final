using System.Diagnostics;
using ParallelECommerce.Models;

namespace ParallelECommerce.Services;

public class BottleneckBenchmarkingService
{
    private const int ReadAttempts = 12;
    private const int BatchRecords = 24;
    private const int BatchChunkSize = 6;
    private const int MaxParallelChunks = 4;

    private readonly CachingCatalogService _cachingService;
    private readonly BatchProcessingService _batchProcessingService;
    private readonly PerformanceMetricsService _performanceMetricsService;

    private readonly object _lock = new();
    private BenchmarkMetricsSnapshot _lastSnapshot = EmptySnapshot();
    private List<BenchmarkOperationSnapshot> _lastBeforeOperations = new();
    private List<BenchmarkOperationSnapshot> _lastAfterOperations = new();

    public BottleneckBenchmarkingService(
        CachingCatalogService cachingService,
        BatchProcessingService batchProcessingService,
        PerformanceMetricsService performanceMetricsService)
    {
        _cachingService = cachingService;
        _batchProcessingService = batchProcessingService;
        _performanceMetricsService = performanceMetricsService;
    }

    public async Task<BenchmarkMetricsSnapshot> ResetAsync(CancellationToken cancellationToken = default)
    {
        await _cachingService.ResetAsync(cancellationToken);
        _performanceMetricsService.Reset();

        lock (_lock)
        {
            _lastBeforeOperations = new List<BenchmarkOperationSnapshot>();
            _lastAfterOperations = new List<BenchmarkOperationSnapshot>();
            _lastSnapshot = EmptySnapshot();
        }

        return GetLastSnapshot();
    }

    public async Task<BenchmarkMetricsSnapshot> RunBeforeBenchmarkAsync(CancellationToken cancellationToken = default)
    {
        await _cachingService.ResetAsync(cancellationToken);

        var operations = new List<BenchmarkOperationSnapshot>
        {
            await BenchmarkAsync(
                operationName: "Product details read",
                phase: "BEFORE",
                scenario: "Direct database read without Redis cache",
                attempts: ReadAttempts,
                operation: () => _cachingService.GetProductDirectAsync(1, cancellationToken),
                finding: "Repeated product reads pay the full backing-store cost every time."),

            await BenchmarkAsync(
                operationName: "Popular products query",
                phase: "BEFORE",
                scenario: "Direct popular-products query without Redis cache",
                attempts: ReadAttempts,
                operation: () => _cachingService.GetPopularProductsDirectAsync(3, cancellationToken),
                finding: "The same read-heavy popular-products query is recomputed for every request."),

            await BenchmarkAsync(
                operationName: "Stock snapshot read",
                phase: "BEFORE",
                scenario: "Direct stock read without short-lived cache",
                attempts: ReadAttempts,
                operation: () => _cachingService.GetStockDirectAsync(1, cancellationToken),
                finding: "Short repeated stock snapshots still hit the backing store."),

            await BenchmarkAsync(
                operationName: "Daily sales batch calculation",
                phase: "BEFORE",
                scenario: "Sequential record-by-record batch processing",
                attempts: 3,
                operation: () => _batchProcessingService.ProcessSequentiallyAsync(BatchRecords),
                finding: "The batch job is serialized; every record waits for the previous record to finish.")
        };

        lock (_lock)
        {
            _lastBeforeOperations = operations;
            _lastSnapshot = BuildSnapshot(_lastBeforeOperations, _lastAfterOperations, DateTime.UtcNow);
        }

        return GetLastSnapshot();
    }

    public async Task<BenchmarkMetricsSnapshot> RunAfterBenchmarkAsync(CancellationToken cancellationToken = default)
    {
        var beforeIsMissing = false;
        lock (_lock)
        {
            beforeIsMissing = _lastBeforeOperations.Count == 0;
        }

        if (beforeIsMissing)
        {
            await RunBeforeBenchmarkAsync(cancellationToken);
        }

        await _cachingService.ResetAsync(cancellationToken);

        // Warm up the cache once. The benchmark then measures the optimized steady state.
        await _cachingService.GetProductCachedAsync(1, cancellationToken);
        await _cachingService.GetPopularProductsCachedAsync(3, cancellationToken);
        await _cachingService.GetStockCachedAsync(1, cancellationToken);

        var operations = new List<BenchmarkOperationSnapshot>
        {
            await BenchmarkAsync(
                operationName: "Product details read",
                phase: "AFTER",
                scenario: "Redis cache-aside product read after warm-up",
                attempts: ReadAttempts,
                operation: () => _cachingService.GetProductCachedAsync(1, cancellationToken),
                finding: "After the first cache fill, repeated product reads are served from Redis."),

            await BenchmarkAsync(
                operationName: "Popular products query",
                phase: "AFTER",
                scenario: "Redis cached popular-products list after warm-up",
                attempts: ReadAttempts,
                operation: () => _cachingService.GetPopularProductsCachedAsync(3, cancellationToken),
                finding: "The expensive read-heavy query is reused from Redis instead of recomputed."),

            await BenchmarkAsync(
                operationName: "Stock snapshot read",
                phase: "AFTER",
                scenario: "Short-lived Redis stock snapshot after warm-up",
                attempts: ReadAttempts,
                operation: () => _cachingService.GetStockCachedAsync(1, cancellationToken),
                finding: "Short-lived stock cache reduces repeated reads while still allowing invalidation."),

            await BenchmarkAsync(
                operationName: "Daily sales batch calculation",
                phase: "AFTER",
                scenario: "Parallel chunk processing with bounded concurrency",
                attempts: 3,
                operation: () => _batchProcessingService.ProcessInParallelChunksAsync(BatchRecords, BatchChunkSize, MaxParallelChunks),
                finding: "The batch is split into chunks and processed in parallel with a safe upper limit.")
        };

        lock (_lock)
        {
            _lastAfterOperations = operations;
            _lastSnapshot = BuildSnapshot(_lastBeforeOperations, _lastAfterOperations, DateTime.UtcNow);
        }

        return GetLastSnapshot();
    }

    public async Task<BenchmarkMetricsSnapshot> RunFullBenchmarkAsync(CancellationToken cancellationToken = default)
    {
        await ResetAsync(cancellationToken);
        await RunBeforeBenchmarkAsync(cancellationToken);
        return await RunAfterBenchmarkAsync(cancellationToken);
    }

    public BenchmarkMetricsSnapshot GetLastSnapshot()
    {
        lock (_lock)
        {
            return CloneSnapshot(_lastSnapshot);
        }
    }

    private static async Task<BenchmarkOperationSnapshot> BenchmarkAsync(
        string operationName,
        string phase,
        string scenario,
        int attempts,
        Func<Task<object>> operation,
        string finding)
    {
        var latencies = new List<double>();
        var totalStopwatch = Stopwatch.StartNew();
        var successfulAttempts = 0;
        var failedAttempts = 0;

        for (var i = 0; i < attempts; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await operation();
                stopwatch.Stop();
                successfulAttempts++;
                latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
            catch
            {
                stopwatch.Stop();
                failedAttempts++;
                latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        totalStopwatch.Stop();
        var sorted = latencies.OrderBy(value => value).ToList();
        var totalMs = Math.Max(totalStopwatch.Elapsed.TotalMilliseconds, 1);

        return new BenchmarkOperationSnapshot
        {
            OperationName = operationName,
            Phase = phase,
            Scenario = scenario,
            Attempts = attempts,
            SuccessfulAttempts = successfulAttempts,
            FailedAttempts = failedAttempts,
            AverageLatencyMs = Round(sorted.Count == 0 ? 0 : sorted.Average()),
            MinLatencyMs = Round(sorted.Count == 0 ? 0 : sorted.First()),
            MaxLatencyMs = Round(sorted.Count == 0 ? 0 : sorted.Last()),
            P95LatencyMs = Round(Percentile(sorted, 0.95)),
            TotalDurationMs = Round(totalStopwatch.Elapsed.TotalMilliseconds),
            ThroughputOpsPerSecond = Round(attempts / totalMs * 1000),
            Finding = finding
        };
    }

    private BenchmarkMetricsSnapshot BuildSnapshot(
        List<BenchmarkOperationSnapshot> beforeOperations,
        List<BenchmarkOperationSnapshot> afterOperations,
        DateTime? lastRunAtUtc)
    {
        var comparisons = BuildComparisons(beforeOperations, afterOperations);
        var beforeTotal = beforeOperations.Sum(operation => operation.TotalDurationMs);
        var afterTotal = afterOperations.Sum(operation => operation.TotalDurationMs);
        var bestComparison = comparisons
            .OrderByDescending(comparison => comparison.BeforeAverageLatencyMs)
            .ThenByDescending(comparison => comparison.LatencyReductionPercent)
            .FirstOrDefault();

        var benchmarkPassed = beforeOperations.Count > 0
            && afterOperations.Count > 0
            && beforeOperations.All(operation => operation.FailedAttempts == 0)
            && afterOperations.All(operation => operation.FailedAttempts == 0)
            && comparisons.Any(comparison => comparison.LatencyReductionPercent > 25);

        return new BenchmarkMetricsSnapshot
        {
            ProviderName = "Requirement 10 benchmarking and bottleneck-analysis service",
            LastRunAtUtc = lastRunAtUtc,
            TotalBenchmarkedOperations = beforeOperations.Count + afterOperations.Count,
            BeforeTotalDurationMs = Round(beforeTotal),
            AfterTotalDurationMs = Round(afterTotal),
            OverallLatencyReductionPercent = beforeTotal <= 0 || afterTotal <= 0 ? 0 : Round((beforeTotal - afterTotal) / beforeTotal * 100),
            IdentifiedBottleneck = bestComparison?.OperationName ?? "Not identified yet",
            RootCause = bestComparison?.Bottleneck ?? "Run the before/after benchmark first.",
            AppliedOptimization = bestComparison?.OptimizationApplied ?? "Run the optimized benchmark first.",
            BenchmarkPassed = benchmarkPassed,
            Operations = beforeOperations.Concat(afterOperations)
                .OrderBy(operation => operation.OperationName)
                .ThenBy(operation => operation.Phase)
                .ToList(),
            Comparisons = comparisons,
            ResourceSummary = BuildResourceSummary(),
            Explanation = benchmarkPassed
                ? "The benchmark measured key operations before and after optimization, identified a bottleneck, and showed numeric latency improvement."
                : "Run POST /demo/benchmark/full to collect before/after measurements and identify the bottleneck."
        };
    }

    private static List<BenchmarkComparisonSnapshot> BuildComparisons(
        List<BenchmarkOperationSnapshot> beforeOperations,
        List<BenchmarkOperationSnapshot> afterOperations)
    {
        var comparisons = new List<BenchmarkComparisonSnapshot>();

        foreach (var before in beforeOperations)
        {
            var after = afterOperations.FirstOrDefault(operation => operation.OperationName == before.OperationName);
            if (after is null)
            {
                continue;
            }

            var reduction = before.AverageLatencyMs <= 0
                ? 0
                : (before.AverageLatencyMs - after.AverageLatencyMs) / before.AverageLatencyMs * 100;
            var speedup = after.AverageLatencyMs <= 0
                ? 0
                : before.AverageLatencyMs / after.AverageLatencyMs;

            comparisons.Add(new BenchmarkComparisonSnapshot
            {
                OperationName = before.OperationName,
                Bottleneck = BottleneckDescription(before.OperationName),
                OptimizationApplied = OptimizationDescription(before.OperationName),
                BeforeAverageLatencyMs = before.AverageLatencyMs,
                AfterAverageLatencyMs = after.AverageLatencyMs,
                BeforeP95LatencyMs = before.P95LatencyMs,
                AfterP95LatencyMs = after.P95LatencyMs,
                LatencyReductionPercent = Round(reduction),
                SpeedupFactor = Round(speedup),
                Conclusion = reduction > 0
                    ? $"Average latency improved by {Round(reduction)}% with a {Round(speedup)}x speedup."
                    : "No latency improvement was observed for this operation."
            });
        }

        return comparisons
            .OrderByDescending(comparison => comparison.LatencyReductionPercent)
            .ToList();
    }

    private object BuildResourceSummary()
    {
        var samples = _performanceMetricsService.GetResourceSamples();
        if (samples.Count == 0)
        {
            return new
            {
                sampleCount = 0,
                explanation = "No resource samples captured yet. Keep the application running for a few seconds, then run the benchmark."
            };
        }

        return new
        {
            sampleCount = samples.Count,
            peakCpuUsagePercent = Round(samples.Max(sample => sample.CpuUsagePercent)),
            averageCpuUsagePercent = Round(samples.Average(sample => sample.CpuUsagePercent)),
            peakWorkingSetMemoryMb = Round(samples.Max(sample => sample.WorkingSetMemoryMb)),
            peakManagedMemoryMb = Round(samples.Max(sample => sample.ManagedMemoryMb)),
            peakBusyWorkers = samples.Max(sample => sample.ThreadPoolBusyWorkers),
            failedHttpRequests = samples.Max(sample => sample.FailedHttpRequests),
            firstSampleAtUtc = samples.Min(sample => sample.TimestampUtc),
            lastSampleAtUtc = samples.Max(sample => sample.TimestampUtc)
        };
    }

    private static string BottleneckDescription(string operationName)
    {
        return operationName switch
        {
            "Product details read" => "Repeated reads hit the backing store instead of reusing a cached product document.",
            "Popular products query" => "A read-heavy catalog query is recomputed for every request.",
            "Stock snapshot read" => "The same short-lived stock value is read repeatedly from the backing store.",
            "Daily sales batch calculation" => "The batch job processes records sequentially, so total time grows linearly with record count.",
            _ => "The operation spends too much time in repeated work."
        };
    }

    private static string OptimizationDescription(string operationName)
    {
        return operationName switch
        {
            "Product details read" => "Redis cache-aside with invalidation on stock-changing operations.",
            "Popular products query" => "Redis caching for the read-heavy popular-products result.",
            "Stock snapshot read" => "Short-lived Redis stock snapshot with invalidation when stock changes.",
            "Daily sales batch calculation" => "Parallel chunk processing with bounded concurrency.",
            _ => "Optimization applied in the after phase."
        };
    }

    private static double Percentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        index = Math.Clamp(index, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }

    private static double Round(double value) => Math.Round(value, 2);

    private static BenchmarkMetricsSnapshot EmptySnapshot()
    {
        return new BenchmarkMetricsSnapshot
        {
            ProviderName = "Requirement 10 benchmarking and bottleneck-analysis service",
            LastRunAtUtc = null,
            TotalBenchmarkedOperations = 0,
            BeforeTotalDurationMs = 0,
            AfterTotalDurationMs = 0,
            OverallLatencyReductionPercent = 0,
            IdentifiedBottleneck = "Not identified yet",
            RootCause = "Run POST /demo/benchmark/full to measure baseline and optimized operations.",
            AppliedOptimization = "Not applied yet",
            BenchmarkPassed = false,
            Operations = new List<BenchmarkOperationSnapshot>(),
            Comparisons = new List<BenchmarkComparisonSnapshot>(),
            ResourceSummary = new { sampleCount = 0 },
            Explanation = "Requirement 10 requires numeric before/after benchmarking and at least one identified bottleneck."
        };
    }

    private static BenchmarkMetricsSnapshot CloneSnapshot(BenchmarkMetricsSnapshot snapshot)
    {
        return new BenchmarkMetricsSnapshot
        {
            ProviderName = snapshot.ProviderName,
            LastRunAtUtc = snapshot.LastRunAtUtc,
            TotalBenchmarkedOperations = snapshot.TotalBenchmarkedOperations,
            BeforeTotalDurationMs = snapshot.BeforeTotalDurationMs,
            AfterTotalDurationMs = snapshot.AfterTotalDurationMs,
            OverallLatencyReductionPercent = snapshot.OverallLatencyReductionPercent,
            IdentifiedBottleneck = snapshot.IdentifiedBottleneck,
            RootCause = snapshot.RootCause,
            AppliedOptimization = snapshot.AppliedOptimization,
            BenchmarkPassed = snapshot.BenchmarkPassed,
            Operations = snapshot.Operations.Select(operation => new BenchmarkOperationSnapshot
            {
                OperationName = operation.OperationName,
                Phase = operation.Phase,
                Scenario = operation.Scenario,
                Attempts = operation.Attempts,
                SuccessfulAttempts = operation.SuccessfulAttempts,
                FailedAttempts = operation.FailedAttempts,
                AverageLatencyMs = operation.AverageLatencyMs,
                MinLatencyMs = operation.MinLatencyMs,
                MaxLatencyMs = operation.MaxLatencyMs,
                P95LatencyMs = operation.P95LatencyMs,
                TotalDurationMs = operation.TotalDurationMs,
                ThroughputOpsPerSecond = operation.ThroughputOpsPerSecond,
                Finding = operation.Finding
            }).ToList(),
            Comparisons = snapshot.Comparisons.Select(comparison => new BenchmarkComparisonSnapshot
            {
                OperationName = comparison.OperationName,
                Bottleneck = comparison.Bottleneck,
                OptimizationApplied = comparison.OptimizationApplied,
                BeforeAverageLatencyMs = comparison.BeforeAverageLatencyMs,
                AfterAverageLatencyMs = comparison.AfterAverageLatencyMs,
                BeforeP95LatencyMs = comparison.BeforeP95LatencyMs,
                AfterP95LatencyMs = comparison.AfterP95LatencyMs,
                LatencyReductionPercent = comparison.LatencyReductionPercent,
                SpeedupFactor = comparison.SpeedupFactor,
                Conclusion = comparison.Conclusion
            }).ToList(),
            ResourceSummary = snapshot.ResourceSummary,
            Explanation = snapshot.Explanation
        };
    }
}
