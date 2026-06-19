namespace ParallelECommerce.Models;

public class StressOperationMetricsSnapshot
{
    public string OperationName { get; set; } = string.Empty;
    public long Attempts { get; set; }
    public long SuccessfulOperations { get; set; }
    public long ExpectedBusinessFailures { get; set; }
    public long UnexpectedFailures { get; set; }
    public double AverageLatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }
}
