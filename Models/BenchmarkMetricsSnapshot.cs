namespace ParallelECommerce.Models;

public class BenchmarkMetricsSnapshot
{
    public string ProviderName { get; set; } = string.Empty;
    public DateTime? LastRunAtUtc { get; set; }
    public int TotalBenchmarkedOperations { get; set; }
    public double BeforeTotalDurationMs { get; set; }
    public double AfterTotalDurationMs { get; set; }
    public double OverallLatencyReductionPercent { get; set; }
    public string IdentifiedBottleneck { get; set; } = string.Empty;
    public string RootCause { get; set; } = string.Empty;
    public string AppliedOptimization { get; set; } = string.Empty;
    public bool BenchmarkPassed { get; set; }
    public List<BenchmarkOperationSnapshot> Operations { get; set; } = new();
    public List<BenchmarkComparisonSnapshot> Comparisons { get; set; } = new();
    public object? ResourceSummary { get; set; }
    public string Explanation { get; set; } = string.Empty;
}
