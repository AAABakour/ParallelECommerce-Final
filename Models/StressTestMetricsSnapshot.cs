namespace ParallelECommerce.Models;

public class StressTestMetricsSnapshot
{
    public string ProviderName { get; set; } = string.Empty;
    public int ConfiguredConcurrentUsers { get; set; }
    public int CompletedUsers { get; set; }
    public int UserFailures { get; set; }
    public long TotalOperationAttempts { get; set; }
    public long SuccessfulOperations { get; set; }
    public long ExpectedBusinessFailures { get; set; }
    public long UnexpectedFailures { get; set; }
    public double DurationMs { get; set; }
    public double ThroughputOperationsPerSecond { get; set; }
    public bool StabilityPassed { get; set; }
    public bool DataIntegrityValidated { get; set; }
    public DateTime? LastRunAtUtc { get; set; }
    public List<StressOperationMetricsSnapshot> Operations { get; set; } = new();
    public StressResourceSummary ResourceSummary { get; set; } = new();
    public object? DataIntegritySummary { get; set; }
    public object? SupportingMetrics { get; set; }
    public string Explanation { get; set; } = string.Empty;
}
