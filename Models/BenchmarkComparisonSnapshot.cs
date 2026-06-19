namespace ParallelECommerce.Models;

public class BenchmarkComparisonSnapshot
{
    public string OperationName { get; set; } = string.Empty;
    public string Bottleneck { get; set; } = string.Empty;
    public string OptimizationApplied { get; set; } = string.Empty;
    public double BeforeAverageLatencyMs { get; set; }
    public double AfterAverageLatencyMs { get; set; }
    public double BeforeP95LatencyMs { get; set; }
    public double AfterP95LatencyMs { get; set; }
    public double LatencyReductionPercent { get; set; }
    public double SpeedupFactor { get; set; }
    public string Conclusion { get; set; } = string.Empty;
}
