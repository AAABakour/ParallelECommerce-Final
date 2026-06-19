namespace ParallelECommerce.Models;

public class BenchmarkOperationSnapshot
{
    public string OperationName { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string Scenario { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public int SuccessfulAttempts { get; set; }
    public int FailedAttempts { get; set; }
    public double AverageLatencyMs { get; set; }
    public double MinLatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
    public double TotalDurationMs { get; set; }
    public double ThroughputOpsPerSecond { get; set; }
    public string Finding { get; set; } = string.Empty;
}
